using UnityBridge.Commands;
using UnityBridge.Models;
using NUnit.Framework;

namespace UnityBridge.Tests.Commands {
    /// <summary>
    /// Tests for FindReferencesCommand.
    /// Focus: parameter validation, error handling, response construction.
    /// </summary>
    [TestFixture]
    public class FindReferencesCommandTests : CommandTestFixture {
        private FindReferencesCommand _command;

        [SetUp]
        public override void SetUp() {
            base.SetUp();
            _command = new FindReferencesCommand();
            Request.action = "find-references";
        }

        [Test]
        public void Execute_WithMissingAsset_ReturnsError() {
            Request.@params.asset = null;

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.HasCompleteResponse, Is.True, "Should call onComplete");
            Assert.That(Responses.CompleteResponse.status, Is.EqualTo("error"));
            Assert.That(Responses.CompleteResponse.error, Does.Contain("Missing required parameter: asset"));
        }

        [Test]
        public void Execute_WithInvalidAsset_ReturnsError() {
            Request.@params.asset = "Assets/__does_not_exist__.asset";

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.HasCompleteResponse, Is.True, "Should call onComplete");
            Assert.That(Responses.CompleteResponse.status, Is.EqualTo("error"));
            Assert.That(Responses.CompleteResponse.error, Does.Contain("Asset not found"));
        }

        [Test]
        public void Execute_ErrorResponse_HasCorrectIdAndAction() {
            Request.@params.asset = null;

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.CompleteResponse.id, Is.EqualTo(Request.id));
            Assert.That(Responses.CompleteResponse.action, Is.EqualTo(Request.action));
        }
    }
}
