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
using Keyfactor.Platform.Extensions.Agents.Delegates;
using Keyfactor.Platform.Extensions.Agents.Enums;

using System.Reflection;

using Moq;
using System.IO;

using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.GCP.Tests
{
    public static class Mocks
    {
        public static Mock<SubmitInventoryUpdate> GetSubmitInventoryDelegateMock() => new Mock<SubmitInventoryUpdate>();

        public static Mock<SubmitEnrollmentRequest> GetSubmitEnrollmentDelegateMock() => new Mock<SubmitEnrollmentRequest>();

        public static Mock<SubmitDiscoveryResults> GetSubmitDiscoveryDelegateMock() => new Mock<SubmitDiscoveryResults>();

        public static Mock<GCPStore> gcpStore;

        private static AnyJobConfigInfo GetMockBaseConfig()
        {
            //config.Store.Inventory
            var ajJob = new AnyJobJobInfo {
                OperationType = Platform.Extensions.Agents.Enums.AnyJobOperationType.Create,
                Alias = "testJob",
                JobId = Guid.NewGuid(),
                JobTypeId = Guid.NewGuid(),
            };
            var ajServer = new AnyJobServerInfo { 
                Username = "", 
                Password = "", 
                UseSSL = true 
            };

            Dictionary<string, string> storeProperties = new Dictionary<string, string>();
            //storeProperties.Add("jsonKey", Mocks.GetFileFromManifest("Keyfactor.Extensions.Orchestrator.GCP.Tests.GCPCreds.json"));

            var ajStore = new AnyJobStoreInfo {
                ClientMachine = "<update me>",
                StorePath = "lucky-rookery-276317",
                Inventory = new List<AnyJobInventoryItem>(),
                Properties = JsonConvert.SerializeObject(storeProperties)
            };

            var ajc = new AnyJobConfigInfo()
            {
                Job = ajJob,
                Server = ajServer,
                Store = ajStore
            };

            return ajc;
        }

        public static Mock<GCPStore> getGCPStoreMock()
        {
            Mock<GCPStore> store =  new Mock<GCPStore>();
            List<AgentCertStoreInventoryItem> inventoryItems = new List<AgentCertStoreInventoryItem>();
            store.Setup(m => m.list()).Returns(inventoryItems);

            return store;
        }

        public static AnyJobConfigInfo GetMockInventoryConfig()
        {
            return GetMockBaseConfig();
        }

        public static AnyJobConfigInfo GetMockManagementAddConfig()
        {
            AnyJobConfigInfo config = GetMockBaseConfig();
            config.Job.OperationType = AnyJobOperationType.Add;
            config.Job.EntryContents = ""; //file contents
            config.Job.Alias = "testAlias";
            config.Store.StorePath = "project-id";
            config.Job.Overwrite = true;

            return config;
        }

        private static string GetFileFromManifest(string path)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(path))
            {
                stream.Position = 0;

                using (var streamReader = new StreamReader(stream))
                {
                    return streamReader.ReadToEnd();
                }
            }

        }
    }
}
