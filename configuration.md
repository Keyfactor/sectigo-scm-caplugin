## Overview

The Sectigo AnyCA Gateway REST plugin extends the capabilities of the Sectigo Certificate Manager to Keyfactor Command via the Keyfactor AnyCA Gateway REST. The plugin represents a fully featured AnyCA REST Plugin with the following capabilies:
* SSL Certificate Synchronization
    * Sync can be filtered by any available SSL Certificate List filter defined by the Cert Manager API
    * All Sync jobs are treated as a full sync because the Cert Manager API does not allow for filtering based on a date/time stamp
    * Certificates will only syncronize once.  If a certificate is found based on Serial Number for the managed CA, and its status is unchanged, it will be skipped for subsequent syncs to minimize impact on Cert Manager API load
* SSL Certificate Enrollment
   * Note about organizations.  The organization for enrollment is selected based on the Organization subject field, as well as any Department specified in the template configuration. If a department is specified, and that department exists within the organization and is valid for issuing certs, the department ID will be used. If no department is specified, the organization ID will be used if the organization is valid for issuing certs. If the organization/department are not valid for issuing certs, the enrollment will fail, as that is a required field for Sectigo.
* SSL Certificate Revocation

## Requirements

For each Organization/Department you plan on using through the gateway, in your Sectigo portal, go to that Organization, select Certificate Settings -> SSL Certificates, and check the "Enable Web/REST API" checkbox.  
In addition, for the admin account you plan to use, make sure it has the API admin type selected in the portal.

## Gateway Registration

In order to enroll for certificates the Keyfactor Command server must trust the trust chain. Once you set your Root and/or Subordinate CA in your Sectigo account, make sure to download and import the certificate chain into the Command Server certificate store

