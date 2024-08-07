using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.CAPlugin.Sectigo
{
	public static class Constants
	{
		public class Config
		{
			public const string API_ENDPOINT = "ApiEndpoint";
			public const string AUTH_TYPE = "AuthType";
			public const string CUSTOMER_URI = "CustomerUri";
			public const string USERNAME = "Username";
			public const string PASSWORD = "Password";
			public const string PICKUP_RETRIES = "PickupRetries";
			public const string PICKUP_DELAY = "PickupDelay";
			public const string PAGE_SIZE = "PageSize";
			public const string EXTERNAL_REQUESTOR_FIELD_NAME = "ExternalRequestorFieldName";
			public const string SYNC_FILTER_PROFILE_ID = "SyncFilterProfileId";
			public const string FORCE_COMPLETE_SYNC = "ForceCompleteSync";
			public const string ENABLED = "Enabled";
			public const string CLIENT_CERTIFICATE = "ClientCertificate";

			public const string MULTIDOMAIN = "MultiDomain";
			public const string ORGANIZATION = "Organization";
			public const string DEPARTMENT = "Department";
		}

		//headers for API client
		public static string CUSTOMER_URI_KEY => "customerUri";
		public static string CUSTOMER_LOGIN_KEY => "login";
		public static string CUSTOMER_PASSWORD_KEY => "password";

	}
}
