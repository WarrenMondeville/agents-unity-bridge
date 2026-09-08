using UnityBridge.Commands;
using UnityBridge.Models;
using NUnit.Framework;

namespace UnityBridge.Tests.Commands {
    /// <summary>
    /// Tests for FindUnusedAssetsCommand.
    /// Focus: successful completion and result construction against the real asset database.
    /// </summary>
    [TestFixture]
    public class FindUnusedAssetsCommandTests : CommandTestFixture {
        private FindUnusedAssetsCommand _command;

        [SetUp]
        public override void SetUp() {
            base.SetUp();
            _command = new FindUnusedAssetsCommand();
            Request.action = "find-unused-assets";
        }

        [Test]
        public void Execute_CompletesWithSuccess() {
            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.HasCompleteResponse, Is.True, "Should call onComplete");
            Assert.That(Responses.CompleteResponse.status, Is.EqualTo("success"));
        }

        [Test]
        public void Execute_PopulatesUnusedAssetsResult() {
            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.CompleteResponse.unusedAssets, Is.Not.Null, "Should populate unusedAssets result");
            Assert.That(Responses.CompleteResponse.unusedAssets.totalAssets, Is.GreaterThanOrEqualTo(0));
            Assert.That(Responses.CompleteResponse.unusedAssets.unusedCount, Is.EqualTo(Responses.CompleteResponse.unusedAssets.unusedAssets.Count));
        }

        [Test]
        public void Execute_Response_HasCorrectIdAndAction() {
            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.CompleteResponse.id, Is.EqualTo(Request.id));
            Assert.That(Responses.CompleteResponse.action, Is.EqualTo(Request.action));
        }
    }
}
