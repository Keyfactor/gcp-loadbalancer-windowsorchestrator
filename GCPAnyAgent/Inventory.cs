//Copyright 2021 Keyfactor
//Licensed under the Apache License, Version 2.0 (the "License");
//you may not use this file except in compliance with the License.
//You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
//Unless required by applicable law or agreed to in writing, software
//distributed under the License is distributed on an "AS IS" BASIS,
//WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//See the License for the specific language governing permissions and
//limitations under the License.


using System;
using System.Collections.Generic;

using Keyfactor.Platform.Extensions.Agents;
using Keyfactor.Platform.Extensions.Agents.Enums;
using Keyfactor.Platform.Extensions.Agents.Delegates;
using Keyfactor.Platform.Extensions.Agents.Interfaces;

using CSS.Common.Logging;

using Google.Apis.Auth.OAuth2;
using Google.Apis.Compute.v1;
using Google.Apis.Services;
using Newtonsoft.Json;
using System.Threading.Tasks;

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Web;

using Data = Google.Apis.Compute.v1.Data;

namespace Keyfactor.Extensions.Orchestrator.GCP
{
    // The Inventory class implementes IAgentJobExtension and is meant to find all of the certificates in a given certificate store on a given server
    //  and return those certificates back to Keyfactor for storing in its database.  Private keys will NOT be passed back to Keyfactor Command 
    public class Inventory : LoggingClientBase, IAgentJobExtension
    {
        public string GetJobClass()
        {
            //Setting to "Inventory" makes this the entry point for all Inventory jobs
            return "Inventory";
        }

        public string GetStoreType()
        {
            //Value must match the Short Name of the corresponding Certificate Store Type in KF Command
            return "GCP";
        }

        public static GoogleCredential GetCredential(AnyJobConfigInfo config)
        {

            //Example Environment variable for Application Default
            //Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", @"C:\development\GCPAnyAgent\Tests\GCPCreds.json");
           
            //Example reading from File
            //GoogleCredential credential = GoogleCredential.FromFile("Keyfactor.Extensions.Orchestrator.GCP.Tests.GCPCreds.json");

            GoogleCredential credential;
            if (config.Server.Password != null) {
               credential = GoogleCredential.FromJson(config.Server.Password);
            } else
            {
                credential = Task.Run(() => GoogleCredential.GetApplicationDefaultAsync()).Result;
            }

            if (credential.IsCreateScopedRequired)
            {
                credential = credential.CreateScoped("https://www.googleapis.com/auth/cloud-platform");
            }
            return credential;
        }

        //Job Entry Point
        public AnyJobCompleteInfo processJob(AnyJobConfigInfo config, SubmitInventoryUpdate submitInventory, SubmitEnrollmentRequest submitEnrollmentRequest, SubmitDiscoveryResults sdr)
        {
            //METHOD ARGUMENTS...
            //config - contains context information passed from KF Command to this job run:
            //
            // config.Server.Username, config.Server.Password - credentials for orchestrated server - use to authenticate to certificate store server.
            //
            // config.Store.ClientMachine - server name or IP address of orchestrated server
            // config.Store.StorePath - location path of certificate store on orchestrated server
            // config.Store.StorePassword - if the certificate store has a password, it would be passed here
            //
            // config.Store.Properties - JSON object containing certain reserved values for Discovery or custom properties set up in the Certificate Store Type
            // config.Store.Properties.dirs.Value.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries) - Certificate Store Discovery Job Scheduler - Directories to search
            // config.Store.Properties.extensions.Value.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries) - Certificate Store Discovery Job Scheduler - Extensions
            // config.Store.Properties.ignoreddirs.Value.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries) - Certificate Store Discovery Job Scheduler - Directories to ignore
            // config.Store.Properties.patterns.Value.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries) - Certificate Store Discovery Job Scheduler - File name patterns to match
            //
            // config.Job.EntryContents - PKCS12 or PEM representation of certificate being added (Management job only)
            // config.Job.Alias - optional string value of certificate alias (used in java keystores and some other store types)
            // config.Job.OpeerationType - enumeration representing function with job type.  Used only with Management jobs where this value determines whether the Management job is a CREATE/ADD/REMOVE job.
            // config.Job.Overwrite - Boolean value telling the AnyAgent whether to overwrite an existing certificate in a store.  How you determine whether a certificate is "the same" as the one provided is AnyAgent implementation dependent
            // config.Job.PfxPassword - For a Management Add job, if the certificate being added includes the private key (therefore, a pfx is passed in config.Job.EntryContents), this will be the password for the pfx.

            //NLog Logging to c:\CMS\Logs\CMS_Agent_Log.txt
            Logger.Debug($"Begin Inventory...");

            //List<AgentCertStoreInventoryItem> is the collection that the interface expects to return from this job.  It will contain a collection of certificates found in the store along with other information about those certificates
            List<AgentCertStoreInventoryItem> inventoryItems = new List<AgentCertStoreInventoryItem>();

            try
            {
                //Code logic to:
                // 1) Connect to the orchestrated server (config.Store.ClientMachine) containing the certificate store to be inventoried (config.Store.StorePath)

                ComputeService computeService = new ComputeService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = GetCredential(config),
                    ApplicationName = "Google-ComputeSample/0.1",
                });

                Dictionary<string, string> storeProperties = JsonConvert.DeserializeObject<Dictionary<string, string>>((string)config.Store.Properties);

                string project = config.Store.StorePath; // storeProperties["project"];

                Dictionary<string, string> existing = config.Store.Inventory.ToDictionary(i => i.Alias, i => i.Thumbprints[0]);

                // 2) Custom logic to retrieve certificates from certificate store.

                SslCertificatesResource.ListRequest request = computeService.SslCertificates.List(project);

                Data.SslCertificateList response;
                do
                {
                    // To execute asynchronously in an async method, replace `request.Execute()` as shown:
                    response = request.Execute();
                    // response = await request.ExecuteAsync();

                    if (response.Items == null)
                    {
                        continue;
                    }

                    Logger.Debug("Found certificates:" + response.Items.Count);

                    // Record inventory
                    /*AgentInventoryItemStatus aiis = existing.ContainsKey(sslCertificate.Name)
                                                    ? existing[sslCertificate.Name].Equals(x.Thumbprint, StringComparison.OrdinalIgnoreCase)
                                                        ? AgentInventoryItemStatus.Unchanged
                                                        : AgentInventoryItemStatus.Modified
                                                    : AgentInventoryItemStatus.New;*/

                    foreach (Data.SslCertificate sslCertificate in response.Items)
                    {
                        //Logger.Debug(JsonConvert.SerializeObject(sslCertificate));
                        if(sslCertificate.Type == "MANAGED")
                        {
                            Logger.Debug("Adding Google Managed Certificate:" + sslCertificate.Name);
                            inventoryItems.Add(new AgentCertStoreInventoryItem()
                            {
                                Alias = sslCertificate.Name,
                                Certificates = new string[] { Convert.ToBase64String(System.Text.Encoding.Default.GetBytes(sslCertificate.Certificate)) },
                                ItemStatus = AgentInventoryItemStatus.Unknown,
                                PrivateKeyEntry = false,
                                UseChainLevel = false
                            });
                        }
                        else
                        {
                            Logger.Debug("Adding Self Managed Certificate with Alias:" + sslCertificate.Name);

                            inventoryItems.Add(new AgentCertStoreInventoryItem()
                            {
                                Alias = sslCertificate.Name,
                                Certificates = new string[] { Convert.ToBase64String(System.Text.Encoding.Default.GetBytes(sslCertificate.SelfManaged.Certificate)) },
                                ItemStatus = AgentInventoryItemStatus.Unknown,
                                PrivateKeyEntry = false,
                                UseChainLevel = false
                            });
                        }
                    }
                    request.PageToken = response.NextPageToken;
                } while (response.NextPageToken != null);

                // 3) Add certificates (no private keys) to the collection below.  If multiple certs in a store comprise a chain, the Certificates array will house multiple certs per InventoryItem.  If multiple certs
                //     in a store comprise separate unrelated certs, there will be one InventoryItem object created per certificate.

                //inventoryItems.Add(new AgentCertStoreInventoryItem()
                //{
                //    ItemStatus = AgentInventoryItemStatus.Unknown, //There are other statuses, but I always use this and let KF command figure out the actual status
                //    Alias = {valueRepresentingChainIdentifier}
                //    PrivateKeyEntry = true|false //You will not pass the private key back, but you can identify if the main certificate of the chain contains a private key
                //    UseChainLevel = true|false,  //true if Certificates will contain > 1 certificate, main cert => intermediate CA cert => root CA cert.  false if Certificates will contain an array of 1 certificate
                //    Certificates = //Array of single X509 certificates in Base64 string format (certificates if chain, single cert if not), something like:
                //    ****************************
                //          foreach(X509Certificate2 certificate in certificates)
                //              certList.Add(Convert.ToBase64String(certificate.Export(X509ContentType.Cert)));
                //              certList.ToArray();
                //    ****************************
                //});

            }
            catch (Exception ex)
            {
                Logger.Error("Error performing certificate inventory: " + ex.Message);
                Logger.Debug(ex.StackTrace);
                //Status: 2=Success, 3=Warning, 4=Error
                return new AnyJobCompleteInfo() { Status = 4, Message = ex.StackTrace };
            }

            try
            {
                //Sends inventoried certificates back to KF Command
                submitInventory.Invoke(inventoryItems);
                //Status: 2=Success, 3=Warning, 4=Error
                return new AnyJobCompleteInfo() { Status = 2, Message = "Successful" };
            }
            catch (Exception ex)
            {
                Logger.Error("Error submitting certificate inventory: " + ex.Message);
                Logger.Debug(ex.StackTrace);
                // NOTE: if the cause of the submitInventory.Invoke exception is a communication issue between the Orchestrator server and the Command server, the job status returned here
                //  may not be reflected in Keyfactor Command.
                return new AnyJobCompleteInfo() { Status = 4, Message = "Custom message you want to show to show up as the error message in Job History in KF Command" };
            }
        }
    }
}