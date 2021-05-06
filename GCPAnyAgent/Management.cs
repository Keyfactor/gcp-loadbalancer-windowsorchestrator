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
using System.IO;
using System.Linq;

using Keyfactor.Platform.Extensions.Agents;
using Keyfactor.Platform.Extensions.Agents.Enums;
using Keyfactor.Platform.Extensions.Agents.Delegates;
using Keyfactor.Platform.Extensions.Agents.Interfaces;

using CSS.Common.Logging;

using Google.Apis.Auth.OAuth2;
using Google.Apis.Compute.v1;
using Google.Apis.Compute.v1.Data;
using Google.Apis.Services;
using Newtonsoft.Json;
using System.Threading.Tasks;

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Collections.Generic;

namespace Keyfactor.Extensions.Orchestrator.GCP
{
    public class Management : LoggingClientBase, IAgentJobExtension
    {
        public string GetJobClass()
        {
            //Setting to "Management" makes this the entry point for all Management jobs
            return "Management";
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
            if (config.Server.Password != null)
            {
                credential = GoogleCredential.FromJson(config.Server.Password);
            }
            else
            {
                credential = Task.Run(() => GoogleCredential.GetApplicationDefaultAsync()).Result;
            }

            if (credential.IsCreateScopedRequired)
            {
                credential = credential.CreateScoped("https://www.googleapis.com/auth/cloud-platform");
            }
            return credential;
        }

        private (byte[], byte[]) GetPemFromPFX(byte[] pfxBytes, char[] pfxPassword)
        {
            Pkcs12Store p = new Pkcs12Store(new MemoryStream(pfxBytes), pfxPassword);

            // Extract private key
            MemoryStream memoryStream = new MemoryStream();
            TextWriter streamWriter = new StreamWriter(memoryStream);
            PemWriter pemWriter = new PemWriter(streamWriter);

            String alias = (p.Aliases.Cast<String>()).SingleOrDefault(a => p.IsKeyEntry(a));
            AsymmetricKeyParameter publicKey = p.GetCertificate(alias).Certificate.GetPublicKey();
            if (p.GetKey(alias) == null) { throw new Exception($"Unable to get the key for alias: {alias}"); }
            AsymmetricKeyParameter privateKey = p.GetKey(alias).Key;
            AsymmetricCipherKeyPair keyPair = new AsymmetricCipherKeyPair(publicKey, privateKey);

            pemWriter.WriteObject(keyPair.Private);
            streamWriter.Flush();
            String privateKeyString = Encoding.ASCII.GetString(memoryStream.GetBuffer()).Trim().Replace("\r", "").Replace("\0", "");
            memoryStream.Close();
            streamWriter.Close();

            // Extract server certificate
            String certStart = "-----BEGIN CERTIFICATE-----\n";
            String certEnd = "\n-----END CERTIFICATE-----";
            Func<String, String> pemify = null;
            pemify = (ss => ss.Length <= 64 ? ss : ss.Substring(0, 64) + "\n" + pemify(ss.Substring(64)));
            String certPem = certStart + pemify(Convert.ToBase64String(p.GetCertificate(alias).Certificate.GetEncoded())) + certEnd;
            return (Encoding.ASCII.GetBytes(certPem), Encoding.ASCII.GetBytes(privateKeyString));
        }

        private void performAdd(ComputeService computeService, SslCertificate sslCertificate, string project)
        {
            SslCertificatesResource.InsertRequest request = computeService.SslCertificates.Insert(sslCertificate, project);
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

        private AnyJobCompleteInfo performAdd(AnyJobConfigInfo config)
        {
            Logger.Debug($"Begin Add...");
            ComputeService computeService = new ComputeService(new BaseClientService.Initializer
            {
                HttpClientInitializer = GetCredential(config),
                ApplicationName = "Google-ComputeSample/0.1",
            });

            byte[] pfxBytes = Convert.FromBase64String(config.Job.EntryContents);
            (byte[] certPem, byte[] privateKey) = GetPemFromPFX(pfxBytes, config.Job.PfxPassword.ToCharArray());
            
            SslCertificate sslCertificate = new SslCertificate{
                Certificate = System.Text.Encoding.Default.GetString(certPem),
                PrivateKey = System.Text.Encoding.Default.GetString(privateKey),
                Name = config.Job.Alias
            };

            string project = config.Store.StorePath;

            try
            {
                performAdd(computeService, sslCertificate, project);
            }
            catch (Exception ex)
            {

                if (config.Job.Overwrite)
                {
                    performDelete(computeService, config.Job.Alias, project);
                    performAdd(computeService, sslCertificate, project);
                }
                else
                {
                    Logger.Error("Error performing certificate add: " + ex.Message);
                    Logger.Debug(ex.StackTrace);
                    throw ex;
                }
            }

            return new AnyJobCompleteInfo() { Status = 2, Message = "Successful" };

        }

        private void performDelete(ComputeService computeService, string alias, string project)
        {
            SslCertificatesResource.DeleteRequest request = computeService.SslCertificates.Delete(project, alias);

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

        private void performDelete(AnyJobConfigInfo config)
        {
            Logger.Debug($"Begin Delete...");
            ComputeService computeService = new ComputeService(new BaseClientService.Initializer
            {
                HttpClientInitializer = GetCredential(config),
                ApplicationName = "Google-ComputeSample/0.1",
            });

            string project = config.Store.StorePath;

            try
            {
                performDelete(computeService, config.Job.Alias, project);
            }
            catch (Exception ex)
            {
                Logger.Error("Error performing certificate delete: " + ex.Message);
                Logger.Debug(ex.StackTrace);
                throw new Exception(ex.StackTrace);
            }
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
            Logger.Debug($"Begin Management...");

            try
            {
                //Management jobs, unlike Discovery, Inventory, and Reenrollment jobs can have 3 different purposes:
                switch (config.Job.OperationType)
                {
                    case AnyJobOperationType.Add:
                        performAdd(config);
                        break;
                    case AnyJobOperationType.Remove:
                        performDelete(config);
                        break;
                    case AnyJobOperationType.Create:
                        // The certificate store is remote
                        break;
                    default:
                        //Invalid OperationType.  Return error.  Should never happen though
                        return new AnyJobCompleteInfo() { Status = 4, Message = $"Site {config.Store.StorePath} on server {config.Store.ClientMachine}: Unsupported operation: {config.Job.OperationType.ToString()}" };
                }
            }
            catch (Exception ex)
            {
                //Status: 2=Success, 3=Warning, 4=Error
                return new AnyJobCompleteInfo() { Status = 4, Message = ex.Message };
            }

            //Status: 2=Success, 3=Warning, 4=Error
            return new AnyJobCompleteInfo() { Status = 2, Message = "Successful" };
        }
    }
}