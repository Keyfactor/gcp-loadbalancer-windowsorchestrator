using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Keyfactor.Platform.Extensions.Agents;
using Keyfactor.Platform.Extensions.Agents.Delegates;
using Keyfactor.Platform.Extensions.Agents.Enums;

using System.Reflection;

using Moq;
using System.IO;

namespace Keyfactor.Extensions.Orchestrator.GCP.Tests
{
    public static class Mocks
    {
        public static Mock<SubmitInventoryUpdate> GetSubmitInventoryDelegateMock() => new Mock<SubmitInventoryUpdate>();

        public static Mock<SubmitEnrollmentRequest> GetSubmitEnrollmentDelegateMock() => new Mock<SubmitEnrollmentRequest>();

        public static Mock<SubmitDiscoveryResults> GetSubmitDiscoveryDelegateMock() => new Mock<SubmitDiscoveryResults>();

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
                Username = "unused", 
                Password = Mocks.GetFileFromManifest("Keyfactor.Extensions.Orchestrator.GCP.Tests.GCPCreds.json"), 
                UseSSL = true 
            };
            var ajStore = new AnyJobStoreInfo { 
                ClientMachine = "<update me>", 
                StorePath = "/nsconfig/ssl/", 
                Inventory = new List<AnyJobInventoryItem>() 
            };
            var ajc = new AnyJobConfigInfo()
            {
                Job = ajJob,
                Server = ajServer,
                Store = ajStore
            };

            return ajc;
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
