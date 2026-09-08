using UnityBridge.Commands;
using UnityBridge.Models;
using UnityBridge.Tests.TestHelpers;
using NUnit.Framework;

namespace UnityBridge.Tests.Commands {
    /// <summary>
    /// Tests for StepCommand using a hand-written FakeEditorPlayMode (no Moq dependency).
    /// Focus: Precondition check (must be playing), Step() call verification, response construction.
    /// </summary>
    [TestFixture]
    public class StepCommandTests : CommandTestFixture {
        private FakeEditorPlayMode _editor;
        private StepCommand _command;

        [SetUp]
        public override void SetUp() {
            base.SetUp();
            _editor = new FakeEditorPlayMode();
            _command = new StepCommand(_editor);
            Request.action = "step";
        }

        [Test]
        public void Execute_WhenNotPlaying_ReturnsError() {
            _editor.SetInitialState(false, false);

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.HasCompleteResponse, Is.True);
            Assert.That(Responses.CompleteResponse.status, Is.EqualTo("error"));
            Assert.That(Responses.CompleteResponse.error, Does.Contain("not in Play Mode"));
        }

        [Test]
        public void Execute_WhenNotPlaying_ErrorMentionsPlayCommand() {
            _editor.SetInitialState(false, false);

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.CompleteResponse.error, Does.Contain("play"));
        }

        [Test]
        public void Execute_WhenNotPlaying_DoesNotCallStep() {
            _editor.SetInitialState(false, false);

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(_editor.StepCallCount, Is.EqualTo(0));
        }

        [Test]
        public void Execute_CallsOnCompleteExactlyOnce() {
            _editor.SetInitialState(true, false);
            var callCount = 0;
            System.Action<CommandResponse> countingCallback = (response) => { callCount++; };

            _command.Execute(Request, Responses.OnProgress, countingCallback);

            Assert.That(callCount, Is.EqualTo(1));
        }

        [Test]
        public void Execute_DoesNotCallOnProgress() {
            _editor.SetInitialState(true, false);

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.ProgressResponses.Count, Is.EqualTo(0));
        }

        [Test]
        public void Execute_ConstructsResponseWithCorrectIdAndAction() {
            _editor.SetInitialState(true, false);

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.CompleteResponse.id, Is.EqualTo(Request.id));
            Assert.That(Responses.CompleteResponse.action, Is.EqualTo(Request.action));
        }

        [Test]
        public void Execute_WhenPlaying_ReturnsSuccess() {
            _editor.SetInitialState(true, false);

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.CompleteResponse.status, Is.EqualTo("success"));
        }

        [Test]
        public void Execute_WhenPlaying_CallsStep() {
            _editor.SetInitialState(true, false);

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(_editor.StepCallCount, Is.EqualTo(1));
        }

        [Test]
        public void Execute_WhenPlaying_IncludesEditorStatus() {
            _editor.SetInitialState(true, false);

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.CompleteResponse.editorStatus, Is.Not.Null);
        }

        [Test]
        public void Execute_EditorStatusReflectsCompiling() {
            _editor.SetInitialState(true, false);
            _editor.IsCompiling = true;

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.CompleteResponse.editorStatus.isCompiling, Is.True);
        }

        [Test]
        public void Execute_ResponseHasDuration() {
            _editor.SetInitialState(true, false);

            _command.Execute(Request, Responses.OnProgress, Responses.OnComplete);

            Assert.That(Responses.CompleteResponse.duration_ms, Is.GreaterThanOrEqualTo(0));
        }
    }
}
