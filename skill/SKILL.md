---
name: unity-bridge
description: >-
  Control the Unity Editor from DeepSeek Harness — run EditMode/PlayMode tests, compile scripts,
  refresh assets, read console logs, check editor status, control Play Mode, and build — through
  the agents-unity-bridge file-based protocol.
whenToUse: >-
  Use when working on or asking about a Unity project: running tests, checking compilation or
  console errors, refreshing assets, entering Play Mode, or building. Requires the
  com.agents-unity-bridge package installed in the Unity project.
---

# Unity Bridge Skill

Control Unity Editor operations from DeepSeek Harness using a reliable file-based communication protocol.

## Overview

The Unity Bridge enables DeepSeek Harness to trigger operations in a running Unity Editor instance without network configuration or port conflicts. It uses a simple file-based protocol where commands are written to `.agents-unity-bridge/command.json` and responses are read from `.agents-unity-bridge/response-{id}.json`.

**Key Features:**
- Execute EditMode and PlayMode tests
- Trigger script compilation
- Refresh asset database
- Check editor status (compilation, play mode, etc.)
- Retrieve Unity console logs
- Control Play Mode (play, pause, step)
- Build projects (direct or custom pipeline)
- Asset dependency analysis (dependencies, references, unused assets, path tracing, search, asset info)
- Prefab management (inspect metadata, dump hierarchy, create from scene)
- Asset inspector dump (Inspector-visible serialized field values of prefabs, assets, and scenes)

**Multi-Project Support:** Each Unity project has its own `.agents-unity-bridge/` directory, allowing multiple projects to be worked on simultaneously.

## Requirements

1. **Unity Package:** Install `com.agents-unity-bridge` in your Unity project
   - Via Package Manager: `https://github.com/WarrenMondeville/agents-unity-bridge.git?path=package`
   - See main package README for installation instructions

2. **Unity Editor:** Must be open with your project loaded

3. **Python 3:** The skill uses a Python script for reliable command execution

## How It Works

The skill uses a CLI tool (`agents-unity-bridge`) that handles:
- UUID generation for command tracking
- Atomic file writes to prevent corruption
- Exponential backoff polling for responses
- File locking handling
- Automatic cleanup of old response files
- Formatted, human-readable output

This approach ensures **deterministic, rock-solid execution** - the script is tested once and behaves identically every time, handling all edge cases (timeouts, file locking, malformed responses, etc.) without requiring DeepSeek Harness to manage these details in-context.

## Usage

### Basic Pattern

When you need to interact with Unity, use the CLI directly:

```bash
agents-unity-bridge [command] [options]
```

All commands automatically:
- Generate a unique UUID for tracking
- Write the command atomically
- Poll for response with timeout
- Format output for readability
- Cleanup response files

### Command Examples

#### Run Tests

Execute Unity tests in EditMode or PlayMode:

```bash
# Run all EditMode tests
agents-unity-bridge run-tests --mode EditMode

# Run tests with filter
agents-unity-bridge run-tests --mode EditMode --filter "MyProject.Tests"

# Run all tests (both modes)
agents-unity-bridge run-tests
```

**Output:**
```
✓ Tests Passed: 410
✗ Tests Failed: 2
○ Tests Skipped: 0
Duration: 1.25s

Failed Tests:
  - MyProject.Tests.AuthTests.LoginWithInvalidCredentials
    Expected: success, Actual: failure
  - MyProject.Tests.NetworkTests.TimeoutHandling
    NullReferenceException: Object reference not set
```

**Parameters:**
- `--mode` - `EditMode` or `PlayMode` (optional, defaults to both)
- `--filter` - Test name filter pattern (optional)
- `--timeout` - Override default 30s timeout

#### Compile Scripts

Trigger Unity script compilation:

```bash
agents-unity-bridge compile
```

**Output (Success):**
```
✓ Compilation Successful
Duration: 2.3s
```

**Output (Failure):**
```
✗ Compilation Failed

Assets/Scripts/Player.cs:25: error CS0103: The name 'invalidVar' does not exist
Assets/Scripts/Enemy.cs:67: error CS0246: Type 'MissingClass' could not be found
```

#### Get Console Logs

Retrieve Unity console output:

```bash
# Get last 20 logs
agents-unity-bridge get-console-logs --limit 20

# Get only errors
agents-unity-bridge get-console-logs --limit 10 --filter Error

# Get warnings
agents-unity-bridge get-console-logs --filter Warning
```

**Output:**
```
Console Logs (last 10, filtered by Error):

[Error] NullReferenceException: Object reference not set
  at Player.Update() in Assets/Scripts/Player.cs:34

[Error] Failed to load asset: missing_texture.png

[Error] (x3) Shader compilation failed
  See Console for details
```

**Parameters:**
- `--limit` - Maximum number of logs (default: 50)
- `--filter` - Filter by type: `Log`, `Warning`, or `Error`

#### Get Editor Status

Check Unity Editor state:

```bash
agents-unity-bridge get-status
```

**Output:**
```
Unity Editor Status:
  - Compilation: ✓ Ready
  - Play Mode: ✏ Editing
  - Updating: No
```

**Possible States:**
- Compilation: `✓ Ready` or `⏳ Compiling...`
- Play Mode: `✏ Editing`, `▶ Playing`, or `⏸ Paused`
- Updating: `Yes` or `No`

#### Refresh Asset Database

Force Unity to refresh assets:

```bash
agents-unity-bridge refresh
```

**Output:**
```
✓ Asset Database Refreshed
Duration: 0.5s
```

#### Play Mode Control

Toggle Play Mode, pause, and step through frames:

```bash
# Enter/exit Play Mode (toggle)
agents-unity-bridge play

# Pause/unpause (while in Play Mode)
agents-unity-bridge pause

# Step one frame (while in Play Mode)
agents-unity-bridge step
```

**Output (play):**
```
✓ play completed
Play Mode: ▶ Playing
Duration: 0.01s
```

**Output (pause):**
```
✓ pause completed
Play Mode: ⏸ Paused
Duration: 0.01s
```

**Output (step):**
```
✓ step completed
Play Mode: ⏸ Paused
Duration: 0.02s
```

**Notes:**
- `play` toggles Play Mode on/off (like the Play button in Unity)
- `pause` and `step` require Play Mode to be active; returns error if not playing
- All three return the resulting `editorStatus` so the caller knows the current state

#### Build Project

Build the Unity project using either direct `BuildPipeline.BuildPlayer()` or a custom build method:

```bash
# Direct build with current active target
agents-unity-bridge build

# Direct build for specific target
agents-unity-bridge build --target Android --development

# Custom build pipeline via static method invocation
agents-unity-bridge build --method MyProject.Builder.BuildEntryPoints.BuildQuest

# With environment variables
agents-unity-bridge build --method MyProject.Builder.BuildEntryPoints.BuildQuest --env BUILD_TYPE=production --env SCRIPTING_BACKEND=il2cpp

# Using a named build profile (from .agents-unity-bridge/build.json)
agents-unity-bridge build --profile quest
```

**Output (Success):**
```
✓ Build Succeeded
Errors: 0
Warnings: 3
Build Time: 45.2s
Output: /path/to/Build_Android.apk
Size: 50.0 MB
Duration: 45.50s
```

**Output (Failure):**
```
✗ Build Failed
Errors: 5
Warnings: 2
Build Time: 30.0s
Duration: 30.50s

Build Failed: 5 error(s), 2 warning(s)
```

**Parameters:**
- `--method` - Fully qualified static method (e.g., `MyProject.Builder.BuildEntryPoints.BuildQuest`)
- `--target` - BuildTarget enum name (e.g., `Android`, `StandaloneWindows64`, `iOS`)
- `--development` - Enable development build flag
- `--env` - Environment variable `KEY=VALUE` (repeatable)
- `--profile` - Named profile from `.agents-unity-bridge/build.json`
- `--output` - Override output path
- `--timeout` - Override default 300s timeout

**Build Profiles:**

For projects with custom build pipelines, create `.agents-unity-bridge/build.json` to define named profiles:

```json
{
  "profiles": {
    "quest": {
      "method": "MyProject.Builder.BuildEntryPoints.BuildQuest",
      "env": { "BUILD_TYPE": "development", "SCRIPTING_BACKEND": "il2cpp" },
      "timeout": 600
    },
    "pico": {
      "method": "MyProject.Builder.BuildEntryPoints.BuildPico"
    }
  }
}
```

**Notes:**
- Default timeout is 5 minutes (300s) — builds are long-running operations
- Direct builds use the currently active build target if `--target` is not specified
- Environment variables are set before method invocation and cleaned up after
- Profile settings are defaults; CLI arguments override them

#### Asset Dependency Analysis

Analyze asset references, find unused assets, and trace dependency paths — powered by Unity's own `AssetDatabase`, so results are always live and exact.

```bash
# What does this asset depend on? (direct by default, --recursive for the full closure)
agents-unity-bridge get-dependencies --asset Assets/Prefabs/Player.prefab --recursive

# What references this asset? (impact analysis before changing/deleting it)
agents-unity-bridge find-references --asset Assets/Materials/Player.mat

# Which assets are unreachable from build scenes + Resources? (unused-asset candidates)
agents-unity-bridge find-unused-assets

# Is there a dependency chain from A to B? (shortest path via BFS)
agents-unity-bridge trace-path --from Assets/Scenes/Main.unity --to Assets/Materials/Fx.mat

# Find assets by name/type
agents-unity-bridge search-assets --query "Player" --type Prefab --limit 20

# Identity + dependency metrics for one asset
agents-unity-bridge get-asset-info --asset Assets/Prefabs/Player.prefab
```

**Output (get-dependencies):**
```
✓ Dependencies (recursive) for: Assets/Prefabs/Player.prefab
Count: 14
Duration: 0.12s

  - Assets/Materials/Player.mat
  - Assets/Textures/Player_Diffuse.png
  ...
```

**Notes:**
- `asset`/`from`/`to` accept either a project path (`Assets/...`) or a 32-character GUID
- `find-references` scans all project assets (Unity has no reverse index); add `--include-packages` to also scan `Packages/`
- `find-unused-assets` treats enabled build scenes and `Resources/` folders as roots; `.cs`/`.asmdef` files are excluded (their usage is code-level, not asset-graph-level)
- All six commands are read-only and remain available while Unity is compiling

#### Prefab Management

Inspect prefab metadata, dump its hierarchy, and create prefab assets from scene GameObjects — powered by Unity's `PrefabUtility`.

```bash
# Prefab metadata (GUID, type, root components, child count, variant info)
agents-unity-bridge manage-prefabs --action get-info --prefab-path Assets/Prefabs/Player.prefab

# Full prefab hierarchy (names, paths, components, nested-prefab info)
agents-unity-bridge manage-prefabs --action get-hierarchy --prefab-path Assets/Prefabs/Player.prefab

# Create a prefab asset from a scene GameObject
agents-unity-bridge manage-prefabs --action create --object Player --prefab-path Assets/Prefabs/Player.prefab
```

**Output (get-info):**
```
✓ Prefab: Assets/Prefabs/Player.prefab
  GUID: abc123def456...
  Type: Regular
  Root: Player
  Child Objects: 5
  Root Components (3):
    - UnityEngine.Transform
    ...
```

**Notes:**
- `create` searches the active scene (or current prefab stage) for the named GameObject; add `--search-inactive` to include inactive objects
- `--allow-overwrite` replaces an existing prefab at the same path (otherwise a unique path is generated)
- `--unlink-if-instance` unpacks an existing prefab instance before saving a new prefab
- `get-info` / `get-hierarchy` are read-only; `create` mutates the asset database

#### Asset Inspector Dump

Dump Inspector-visible serialized field values of a prefab, asset, or scene — powered by `SerializedObject` / `SerializedProperty`, so it always reflects Unity's live data (no text serialization required).

```bash
# Prefab: full GameObject hierarchy + per-component field values
agents-unity-bridge dump-asset --asset Assets/Prefabs/BatPF.prefab

# ScriptableObject / other .asset
agents-unity-bridge dump-asset --asset Assets/Data/SampleData.asset

# Scene (the currently open scene)
agents-unity-bridge dump-asset --asset Assets/Scenes/Main.unity
```

**Output (prefab):**
```
✓ Asset Dump: Assets/Prefabs/BatPF.prefab (prefab, 3 objects)

BatPF
  Transform:
    localPosition: (27.13, -6.38, 0)
  Rigidbody2D:
    mass: 3
    bodyType: Dynamic
Data
  EntityData:
    _entityName: Bat
    _walkSpeed: 1.5
```

**Notes:**
- Outputs only **Inspector-visible** fields (Unity internal fields are filtered automatically)
- Field names are cleaned (`m_LocalPosition` → `localPosition`); MonoBehaviour/ScriptableObject show their script name
- Values are type-aware: vectors as `(x, y, z)`, enums as names, object references as asset paths, arrays as `[N items]`
- `.unity` dumps the **currently open** scene (no scene loading); read-only, available while compiling

### Advanced Options

#### Timeout Configuration

Override the default 30-second timeout:

```bash
agents-unity-bridge run-tests --timeout 60
```

Use longer timeouts for:
- Large test suites
- PlayMode tests (which start/stop Play Mode)
- Full project compilation

#### Cleanup Old Responses

Automatically remove old response files before executing:

```bash
agents-unity-bridge compile --cleanup
```

This removes response files older than 1 hour. Useful for maintaining a clean workspace.

#### Verbose Output

See detailed execution progress:

```bash
agents-unity-bridge run-tests --verbose
```

Prints:
- Command ID
- Polling attempts
- Response file detection
- Cleanup operations

### Error Handling

The script provides clear error messages for common issues:

**Unity Not Running:**
```
Error: Unity Editor not detected. Ensure Unity is open with the project loaded.
```

**Command Timeout:**
```
Error: Command timed out after 30s. Check Unity Console for errors.
```

**Invalid Parameters:**
```
Error: Failed to write command file: Invalid mode 'InvalidMode'
```

**Exit Codes:**
- `0` - Success
- `1` - Error (Unity not running, invalid params, etc.)
- `2` - Timeout

## Integration with DeepSeek Harness

When you're working in a Unity project directory, you can ask DeepSeek Harness to perform Unity operations naturally:

- "Run the Unity tests in EditMode"
- "Check if there are any compilation errors"
- "Show me the last 10 error logs from Unity"
- "Refresh the Unity asset database"
- "Enter Play Mode"
- "Pause the editor"
- "Step one frame"
- "Build for Android"
- "Run the Quest build"
- "Set up build profiles for my project"
- "What references this material?"
- "Which assets are unused?"
- "Find the dependency path between this scene and this material"

DeepSeek Harness will automatically use this skill to execute the commands via the Python script.

## File Protocol Details

### Command Format

Written to `.agents-unity-bridge/command.json`:

```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "action": "run-tests",
  "params": {
    "testMode": "EditMode",
    "filter": "MyTests"
  }
}
```

### Response Format

Read from `.agents-unity-bridge/response-{id}.json`:

```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "status": "success",
  "action": "run-tests",
  "duration_ms": 1250,
  "result": {
    "passed": 410,
    "failed": 0,
    "skipped": 0,
    "failures": []
  }
}
```

**Status Values:**
- `running` - Command in progress (may see intermediate responses)
- `success` - Command completed successfully
- `failure` - Command completed with failures (e.g., failed tests)
- `error` - Command execution error

## Project Structure

```
skill/
├── SKILL.md                    # This file
├── pyproject.toml              # Package configuration
├── src/
│   └── agents_unity_bridge/
│       ├── __init__.py         # Package version
│       └── cli.py              # CLI implementation
├── tests/
│   └── test_cli.py             # Unit tests
└── references/
    ├── COMMANDS.md             # Detailed command specifications
    └── EXTENDING.md            # Guide for adding custom commands
```

## Detailed Documentation

For more information, see:

- **[COMMANDS.md](references/COMMANDS.md)** - Complete command reference with all parameters, response formats, and edge cases
- **[EXTENDING.md](references/EXTENDING.md)** - Tutorial for adding custom commands to the Unity Bridge for project-specific workflows

## Troubleshooting

### Unity Not Responding

**Symptoms:** Commands timeout or "Unity not detected" error

**Solutions:**
1. Ensure Unity Editor is open with the project loaded
2. Check that the package is installed (`Window > Package Manager`)
3. Verify `.agents-unity-bridge/` directory exists in project root
4. Check Unity Console for errors from AgentsBridge package

### Response File Issues

**Symptoms:** "Failed to parse response JSON" error

**Solutions:**
1. Check Unity Console for AgentsBridge errors
2. Manually inspect `.agents-unity-bridge/response-*.json` files
3. Try cleaning up old responses with `--cleanup` flag
4. Restart Unity Editor if file system is in bad state

### Performance Issues

**Symptoms:** Slow response times, frequent timeouts

**Solutions:**
1. Increase timeout with `--timeout 60` or higher
2. Close unnecessary Unity Editor windows
3. Reduce test scope with `--filter` parameter
4. Check system resources (CPU, memory)

### File Locking Errors

**Symptoms:** Intermittent errors reading/writing files

**Solutions:**
1. The CLI handles file locking automatically with retries
2. If persistent, check for antivirus interference
3. Verify file permissions on `.agents-unity-bridge/` directory

## Installation

### Quick Install

```bash
pip install agents-unity-bridge
agents-unity-bridge install-skill
```

This installs the CLI and the DeepSeek Harness skill.

### Verify Setup

```bash
agents-unity-bridge health-check
```

### Updating

```bash
agents-unity-bridge update
```

This upgrades the pip package and reinstalls the skill.

### Uninstalling

```bash
agents-unity-bridge uninstall-skill
pip uninstall agents-unity-bridge
```

### Development Installation

```bash
cd agents-unity-bridge/skill
pip install -e ".[dev]"
agents-unity-bridge install-skill
```

## Why a CLI Tool?

The skill uses a CLI tool instead of implementing the protocol directly in DeepSeek Harness prompts for several critical reasons:

**Consistency:** UUID generation, polling logic, and error handling work identically every time. Without the CLI, DeepSeek Harness might implement these differently across sessions, leading to subtle bugs.

**Reliability:** All edge cases are handled once in tested code:
- File locking when Unity writes responses
- Exponential backoff for polling
- Atomic command writes to prevent corruption
- Graceful handling of malformed JSON
- Proper cleanup of stale files

**Error Messages:** Clear, actionable error messages for all failure modes. DeepSeek Harness doesn't have to figure out what went wrong each time.

**Token Efficiency:** The CLI handles complexity, so DeepSeek Harness doesn't need to manage low-level details in-context. The SKILL.md stays concise while providing full functionality.

**Deterministic Exit Codes:** Shell integration works reliably with standard exit codes (0=success, 1=error, 2=timeout).

**Rock Solid:** Test the CLI once, it works forever. No variability between DeepSeek Harness sessions.

## Support

For issues or questions:
- Package Issues: https://github.com/WarrenMondeville/agents-unity-bridge/issues
- Skill Issues: Report in the same repository with `[Skill]` prefix
