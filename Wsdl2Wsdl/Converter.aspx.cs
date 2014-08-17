﻿//------------------------------------------------------------------------------
// <auto-generated>
//     Este código fue generado por una herramienta.
//     Versión de runtime:4.0.30319.1022
//
//     Los cambios en este archivo podrían causar un comportamiento incorrecto y se perderán si
//     se vuelve a generar el código.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Wsdl2Wsdl
{
	using System;
	using System.Web;
	using System.Web.UI;

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
	using WsdlConverter;

	
	public partial class Converter : System.Web.UI.Page
	{

		protected void Page_Load(object sender, EventArgs e)
		{
			if (!Request.IsAuthenticated)
			{
				var varUriContent = Request.QueryString [0];
				Convertir (varUriContent);

			}
		}

		protected void Convertir(String ruta)
		{
			//ruta = "http://148.247.102.37:8080/wsdlcomparison/servicios/WSDL2.0/ServicioDeUbicacionChilis.wsdl"; //TESTING

			var originalWsdl = new WebClient().DownloadData(ruta);
			var wsdl2 = new XmlDocument();
			wsdl2.Load(new MemoryStream(originalWsdl));

			var converter = new Wsdl2WsdlConverter();
			var res = converter.Convert(wsdl2);
			Response.ContentType = "text/xml";
			res.Write(Response.OutputStream);
			Response.End();
		}

	}
}
