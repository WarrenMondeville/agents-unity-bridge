using UnityBridge.Commands;
using UnityBridge.Models;
using NUnit.Framework;

namespace UnityBridge.Tests.Commands {
    /// <summary>
    /// Tests for SearchAssetsCommand.
    /// Focus: parameter validation, successful search against the real asset database,
    /// and response construction.
    /// </summary>
    [TestFixture]
    public class SearchAssetsCommandTests : CommandTestFixture {
        private SearchAssetsCommand _command;

        [SetUp]
        public override void SetUp() {
            base.SetUp();
            _command = new SearchAssetsCommand();
            Request.action = "search-assets";
        }

        [Test]
        public void Execute_WithMissingQuery_ReturnsError() {
            Request.@params.query = null;

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.HasCompleteResponse, Is.True, "Should call onComplete");
            Assert.That(Responses.CompleteResponse.status, Is.EqualTo("error"));
            Assert.That(Responses.CompleteResponse.error, Does.Contain("Missing required parameter: query"));
        }

        [Test]
        public void Execute_WithQuery_ReturnsSuccessAndPopulatesSearchResult() {
            // A nonsense query deterministically matches nothing.
            Request.@params.query = "zzz_nonexistent_xyz";

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.HasCompleteResponse, Is.True, "Should call onComplete");
            Assert.That(Responses.CompleteResponse.status, Is.EqualTo("success"));
            Assert.That(Responses.CompleteResponse.searchResult, Is.Not.Null, "Should populate searchResult");
            Assert.That(Responses.CompleteResponse.searchResult.count, Is.EqualTo(0));
        }

        [Test]
        public void Execute_ErrorResponse_HasCorrectIdAndAction() {
            Request.@params.query = null;

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.CompleteResponse.id, Is.EqualTo(Request.id));
            Assert.That(Responses.CompleteResponse.action, Is.EqualTo(Request.action));
        }
    }
}
