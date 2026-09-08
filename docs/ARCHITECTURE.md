# Architecture

## File Structure

```
package/                     # Unity package (UPM)
├── Editor/
│   ├── AgentsBridge.cs          # Main coordinator
│   ├── AgentsBridge.asmdef      # Assembly definition
│   ├── Commands/
│   │   ├── ICommand.cs          # Command interface
│   │   ├── RunTestsCommand.cs   # Test execution
│   │   ├── CompileCommand.cs    # Script compilation
│   │   ├── RefreshCommand.cs    # Asset refresh
│   │   ├── GetStatusCommand.cs  # Editor status
│   │   ├── GetConsoleLogsCommand.cs  # Console logs
│   │   ├── BuildCommand.cs      # Build (direct or custom method)
│   │   ├── AssetAnalysisUtil.cs # Shared path/bool/int helpers
│   │   ├── GetDependenciesCommand.cs  # Forward dependencies
│   │   ├── FindReferencesCommand.cs   # Reverse references
│   │   ├── FindUnusedAssetsCommand.cs # Unreachable assets
│   │   ├── TracePathCommand.cs        # Shortest dependency path (BFS)
│   │   ├── SearchAssetsCommand.cs     # Asset search
│   │   ├── GetAssetInfoCommand.cs     # Single-asset identity/metrics
│   │   ├── ManagePrefabsCommand.cs    # Prefab metadata/hierarchy/create
│   │   └── DumpAssetCommand.cs        # Inspector-visible field dump (prefab/asset/scene)
│   └── Models/
│       ├── CommandRequest.cs    # Request DTO
│       └── CommandResponse.cs   # Response DTO
└── Tests/                   # Unity tests

skill/                       # AI agent skill (Python)
├── src/
│   └── agents_unity_bridge/
│       └── cli.py           # Deterministic command script
├── references/
│   ├── COMMANDS.md          # Complete command specification
│   └── EXTENDING.md         # Guide for adding custom commands
└── SKILL.md                 # Skill documentation
```

## Command Pattern

All commands implement `ICommand`:

```csharp
public interface ICommand {
    void Execute(
        CommandRequest request,
        Action<CommandResponse> onProgress,
        Action<CommandResponse> onComplete
    );
}
```

This enables:
- Progress reporting during execution
- Consistent error handling
- Easy extension with new commands

## Adding New Commands

See [skill/references/EXTENDING.md](../skill/references/EXTENDING.md) for a complete guide on adding custom commands.
