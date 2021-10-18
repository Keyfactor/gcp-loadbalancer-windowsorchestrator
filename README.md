# GCP Load Balancer
## Orchestrator

The GCP Load Balancer Orchestrator allows for the management of Google Cloud Platform Load Balancer certificate stores.  Inventory, Management-Add, and Management-Remove functions are supported.  Also, re-binding to endpoints IS supported for certificate renewals (but NOT adding new certificates).  The orchestrator uses the Google Cloud Compute Engine API to manage stores.

<!-- add integration specific information below -->
*** 

# Introduction 
- TODO:

# Setting up GCP Cert Store Type

# Authentication
A service account is necessary for authentication to GCP.  The following are the required permissions:
- compute.sslCertificates.create
- compute.sslCertificates.delete
- compute.sslCertificates.list

The agent supports having credentials provided by the environment, environment variable, or passed manually from Keyfactor Command.  
You can read more about the first two options [here](https://cloud.google.com/docs/authentication/production#automatically).

To pass credentials from Keyfactor Command you need to first create a service account and then download a service account key.  
Instructions are [here](https://cloud.google.com/docs/authentication/production#manually).  
Remember to assign the appropriate role/permissions for the service account.
Afterwards inside Keyfactor Command copy and paste the contents of the service account key in the password field for the GCP Certificate Store Type.

# Supported Functionality
- Inventory, Management

# Not Implemented/Supported
- Binding

 ***

### License
[Apache](https://apache.org/licenses/LICENSE-2.0)
