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
