using System;
using UnityBridge.Models;

namespace UnityBridge.Commands {
    public interface ICommand {
        void Execute(CommandRequest request, Action<CommandResponse> onProgress, Action<CommandResponse> onComplete);
    }
}
