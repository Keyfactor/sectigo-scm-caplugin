using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.CAPlugin.Sectigo.API
{
	public class Error
	{
		[JsonProperty("code")]
		public int Code;

		[JsonProperty("description")]
		public string Description;
	}
}
