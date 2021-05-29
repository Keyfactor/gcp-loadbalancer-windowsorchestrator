﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Newtonsoft.Json;

using CSS.Common.Logging;

using Keyfactor.Platform.Extensions.Agents;

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
        private ComputeService service;

        public GCPStore(AnyJobConfigInfo config)
        {
            this.project = config.Store.StorePath;
            Dictionary<string, string> storeProperties = JsonConvert.DeserializeObject<Dictionary<string, string>>((string)config.Store.Properties);
            this.jsonKey = storeProperties["jsonKey"];

            Logger.Debug("project: " + this.project);
            Logger.Debug("jsonKey size:" + this.jsonKey.Length);
        }

        public void insert(SslCertificate sslCertificate, bool overwrite)
        {
            try
            {
                insert(sslCertificate);
            }
            catch (Exception ex)
            {
                if (overwrite)
                {
                    try
                    {
                        delete(sslCertificate.Name);
                        insert(sslCertificate);
                    }
                    catch (Exception ex2)
                    {
                        Logger.Error("Error performing certificate add with overwrite: " + ex2.Message);
                        Logger.Debug(ex2.StackTrace);
                        throw ex2;
                    }
                }
                else
                {
                    Logger.Error("Error performing certificate add: " + ex.Message);
                    Logger.Debug(ex.StackTrace);
                    throw ex;
                }
            }
        }

        public List<AgentCertStoreInventoryItem> list()
        {
            List<AgentCertStoreInventoryItem> inventoryItems = new List<AgentCertStoreInventoryItem>();
            SslCertificatesResource.ListRequest request = getComputeService().SslCertificates.List(this.project);

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
                request.PageToken = response.NextPageToken;
            } while (response.NextPageToken != null);

            return inventoryItems;
        }

        public void insert(SslCertificate sslCertificate)
        {
            SslCertificatesResource.InsertRequest request = getComputeService().SslCertificates.Insert(sslCertificate, this.project);
            Operation response = request.Execute();

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
            SslCertificatesResource.DeleteRequest request = this.getComputeService().SslCertificates.Delete(this.project, alias);
            Operation response = request.Execute();

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
    }
}