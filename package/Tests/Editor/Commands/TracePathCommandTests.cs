using UnityBridge.Commands;
using UnityBridge.Models;
using NUnit.Framework;

namespace UnityBridge.Tests.Commands {
    /// <summary>
    /// Tests for TracePathCommand.
    /// Focus: parameter validation, error handling, response construction.
    /// </summary>
    [TestFixture]
    public class TracePathCommandTests : CommandTestFixture {
        private TracePathCommand _command;

        [SetUp]
        public override void SetUp() {
            base.SetUp();
            _command = new TracePathCommand();
            Request.action = "trace-path";
        }

        [Test]
        public void Execute_WithMissingFrom_ReturnsError() {
            Request.@params.from = null;
            Request.@params.to = "Assets/To.asset";

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.HasCompleteResponse, Is.True, "Should call onComplete");
            Assert.That(Responses.CompleteResponse.status, Is.EqualTo("error"));
            Assert.That(Responses.CompleteResponse.error, Does.Contain("Missing required parameter: from"));
        }

        [Test]
        public void Execute_WithMissingTo_ReturnsError() {
            Request.@params.from = "Assets/From.asset";
            Request.@params.to = null;

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.HasCompleteResponse, Is.True, "Should call onComplete");
            Assert.That(Responses.CompleteResponse.status, Is.EqualTo("error"));
            Assert.That(Responses.CompleteResponse.error, Does.Contain("Missing required parameter: to"));
        }

        [Test]
        public void Execute_ErrorResponse_HasCorrectIdAndAction() {
            Request.@params.from = null;
            Request.@params.to = null;

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.CompleteResponse.id, Is.EqualTo(Request.id));
            Assert.That(Responses.CompleteResponse.action, Is.EqualTo(Request.action));
        }
    }
}
