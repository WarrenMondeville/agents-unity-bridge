# Unity Bridge Protocol Specification

This document defines the file-based protocol used by **Agents Unity Bridge** to let any
external program (AI agent, CI runner, build script, or interactive shell) drive a running
Unity Editor. It is intentionally language- and agent-agnostic: any client that can read and
write JSON files in a well-known directory can speak this protocol.

The protocol is versioned as `1` (see [Versioning](#versioning)).

---

## 1. Overview

A **client** and the **Unity Editor** communicate through a per-project directory named
`.agents-unity-bridge/` located at the Unity project root (the directory that contains
`Assets/` and `ProjectSettings/`).

Communication is request/response with exactly one in-flight request at a time:

```
client                          Unity Editor (AgentsBridge)
  │                                      │
  │ 1. write command.json (atomic)       │
  │ ─────────────────────────────────────>
  │                                      │ 2. poll command.json every editor frame
  │                                      │ 3. delete command.json, execute
  │ 4. poll response-<id>.json           │ 5. write response-<id>.json (atomic)
  │ <─────────────────────────────────────
  │ 6. read response, delete it          │
```

Both sides write files **atomically** (write to a `.tmp` file, then rename over the target).
A reader therefore never observes a partially-written JSON file.

---

## 2. Directory Layout

```
<UnityProjectRoot>/.agents-unity-bridge/
├── command.json              # the single pending request (may not exist)
├── response-<uuid>.json      # one response per command id (transient)
└── build.json                # OPTIONAL: build profiles (see build command)
```

- The directory is created by the Unity bridge on startup and by the client on demand.
- It should be listed in `.gitignore` (runtime files only).
- Each Unity project has its own directory; multiple projects can be driven independently.

---

## 3. Request (Command)

A request is a single JSON object written to `command.json`.

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

| Field    | Type   | Required | Description |
|----------|--------|----------|-------------|
| `id`     | string | yes      | Unique request identifier. Clients MUST use a UUID. |
| `action` | string | yes      | Command name (see [Commands](#6-commands)). |
| `params` | object | no       | Command-specific parameters. All values are strings (see note). |

> **String convention:** Unity deserializes with `JsonUtility`, which models every
> parameter field as a `string`. Booleans and integers are therefore sent as the strings
> `"true"` / `"false"` and `"42"` rather than JSON literals.

---

## 4. Response

A response is written to `response-<id>.json` where `<id>` matches the request `id`.

```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "status": "success",
  "action": "run-tests",
  "duration_ms": 1250,
  "result": { "passed": 410, "failed": 0, "skipped": 0, "failures": [] }
}
```

### 4.1 Common fields

| Field         | Type   | Description |
|---------------|--------|-------------|
| `id`          | string | Echo of the request id. |
| `status`      | string | One of `running`, `success`, `failure`, `error`. |
| `action`      | string | Echo of the request action. |
| `duration_ms` | number | Wall-clock duration in milliseconds (absent on `error`). |
| `error`       | string | Present on `failure`/`error`, and always on `error`. |
| `progress`    | object | Present on `running` progress updates (see below). |

### 4.2 Status values

| Status    | Meaning |
|-----------|---------|
| `running` | Command is still executing. A client MUST keep polling. Long commands emit progress updates. |
| `success` | Completed successfully. |
| `failure` | Completed but reported failures (e.g. failed tests, failed build). |
| `error`   | Command could not run (unknown action, invalid params, busy editor, exception). |

### 4.3 Progress (`running`) updates

```json
{ "current": 12, "total": 410, "currentTest": "MyTests.Player.Move" }
```

| Field         | Type   | Description |
|---------------|--------|-------------|
| `current`     | number | Items completed so far. |
| `total`       | number | Total items (0 when unknown). |
| `currentTest` | string | Optional: currently running test name. |

---

## 5. Concurrency & Editor-State Safety

- **One command at a time.** If a command arrives while another is processing, the editor
  deletes the new request and responds `status: "error"` with a "busy" message.
- **Compilation / asset-update guard.** While the editor is compiling or updating, only
  read-only commands are accepted; mutating commands are rejected with `error`.
  Read-only commands: `get-status`, `get-console-logs`, `get-dependencies`,
  `find-references`, `find-unused-assets`, `trace-path`, `search-assets`, `get-asset-info`,
  `dump-asset`.
- **Stuck-command recovery.** A command that runs longer than 5 minutes is force-reset
  server-side; the client should enforce its own timeout (recommended 30s default, 300s for
  builds).

---

## 6. Commands

### 6.1 `run-tests` — run Unity tests

**Params**

| Param      | Description |
|------------|-------------|
| `testMode` | `"EditMode"`, `"PlayMode"`, or absent for both. |
| `filter`   | Optional. Semicolon-separated test name filters. |

**Response** — `result`:

| Field      | Type   | Description |
|------------|--------|-------------|
| `passed`   | number | |
| `failed`   | number | |
| `skipped`  | number | |
| `failures` | array  | `{ "name": string, "message": string }` entries. |

Status is `failure` when `failed > 0`.

### 6.2 `compile` — trigger script compilation

No params. Status `success`/`failure`; `error` contains compiler errors on `failure`.

### 6.3 `refresh` — refresh the asset database

No params.

### 6.4 `get-status` — read editor state

No params. **Response** — `editorStatus`:

| Field        | Type    | Description |
|--------------|---------|-------------|
| `isCompiling`| boolean | |
| `isUpdating` | boolean | |
| `isPlaying`  | boolean | |
| `isPaused`   | boolean | |

### 6.5 `get-console-logs` — read Unity console

**Params**

| Param    | Description |
|----------|-------------|
| `limit`  | Optional. Max entries (default 50). |
| `filter` | Optional. `"Log"`, `"Warning"`, or `"Error"`. |

**Response** — `consoleLogs`: array of
`{ "message", "stackTrace", "type", "count" }`.

### 6.6 `play` / `pause` / `step` — Play Mode control

No params. `play` toggles Play Mode; `pause` toggles pause (requires Play Mode); `step`
advances one frame (requires Play Mode). All return the resulting `editorStatus`.

### 6.7 `build` — build the project

**Params**

| Param         | Description |
|---------------|-------------|
| `method`      | Optional. Fully-qualified static method `Namespace.Class.Method` to invoke. When absent, a direct `BuildPipeline.BuildPlayer` is used. |
| `target`      | Optional. `BuildTarget` enum name (`Android`, `StandaloneWindows64`, ...). Defaults to the active target. |
| `development` | Optional. `"true"` for a development build. |
| `env`         | Optional. Semicolon-separated `KEY=VALUE` pairs set before invoking `method`. |
| `output`      | Optional. Output path override (direct builds only). |

**Response** — `buildInfo`:

| Field          | Type    | Description |
|----------------|---------|-------------|
| `buildResult`  | string  | `Succeeded` / `Failed` / `Cancelled` / `Unknown`. |
| `totalErrors`  | number  | |
| `totalWarnings`| number  | |
| `totalSeconds` | number  | Build duration. |
| `outputPath`   | string  | |
| `sizeBytes`    | number  | Total build size. |
| `method`       | string  | `"direct"` or the invoked method path. |

Optional `build.json` profiles (client-side): `{ "profiles": { "<name>": { "method", "env", "timeout" } } }`.

### 6.8 Asset dependency analysis

#### `get-dependencies`

**Params:** `asset` (path or GUID), `recursive` (`"true"` for transitive).

**Response** — `assetDependencies`: `{ "asset", "dependencies": [string], "count", "recursive" }`.

#### `find-references`

**Params:** `asset`, `includePackages` (`"true"`).

**Response** — `assetReferences`: `{ "asset", "references": [string], "count" }`.

#### `find-unused-assets`

**Params:** `includePackages` (`"true"`).

**Response** — `unusedAssets`: `{ "unusedAssets": [string], "totalAssets", "unusedCount", "roots": [string] }`.

#### `trace-path`

**Params:** `from`, `to`, `maxDepth`.

**Response** — `tracePath`: `{ "from", "to", "path": [string], "depth", "found" }`.

#### `search-assets`

**Params:** `query`, `type`, `limit`.

**Response** — `searchResult`: `{ "query", "results": [string], "count" }`.

#### `get-asset-info`

**Params:** `asset`.

**Response** — `assetInfo`: `{ "path", "guid", "type", "sizeBytes", "directDependencyCount", "dependencyCount" }`.

#### `dump-asset`

**Params:** `asset`.

**Response** — `assetDump`:
`{ "asset", "assetType" ("prefab"|"scene"|"asset"), "rootName", "gameObjectCount", "gameObjects", "components", "message" }`.

### 6.9 `manage-prefabs` — prefab metadata / hierarchy / create

**Params**

| Param             | Description |
|-------------------|-------------|
| `prefabAction`    | `"get-info"` \| `"get-hierarchy"` \| `"create"`. |
| `prefabPath`      | Prefab asset path. |
| `objectName`      | (create) Scene GameObject name to create from. |
| `searchInactive`  | (create) `"true"` to include inactive objects. |
| `allowOverwrite`  | (create) `"true"`. |
| `unlinkIfInstance`| (create) `"true"`. |

**Response** — `prefabResult` (see `PrefabResult` in `CommandResponse.cs` for the full
field set, which varies by `prefabAction`).

---

## 7. CLI Contract (agent integration)

The reference client is the Python CLI `agents-unity-bridge` (package
`agents_unity_bridge`). Any agent can use it, or re-implement the file protocol directly.

```bash
agents-unity-bridge <action> [options] [--project DIR] [--format human|json] [--timeout SEC] [--verbose]
```

- **Project selection** (highest to lowest priority):
  1. `--project DIR`
  2. environment variable `UNITY_BRIDGE_PROJECT`
  3. walk up from the current directory looking for `Assets/` + `ProjectSettings/`
  4. current directory
- **Output format:** `--format human` (default, decorated text) or `--format json`
  (the raw response object, machine-parseable).
- **Exit codes:** `0` success · `1` error (Unity not running, invalid params, command failed) · `2` timeout.

For agents that prefer structured output, use `--format json`; for humans or transcripts,
use `--format human`.

---

## 8. Security

- **Response id validation:** the response filename is `response-<id>.json`; both sides
  validate `<id>` against `^[a-fA-F0-9\-]+$` (client additionally requires a full UUID) to
  prevent path traversal.
- **Symlink rejection:** the client refuses to use `.agents-unity-bridge/` if it is a
  symlink.
- **Scope:** both sides write only inside `.agents-unity-bridge/`.
- **Permissions:** on non-Windows platforms the directory and files are created with mode
  `0700` / `0600`.

---

## 9. Versioning

The protocol itself is not yet stamped in the wire format; this document describes the
behavior of the current bridge (`com.agents-unity-bridge` 0.7.x). A future release may add
an explicit `protocolVersion` field to requests/responses for negotiation. Until then, treat
the field names and semantics in this document as the compatibility contract.
