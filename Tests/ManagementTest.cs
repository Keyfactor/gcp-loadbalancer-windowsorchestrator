﻿//Copyright 2021 Keyfactor
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

using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Keyfactor.Extensions.Orchestrator.GCP.Tests
{
    //[TestClass]
    //public class ManagementTests
    //{
    //    [TestMethod]
    //    public void ReturnsTheCorrectJobClassAndStoreType()
    //    {
    //        var inventory = new Management();
    //        inventory.GetJobClass().Should().Be("Management");
    //        inventory.GetStoreType().Should().Be("GCP");
    //    }

    //    [TestMethod]
    //    public void JobInvokesCorrectDelegatesa()
    //    {
    //        var inventory = new Mock<Management>() { CallBase = true };
            
    //        var mockInventoryDelegate = Mocks.GetSubmitInventoryDelegateMock();
    //        //mockInventoryDelegate.Setup(m => m.Invoke(It.IsAny<List<AgentCertStoreInventoryItem>>())).Returns(true);
    //        var result = inventory.Object.processJob(Mocks.GetMockManagementAddConfig(), mockInventoryDelegate.Object, Mocks.GetSubmitEnrollmentDelegateMock().Object, Mocks.GetSubmitDiscoveryDelegateMock().Object);

    //        //mockInventoryDelegate.Verify(m => m(It.IsAny<List<AgentCertStoreInventoryItem>>()));
    //    }
    //}
}
