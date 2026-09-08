using System.Collections.Generic;
using UnityBridge.Commands;

namespace UnityBridge.Tests.TestHelpers {
    /// <summary>
    /// Hand-written fake of <see cref="IEditorPlayMode"/> used to test play/pause/step
    /// commands without pulling in the external Moq dependency.
    ///
    /// Property writes and <see cref="Step"/> calls are recorded so tests can assert
    /// on the exact mutations a command performs, and <see cref="SetInitialState"/>
    /// seeds the editor's pre-existing state without recording a write.
    /// </summary>
    public class FakeEditorPlayMode : IEditorPlayMode {
        /// <summary>Every value assigned to <see cref="IsPlaying"/>, in order.</summary>
        public List<bool> IsPlayingWrites { get; } = new List<bool>();

        /// <summary>Every value assigned to <see cref="IsPaused"/>, in order.</summary>
        public List<bool> IsPausedWrites { get; } = new List<bool>();

        /// <summary>How many times <see cref="Step"/> was called.</summary>
        public int StepCallCount { get; private set; }

        private bool _isPlaying;
        private bool _isPaused;

        public bool IsPlaying {
            get => _isPlaying;
            set {
                _isPlaying = value;
                IsPlayingWrites.Add(value);
            }
        }

        public bool IsPaused {
            get => _isPaused;
            set {
                _isPaused = value;
                IsPausedWrites.Add(value);
            }
        }

        public bool IsCompiling { get; set; }
        public bool IsUpdating { get; set; }

        public void Step() {
            StepCallCount++;
        }

        /// <summary>
        /// Seed the editor's pre-existing state without recording a property write,
        /// so tests can distinguish initial state from mutations made by a command.
        /// </summary>
        public void SetInitialState(bool playing, bool paused) {
            _isPlaying = playing;
            _isPaused = paused;
        }
    }
}
