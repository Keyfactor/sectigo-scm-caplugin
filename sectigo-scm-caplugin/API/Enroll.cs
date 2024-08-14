using Keyfactor.Extensions.CAPlugin.Sectigo.Models;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.CAPlugin.Sectigo.API
{
	public class EnrollRequest
	{
		public int orgId { get; set; }

		public string csr { get; set; }

		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public string subjAltNames { get; set; }

		public int certType { get; set; }

		public int numberServers { get; set; }

		public int serverType { get; set; }

		public int term { get; set; }

		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public string comments { get; set; }

		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public List<CustomField> customFields { get; set; }

		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public string externalRequester { get; set; }
	}

	public class EnrollResponse
	{
		public int sslId { get; set; }

		public string renewId { get; set; }
	}
}
