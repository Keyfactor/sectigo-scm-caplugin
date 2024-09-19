using Keyfactor.AnyGateway.Extensions;
using Keyfactor.Extensions.CAPlugin.Sectigo.API;
using Keyfactor.Extensions.CAPlugin.Sectigo.Client;
using Keyfactor.Extensions.CAPlugin.Sectigo.Models;
using Keyfactor.Logging;
using Keyfactor.PKI;
using Keyfactor.PKI.Enums.EJBCA;

using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

using Org.BouncyCastle.Asn1.Cmp;
using Org.BouncyCastle.Pqc.Crypto.Lms;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using static Org.BouncyCastle.Math.EC.ECCurve;

namespace Keyfactor.Extensions.CAPlugin.Sectigo
{
	public class SectigoCAPlugin : IAnyCAPlugin
	{
		private SectigoConfig _config;
		private readonly ILogger _logger;
		private ICertificateDataReader _certificateDataReader;

		public SectigoCAPlugin()
		{
			_logger = LogHandler.GetClassLogger<SectigoCAPlugin>();
		}

		public void Initialize(IAnyCAPluginConfigProvider configProvider, ICertificateDataReader certificateDataReader)
		{
			_certificateDataReader = certificateDataReader;
			string rawConfig = JsonConvert.SerializeObject(configProvider.CAConnectionData);
			_config = JsonConvert.DeserializeObject<SectigoConfig>(rawConfig);
			if (_config.PageSize > 200)
			{
				_config.PageSize = 200; // Largest value allowed by API
			}
		}

		public async Task<EnrollmentResult> Enroll(string csr, string subject, Dictionary<string, string[]> san, EnrollmentProductInfo productInfo, RequestFormat requestFormat, EnrollmentType enrollmentType)
		{
			_logger.MethodEntry(LogLevel.Debug);
			_logger.LogInformation($"Begin {enrollmentType} enrollment for {subject}");
			try
			{
				_logger.LogDebug("Parse Subject for Common Name, Organization, and Org Unit");

				string commonName = ParseSubject(subject, "CN=", false);
				if (!string.IsNullOrEmpty(commonName))
				{
					_logger.LogTrace($"Common Name: {commonName}");
				}

				string orgStr = null;
				if (productInfo.ProductParameters.ContainsKey("Organization"))
				{
					if (!productInfo.ProductParameters.TryGetValue("Organization", out orgStr))
					{
						throw new Exception("Organization parameter could not be parsed, check configuration");
					}
				}

				if (string.IsNullOrEmpty(orgStr))
				{
					orgStr = ParseSubject(subject, "O=");
				}

				_logger.LogTrace($"Organization: {orgStr}");

				string ouStr = ParseSubject(subject, "OU=", false);

				string department = null;
				if (productInfo.ProductParameters.ContainsKey("Department"))
				{
					department = productInfo.ProductParameters["Department"];
					_logger.LogTrace($"Department: {department}");
				}
				var client = SectigoClient.InitializeClient(_config);
				var fieldList = Task.Run(async () => await client.ListCustomFields()).Result;
				var mandatoryFields = fieldList.CustomFields?.Where(f => f.mandatory);

				_logger.LogDebug("Check for mandatory custom fields");
				foreach (CustomField reqField in mandatoryFields)
				{
					_logger.LogTrace($"Checking product parameters for {reqField.name}");
					if (!productInfo.ProductParameters.ContainsKey(reqField.name))
					{
						_logger.MethodExit(LogLevel.Debug);
						throw new Exception($"Template {productInfo.ProductID} or Enrollment Fields do not contain a mandatory custom field value for of {reqField.name}");
					}
				}
				_logger.LogDebug($"Search for Organization by Name {orgStr}");

				int requestOrgId = 0;
				var org = Task.Run(async () => await GetOrganizationAsync(orgStr)).Result;
				if (org == null)
				{
					string err = $"Unable to find Organization by Name {orgStr} ";
					_logger.LogError($"{err}");
					throw new Exception(err);
				}

				if (!string.IsNullOrEmpty(department))
				{
					// If department is specified in the config for this product type, look up the department configuration

					if (org.departments == null || org.departments.Count == 0)
					{
						string err = $"Department {department} not found: no departments found in organization {orgStr}";
						_logger.LogError($"{err}");
						throw new Exception(err);
					}

					Department dep = org.departments.Where(d => d.name.Equals(department, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

					if (dep == null)
					{
						string err = $"{department} does not exist as a department of {orgStr}. Please verify configuration";
						_logger.LogError($"{err}");
						throw new Exception(err);
					}

					_logger.LogDebug($"Retrieving details of department {department}");

					var orgDetails = Task.Run(async () => await client.GetOrganizationDetails(dep.id)).Result;

					if (orgDetails.CertTypes == null || orgDetails.CertTypes.Count == 0)
					{
						string err = $"Department {department} does not contain a valid certificate type configuration. Please verify account configuration.";
						_logger.LogError($"{err}");
						throw new Exception(err);
					}

					_logger.LogDebug($"Department {dep.name} is valid. Using ID {dep.id} for request");
					requestOrgId = dep.id;
				}
				else
				{
					// If no department is specified, look up the config of the organization itself

					_logger.LogDebug($"Retrieving details of organization {orgStr}");
					var orgDetails = Task.Run(async () => await client.GetOrganizationDetails(org.id)).Result;

					if (orgDetails.CertTypes == null || orgDetails.CertTypes.Count == 0)
					{
						string err = $"Organization {orgStr} does not contain a valid certificate type configuration, and no department was specified.Please verify account configuration.";
						_logger.LogError($"{err}");
						if (!string.IsNullOrEmpty(ouStr))
						{
							_logger.LogError("NOTE: Organizational Unit subject field has been deprecated. Department names must now be specified in the gateway template configuration. See documentation for details.");
						}
						throw new Exception(err);
					}

					_logger.LogDebug($"Organization {org.name} is valid. Using ID {org.id} for request");
					requestOrgId = org.id;
				}

				//Check if SAN matches the SUBJECT CN when multidomain = false (single domain cert).
				//If true, we need to send empty san array. if different, join array (remove if one?)
				bool isMultiDomain = bool.Parse(productInfo.ProductParameters["MultiDomain"]);
				string sanList = ParseSanList(san, isMultiDomain, commonName);

				var enrollmentProfile = Task.Run(async () => await GetProfile(int.Parse(productInfo.ProductID))).Result;
				if (enrollmentProfile != null)
				{
					_logger.LogTrace($"Found {enrollmentProfile.name} profile for enroll request");
				}

				int sslId;
				string priorSn = string.Empty;
				Certificate newCert = null;

				switch (enrollmentType)
				{
					case EnrollmentType.New:
					case EnrollmentType.Reissue:
					case EnrollmentType.Renew:
					case EnrollmentType.RenewOrReissue:
						string comment = "";
						if (productInfo.ProductParameters.ContainsKey("Keyfactor-Requester"))
						{
							comment = $"CERTIFICATE_REQUESTOR: {productInfo.ProductParameters["Keyfactor-Requester"]}";
						}
						EnrollRequest request = new EnrollRequest
						{
							csr = csr,
							orgId = requestOrgId,
							term = Task.Run(async () => await GetProfileTerm(int.Parse(productInfo.ProductID))).Result,
							certType = enrollmentProfile.id,
							//External requestor is expected to be an email. Use config to pull the enrollment field or send blank
							//sectigo will default to the account (API account) making the request.
							externalRequester = GetExternalRequestor(productInfo),
							numberServers = 1,
							serverType = -1,
							subjAltNames = sanList,//,
							comments = comment
						};

						_logger.LogDebug($"Submit {enrollmentType} request");
						var jsonReq = JsonConvert.SerializeObject(request, Formatting.Indented);
						_logger.LogDebug($"Request object: {jsonReq}");
						sslId = Task.Run(async () => await client.Enroll(request)).Result;
						newCert = Task.Run(async () => await client.GetCertificate(sslId)).Result;
						_logger.LogDebug($"Enrolled for Certificate {newCert.CommonName} (ID: {newCert.Id}) | Status: {newCert.status}. Attempt to Pickup Certificate.");
						break;

					default:
						throw new Exception($"Unsupported enrollment type {enrollmentType}");
				}

				return await PickUpEnrolledCertificate(newCert);
			}
			catch (HttpRequestException httpEx)
			{
				_logger.LogError($"Enrollment Failed due to a HTTP error: {httpEx.Message}");
				throw new Exception(httpEx.Message);
			}
			catch (Exception ex)
			{
				_logger.LogError($"Enrollment Failed with the following error: {ex.Message}");
				string retError = ex.Message;
				if (ex.InnerException != null)
				{
					_logger.LogError($"Inner Exception Message: {ex.InnerException.Message}");
					retError = ex.InnerException.Message;
				}

				throw new Exception(retError);
			}
		}

		public Dictionary<string, PropertyConfigInfo> GetCAConnectorAnnotations()
		{
			return new Dictionary<string, PropertyConfigInfo>()
			{
				[Constants.Config.API_ENDPOINT] = new PropertyConfigInfo()
				{
					Comments = "The Sectigo API endpoint to connect to. There are a few possible values, depending on your Sectigo account configuration. NOTE: If doing Certificate Auth, the endpoint should end in /private/",
					Hidden = false,
					DefaultValue = "https://hard.cert-manager.com/",
					Type = "String"
				},
				[Constants.Config.CUSTOMER_URI] = new PropertyConfigInfo()
				{
					Comments = "This is a static value that represents the Sectigo account name. This can be found as part of the portal login URL. Ex: https://hard.cert-manager.com/customer/{CustomerUri}",
					Hidden = false,
					DefaultValue = "",
					Type = "String"
				},
				[Constants.Config.AUTH_TYPE] = new PropertyConfigInfo()
				{
					Comments = "This value must be either Password or Certificate. It will determine which credentials are used to connect to the API. NOTE: Certificate Auth will not work properly if there is a proxy doing TLS inspection.",
					Hidden = false,
					DefaultValue = "Password",
					Type = "String"
				},
				[Constants.Config.USERNAME] = new PropertyConfigInfo()
				{
					Comments = "This is the username associated with the API login and will determine the security role in the Certificate Manager platform.",
					Hidden = false,
					DefaultValue = "",
					Type = "String"
				},
				[Constants.Config.PASSWORD] = new PropertyConfigInfo()
				{
					Comments = "If AuthType is set to Password, this is the password associated with the API login. Ignored for Certificate AuthType.",
					Hidden = true,
					DefaultValue = "",
					Type = "String"
				},
				[Constants.Config.CLIENT_CERTIFICATE] = new PropertyConfigInfo()
				{
					Comments = "If AuthType is set to Certificate, this is the certificate the Gateway will use to authenticate to the API.",
					Hidden = false,
					DefaultValue = "",
					Type = "ClientCertificate"
				},
				[Constants.Config.PICKUP_RETRIES] = new PropertyConfigInfo()
				{
					Comments = "This setting determines the number of times the service will attempt to download a certificate after successful enrollment. If the certificate cannot be downloaded during this period it will be picked up during the next sync.",
					Hidden = false,
					DefaultValue = 5,
					Type = "Number"
				},
				[Constants.Config.PICKUP_DELAY] = new PropertyConfigInfo()
				{
					Comments = "This is the number of seconds between retries. Be aware that the total # of retries times the number of seconds will be the maximum amount of time the Command portal will be occupied during enrollment. If the duration is too long, the request may timeout and cause unexpected results.",
					Hidden = false,
					DefaultValue = 10,
					Type = "Number"
				},
				[Constants.Config.PAGE_SIZE] = new PropertyConfigInfo()
				{
					Comments = "This is the number of records that will be processed per API call during a sync.",
					Hidden = false,
					DefaultValue = 25,
					Type = "Number"
				},
				[Constants.Config.EXTERNAL_REQUESTOR_FIELD_NAME] = new PropertyConfigInfo()
				{
					Comments = "If you wish to be able to specify at enroll-time a requestor email address for enrollment notifications, first define a requestor field name in this setting. Afterwards, you can create a custom Enrollment Field in Command with that same name, and supply the email address in that enrollment field. If no custom requestor field is provided, the API will use the email address of the API user itself.",
					Hidden = false,
					DefaultValue = "",
					Type = "String"
				},
				[Constants.Config.SYNC_FILTER_PROFILE_ID] = new PropertyConfigInfo()
				{
					Comments = "Comma-separated list of profile IDs to filter the sync on. If not provided, all certificates will be returned.",
					Hidden = false,
					DefaultValue = "",
					Type = "String"
				},
				[Constants.Config.FORCE_COMPLETE_SYNC] = new PropertyConfigInfo()
				{
					Comments = "By default, the sync only updates database records if the status of the certificate has changed. Set this to true to force all records to sync/update.",
					Hidden = false,
					DefaultValue = false,
					Type = "Boolean"
				},
				[Constants.Config.ENABLED] = new PropertyConfigInfo()
				{
					Comments = "Flag to Enable or Disable gateway functionality. Disabling is primarily used to allow creation of the CA prior to configuration information being available.",
					Hidden = false,
					DefaultValue = true,
					Type = "Boolean"
				}
			}; 
		}

		public List<string> GetProductIds()
		{
			var profileIds = Task.Run(async () => await GetProfileIds()).Result;
			return profileIds.ConvertAll<string> (x => x.ToString ());
		}

		public async Task<AnyCAPluginCertificate> GetSingleRecord(string caRequestID)
		{
			_logger.MethodEntry(LogLevel.Debug);

			_logger.LogTrace($"Get Single Certificate Detail from Sectigo (sslId: {caRequestID})");
			int sslId = int.Parse(caRequestID.Split('-')[0]);

			var client = SectigoClient.InitializeClient(_config);
			var singleCert = Task.Run(async () => await client.GetCertificate(sslId)).Result;
			_logger.LogTrace($"{singleCert.CommonName} ({singleCert.status}) retrieved from Sectigo.");

			//Pending external validation, cannot download certificate data from API
			if (ConvertToKeyfactorStatus(singleCert.status, sslId) == (int)EndEntityStatus.EXTERNALVALIDATION || ConvertToKeyfactorStatus(singleCert.status, sslId) == (int)EndEntityStatus.REVOKED)
			{
				return new AnyCAPluginCertificate()
				{
					CARequestID = caRequestID,
					ProductID = singleCert.CertType.id.ToString(),
					Status = ConvertToKeyfactorStatus(singleCert.status, sslId),
					RevocationDate = singleCert.revoked ?? DateTime.UtcNow
				};
			}

			var certData = PickupSingleCert(sslId, singleCert.CommonName);
			if (certData != null)
			{
				_logger.MethodExit(LogLevel.Debug);
				return new AnyCAPluginCertificate()
				{
					CARequestID = caRequestID,
					Certificate = Convert.ToBase64String(certData.GetRawCertData()),
					ProductID = singleCert.CertType.id.ToString(),
					Status = ConvertToKeyfactorStatus(singleCert.status, sslId),
					RevocationReason = ConvertToKeyfactorStatus(singleCert.status, sslId) == 21 ? 0 : 0xffffff,
					RevocationDate = singleCert.revoked ?? DateTime.UtcNow
				};
			}

			throw new Exception("Failed to download certificate data from Sectigo.");

		}

		public Dictionary<string, PropertyConfigInfo> GetTemplateParameterAnnotations()
		{
			return new Dictionary<string, PropertyConfigInfo>()
			{
				[Constants.Config.MULTIDOMAIN] = new PropertyConfigInfo()
				{
					Comments = "This flag lets Keyfactor know if the certificate can contain multiple domain names. Depending on the setting, the SAN entries of the request will change to support Sectigo requirements.",
					Hidden = false,
					DefaultValue = true,
					Type = "Boolean"
				},
				[Constants.Config.ORGANIZATION] = new PropertyConfigInfo()
				{
					Comments = "If the organization name is provided here, the Sectigo gateway will use that organization name in requests instead of whatever is in the O= field in the request subject.",
					Hidden = false,
					DefaultValue = "",
					Type = "String"
				},
				[Constants.Config.DEPARTMENT] = new PropertyConfigInfo()
				{
					Comments = "If your Sectigo account is using department-level products, put the appropriate department name here. Previously, this was alternatively supplied in the OU= subject field, which is now deprecated.",
					Hidden = false,
					DefaultValue = "",
					Type = "String"
				}
			};
		}

		public async Task Ping()
		{
			_logger.MethodEntry(LogLevel.Trace);
			if (!_config.Enabled)
			{
				_logger.LogWarning($"The CA is currently in the Disabled state. It must be Enabled to perform operations. Skipping connectivity test...");
				_logger.MethodExit(LogLevel.Trace);
				return;
			}

			try
			{
				_logger.LogDebug("Attempting to ping Sectigo API");
				var client = SectigoClient.InitializeClient(_config);
				_ = Task.Run(async () => await client.ListOrganizations()).Result;
			}
			catch (Exception ex)
			{
				_logger.LogError($"There was an error contacting Sectigo: {ex.Message}.");
				throw new Exception($"There was an error contacting Sectigo:  {ex.Message}.", ex);
			}
		}

		public async Task<int> Revoke(string caRequestID, string hexSerialNumber, uint revocationReason)
		{
			_logger.MethodEntry(LogLevel.Debug);

			try
			{
				var client = SectigoClient.InitializeClient(_config);
				var response = Task.Run(async () => await client.RevokeSslCertificateById(int.Parse(caRequestID), RevokeReasonToString(revocationReason))).Result;

				_logger.MethodExit(LogLevel.Debug);
				if (response)//will throw an exception if false
				{
					return (int)EndEntityStatus.REVOKED;//revoked
				}
				return -1;
			}
			catch (Exception ex)
			{
				throw new Exception($"Unable to revoke certificate with request ID {caRequestID}. Error: {ex.Message}", ex);
			}
		}

		public async Task Synchronize(BlockingCollection<AnyCAPluginCertificate> blockingBuffer, DateTime? lastSync, bool fullSync, CancellationToken cancelToken)
		{
			_logger.MethodEntry(LogLevel.Debug);

			Task producerTask = null;
			CancellationTokenSource newCancelToken = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);

			try
			{
				var certsToAdd = new BlockingCollection<Certificate>(100);
				_logger.LogInformation($"Begin Paging Certificate List");
				int pageSize = 25;
				if (_config.PageSize > 0)
				{
					pageSize = _config.PageSize;
				}

				var filter = new Dictionary<string, string[]>();
				if (!string.IsNullOrEmpty(_config.SyncFilterProfileId))
				{
					string[] filterProfileIds = _config.SyncFilterProfileId.Split(',');
					filter.Add("sslTypeId", filterProfileIds);
				}
				var client = SectigoClient.InitializeClient(_config);
				producerTask = client.CertificateListProducer(certsToAdd, newCancelToken.Token, _config.PageSize, filter);

				foreach (Certificate certToAdd in certsToAdd.GetConsumingEnumerable())
				{
					if (cancelToken.IsCancellationRequested)
					{
						_logger.LogWarning($"Task was canceled. Stopping Synchronize task.");
						blockingBuffer.CompleteAdding();
						break;
					}

					if (producerTask.Exception != null)
					{
						_logger.LogError($"Synchronize task failed with the following message: {producerTask.Exception.Flatten().Message}");
						throw producerTask.Exception.Flatten();
					}

					string dbCertId = null;
					int dbCertStatus = -1;
					//serial number is blank on certs that have not been issued (awaiting approval)
					if (!String.IsNullOrEmpty(certToAdd.SerialNumber))
					{
						dbCertId = _certificateDataReader.GetRequestIDBySerialNumber(certToAdd.SerialNumber).Result;
						if (!string.IsNullOrEmpty(dbCertId))
						{
							dbCertStatus = _certificateDataReader.GetStatusByRequestID(dbCertId).Result;
						}
					}


					int syncReqId = 0;
					string certData = string.Empty;
					if (!string.IsNullOrEmpty(dbCertId))
					{
						//are we syncing a reissued cert?
						//Reissued certs keep the same ID, but may have different data and cause index errors on sync
						//Removed reissued certs from enrollment, but may be some stragglers for legacy installs
						if (dbCertId.Contains('-'))
						{
							syncReqId = int.Parse(dbCertId.Split('-')[0]);
						}
						else
						{
							syncReqId = int.Parse(dbCertId);
						}

						//we found an existing cert from the DB by serial number.
						//This should already be in the DB so no need to sync again unless status changes or
						//admin has forced a complete sync
						if (dbCertStatus == ConvertToKeyfactorStatus(certToAdd.status, certToAdd.Id) && !_config.ForceCompleteSync)
						{
							_logger.LogTrace($"Certificate {certToAdd.CommonName} (Id: {certToAdd.Id}) already synced. Skipping.");
							continue;
						}
						var statusMessage = dbCertStatus == ConvertToKeyfactorStatus(certToAdd.status, certToAdd.Id) ? "not changed" : "changed";
						var forcedMessage = _config.ForceCompleteSync ? "Complete sync forced by configuration. " : string.Empty;

						_logger.LogTrace($"Certificate {certToAdd.CommonName} status {statusMessage}.{forcedMessage} Syncing certificate.");

					}

					//Download to get full certdata required for sync process
					_logger.LogTrace($"Attempt to Pickup Certificate {certToAdd.CommonName} (ID: {certToAdd.Id})");
					var certdataApi = Task.Run(async () => await client.PickupCertificate(certToAdd.Id, certToAdd.CommonName)).Result;
					if (certdataApi != null)
						certData = Convert.ToBase64String(certdataApi.GetRawCertData());

					if (certToAdd == null || String.IsNullOrEmpty(certToAdd.SerialNumber) || String.IsNullOrEmpty(certToAdd.CommonName) || String.IsNullOrEmpty(certData))
					{
						_logger.LogDebug($"Certificate Data unavailable for {certToAdd.CommonName} (ID: {certToAdd.Id}). Skipping ");
						continue;
					}

					AnyCAPluginCertificate caCertToAdd = new AnyCAPluginCertificate
					{
						CARequestID = syncReqId == 0 ? certToAdd.Id.ToString() : syncReqId.ToString(),
						ProductID = certToAdd.CertType.id.ToString(),
						Certificate = certData,
						Status = ConvertToKeyfactorStatus(certToAdd.status, certToAdd.Id),
						RevocationReason = ConvertToKeyfactorStatus(certToAdd.status, certToAdd.Id) == (int)EndEntityStatus.REVOKED ? 0 : 0xffffff,
						RevocationDate = certToAdd.revoked ?? DateTime.UtcNow
					};

					if (blockingBuffer.TryAdd(caCertToAdd, 50, cancelToken))
					{
						_logger.LogDebug($"Added {certToAdd.CommonName} (ID:{(syncReqId == 0 ? certToAdd.Id.ToString() : syncReqId.ToString())}) to queue for synchronization");
					}
					else
					{
						_logger.LogDebug($"Adding {certToAdd.CommonName} to queue was blocked. Retrying");
					}
				}
				_logger.LogInformation($"Adding Certificates to Queue is Complete.");
				blockingBuffer.CompleteAdding();
			}
			catch (Exception ex)
			{
				//gracefully exit so any certs added to queue prior to failure will still sync
				_logger.LogError($"Synchronize Task failed. {ex.Message} | {ex.StackTrace}");
				if (producerTask != null && !producerTask.IsCompleted)
				{
					newCancelToken.Cancel();
				}

				blockingBuffer.CompleteAdding();
			}
			_logger.MethodExit(LogLevel.Debug);

		}

		public async Task ValidateCAConnectionInfo(Dictionary<string, object> connectionInfo)
		{
			_logger.MethodEntry(LogLevel.Debug);
			//determine required fields
			//URL
			//Auth Type (Cert or UN PASSWORD)
			List<string> errors = new List<string>();
			errors.Add(ValidateConfigurationKey(connectionInfo, Constants.Config.API_ENDPOINT));
			errors.Add(ValidateConfigurationKey(connectionInfo, Constants.Config.AUTH_TYPE));

			_logger.MethodExit(LogLevel.Debug);
			if (errors.Any(s => !String.IsNullOrEmpty(s)))
			{
				throw new Exception(String.Join("|", errors.All(s => !String.IsNullOrEmpty(s))));
			}
		}

		private static string ValidateConfigurationKey(Dictionary<string, object> connectionInfo, string key)
		{
			if (!connectionInfo.TryGetValue(key, out object tempValue) && tempValue != null)
			{
				return $"{key} is a required configuration value";
			}

			return string.Empty;
		}

		public async Task ValidateProductInfo(EnrollmentProductInfo productInfo, Dictionary<string, object> connectionInfo)
		{
			_logger.MethodEntry(LogLevel.Debug);
			string rawConfig = JsonConvert.SerializeObject(connectionInfo);
			var parsedConfig = JsonConvert.DeserializeObject<SectigoConfig>(rawConfig);
			SectigoClient localClient = SectigoClient.InitializeClient(parsedConfig);

			var profileList = Task.Run(async () => await localClient.ListSslProfiles()).Result;
			if (profileList.SslProfiles.Where(p => p.id == int.Parse(productInfo.ProductID)).Count() == 0)
			{
				_logger.MethodExit(LogLevel.Debug);
				throw new Exception($"Unable to find SSl Profile with ID {productInfo.ProductID}");
			}
			_logger.MethodExit(LogLevel.Debug);
		}

		private async Task<Organization> GetOrganizationAsync(string orgName)
		{
			var client = SectigoClient.InitializeClient(_config);
			var orgList = await client.ListOrganizations();
			return orgList.Organizations.Where(x => x.name.ToLower().Equals(orgName.ToLower())).FirstOrDefault();
		}

		private async Task<int> GetProfileTerm(int profileId)
		{
			var client = SectigoClient.InitializeClient(_config);
			var profileList = await client.ListSslProfiles();
			return profileList.SslProfiles.Where(x => x.id == profileId).FirstOrDefault().terms[0];
		}

		private async Task<Profile> GetProfile(int profileId)
		{
			var client = SectigoClient.InitializeClient(_config);
			var profileList = await client.ListSslProfiles();
			return profileList.SslProfiles.Where(x => x.id == profileId).FirstOrDefault();
		}

		private async Task<List<int>> GetProfileIds()
		{
			var client = SectigoClient.InitializeClient(_config);
			var profileList = await client.ListSslProfiles();
			return profileList.SslProfiles.Select(x => x.id).ToList();
		}

		private string GetExternalRequestor(EnrollmentProductInfo productInfo)
		{
			if (!String.IsNullOrEmpty(_config.ExternalRequestorFieldName))
			{
				if (!String.IsNullOrEmpty(productInfo.ProductParameters[_config.ExternalRequestorFieldName]))
				{
					return productInfo.ProductParameters[_config.ExternalRequestorFieldName];
				}
			}
			return string.Empty;
		}

		private async Task<EnrollmentResult> PickUpEnrolledCertificate(Certificate sslCert)
		{
			if (sslCert.status.Equals("Issued", StringComparison.InvariantCultureIgnoreCase) ||
				sslCert.status.Equals("Applied", StringComparison.InvariantCultureIgnoreCase))
			{
				return await PickUpEnrolledCertificate(sslCert.Id, sslCert.CommonName);
			}

			_logger.LogInformation($"Certificate {sslCert.CommonName} (ID: {sslCert.Id}) has not been issued. Certificate will be picked up during synchronization after approval.");
			return new EnrollmentResult
			{
				CARequestID = $"{sslCert.Id}",
				Status = (int)EndEntityStatus.EXTERNALVALIDATION,
				StatusMessage = "Certificate requires approval. Certificate will be picked up during synchronization after approval."
			};
		}

		private async Task<EnrollmentResult> PickUpEnrolledCertificate(int sslId, string subject)
		{
			_logger.MethodEntry(LogLevel.Debug);
			int retryCounter = 0;
			Thread.Sleep(5 * 1000);//small static delay as an attempt to avoid retries all together
			while (retryCounter < _config.PickupRetries)
			{
				_logger.LogDebug($"Try number {retryCounter + 1} to pickup enrolled certificate");
				var client = SectigoClient.InitializeClient(_config);
				var certificate = Task.Run(async () => await client.PickupCertificate(sslId, subject)).Result;
				if (certificate != null && !String.IsNullOrEmpty(certificate.Subject))
				{
					_logger.LogInformation($"Successfully enrolled for certificate {certificate.Subject}");
					_logger.MethodExit(LogLevel.Debug);
					return new EnrollmentResult
					{
						CARequestID = $"{sslId}",
						Certificate = Convert.ToBase64String(certificate.GetRawCertData()),
						Status = (int)EndEntityStatus.GENERATED,
						StatusMessage = $"Successfully enrolled for certificate {certificate.Subject}"
					};
				}
				Thread.Sleep(_config.PickupDelayInSeconds * 1000);//convert seconds to ms for delay.
				retryCounter++;
			}

			_logger.MethodExit(LogLevel.Debug);
			return new EnrollmentResult
			{
				CARequestID = $"{sslId}",
				Status = (int)EndEntityStatus.EXTERNALVALIDATION,
				StatusMessage = "Failed to pickup certificate. Check SCM portal to determine if addtional approval is required"
			};
		}

		public X509Certificate2 PickupSingleCert(int sslId, string subject)
		{
			_logger.MethodEntry(LogLevel.Debug);
			int retryCounter = 0;
			Thread.Sleep(5 * 1000);//small static delay as an attempt to avoid retries all together
			while (retryCounter < _config.PickupRetries)
			{
				_logger.LogDebug($"Try number {retryCounter + 1} to pickup single certificate");
				var client = SectigoClient.InitializeClient(_config);
				var certificate = Task.Run(async () => await client.PickupCertificate(sslId, subject)).Result;
				if (certificate != null && !String.IsNullOrEmpty(certificate.Subject))
				{
					_logger.LogInformation($"Successfully picked up certificate {certificate.Subject}");
					_logger.MethodExit(LogLevel.Debug);
					return certificate;
				}
				Thread.Sleep(_config.PickupDelayInSeconds * 1000);//convert seconds to ms for delay.
				retryCounter++;
			}

			_logger.MethodExit(LogLevel.Debug);
			return null;
		}

		private static string ParseSubject(string subject, string rdn, bool required = true)
		{
			string escapedSubject = subject.Replace("\\,", "|");
			string rdnString = escapedSubject.Split(',').ToList().Where(x => x.Contains(rdn)).FirstOrDefault();

			if (!string.IsNullOrEmpty(rdnString))
			{
				return rdnString.Replace(rdn, "").Replace("|", ",").Trim();
			}
			else if (required)
			{
				throw new Exception($"The request is missing a {rdn} value");
			}
			else
			{
				return null;
			}
		}

		private static string ParseSanList(Dictionary<string, string[]> san, bool multiDomain, string commonName)
		{
			string sanList = string.Empty;
			List<string> allSans = new List<string>();
			foreach (var k in san.Keys)
			{
				allSans.AddRange(san[k].ToList());
			}

			if (!multiDomain)
			{
				if (!string.IsNullOrEmpty(commonName) && allSans.Contains(commonName) && allSans.Count() > 1)
				{
					List<string> sans = allSans.ToList();
					sans.Remove(commonName);
					sanList = string.Join(",", sans.ToArray());
				}
				else
				{
					List<string> sans = allSans.ToList();
					sanList = string.Join(",", sans.ToArray());
				}
			}
			else
			{
				if (!string.IsNullOrEmpty(commonName) && allSans.Contains(commonName))
				{
					List<string> sans = allSans.ToList();
					sans.Remove(commonName);
					sanList = string.Join(",", sans.ToArray());
				}
				else
				{
					List<string> sans = allSans.ToList();
					sanList = string.Join(",", sans.ToArray());
				}
			}
			return sanList;
		}

		private static int ConvertToKeyfactorStatus(string status, int sslId)
		{
			switch (status.ToUpper())
			{
				case "ISSUED":
				case "ENROLLED - PENDING DOWNLOAD":
				case "APPROVED":
				case "APPLIED":
				case "DOWNLOADED":
				case "EXPIRED":
					return (int)EndEntityStatus.GENERATED;

				case "REQUESTED":
				case "AWAITING APPROVAL":
				case "NOT ENROLLED":
				case "INIT":
					return (int)EndEntityStatus.EXTERNALVALIDATION;

				case "REVOKED":
					return (int)EndEntityStatus.REVOKED;

				case "INVALID":
				case "DECLINED":
				case "REJECTED":
					return (int)(EndEntityStatus.FAILED);

				case "ANY":
				default:
					throw new Exception($"Request ID {sslId} has unknown status {status}");
			}
		}
		public static string RevokeReasonToString(UInt32 revokeType)
		{
			switch (revokeType)
			{
				case 1:
					return "Compromised Key";

				case 2:
					return "CA Compromised";

				case 3:
					return "Affiliation Changed";

				case 4:
					return "Superseded";

				case 5:
					return "Cessation of Operation";

				case 6:
					return "Certificate Hold";

				default:
					return "Unspecified";
			}
		}
	}
}
