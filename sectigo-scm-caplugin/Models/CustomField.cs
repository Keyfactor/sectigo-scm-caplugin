using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.CAPlugin.Sectigo.Models
{
	public class CustomField
	{
		public string name { get; set; }

		public string value { get; set; }

		[JsonIgnore]
		public bool mandatory { get; set; }
	}
}
