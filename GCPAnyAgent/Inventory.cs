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
using Keyfactor.Platform.Extensions.Agents.Interfaces;

using CSS.Common.Logging;


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


        public AnyJobCompleteInfo processJob(AnyJobConfigInfo config, SubmitInventoryUpdate submitInventory, SubmitEnrollmentRequest submitEnrollmentRequest, SubmitDiscoveryResults sdr)
        {
            GCPStore store;
            try
            {
                store = new GCPStore(config);
            }
            catch(Exception ex)
            {
                return new AnyJobCompleteInfo() { Status = 4, Message = "Error creating GCPStore object.  Validate credentials." };
            }
            return processJob(config, submitInventory, submitEnrollmentRequest, sdr, store);
        }

        //Job Entry Point
        public AnyJobCompleteInfo processJob(AnyJobConfigInfo config, SubmitInventoryUpdate submitInventory, SubmitEnrollmentRequest submitEnrollmentRequest, SubmitDiscoveryResults sdr, GCPStore store)
        {
            Logger.Debug($"Begin Inventory...");

            //List<AgentCertStoreInventoryItem> is the collection that the interface expects to return from this job.  It will contain a collection of certificates found in the store along with other information about those certificates
            List<AgentCertStoreInventoryItem> inventoryItems = new List<AgentCertStoreInventoryItem>();
            
            try
            {
                //Code logic to:
                // 1) Connect to the orchestrated server (config.Store.ClientMachine) containing the certificate store to be inventoried (config.Store.StorePath)
                //GCPStore store = new GCPStore(config);

                // 2) Custom logic to retrieve certificates from certificate store.
                inventoryItems = store.list();
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
                Logger.Debug("Sending certificates back to Command:" + inventoryItems.Count);
                //Sends inventoried certificates back to KF Command
                bool status = submitInventory.Invoke(inventoryItems);
                Logger.Debug("Send Certificate response: " + status);
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