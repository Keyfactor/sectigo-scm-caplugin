<h1 align="center" style="border-bottom: none">
    Sectigo Certificate Manager   Gateway AnyCA Gateway REST Plugin
</h1>

<p align="center">
  <!-- Badges -->
<img src="https://img.shields.io/badge/integration_status-production-3D1973?style=flat-square" alt="Integration Status: production" />
<a href="https://github.com/Keyfactor/sectigo-scm-caplugin/releases"><img src="https://img.shields.io/github/v/release/Keyfactor/sectigo-scm-caplugin?style=flat-square" alt="Release" /></a>
<img src="https://img.shields.io/github/issues/Keyfactor/sectigo-scm-caplugin?style=flat-square" alt="Issues" />
<img src="https://img.shields.io/github/downloads/Keyfactor/sectigo-scm-caplugin/total?style=flat-square&label=downloads&color=28B905" alt="GitHub Downloads (all assets, all releases)" />
</p>

<p align="center">
  <!-- TOC -->
  <a href="#support">
    <b>Support</b>
  </a> 
  ·
  <a href="#requirements">
    <b>Requirements</b>
  </a>
  ·
  <a href="#installation">
    <b>Installation</b>
  </a>
  ·
  <a href="#license">
    <b>License</b>
  </a>
  ·
  <a href="https://github.com/orgs/Keyfactor/repositories?q=anycagateway">
    <b>Related Integrations</b>
  </a>
</p>


The Sectigo AnyCA Gateway REST plugin extends the capabilities of the Sectigo Certificate Manager to Keyfactor Command via the Keyfactor AnyCA Gateway REST. The plugin represents a fully featured AnyCA REST Plugin with the following capabilies:
* SSL Certificate Synchronization
    * Sync can be filtered by any available SSL Certificate List filter defined by the Cert Manager API
    * All Sync jobs are treated as a full sync because the Cert Manager API does not allow for filtering based on a date/time stamp
    * Certificates will only syncronize once.  If a certificate is found based on Serial Number for the managed CA, and its status is unchanged, it will be skipped for subsequent syncs to minimize impact on Cert Manager API load
* SSL Certificate Enrollment
   * Note about organizations.  The organization for enrollment is selected based on the Organization subject field, as well as any Department specified in the template configuration. If a department is specified, and that department exists within the organization and is valid for issuing certs, the department ID will be used. If no department is specified, the organization ID will be used if the organization is valid for issuing certs. If the organization/department are not valid for issuing certs, the enrollment will fail, as that is a required field for Sectigo.
* SSL Certificate Revocation

## Compatibility

The Sectigo Certificate Manager   Gateway AnyCA Gateway REST plugin is compatible with the Keyfactor AnyCA Gateway REST 24.2.0 and later.

## Support
The Sectigo Certificate Manager   Gateway AnyCA Gateway REST plugin is supported by Keyfactor for Keyfactor customers. If you have a support issue, please open a support ticket with your Keyfactor representative. If you have a support issue, please open a support ticket via the Keyfactor Support Portal at https://support.keyfactor.com. 

> To report a problem or suggest a new feature, use the **[Issues](../../issues)** tab. If you want to contribute actual bug fixes or proposed enhancements, use the **[Pull requests](../../pulls)** tab.

## Requirements

For each Organization/Department you plan on using through the gateway, in your Sectigo portal, go to that Organization, select Certificate Settings -> SSL Certificates, and check the "Enable Web/REST API" checkbox.  
In addition, for the admin account you plan to use, make sure it has the API admin type selected in the portal.

## Installation

1. Install the AnyCA Gateway REST per the [official Keyfactor documentation](https://software.keyfactor.com/Guides/AnyCAGatewayREST/Content/AnyCAGatewayREST/InstallIntroduction.htm).

2. On the server hosting the AnyCA Gateway REST, download and unzip the latest [Sectigo Certificate Manager   Gateway AnyCA Gateway REST plugin](https://github.com/Keyfactor/sectigo-scm-caplugin/releases/latest) from GitHub.

3. Copy the unzipped directory (usually called `net6.0`) to the Extensions directory:

    ```shell
    Program Files\Keyfactor\AnyCA Gateway\AnyGatewayREST\net6.0\Extensions
    ```

    > The directory containing the Sectigo Certificate Manager   Gateway AnyCA Gateway REST plugin DLLs (`net6.0`) can be named anything, as long as it is unique within the `Extensions` directory.

4. Restart the AnyCA Gateway REST service.

5. Navigate to the AnyCA Gateway REST portal and verify that the Gateway recognizes the Sectigo Certificate Manager   Gateway plugin by hovering over the ⓘ symbol to the right of the Gateway on the top left of the portal.

## Configuration

1. Follow the [official AnyCA Gateway REST documentation](https://software.keyfactor.com/Guides/AnyCAGatewayREST/Content/AnyCAGatewayREST/AddCA-Gateway.htm) to define a new Certificate Authority, and use the notes below to configure the **Gateway Registration** and **CA Connection** tabs:

    * **Gateway Registration**

        In order to enroll for certificates the Keyfactor Command server must trust the trust chain. Once you set your Root and/or Subordinate CA in your Sectigo account, make sure to download and import the certificate chain into the Command Server certificate store

    * **CA Connection**

        Populate using the configuration fields collected in the [requirements](#requirements) section.

        * **ApiEndpoint** - The Sectigo API endpoint to connect to. There are a few possible values, depending on your Sectigo account configuration. NOTE: If doing Certificate Auth, the endpoint should end in /private/ 
        * **CustomerUri** - This is a static value that represents the Sectigo account name. This can be found as part of the portal login URL. Ex: https://hard.cert-manager.com/customer/{CustomerUri} 
        * **AuthType** - This value must be either Password or Certificate. It will determine which credentials are used to connect to the API. NOTE: Certificate Auth will not work properly if there is a proxy doing TLS inspection. 
        * **Username** - This is the username associated with the API login and will determine the security role in the Certificate Manager platform. 
        * **Password** - If AuthType is set to Password, this is the password associated with the API login. Ignored for Certificate AuthType. 
        * **ClientCertificate** - If AuthType is set to Certificate, this is the certificate the Gateway will use to authenticate to the API. 
        * **PickupRetries** - This setting determines the number of times the service will attempt to download a certificate after successful enrollment. If the certificate cannot be downloaded during this period it will be picked up during the next sync. 
        * **PickupDelay** - This is the number of seconds between retries. Be aware that the total # of retries times the number of seconds will be the maximum amount of time the Command portal will be occupied during enrollment. If the duration is too long, the request may timeout and cause unexpected results. 
        * **PageSize** - This is the number of records that will be processed per API call during a sync. 
        * **ExternalRequestorFieldName** - If you wish to be able to specify at enroll-time a requestor email address for enrollment notifications, first define a requestor field name in this setting. Afterwards, you can create a custom Enrollment Field in Command with that same name, and supply the email address in that enrollment field. If no custom requestor field is provided, the API will use the email address of the API user itself. 
        * **SyncFilterProfileId** - Comma-separated list of profile IDs to filter the sync on. If not provided, all certificates will be returned. 
        * **ForceCompleteSync** - By default, the sync only updates database records if the status of the certificate has changed. Set this to true to force all records to sync/update. 
        * **Enabled** - Flag to Enable or Disable gateway functionality. Disabling is primarily used to allow creation of the CA prior to configuration information being available. 

2. When defining templates, the product IDs are unique to your Sectigo account. Log in to your Sectigo portal and go to your product types, and you should be able to retrieve the ID numbers there.

3. Follow the [official Keyfactor documentation](https://software.keyfactor.com/Guides/AnyCAGatewayREST/Content/AnyCAGatewayREST/AddCA-Keyfactor.htm) to add each defined Certificate Authority to Keyfactor Command and import the newly defined Certificate Templates.

4. In Keyfactor Command (v12.3+), for each imported Certificate Template, follow the [official documentation](https://software.keyfactor.com/Core-OnPrem/Current/Content/ReferenceGuide/Configuring%20Template%20Options.htm) to define enrollment fields for each of the following parameters:

    * **MultiDomain** - This flag lets Keyfactor know if the certificate can contain multiple domain names. Depending on the setting, the SAN entries of the request will change to support Sectigo requirements. 
    * **Organization** - If the organization name is provided here, the Sectigo gateway will use that organization name in requests instead of whatever is in the O= field in the request subject. 
    * **Department** - If your Sectigo account is using department-level products, put the appropriate department name here. Previously, this was alternatively supplied in the OU= subject field, which is now deprecated. 



## License

Apache License 2.0, see [LICENSE](LICENSE).

## Related Integrations

See all [Keyfactor Any CA Gateways (REST)](https://github.com/orgs/Keyfactor/repositories?q=anycagateway).