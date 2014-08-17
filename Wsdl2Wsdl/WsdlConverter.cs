using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Services.Description;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using Binding = System.Web.Services.Description.Binding;
using Import = System.Web.Services.Description.Import;
using Message = System.Web.Services.Description.Message;
using MessageBinding = System.Web.Services.Description.MessageBinding;
using Operation = System.Web.Services.Description.Operation;
using OperationBinding = System.Web.Services.Description.OperationBinding;
using Service = System.Web.Services.Description.Service;

namespace WsdlConverter
{

	public class Wsdl2WsdlConverter
	{
		private int unique = 0;

		enum SoapVersion
		{
			Soap11,
			Soap12
		}

		public delegate void OnDocumentReference(object source, ref string url);

		public event OnDocumentReference DocumentReference;

		public void InvokeDocumentReference(ref string url)
		{
			OnDocumentReference handler = DocumentReference;
			if (handler != null) handler(this, ref url);
		}

		public ServiceDescription Convert(XmlDocument wsdl2Xml)
		{
			CannonizePrefix(wsdl2Xml);
			var ser = new XmlSerializer(typeof(DescriptionType));
			var reader = new StringReader(wsdl2Xml.OuterXml);
			var wsdl2 = (DescriptionType)ser.Deserialize(reader);

			var wsdl1 = new ServiceDescription();

			ConvertDescription(wsdl2, wsdl1);
			ConvertImports(wsdl2, wsdl1);
			ConvertTypes(wsdl2, wsdl1);
			ConvertInterface(wsdl2, wsdl1);
			ConvertBinding(wsdl2, wsdl1);
			ConvertService(wsdl2, wsdl1);

			//not sure why needs to do it... but otherwise code generation fails
			var m = new MemoryStream();
			wsdl1.Write(m);
			string s = Encoding.UTF8.GetString(m.GetBuffer());
			wsdl1 = ServiceDescription.Read(new StringReader(s));

			return wsdl1;
		}

		private void ConvertImports(DescriptionType wsdl2, ServiceDescription wsdl1)
		{
			var imports = wsdl2.Items.Where(s => s is ImportType);

			foreach (ImportType i in imports)
			{
				string loc = i.location;
				InvokeDocumentReference(ref loc);
				wsdl1.Imports.Add(new Import() { Namespace = i.@namespace, Location = loc });
			}

			var includes = wsdl2.Items.Where(s => s is IncludeType);

			foreach (IncludeType i in includes)
			{
				string loc = i.location;
				InvokeDocumentReference(ref loc);
				wsdl1.Imports.Add(new Import() { Location = loc });
			}
		}

		private void CannonizePrefix(XmlDocument wsdl2Xml)
		{
			var schemas = wsdl2Xml.SelectNodes("//*[local-name(.)='schema']");

			foreach (XmlNode s in schemas)
			{
				var nodes = s.SelectNodes(".//@type | .//@ref");
				foreach (XmlNode n in nodes)
				{
					if (String.IsNullOrEmpty(n.Value))
						continue;

					string[] qname = n.Value.Split(':');
					if (qname.Length != 2)
						continue;
					string prefix = qname[0];
					string @namespace = n.GetNamespaceOfPrefix(prefix);

					string attrName = "xmlns:" + prefix;
					if (s.Attributes[attrName] != null)
						continue;

					var attr = wsdl2Xml.CreateAttribute(attrName);
					attr.Value = @namespace;

					s.Attributes.Append(attr);
				}
			}
		}

		private void ConvertService(DescriptionType wsdl2, ServiceDescription wsdl1)
		{
			var services = wsdl2.Items.Where(s => s is ServiceType);

			foreach (ServiceType s2 in services)
			{
				var s1 = new Service();
				wsdl1.Services.Add(s1);
				s1.Name = s2.name;

				foreach (EndpointType e2 in s2.Items)
				{
					var p1 = new Port();
					s1.Ports.Add(p1);

					p1.Name = e2.name;
					p1.Binding = e2.binding;

					var sab = new SoapAddressBinding() { Location = e2.address };
					var sab12 = new Soap12AddressBinding() { Location = e2.address };
					p1.Extensions.Add(sab);
					p1.Extensions.Add(sab12);
				}
			}
		}

		private void ConvertBinding(DescriptionType wsdl2, ServiceDescription wsdl1)
		{
			var bindings = wsdl2.Items.Where(s => s is BindingType);

			foreach (BindingType b2 in bindings)
			{
				var b1 = new Binding();
				wsdl1.Bindings.Add(b1);

				b1.Name = b2.name;
				b1.Type = b2.@interface;

				var v = GetSoapVersion(b2);
				if (v == SoapVersion.Soap11)
					b1.Extensions.Add(new SoapBinding { Transport = "http://schemas.xmlsoap.org/soap/http" });
				else
					b1.Extensions.Add(new Soap12Binding { Transport = "http://schemas.xmlsoap.org/soap/http" });

				var operations = b2.Items.Where(s => s is BindingOperationType);

				foreach (BindingOperationType op2 in operations)
				{
					var op1 = new OperationBinding();
					b1.Operations.Add(op1);

					op1.Name = op2.@ref.Name;

					var soap = v == SoapVersion.Soap11 ? new SoapOperationBinding() : new Soap12OperationBinding();
					op1.Extensions.Add(soap);

					soap.SoapAction = FindSoapAction(op2);
					soap.Style = SoapBindingStyle.Document;

					var body = v == SoapVersion.Soap11 ? new SoapBodyBinding() : new Soap12BodyBinding();
					body.Use = SoapBindingUse.Literal;

					op1.Input = new InputBinding();
					op1.Input.Extensions.Add(body);

					op1.Output = new OutputBinding();
					op1.Output.Extensions.Add(body);

					HandleHeaders(wsdl1, op2, ItemsChoiceType1.input, op1.Input, v);
					HandleHeaders(wsdl1, op2, ItemsChoiceType1.output, op1.Output, v);

				}
			}
		}

		private void HandleHeaders(ServiceDescription wsdl, BindingOperationType op2, ItemsChoiceType1 type, MessageBinding mb, SoapVersion v)
		{
			if (op2.Items == null)
				return;

			for (int i = 0; i < op2.Items.Length; i++)
			{
				var curr = op2.Items[i] as BindingOperationMessageType;
				if (curr == null)
					continue;

				if (op2.ItemsElementName[i] != type)
					continue;

				if (curr.Items == null)
					continue;

				var headers = curr.Items.Where(s => s.LocalName == "header"
					&& s.NamespaceURI == "http://www.w3.org/ns/wsdl/soap");
				foreach (var n in headers)
				{
					var header = v == SoapVersion.Soap11 ? new SoapHeaderBinding() : new Soap12HeaderBinding();
					header.Use = SoapBindingUse.Literal;

					if (n.Attributes["element"] == null)
						return;

					var qName = new XmlQualifiedName(n.Attributes["element"].Value);
					const string partName = "H";
					header.Message = CreateMessage(wsdl, String.Format("{0}Header_{1}", op2.@ref.Name, unique++), qName, partName);
					header.Part = partName;

					mb.Extensions.Add(header);
				}

			}
		}

		private string FindSoapAction(BindingOperationType op2)
		{
			if (op2.AnyAttr == null)
				return String.Empty;

			var v = op2.AnyAttr.SingleOrDefault(
				s => (s.LocalName == "soapAction" || s.LocalName == "action") && s.NamespaceURI == "http://www.w3.org/ns/wsdl/soap");

			return v == null ? String.Empty : v.Value;
		}

		private SoapVersion GetSoapVersion(BindingType b2)
		{
			if (b2.AnyAttr == null)
				return SoapVersion.Soap12;

			var v1 = b2.AnyAttr.Where(
				s => s.LocalName == "version" && s.NamespaceURI == "http://www.w3.org/ns/wsdl/soap" && s.Value == "1.1");

			return v1.Count() == 0 ? SoapVersion.Soap12 : SoapVersion.Soap11;
		}

		private void ConvertInterface(DescriptionType wsdl2, ServiceDescription wsdl1)
		{
			var interfaces = wsdl2.Items.Where(s => s is InterfaceType);

			foreach (InterfaceType i2 in interfaces)
			{
				var p1 = new PortType();
				wsdl1.PortTypes.Add(p1);

				p1.Name = i2.name;

				if (i2.Items == null)
					continue;

				foreach (Object obj in i2.Items)
				{
					var op2 = obj as InterfaceOperationType;
					if (op2 == null)
						continue;

					var op1 = new Operation();
					p1.Operations.Add(op1);

					op1.Name = op2.name;

					var msg2In = FindMessage(op2, ItemsChoiceType.input);
					var msg1In = CreateMessage(wsdl1, op1.Name + "_in", msg2In.element, "parametere");
					op1.Messages.Add(new OperationInput() { Message = msg1In });

					var msg2Out = FindMessage(op2, ItemsChoiceType.output);

					bool isOneWay = msg2Out == null;
					if (!isOneWay)
					{
						var msg1Out = CreateMessage(wsdl1, op1.Name + "_out", msg2Out.element, "parameters");
						op1.Messages.Add(new OperationOutput() { Message = msg1Out });
					}
				}
			}
		}

		private XmlQualifiedName CreateMessage(ServiceDescription wsdl1, string name, XmlQualifiedName element, string partName)
		{
			var res = new XmlQualifiedName(name, wsdl1.TargetNamespace);

			if (HasMessage(wsdl1.Messages, name))
				return res;

			var msg = new Message();
			wsdl1.Messages.Add(msg);

			msg.Name = name;

			var p = new MessagePart();
			p.Name = partName;
			p.Element = element;
			msg.Parts.Add(p);

			return res;
		}

		private bool HasMessage(MessageCollection messages, string name)
		{
			foreach (Message m in messages)
			{
				if (m.Name == name)
					return true;
			}
			return false;
		}

		private MessageRefType FindMessage(InterfaceOperationType op2, ItemsChoiceType type)
		{
			for (int i = 0; i < op2.ItemsElementName.Length; i++)
			{
				if (op2.ItemsElementName[i] == type)
					return op2.Items[i] as MessageRefType;
			}

			return null;
		}

		private void ConvertTypes(DescriptionType wsdl2, ServiceDescription wsdl1)
		{

			var types = wsdl2.Items.FirstOrDefault(s => s is TypesType) as TypesType;
			if (types == null)
				return;

			foreach (var e in types.Any)
			{
				var sc = new XmlDocument();
				var scRoot = sc.ImportNode(e, true);
				if (scRoot.LocalName.ToLower() == "schema")
				{
					var s = XmlSchema.Read(new StringReader(scRoot.OuterXml), null);
					wsdl1.Types.Schemas.Add(s);
				}
			}
		}



		private void ConvertDescription(DescriptionType wsdl2, ServiceDescription wsdl1)
		{
			wsdl1.TargetNamespace = wsdl2.targetNamespace;
		}
		/*
		public static void Main(String[] args) {

			string originalUrl = "D:\\CINVESTAV\\Tesis\\servicios\\WSDL2.0\\ServicioDeUbicacionChilis.wsdl";

			var originalWsdl = new WebClient().DownloadData(originalUrl);
			var wsdl2 = new XmlDocument();
			wsdl2.Load(new MemoryStream(originalWsdl));

			var converter = new Wsdl2WsdlConverter();
			var res = converter.Convert(wsdl2);

			var sw = new StreamWriter(Console.OpenStandardOutput());
			sw.AutoFlush = true;
			res.Write(sw);

			System.Console.WriteLine("tada!");
			Console.ReadLine(); //Pause
		}*/

	}
}
