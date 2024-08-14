using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.CAPlugin.Sectigo
{
	public class SectigoConfig
	{
		public SectigoConfig()
		{
			
		}

		[JsonProperty("ApiEndpoint")]
		public string ApiEndpoint { get; set; }

		[JsonProperty("AuthType")]
		public string AuthenticationType { get; set; }

		[JsonProperty("CustomerUri")]
		public string CustomerUri { get; set; }

		[JsonProperty("Username")]
		public string Username { get; set; }

		[JsonProperty("Password")]
		public string Password { get; set; }

		[JsonProperty("PickupRetries")]
		public int PickupRetries { get; set; }

		[JsonProperty("PickupDelay")]
		public int PickupDelayInSeconds { get; set; }

		[JsonProperty("PageSize")]
		public int PageSize { get; set; }

		[JsonProperty("ExternalRequestorFieldName")]
		public string ExternalRequestorFieldName { get; set; }

		[JsonProperty("SyncFilterProfileId")]
		public string SyncFilterProfileId { get; set; }

		[JsonProperty("ForceCompleteSync", DefaultValueHandling = DefaultValueHandling.Populate)]
		public bool ForceCompleteSync { get; set; } = false;

		[JsonProperty("Enabled")]
		public bool Enabled { get; set; } = true;

		[JsonProperty("ClientCertificate")]
		public ClientCertificate Certificate { get; set; }
	}

	public class ClientCertificate
	{
		public string StoreName { get; set; }
		public string StoreLocation { get; set; }
		public string Thumbprint { get; set; }
		public string CertificatePath { get; set; }
		public string CertificatePassword { get; set; }
	}
}
