using UnityBridge.Commands;
using UnityBridge.Models;
using NUnit.Framework;

namespace UnityBridge.Tests.Commands {
    /// <summary>
    /// Tests for DumpAssetCommand.
    /// Focus: parameter validation and error handling. SerializedObject traversal is
    /// exercised against the real asset database; validation paths are deterministic.
    /// </summary>
    [TestFixture]
    public class DumpAssetCommandTests : CommandTestFixture {
        private DumpAssetCommand _command;

        [SetUp]
        public override void SetUp() {
            base.SetUp();
            _command = new DumpAssetCommand();
            Request.action = "dump-asset";
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
        public void Execute_WithUnsupportedExtension_ReturnsError() {
            Request.@params.asset = "Assets/SomeFile.mat";

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.HasCompleteResponse, Is.True, "Should call onComplete");
            Assert.That(Responses.CompleteResponse.status, Is.EqualTo("error"));
            Assert.That(Responses.CompleteResponse.error, Does.Contain("Unsupported asset type"));
        }

        [Test]
        public void Execute_WithNonexistentAsset_ReturnsError() {
            Request.@params.asset = "Assets/__does_not_exist__.asset";

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.HasCompleteResponse, Is.True, "Should call onComplete");
            Assert.That(Responses.CompleteResponse.status, Is.EqualTo("error"));
            Assert.That(Responses.CompleteResponse.error, Does.Contain("No asset found"));
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
