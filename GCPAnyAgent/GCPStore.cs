﻿// Copyright 2021 Keyfactor
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Newtonsoft.Json;

using CSS.Common.Logging;

using Keyfactor.Platform.Extensions.Agents;

using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Compute.v1;
using Google.Apis.Compute.v1.Data;
using Google.Apis.Services;

using Data = Google.Apis.Compute.v1.Data;

using Keyfactor.Platform.Extensions.Agents.Enums;

namespace Keyfactor.Extensions.Orchestrator.GCP
{
    public class GCPStore : LoggingClientBase
    {
        private string jsonKey;
        private string project;
        private string region = string.Empty;
        private ComputeService service;

        public GCPStore(AnyJobConfigInfo config)
        {
            SetProjectAndRegion(config.Store.StorePath);
            Dictionary<string, string> storeProperties = JsonConvert.DeserializeObject<Dictionary<string, string>>((string)config.Store.Properties);
            this.jsonKey = storeProperties["jsonKey"];

            Logger.Debug("project: " + this.project);
            Logger.Debug("jsonKey size:" + this.jsonKey.Length);
        }

        public void insert(SslCertificate sslCertificate, bool overwrite)
        {
            string alias = sslCertificate.Name;
            string tempAlias = alias + "-temp";
            string targetCertificateSelfLink = string.Empty;
            string tempCertificateSelfLink = string.Empty;

            try
            {
                try
                {
                    targetCertificateSelfLink = GetCeritificateSelfLink(alias);
                }
                catch (Google.GoogleApiException ex)
                {
                    if (ex.HttpStatusCode != System.Net.HttpStatusCode.NotFound)
                        throw ex;
                }

                //SCENARIO => certificate alias exists, but overwrite flag not set.  ERROR
                if (!string.IsNullOrEmpty(targetCertificateSelfLink) && !overwrite)
                {
                    string message = "Overwrite flag not set but certificate exists.  If attempting to renew, please check overwrite when scheduling this job.";
                    Logger.Error(message);
                    throw new Exception(message);
                }

                //SCENARIO => certificate alias does not exist and overwrite not set.  Because overwrite was not set we do not need to check for temporary alias that may have been created in an earlier
                //  job but not removed due to error.  Since overwrite is not set, the renewal workflow that could have generated a temporary alias would not have been run.  INSERT NEW CERTIFICATE, NO BINDINGS
                if (string.IsNullOrEmpty(targetCertificateSelfLink) && !overwrite)
                {
                    Logger.Debug("Certificate is not in GCP.  Insert new certificate.");
                    insert(sslCertificate);
                    return;
                }

                // check for existence of cert with this temporary alias in GCP
                Logger.Debug($"Get cert for temp alias - {tempAlias}");
                try
                {
                    tempCertificateSelfLink = GetCeritificateSelfLink(tempAlias);
                }
                catch (Google.GoogleApiException ex)
                {
                    if (ex.HttpStatusCode != System.Net.HttpStatusCode.NotFound)
                        throw ex;
                }

                //SCENARIO => Overwrite flag set.  Neither the passed in alias nor the temporary alias exists, so no clean up from a previous job is necessary.  No
                //  certificate exists.  INSERT NEW CERTIFICATE, NO BINDINGS
                if (string.IsNullOrEmpty(targetCertificateSelfLink) && string.IsNullOrEmpty(tempCertificateSelfLink))
                {
                    Logger.Debug("Certificate is not in GCP.  Insert new certificate.");
                    insert(sslCertificate);
                    return;
                }

                //SCENARIO => certificate exists for passed in alias 
                if (!string.IsNullOrEmpty(targetCertificateSelfLink))
                {
                    //SCENARIO => if temporary certificate does not already exist, it must be added so it can be bound next as a temporary pre-cursor to removing desired alias, adding it and binding with it
                    if (string.IsNullOrEmpty(tempCertificateSelfLink))
                    {
                        Logger.Debug("Certificate exists in GCP.  Begin renewal by adding certificate with temporary alias.");
                        SslCertificate tempSSLCertificate = new SslCertificate() { Certificate = sslCertificate.Certificate, PrivateKey = sslCertificate.PrivateKey, Name = tempAlias };
                        insert(tempSSLCertificate);
                        try
                        {
                            tempCertificateSelfLink = GetCeritificateSelfLink(tempAlias);
                        }
                        catch (Google.GoogleApiException ex)
                        {
                            if (ex.HttpStatusCode != System.Net.HttpStatusCode.NotFound)
                                throw ex;
                        }
                    }

                    //SCENARIO => renew certificate process - bind to temporary alias, delete previous version of cert with desired alias, add renewed certificate, update bindings to renewed cert and remove temp bindings,
                    //   delete cert with temp alias
                    Logger.Debug("Bind cert with temp alias, delete cert to be renewed, add renewed cert, update bindings to new renewed cert, delete temp cert");
                    processBindings(targetCertificateSelfLink, tempCertificateSelfLink);
                    delete(alias);
                    insert(sslCertificate);
                    processBindings(tempCertificateSelfLink, targetCertificateSelfLink);
                    delete(tempAlias);
                }
                //SCENARIO => certificate does NOT exist for passed in alias.  certificate MUST exist for temporary alias since we already know one or both MUST exist from previous check.
                //  Add renewed certificate with passed in alias, bind it while removing temporary alias from binding (if exists), delete temporary alias cert
                else
                {
                    Logger.Debug("Certificate is not in GCP, but temporary one is - Cleanup of prior error state.  insert renewed certificate, bind renewed certificate and remove temp binding, delete temporary certificate.");
                    insert(sslCertificate);
                    processBindings(tempCertificateSelfLink, targetCertificateSelfLink);
                    delete(tempAlias);
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error adding or binding certificate: " + ex.Message);
                Logger.Debug(ex.StackTrace);
                throw ex;
            }
        }

        public List<AgentCertStoreInventoryItem> list()
        {
            List<AgentCertStoreInventoryItem> inventoryItems = new List<AgentCertStoreInventoryItem>();
            SslCertificatesResource.ListRequest globalRequest = getComputeService().SslCertificates.List(this.project);
            RegionSslCertificatesResource.ListRequest regionRequest = getComputeService().RegionSslCertificates.List(this.project, this.region);
            
            SslCertificateList response;
            do
            {
                // To execute asynchronously in an async method, replace `request.Execute()` as shown:
                response = string.IsNullOrEmpty(region) ? globalRequest.Execute() : regionRequest.Execute();
                // response = string.IsNullOrEmpty(region) ? await globalRequest.ExecuteAsync() : await regionRequest.ExecuteAsync();

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
                    if (sslCertificate.Type == "MANAGED")
                    {
                        Logger.Debug("Adding Google Managed Certificate:" + sslCertificate.Name);
                        inventoryItems.Add(new AgentCertStoreInventoryItem()
                        {
                            Alias = sslCertificate.Name,
                            Certificates = new string[] { sslCertificate.Certificate },
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
                            Certificates = new string[] { sslCertificate.SelfManaged.Certificate },
                            ItemStatus = AgentInventoryItemStatus.Unknown,
                            PrivateKeyEntry = false,
                            UseChainLevel = false
                        });
                    }
                }
                if (string.IsNullOrEmpty(region))
                {
                    globalRequest.PageToken = response.NextPageToken;
                }
                else
                {
                    regionRequest.PageToken = response.NextPageToken;
                }
            } while (response.NextPageToken != null);

            return inventoryItems;
        }

        public void insert(SslCertificate sslCertificate)
        {
            Operation response = new Operation();
            if (string.IsNullOrEmpty(region))
            {
                SslCertificatesResource.InsertRequest request = getComputeService().SslCertificates.Insert(sslCertificate, this.project);
                response = request.Execute();
                System.Threading.Thread.Sleep(10000);
            }
            else
            {
                RegionSslCertificatesResource.InsertRequest request = getComputeService().RegionSslCertificates.Insert(sslCertificate, this.project, region);
                response = request.Execute();
                System.Threading.Thread.Sleep(10000);
            }

            if (response.HttpErrorStatusCode != null)
            {
                Logger.Error("Error performing certificate add: " + response.HttpErrorMessage);
                Logger.Debug(response.HttpErrorStatusCode);
                throw new Exception(response.HttpErrorMessage);
            }
            if (response.Error != null)
            {
                Logger.Error("Error performing certificate add: " + response.Error.ToString());
                Logger.Debug(response.Error.ToString());
                throw new Exception(response.Error.ToString());
            }
        }

        public void delete(string alias)
        {
            Operation response = new Operation();
            if (string.IsNullOrEmpty(region))
            {
                SslCertificatesResource.DeleteRequest request = getComputeService().SslCertificates.Delete(this.project, alias);
                response = request.Execute();
            }
            else
            {
                RegionSslCertificatesResource.DeleteRequest request = getComputeService().RegionSslCertificates.Delete(this.project, region, alias);
                response = request.Execute();
            }

            if (response.HttpErrorStatusCode != null)
            {
                Logger.Error("Error performing certificate delete: " + response.HttpErrorMessage);
                Logger.Debug(response.HttpErrorStatusCode);
                throw new Exception(response.HttpErrorMessage);
            }
            if (response.Error != null)
            {
                Logger.Error("Error performing certificate delete: " + response.Error.ToString());
                Logger.Debug(response.Error.ToString());
                throw new Exception(response.Error.ToString());
            }
        }

        private void processBindings(string prevCertificateSelfLink, string newCertificateSelfLink)
        {
            try
            {
                // For HTTPS proxy resources
                TargetHttpsProxyList httpsProxyList = new TargetHttpsProxyList();
                if (string.IsNullOrEmpty(region))
                {
                    TargetHttpsProxiesResource.ListRequest request = new TargetHttpsProxiesResource(getComputeService()).List(project);
                    httpsProxyList = request.Execute();
                }
                else
                {
                    RegionTargetHttpsProxiesResource.ListRequest request = new RegionTargetHttpsProxiesResource(getComputeService()).List(project, region);
                    httpsProxyList = request.Execute();
                }

                if (httpsProxyList.Items != null)
                {
                    foreach (TargetHttpsProxy proxy in httpsProxyList.Items)
                    {
                        if (proxy.SslCertificates.Contains(prevCertificateSelfLink) || proxy.SslCertificates.Contains(newCertificateSelfLink))
                        {
                            List<string> sslCertificates = (List<string>)proxy.SslCertificates;

                            if (proxy.SslCertificates.Contains(prevCertificateSelfLink))
                                sslCertificates.Remove(prevCertificateSelfLink);
                            if (proxy.SslCertificates.Contains(newCertificateSelfLink))
                                sslCertificates.Remove(newCertificateSelfLink);

                            sslCertificates.Add(newCertificateSelfLink);

                            Operation response = new Operation();

                            if (string.IsNullOrEmpty(region))
                            {
                                TargetHttpsProxiesSetSslCertificatesRequest httpsCertRequest = new TargetHttpsProxiesSetSslCertificatesRequest();
                                httpsCertRequest.SslCertificates = sslCertificates;
                                TargetHttpsProxiesResource.SetSslCertificatesRequest setSSLRequest = new TargetHttpsProxiesResource(getComputeService()).SetSslCertificates(httpsCertRequest, project, proxy.Name);
                                response = setSSLRequest.Execute();
                            }
                            else
                            {
                                RegionTargetHttpsProxiesSetSslCertificatesRequest httpsCertRequest = new RegionTargetHttpsProxiesSetSslCertificatesRequest();
                                httpsCertRequest.SslCertificates = sslCertificates;
                                RegionTargetHttpsProxiesResource.SetSslCertificatesRequest setSSLRequest = new RegionTargetHttpsProxiesResource(getComputeService()).SetSslCertificates(httpsCertRequest, project, region, proxy.Name);
                                response = setSSLRequest.Execute();
                            }

                            if (response.HttpErrorStatusCode != null)
                            {
                                Logger.Error($"Error setting SSL Certificates for resource: {proxy.Name} " + response.HttpErrorMessage);
                                throw new Exception(response.HttpErrorMessage);
                            }
                            if (response.Error != null)
                            {
                                Logger.Error($"Error setting SSL Certificates for resource: {proxy.Name} " + response.Error.ToString());
                                throw new Exception(response.Error.ToString());
                            }
                        }
                    }
                }


                // For SSL proxy resources
                //TargetSslProxiesResource.ListRequest sslRequest = new TargetSslProxiesResource(getComputeService()).List(project);
                //TargetSslProxyList proxyList = sslRequest.Execute();

                //if (proxyList.Items != null)
                //{
                //    foreach (TargetSslProxy proxy in proxyList.Items)
                //    {
                //        if (proxy.SslCertificates.Contains(prevCertificateSelfLink) || proxy.SslCertificates.Contains(newCertificateSelfLink))
                //        {
                //            List<string> sslCertificates = (List<string>)proxy.SslCertificates;

                //            if (proxy.SslCertificates.Contains(prevCertificateSelfLink))
                //                sslCertificates.Remove(prevCertificateSelfLink);
                //            if (proxy.SslCertificates.Contains(newCertificateSelfLink))
                //                sslCertificates.Remove(newCertificateSelfLink);

                //            sslCertificates.Add(newCertificateSelfLink);

                //            TargetSslProxiesSetSslCertificatesRequest sslCertRequest = new TargetSslProxiesSetSslCertificatesRequest();
                //            sslCertRequest.SslCertificates = sslCertificates;
                //            TargetSslProxiesResource.SetSslCertificatesRequest setSSLRequest = new TargetSslProxiesResource(getComputeService()).SetSslCertificates(sslCertRequest, project, proxy.Name);
                //            Operation response = setSSLRequest.Execute();

                //            if (response.HttpErrorStatusCode != null)
                //            {
                //                Logger.Error($"Error setting SSL Certificates for resource: {proxy.Name} " + response.HttpErrorMessage);
                //                throw new Exception(response.HttpErrorMessage);
                //            }
                //            if (response.Error != null)
                //            {
                //                Logger.Error($"Error setting SSL Certificates for resource: {proxy.Name} " + response.Error.ToString());
                //                throw new Exception(response.Error.ToString());
                //            }

                //        }
                //    }
                //}
            }
            catch (Exception ex)
            {
                string message = "Error attempting to bind added certificate to resource " + ex.Message;
                Logger.Error(message);
                throw new Exception(message);
            }
        }

        private string GetCeritificateSelfLink(string prevAlias)
        {
            SslCertificate certificate = new SslCertificate();

            if (string.IsNullOrEmpty(region))
            {
                SslCertificatesResource.GetRequest request = this.getComputeService().SslCertificates.Get(project, prevAlias);
                certificate = request.Execute();
            }
            else
            {
                RegionSslCertificatesResource.GetRequest request = this.getComputeService().RegionSslCertificates.Get(project, region, prevAlias);
                certificate = request.Execute();
            }

            if (certificate == null || string.IsNullOrEmpty(certificate.Certificate))
                return null;

            return certificate.SelfLink;
        }

        private ComputeService getComputeService()
        {
            if (this.service == null) {
                Logger.Debug("Initializing new Compute Service");
                this.service = new ComputeService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = GetCredential(),
                    ApplicationName = "Google-ComputeSample/0.1",
                });

            }
            return this.service;
        }

        private GoogleCredential GetCredential()
        {

            //Example Environment variable for Application Default
            //Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", @"C:\development\GCPAnyAgent\Tests\GCPCreds.json");

            //Example reading from File
            //GoogleCredential credential = GoogleCredential.FromFile("Keyfactor.Extensions.Orchestrator.GCP.Tests.GCPCreds.json");

            GoogleCredential credential;

            if (String.IsNullOrWhiteSpace(this.jsonKey))
            {
                Logger.Debug("Loading credentials from application default");
                credential = Task.Run(() => GoogleCredential.GetApplicationDefaultAsync()).Result;
            }
            else
            {
                Logger.Debug("Loading key from store properties");
                credential = GoogleCredential.FromJson(jsonKey);
            }

            if (credential.IsCreateScopedRequired)
            {
                credential = credential.CreateScoped("https://www.googleapis.com/auth/cloud-platform");
            }
            return credential;
        }

        private void SetProjectAndRegion(string storePath)
        {
            project = storePath;
            if (storePath.Contains("/"))
            {
                string[] projectRegion = storePath.Split('/');
                project = projectRegion[0];
                region = projectRegion[1];
            }
        }
    }
}
