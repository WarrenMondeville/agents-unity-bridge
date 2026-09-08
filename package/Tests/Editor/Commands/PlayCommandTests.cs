using UnityBridge.Commands;
using UnityBridge.Models;
using UnityBridge.Tests.TestHelpers;
using NUnit.Framework;

namespace UnityBridge.Tests.Commands {
    /// <summary>
    /// Tests for PlayCommand using a hand-written FakeEditorPlayMode (no Moq dependency).
    /// Focus: Toggle behavior, response construction, editorStatus reflects fake state.
    /// </summary>
    [TestFixture]
    public class PlayCommandTests : CommandTestFixture {
        private FakeEditorPlayMode _editor;
        private PlayCommand _command;

        [SetUp]
        public override void SetUp() {
            base.SetUp();
            _editor = new FakeEditorPlayMode();
            _command = new PlayCommand(_editor);
            Request.action = "play";
        }

        [Test]
        public void Execute_CallsOnCompleteExactlyOnce() {
            var callCount = 0;
            System.Action<CommandResponse> countingCallback = (response) => { callCount++; };

            _command.Execute(Request, Responses.OnProgress, countingCallback);

            Assert.That(callCount, Is.EqualTo(1), "onComplete should be called exactly once");
        }

        [Test]
        public void Execute_DoesNotCallOnProgress() {
            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.ProgressResponses.Count, Is.EqualTo(0), "Should not call onProgress for synchronous command");
        }

        [Test]
        public void Execute_ConstructsResponseWithCorrectIdAndAction() {
            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.CompleteResponse, Is.Not.Null, "Should call onComplete");
            Assert.That(Responses.CompleteResponse.id, Is.EqualTo(Request.id));
            Assert.That(Responses.CompleteResponse.action, Is.EqualTo(Request.action));
        }

        [Test]
        public void Execute_IncludesEditorStatusInResponse() {
            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.CompleteResponse.editorStatus, Is.Not.Null, "Should include editorStatus in response");
        }

        [Test]
        public void Execute_ResponseHasDuration() {
            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.CompleteResponse.duration_ms, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void Execute_ResponseStatusIsSuccess() {
            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.CompleteResponse.status, Is.EqualTo("success"));
        }

        [Test]
        public void Execute_EchoesRequestIdInResponse() {
            var uniqueId = "test-unique-" + System.Guid.NewGuid().ToString();
            Request.id = uniqueId;

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.CompleteResponse.id, Is.EqualTo(uniqueId));
        }

        [Test]
        public void Execute_WhenNotPlaying_TogglesIsPlayingToTrue() {
            _editor.SetInitialState(false, false);

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(_editor.IsPlayingWrites, Has.Count.EqualTo(1));
            Assert.That(_editor.IsPlayingWrites[0], Is.True);
        }

        [Test]
        public void Execute_WhenPlaying_TogglesIsPlayingToFalse() {
            _editor.SetInitialState(true, false);

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(_editor.IsPlayingWrites, Has.Count.EqualTo(1));
            Assert.That(_editor.IsPlayingWrites[0], Is.False);
        }

        [Test]
        public void Execute_EditorStatusReflectsCompiling() {
            _editor.IsCompiling = true;

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.CompleteResponse.editorStatus.isCompiling, Is.True);
        }

        [Test]
        public void Execute_EditorStatusReflectsUpdating() {
            _editor.IsUpdating = true;

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.CompleteResponse.editorStatus.isUpdating, Is.True);
        }

        [Test]
        public void Execute_WhenEnteringPlayMode_EditorStatusShowsPlaying() {
            _editor.SetInitialState(false, false);

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.CompleteResponse.editorStatus.isPlaying, Is.True,
                "Should report intended state (playing) even if editor hasn't transitioned yet");
        }

        [Test]
        public void Execute_WhenExitingPlayMode_EditorStatusShowsStopped() {
            _editor.SetInitialState(true, false);

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.CompleteResponse.editorStatus.isPlaying, Is.False,
                "Should report intended state (stopped) even if editor hasn't transitioned yet");
            Assert.That(Responses.CompleteResponse.editorStatus.isPaused, Is.False,
                "Should clear paused state when exiting play mode");
        }
    }
}
