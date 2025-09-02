using Keyfactor.AnyGateway.Extensions;
using Keyfactor.Extensions.CAPlugin.Sectigo.API;
using Keyfactor.Extensions.CAPlugin.Sectigo.Models;
using Keyfactor.Logging;

using Microsoft.Extensions.Logging;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.CAPlugin.Sectigo.Client
{
	public class SectigoClient
	{
		private static ILogger Logger => LogHandler.GetClassLogger<SectigoClient>();

		private HttpClient RestClient { get; }

		public SectigoClient(HttpClient client)
		{
			RestClient = client;
		}

		public async Task<Certificate> GetCertificate(int sslId)
		{
			var response = await RestClient.GetAsync($"api/ssl/v1/{sslId}");
			return await ProcessResponse<Certificate>(response);
		}

		public async Task CertificateListProducer(BlockingCollection<Certificate> certs,
				CancellationToken cancelToken, int pageSize = 25, string filter = "")
		{
			int batchCount;
			int blockedCount;
			int totalCount = 0;

			List<Certificate> certificatePageToProcess;
			try
			{
				//Paging loop will iterate though the certificates until all certificates have been returned
				do
				{
					if (cancelToken.IsCancellationRequested)
					{
						certs.CompleteAdding();
						break;
					}

					int certIndex = totalCount > 0 ? (totalCount - 1) : 0;
					Logger.LogInformation($"Request Certificates at Position {certIndex} with Page Size {pageSize}");
					certificatePageToProcess = await PageCertificates(certIndex, pageSize, filter);
					Logger.LogDebug($"Found {certificatePageToProcess.Count} certificate to process");

					//Processing Loop will add and retry adding to queue until all certificates have been processed for a page
					batchCount = 0;
					blockedCount = 0;
					do
					{
						Certificate cert = certificatePageToProcess[batchCount];
						Logger.LogDebug($"Processing: {cert}");
						Certificate certDetails = null;
						try
						{
							if (certDetails == null)
								certDetails = await GetCertificate(cert.Id);
						}
						catch (Exception aEx)
						{
							Logger.LogError($"Error requesting certificate details. Skipping certificate. {aEx.Message}");
							batchCount++;
							continue;
						}

						if (certs.TryAdd(certDetails, 50, cancelToken))
						{
							batchCount++;
							totalCount++;
						}
						else
						{
							Logger.LogTrace($"Adding {cert.Id} to queue was blocked. Retry");
							blockedCount++;//TODO: If blocked count is excessive, should we skip?
						}
						certIndex++;
					}
					while (batchCount < certificatePageToProcess.Count);

					Logger.LogInformation($"Added {batchCount} certificates to queue for processing.");
				} while (certificatePageToProcess.Count == pageSize);//if the API returns less than we requested, we assume we have reached the end
			}
			catch (HttpRequestException hEx)
			{
				Logger.LogError($"Sync interrupted by HTTP Exception. {hEx.InnerException.Message}");
				certs.CompleteAdding();//Stops the consuming enumerable and sync will continue until the queue is empty
			}
			catch (Exception ex)
			{
				//fail gracefully and stop syncing.
				Logger.LogError($"Sync interrupted by General Exception. {ex.Message}");
				certs.CompleteAdding();//Stops the consuming enumerable and sync will continue until the queue is empty
			}
		}

		public async Task CertificateListProducer(BlockingCollection<Certificate> certs,
						CancellationToken cancelToken, int pageSize = 25, Dictionary<string, string[]> filter = null)
		{
			if (filter != null && filter.Count > 0)
			{
				//each kvp key = type, value each filter
				foreach (var s in filter)
				{
					foreach (var value in s.Value)
						await CertificateListProducer(certs, cancelToken, pageSize, $"{s.Key}={value}");
				}
			}
			else
			{
				// No filters
				await CertificateListProducer(certs, cancelToken, pageSize, "");
			}

			certs.CompleteAdding();
		}

		public async Task<List<Certificate>> PageCertificates(int position = 0, int size = 25, string filter = "")
		{
			string filterQueryString = string.IsNullOrEmpty(filter) ? string.Empty : $"&{filter}";
			Logger.LogTrace($"API Request: api/ssl/v1?position={position}&size={size}{filterQueryString}".TrimEnd());
			var response = await RestClient.GetAsync($"api/ssl/v1?position={position}&size={size}{filterQueryString}".TrimEnd());
			return await ProcessResponse<List<Certificate>>(response);
		}

		public async Task<bool> RevokeSslCertificateById(int sslId, int revcode, string revreason)
		{
			var data = new
			{
				reasonCode = revcode,
				reason = revreason
			};
			var response = await RestClient.PostAsJsonAsync($"api/ssl/v1/revoke/{sslId}", data);
			if (response.IsSuccessStatusCode)
			{
				return true;
			}
			var failedResp = ProcessResponse<RevocationResponse>(response).Result;
			return failedResp.IsSuccess;//Should throw an exception with error message from API
		}

		public async Task<ListOrganizationsResponse> ListOrganizations()
		{
			var response = await RestClient.GetAsync("api/organization/v1");
			if (response.IsSuccessStatusCode)
			{
				string responseContent = await response.Content.ReadAsStringAsync();
				Logger.LogTrace($"Raw Response: {responseContent}");
			}
			var orgsResponse = await ProcessResponse<List<Organization>>(response);

			return new ListOrganizationsResponse { Organizations = orgsResponse };
		}

		public async Task<OrganizationDetailsResponse> GetOrganizationDetails(int orgId)
		{
			var response = await RestClient.GetAsync($"api/organization/v1/{orgId}");
			if (response.IsSuccessStatusCode)
			{
				string responseContent = await response.Content.ReadAsStringAsync();
				Logger.LogTrace($"Raw Response: {responseContent}");
			}

			var orgDetailsResponse = await ProcessResponse<OrganizationDetailsResponse>(response);
			return orgDetailsResponse;
		}

		public async Task<ListPersonsResponse> ListPersons(int orgId)
		{
			int pageSize = 25;
			List<Person> responseList = new List<Person>();
			List<Person> partialList = new List<Person>();
			do
			{
				partialList = await PagePersons(orgId, responseList.Count - 1, pageSize);
				responseList.AddRange(partialList);
			}
			while (partialList.Count == pageSize);

			return new ListPersonsResponse() { Persons = responseList };
		}

		public async Task<ListCustomFieldsResponse> ListCustomFields()
		{
			var response = await RestClient.GetAsync("api/ssl/v1/customFields");
			return new ListCustomFieldsResponse { CustomFields = await ProcessResponse<List<CustomField>>(response) };
		}

		public async Task<ListSslProfilesResponse> ListSslProfiles(int? orgId = null)
		{
			string urlSuffix = string.Empty;
			if (orgId.HasValue)
			{
				urlSuffix = $"?organizationId={orgId}";
			}

			var response = await RestClient.GetAsync($"api/ssl/v1/types{urlSuffix}");
			return new ListSslProfilesResponse { SslProfiles = await ProcessResponse<List<Profile>>(response) };
		}

		public async Task<List<Person>> PagePersons(int orgId, int position = 0, int size = 25)
		{
			var response = await RestClient.GetAsync($"api/person/v1?position={position}&size={size}&organizationId={orgId}");
			return await ProcessResponse<List<Person>>(response);
		}

		public async Task<int> Enroll(EnrollRequest request)
		{
			try
			{
				var response = await RestClient.PostAsJsonAsync("api/ssl/v1/enroll", request);
				var enrollResponse = await ProcessResponse<EnrollResponse>(response);

				return enrollResponse.sslId;
			}
			catch (InvalidOperationException invalidOp)
			{
				throw new Exception($"Invalid Operation. {invalidOp.Message}|{invalidOp.StackTrace}", invalidOp);
			}
			catch (HttpRequestException httpEx)
			{
				throw new Exception($"HttpRequestException. {httpEx.Message}|{httpEx.StackTrace}", httpEx);
			}
			catch (Exception)
			{
				throw;
			}
		}

		public async Task<int> Renew(int sslId)
		{
			try
			{
				var response = await RestClient.PostAsJsonAsync($"api/ssl/v1/renewById/{sslId}", "");
				var renewResponse = await ProcessResponse<EnrollResponse>(response);

				return renewResponse.sslId;
			}
			catch (InvalidOperationException invalidOp)
			{
				throw new Exception($"Invalid Operation. {invalidOp.Message}|{invalidOp.StackTrace}");
			}
			catch (HttpRequestException httpEx)
			{
				throw new Exception($"HttpRequestException. {httpEx.Message}|{httpEx.StackTrace}");
			}
			catch (Exception)
			{
				throw;
			}
		}

		public async Task<X509Certificate2> PickupCertificate(int sslId, string subject)
		{
			var response = await RestClient.GetAsync($"api/ssl/v1/collect/{sslId}/x509CO");

			if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength > 0)
			{
				string pemChain = await response.Content.ReadAsStringAsync();

				string[] splitChain = pemChain.Replace("\r\n", string.Empty).Split(new string[] { "-----" }, StringSplitOptions.RemoveEmptyEntries);

				return new X509Certificate2(Convert.FromBase64String(splitChain[1]));
			}
			return null;
			//return new X509Certificate2();
		}

		public async Task Reissue(ReissueRequest request, int sslId)
		{
			var response = await RestClient.PostAsJsonAsync($"api/ssl/v1/replace/{sslId}", request);
			response.EnsureSuccessStatusCode();
		}

		#region Static Methods

		private static async Task<T> ProcessResponse<T>(HttpResponseMessage response)
		{
			if (response.IsSuccessStatusCode)
			{
				string responseContent = await response.Content.ReadAsStringAsync();
				return JsonConvert.DeserializeObject<T>(responseContent);
			}
			else
			{
				var error = JsonConvert.DeserializeObject<Error>(await response.Content.ReadAsStringAsync());
				throw new Exception($"{error.Code} | {error.Description}");
			}
		}

		public static SectigoClient InitializeClient(SectigoConfig config, ICertificateResolver certResolver)
		{
			Logger.MethodEntry(LogLevel.Debug);
		
			HttpClientHandler clientHandler = new HttpClientHandler();

			if (config.AuthenticationType.ToLower() == "certificate")
			{
				clientHandler.ClientCertificateOptions = ClientCertificateOption.Manual;
				Logger.LogTrace($"Cert info: \nSource: {config.Certificate.Source}\nThumb: {config.Certificate.Thumbprint}\nStoreName: {config.Certificate.StoreName}\nStoreLoc: {config.Certificate.StoreLocation}\nPath: {config.Certificate.CertificatePath}\nPass: {config.Certificate.CertificatePassword}\nImported: {config.Certificate.ImportedCertificate}\nImportedPass: {config.Certificate.ImportedCertificatePassword}");
				X509Certificate2 authCert = certResolver.ResolveCertificate(config.Certificate);
				if (authCert == null)
				{
					Logger.MethodExit(LogLevel.Debug);
					throw new Exception("AuthType set to Certificate, but no certificate found!");
				}

				Logger.LogDebug($"CERT DETAILS: \nSerial Number: {authCert.GetSerialNumberString}\nHas PK: {authCert.HasPrivateKey.ToString()}\nSubject: {authCert.Subject}");

				Logger.LogTrace("Checking for private key permissions.");
				try
				{
					//https://www.pkisolutions.com/accessing-and-using-certificate-private-keys-in-net-framework-net-core/
					_ = authCert.GetRSAPrivateKey();
					_ = authCert.GetDSAPrivateKey();
					_ = authCert.GetECDsaPrivateKey();
				}
				catch
				{
					throw new Exception("Unable to access the authentication certificate's private key.");
				}


				clientHandler.ClientCertificates.Add(authCert);
			}

			string apiEndpoint = config.ApiEndpoint;
			if (!apiEndpoint.EndsWith("/"))
			{
				apiEndpoint += "/";
			}

			HttpClient restClient = new HttpClient(clientHandler)
			{
				BaseAddress = new Uri(apiEndpoint)
			};

			restClient.DefaultRequestHeaders.Add(Constants.CUSTOMER_URI_KEY, config.CustomerUri);
			restClient.DefaultRequestHeaders.Add(Constants.CUSTOMER_LOGIN_KEY, config.Username);
			//Determine

			if (config.AuthenticationType.ToLower() == "password")
			{
				restClient.DefaultRequestHeaders.Add(Constants.CUSTOMER_PASSWORD_KEY, config.Password);
			}

			Logger.MethodExit(LogLevel.Debug);
			return new SectigoClient(restClient);
		}

		#endregion
	}
}
