using UnityBridge.Commands;
using UnityBridge.Models;
using NUnit.Framework;

namespace UnityBridge.Tests.Commands {
    /// <summary>
    /// Tests for ManagePrefabsCommand.
    /// Focus: parameter validation and error handling for get-info / get-hierarchy / create.
    /// PrefabUtility internals are exercised but not mocked; validation paths are deterministic.
    /// </summary>
    [TestFixture]
    public class ManagePrefabsCommandTests : CommandTestFixture {
        private ManagePrefabsCommand _command;

        [SetUp]
        public override void SetUp() {
            base.SetUp();
            _command = new ManagePrefabsCommand();
            Request.action = "manage-prefabs";
        }

        [Test]
        public void Execute_WithMissingPrefabAction_ReturnsError() {
            Request.@params.prefabAction = null;

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.HasCompleteResponse, Is.True, "Should call onComplete");
            Assert.That(Responses.CompleteResponse.status, Is.EqualTo("error"));
            Assert.That(Responses.CompleteResponse.error, Does.Contain("prefabAction"));
        }

        [Test]
        public void Execute_WithUnknownPrefabAction_ReturnsError() {
            Request.@params.prefabAction = "bogus-action";

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.HasCompleteResponse, Is.True, "Should call onComplete");
            Assert.That(Responses.CompleteResponse.status, Is.EqualTo("error"));
            Assert.That(Responses.CompleteResponse.error, Does.Contain("Unknown or missing prefabAction"));
        }

        [Test]
        public void GetInfo_WithMissingPrefabPath_ReturnsError() {
            Request.@params.prefabAction = "get-info";
            Request.@params.prefabPath = null;

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.HasCompleteResponse, Is.True, "Should call onComplete");
            Assert.That(Responses.CompleteResponse.status, Is.EqualTo("error"));
            Assert.That(Responses.CompleteResponse.error, Does.Contain("prefabPath"));
        }

        [Test]
        public void GetInfo_WithNonexistentPrefab_ReturnsError() {
            Request.@params.prefabAction = "get-info";
            Request.@params.prefabPath = "Assets/__does_not_exist__.prefab";

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.HasCompleteResponse, Is.True, "Should call onComplete");
            Assert.That(Responses.CompleteResponse.status, Is.EqualTo("error"));
            Assert.That(Responses.CompleteResponse.error, Does.Contain("No prefab asset found"));
        }

        [Test]
        public void GetHierarchy_WithMissingPrefabPath_ReturnsError() {
            Request.@params.prefabAction = "get-hierarchy";
            Request.@params.prefabPath = null;

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.HasCompleteResponse, Is.True, "Should call onComplete");
            Assert.That(Responses.CompleteResponse.status, Is.EqualTo("error"));
            Assert.That(Responses.CompleteResponse.error, Does.Contain("prefabPath"));
        }

        [Test]
        public void Create_WithMissingObjectName_ReturnsError() {
            Request.@params.prefabAction = "create";
            Request.@params.objectName = null;
            Request.@params.prefabPath = "Assets/Prefabs/New.prefab";

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.HasCompleteResponse, Is.True, "Should call onComplete");
            Assert.That(Responses.CompleteResponse.status, Is.EqualTo("error"));
            Assert.That(Responses.CompleteResponse.error, Does.Contain("objectName"));
        }

        [Test]
        public void Create_WithMissingPrefabPath_ReturnsError() {
            Request.@params.prefabAction = "create";
            Request.@params.objectName = "SomeObject";
            Request.@params.prefabPath = null;

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.HasCompleteResponse, Is.True, "Should call onComplete");
            Assert.That(Responses.CompleteResponse.status, Is.EqualTo("error"));
            Assert.That(Responses.CompleteResponse.error, Does.Contain("prefabPath"));
        }

        [Test]
        public void Execute_ErrorResponse_HasCorrectIdAndAction() {
            Request.@params.prefabAction = null;

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.CompleteResponse.id, Is.EqualTo(Request.id));
            Assert.That(Responses.CompleteResponse.action, Is.EqualTo(Request.action));
        }
    }
}
