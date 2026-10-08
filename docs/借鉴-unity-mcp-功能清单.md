# 借鉴 unity-mcp 功能清单（agents-unity-bridge）

> 研究对象：`F:\unity-mcp`（CoplayDev **MCP for Unity** v10.3.0）
> 研究目的：找出 unity-mcp 中**值得 agents-unity-bridge 借鉴**的功能与工程实践
> 本文性质：**只做调研与记录，不含任何代码改动**
> 研究方式：主代理通读关键源码 + 3 个并行只读子代理深挖（Unity C# 侧、Python Server 侧、工程/CI/测试基建），关键论断由主代理逐条独立复核

---

## 0. 结论速览

unity-mcp 是**创作型**桥接（50 个 MCP 工具 + 25 个 resources，建对象/改脚本/管资源/生成资产），我们是**验证型**（17 个命令，跑测试/编译/看日志/资产分析）。**大部分"功能"与我们的定位冲突，不该盲目照搬**；真正该借的是**工程实践**与少数**验证类功能**。

| # | 借鉴点 | 类型 | 价值 | 成本 |
|---|---|---|---|---|
| 1 | 免许可 Roslyn 编译门禁 + 多版本矩阵 | CI | 高 | S–M |
| 2 | 就绪门禁 + `preflight` 守卫（fail-open） | 可靠性 | 高 | S |
| 3 | 结构化错误信封（`hint`/`reason`/`retry_after_ms`） | 可靠性 | 高 | XS |
| 4 | 重载感知重试（单 deadline + 状态相关退避上限 + grace） | 可靠性 | 高 | S |
| 5 | 分页契约（`cursor`/`nextCursor`/`totalCount`/`hasMore`） | 体验 | 高 | XS |
| 6 | 双份文件 drift-guard 测试 | 工程 | 高 | XS |
| 7 | 截图 / 视觉验证 | 功能 | 高 | M |
| 8 | `batch_execute`（一次往返多条命令） | 功能 | 高 | S–M |
| 9 | 域重载安全异步等待 + 编译边沿存 SessionState | 正确性 | 高 | S |
| 10 | 属性 + `TypeCache` 自动发现（免改字典） | 工程 | 高 | S |
| 11 | 参数宽容归一（snake/camel、字符串化数组、InvariantCulture） | 可靠性 | 中高 | XS |
| 12 | `get_sha` + `stale_file`（乐观并发） | 正确性 | 中高 | S |
| 13 | `job_id` + poll 契约（含 `clear_stuck` 逃逸口） | 功能 | 中高 | M |
| 14 | 客户端配置器架构（24 个，接口+基类+注册表） | 安装 | 中高 | M |
| 15 | 资源（只读状态面）概念 | 架构 | 中 | M |
| 16 | 能力分级 + `ping` 探测（优雅降级） | 体验 | 中 | S |
| 17 | 多实例路由（歧义时拒绝猜测，结构化返回候选） | 正确性 | 中 | S–M |
| 18 | 兼容 shim 政策 + 退出码分类学 + 版本单源 + 元测试 | 工程 | 中高 | XS–S |
| 19 | `unity_reflect` / `unity_docs` + 信任层级 | 功能 | 中 | M |
| 20 | `execute_menu_item` + `menu-items` 资源 | 功能 | 中 | XS–S |

成本：XS < 1h，S ≈ 半天，M ≈ 1~3 天，L ≈ 1 周。

**另有本轮顺带发现的我们自身一个潜在缺陷**，见 §7。

---

## 1. 许可前提（与上一轮 Locus 不同，这条是好消息）

| 项目 | 许可证 |
|---|---|
| **agents-unity-bridge**（本仓库） | **Apache-2.0** |
| **unity-mcp** | **MIT**（`LICENSE`：`MIT License / Copyright (c) 2025 CoplayDev`） |
| ~~Locus~~（上一轮） | ~~GPL-3.0-or-later~~ |

**MIT 与 Apache-2.0 兼容**：可以把 MIT 代码并入本项目，只需保留 MIT 版权与许可声明（并在分发时附带其许可文本）。所以**脚本级（如 `tools/compile-check.sh`）的移植比 Locus 那轮自由得多**；但完整文件复制仍应记录来源与许可，便于日后合规审查。

---

## 2. 定位差异：决定"哪些该借、哪些不该借"

| 维度 | agents-unity-bridge | unity-mcp |
|---|---|---|
| 协议 | 文件：`command.json` ↔ `response-{uuid}.json` | MCP（stdio/HTTP）→ Python FastMCP → WebSocket/HTTP → Unity |
| 工具面 | 17 个固定命令 | **50 个 MCP 工具 + 25 个 resources** |
| 定位 | **验证型**：跑测试、编译、看日志、资产分析、预制体检查 | **创作型**：建 GameObject/场景、改脚本、管资源、生成资产 |
| 进程模型 | 无宿主进程（CLI + Unity 轮询） | 需要常驻 Python server |
| 多实例 | 每工程一个 `.agents-unity-bridge/` 目录 | 注册表 + `Name@hash` + `set_active_instance` |
| C# CI | **无** | 免许可 Roslyn 编译门禁 + 多版本矩阵 |

**结论**：`manage_gameobject` / `manage_asset` / `manage_material` / `manage_physics` / `manage_vfx` … 这类创作型工具**不是我们的方向**（且用户上一轮已明确"不纳入动态 C# 执行"，同一逻辑适用于"不扩张为创作型"）。该借的是**可靠性与工程实践**，加少数**验证类功能**（截图、batch、reflect/docs）。

---

## 3. 可靠性 / 正确性层（最高优先）

### 3.1 就绪门禁 + `preflight` 守卫 ⭐

**就绪门禁**（`Server/src/services/resources/editor_state.py:178-216`）：服务端计算并返回

```python
blocking = []                     # compiling / domain_reload / running_tests / asset_refresh / stale_status
ready_for_tools = len(blocking) == 0
"advice": {
    "ready_for_tools": ...,
    "blocking_reasons": blocking,
    "recommended_retry_after_ms": 0 if ready else 500,
    "recommended_next_action": "none" if ready else "retry_later",
}
"staleness": {"age_ms": ..., "is_stale": ...}   # age > 2000ms ⇒ stale（覆盖编辑器失焦限流）
```

`stale_status` 作为**阻塞原因之一**很巧妙：状态快照本身过期（编辑器失焦被 Unity 限流）也会被识别出来，而不是把陈旧状态当实时状态用。

**`preflight` 守卫**（`Server/src/services/tools/preflight.py`，110 行）是**顶级借鉴项**：

- **声明式前置条件**：`preflight(ctx, requires_no_tests=, wait_for_no_compile=, refresh_if_dirty=, max_wait_s=30.0)`，每个工具声明自己要什么，守卫统一处理。
- **服务端强制**：注释明说 *"used by tools so they behave safely **even if the client never reads resources**"* —— 不依赖 Agent 自觉，正是"防呆"。
- **有界等待**：`wait_for_no_compile` 每 250ms 轮询，超 `max_wait_s` 后返回 busy。
- **失败即开放（fail-open）**：*"If we cannot determine readiness, fall back to proceeding (tools already contain retry logic)"* / *"Unknown state; proceed rather than blocking (avoids false positives when Unity is reachable but status isn't)"* —— 守卫读不到状态时**不阻塞**。这是刻意的取舍，值得我们照抄。
- **busy 载荷机器可读**：`MCPResponse(success=False, error="busy", message=reason, hint="retry", data={"reason": ..., "retry_after_ms": ...})`。
- **`refresh_if_dirty`**：检测到 `assets.external_changes_dirty` 就先刷新再放行（尽力而为，失败则继续）。
- **pytest 下自动 no-op**（`_in_pytest()`），保证测试套件不被守卫破坏。

**对我们的意义**：我们目前靠 `SafeDuringCompileCommands` 白名单 + 超时重试猜。改成"统一就绪判定 + 声明式前置条件 + 机器可读 retry 建议"，可把"60 秒卡死超时"变成"0.1 秒的 busy + 500ms 后重试"。

### 3.2 结构化错误信封 ⭐（XS）

`Server/src/models/models.py:6-14`：

```python
class MCPResponse(BaseModel):
    success: bool
    message: str | None = None
    error: str | None = None
    data: Any | None = None
    # Optional hint for clients about how to handle the response.
    # Supported values:
    #   - "retry": Unity is temporarily reloading; call should be retried politely.
    hint: str | None = None
```

- `hint` 是**封闭词表**（`retry`、`select_instance`），`data.reason` 是稳定 token + `retry_after_ms`。
- 我们的 `EXIT_SUCCESS/ERROR/TIMEOUT` 无法区分"Unity 正在重载，睡 250ms 再来"和"你的 JSON 写错了，别重试"，Agent 只能猜 —— 要么过早放弃，要么热循环。

### 3.3 重载感知重试（单 deadline + 状态相关退避上限 + grace）

`Server/src/transport/legacy/unity_connection.py:290-297, 471-502`：

- 用**单一 `deadline = now + total_timeout`**（`command_total_timeout=600`）预算重试，而不是"最多 N 次"。
- 退避上限**由观测到的 Unity 状态决定**：`cap = 5.0`（状态文件说 reloading）/ `0.25`（瞬时 socket 错 ECONNREFUSED/RESET/TIMEDOUT）/ 否则 `3.0`；`sleep = min(cap, jitter*(2**attempt))`，`jitter=uniform(0.1,0.3)`。**快错少等、慢重载多等**。
- 预检读状态文件，若 reloading 则直接断开已失效的 socket 并返回 retry hint，**不发包**。
- **`+grace` 规则**（`:291-325`）：客户端等待必须**长于**服务端自身超时（`requested + 5.0s`），否则会出现幽灵超时。
- 我们的退避上限是 **1 秒**，而 Unity domain reload 常需 10~20 秒 —— 我们会在 Unity 回来之前就宣布失败，而 Agent 的自然反应是重发，**恰好加重重载**。

### 3.4 域重载安全的异步等待 + 编译边沿持久化 ⭐

`MCPForUnity/Editor/Tools/RefreshUnity.cs:189-254`（子代理 A 复核，含注释原文）：

- 等的是 **`CompilationPipeline.compilationStarted` 事件**，不是轮询。
- **故意不加 `RunContinuationsAsynchronously`**，原因：否则响应的续体会被 domain reload 丢到已废弃的 synchronization context —— *"the response has to be on the wire before the compile finishes, because the domain reload follows compilationFinished directly and a cached no-change compile lasts ~110 ms"*。
- 编译边沿存 **`SessionState`**（`EditorStateCache.cs:26-43`）而非静态字段，因为静态字段随重载消亡。
- 响应里 `compile_started = null` 表示"未观测到"，**不会被误读为"没开始"**（`RefreshUnity.cs:110-119`）。

**对我们的意义**：我们的 `CompileCommand` 在编译结束时才应答，**正好落在 domain reload 会吃掉响应的窗口**；而且我们的轮询依赖 `EditorApplication.update` 与静态状态。

### 3.5 分页契约 ⭐（XS）

`MCPForUnity/Editor/Tools/Pagination.cs`：

- 请求：`PageSize = 50` 默认、`Cursor = 0`（**0 基**）；`FromParams` 同时接受 `page_size|pageSize` 与 `cursor | page_number|pageNumber`（1 基 `page_number` 转 `(n-1)*pageSize`），非法值回落到默认。
- 响应：`items, cursor, nextCursor, totalCount, pageSize` + 计算属性 `hasMore => NextCursor.HasValue`；`nextCursor = endIndex < totalCount ? endIndex : null` —— **null 是"最后一页"的显式信号**。

**我们的问题**：`get-console-logs` 只有 `--limit` 取尾部，**没有 total**，Agent 无法区分"就这些"和"还有更多"。

> 注：unity-mcp 自己也没把这个契约用一致（`ReadConsole.cs:536-554` 用**字符串** `nextCursor` + `truncated`，`ManageScene.cs` 用 `next_cursor`）。教训是**契约要定义一次、处处使用**。

### 3.6 参数宽容归一（XS）

`Server/src/services/tools/utils.py`：`coerce_bool` 接受 `true/"true"/"1"/"yes"/"on"`；`parse_json_payload` 还原被 LLM 字符串化的数组/对象；`normalize_properties` **显式拒绝** `"[object Object]"` / `"undefined"` / `"null"` / `""` 并给出可操作报错；`normalize_vector3` 接受 `[x,y,z]` / `{x,y,z}` / `"[1,2,3]"` / `"1, 2, 3"`；`normalize_color` 自动判别 0–1 与 0–255。

C# 侧 `ToolParams.cs`：snake_case ↔ camelCase **双向**解析、字符串化/双重序列化数组解包、数字用 **InvariantCulture** 解析（`"1.5"` 在 de-DE/tr-TR 下含义一致）。

**对我们的意义**：我们的 Python 写 snake_case、C# 读字面键，任何 camelCase 都会**静默失败**；Agent 手写 `command.json` 时 `"count": "5"` 会白跑一轮往返。

### 3.7 `get_sha` + `stale_file`（乐观并发）

`MCPForUnity/Editor/Tools/ManageScript.cs:555`：

```csharp
return new ErrorResponse("stale_file", new { status = "stale_file",
    expected_sha256 = preconditionSha256, current_sha256 = currentSha });
```

配合 `get_sha` 工具与 SKILL.md 的 Error Recovery 表：*"`stale_file` error → File changed since SHA → Re-fetch SHA with `get_sha`, retry"*。错误里**同时给出期望与实际哈希**，Agent 可直接重取重试。

### 3.8 多实例：歧义时拒绝猜测

`Server/src/transport/legacy/unity_connection.py:586-597` 注释原文：*"2+ instances connected and nothing pinned. **Refuse to guess** — silently routing to the most-recently-heartbeated editor lets an unbound session retarget another project's Unity (#1023)."* 错误以 `InstanceSelectionRequiredError(available_instances=...)` **结构化返回候选**（既作字段也拼进文本，便于不同客户端解析）。

我们"每工程一个目录"已是良好的隔离原语；可借的是**失败模式**：歧义 → 结构化拒绝并列出候选，绝不静默选一个。

---

## 4. 功能层（需按定位取舍）

### 4.1 截图 / 视觉验证 ⭐（能力缺口）

- `MCPForUnity/Runtime/Helpers/ScreenshotUtility.cs:51`：*"Base64-encoded PNG image data. **Only populated when `include_image` is true.**"*
- `manage_camera(action="screenshot", include_image=True, max_resolution=512)`；`batch="surround"` 一次出 6 个视角；`capture_source="scene_view"` 抓编辑器视口；`view_target`/`view_position` 免建相机定点拍摄。
- **token 预算指导**（SKILL.md）：*"Keep `max_resolution` at 256–512 to balance quality vs. token cost."*
- Python 侧把 base64 **从 JSON 文本里摘出来**作为独立 `ImageContent` 块，文本摘要删掉 `imageBase64`（`Server/src/services/tools/utils.py:405-456`）。

**意义**：UI/渲染类工作"看一眼"远胜文本描述。我们**完全没有**视觉验证能力；上一轮 Locus 也有（`unity_capture_viewport`）—— **两处独立印证**。

### 4.2 `batch_execute`（一次往返多条命令）⭐

`MCPForUnity/Editor/Tools/BatchExecute.cs`：`{commands:[{tool,params}], failFast, parallel, maxParallelism}`，**串行在主线程执行**（`parallel` 只是提示，显式拒绝真并行以保证 Unity 安全）；上限**默认 25 / 硬上限 100**，可通过 EditorPrefs（`MCPForUnity.BatchExecute.MaxCommands`）配置，并**在 editor state 里公布** `batch_execute_max_commands`（`EditorStateCache.cs:264-265`）—— 让 Agent 知道上限，而不是撞墙。

**对文件协议的收益尤其大**：我们每条命令 = 一次文件写入 + 轮询等待；把 N 次编辑合成一次往返，是**数量级的 Agent 时间节省**。

### 4.3 `unity_reflect` / `unity_docs` + 信任层级

- `unity_reflect`：实时反射查 Unity 与**项目自身**类型（`search` 类型 / `get_type` 成员摘要 / `get_member` 完整签名）。
- `unity_docs`：抓官方文档（ScriptReference / Manual / package），`lookup` 并行搜索，**不需要 Unity 连接**。
- SKILL.md 明确写下**信任层级：`reflection > project assets > docs`**，并给出工作流 `unity_reflect search → get_type → get_member → unity_docs lookup`。

**意义**：Agent 写 Unity 代码时，这比训练数据可靠得多。我们没有任何 API 发现能力。

### 4.4 `execute_menu_item` + `menu-items` 资源（XS–S）

`MCPForUnity/Editor/Tools/ExecuteMenuItem.cs:16-44`：约 50 行换来"任意编辑器菜单动作"，只有一条黑名单（`File/Quit`），菜单项非法/禁用/依赖上下文时给出诚实报错；`mcpforunity://menu-items` 资源枚举可用菜单。

**意义**：极低成本的"能力逃生口"。但它绕过固定命令白名单，应放在显式开关/分组之后。

### 4.5 资源（只读状态面）概念

25 个资源：`editor/state`、`editor/active-tool`、`editor/prefab-stage`、`editor/selection`、`editor/windows`、`instances`、`menu-items`、`tool-groups`、`custom-tools`、`project/info`、`project/layers`、`project/tags`、`scene/gameobject/{id}`、`scene/gameobject/{id}/components`、`scene/gameobject/{id}/component/{name}`、`scene/cameras`、`scene/volumes`、`rendering/stats`、`pipeline/renderer-features`、`prefab/{path}`、`prefab/{path}/hierarchy`、`prefab-api`、`scene/gameobject-api`、`tests`、`tests/{mode}`。

CLAUDE.md 的原则：*"Use Resources for Reading — Keep them smart and focused rather than 'read everything' type resources."*

**意义**：把"读"与"写"分开——只读状态便宜、可缓存、可频繁轮询；我们把 `get-status`/`dump-asset` 这类读操作也做成了命令。**渐进式读取**也在这里体现（`resources-reference.md`：先 `include_properties=false` 拿组件列表，再按需读单个组件）。

### 4.6 能力分级 + `ping` 探测（优雅降级）

- Camera 分 **Tier 1**（永远可用：create/target/lens/priority/list/screenshot）与 **Tier 2**（需 `com.unity.cinemachine`：brain/body/aim/noise/扩展/混合），并说明 *"Use `ping` to check Cinemachine availability"*。
- Graphics：*"Use `ping` to check pipeline status"*。
- UI：*"Read `mcpforunity://project/info` first to detect uGUI/TMP/Input System/UI Toolkit availability"*。
- `manage_script_capabilities`：查询脚本管理能力。

**意义**：与上一轮 Locus 的 `AccessProbe`（"measure, never assume"）**同源**——两处独立印证。

### 4.7 `job_id` + poll 契约（含逃逸口）

`run_tests` 立即返回 `job_id`（`RunTests.cs:49-58`），`get_test_job` 轮询（`GetTestJob.cs`）；`TestJobManager.cs` 把 job 状态持久化到 **SessionState**（可跨重载）、节流写入、上限 10 个 job / 25 条失败记录、60 秒 stuck 阈值、`stuck_suspected`、机器可读 `blocked_reason` 分类（`editor_unfocused|compiling|asset_import|unknown`）。

**`clear_stuck`** 是非显然的一半：*"a job was lost to a domain reload and is blocking every subsequent run"* —— 一个会因重载永久卡死自己的桥，必须提供**文档化的逃生口**。

### 4.8 脚本编辑 / 校验（创作型，需拍板）

`create_script` / `manage_script` / `script_apply_edits`（锚点式结构化编辑）/ `apply_text_edits` / `validate_script` / `get_sha` / `delete_script` / `find_in_file`，且**创建/编辑会自动触发导入+编译**，因此 SKILL.md 明说 *"calling `refresh_unity` afterward is redundant"*（反冗余调用指导）。

**权衡**：这类能力属于"创作型扩张"，与我们验证型定位和上一轮"不纳入动态 C# 执行"的取向一致地**需要单独拍板**。

---

## 5. Skill 与安装层（与我们刚做的 `install-skill` 最直接对应）

### 5.1 客户端配置器架构（24 个）⭐

`MCPForUnity/Editor/Clients/`：**接口 + 71KB 基类 + 注册表 + 每客户端一个小类**，共 24 个（Antigravity、Antigravity IDE、CherryStudio、Claude Code、Claude Desktop、Cline、CodeBuddy CLI、**Codex**、Copilot CLI、Cursor、Gemini CLI、Kilo Code、Kimi Code、Kiro、OpenClaw、OpenCode、Pi、Qwen Code、Rider、Trae CN、Trae、VS Code、VS Code Insiders、Windsurf）。

`IMcpClientConfigurator.cs` 的契约里有几条值得我们学：

| 成员 | 价值 |
|---|---|
| `IsInstalled` | **只给真正装了的客户端写配置**（"configure all detected" 会先过滤），比我们"全部安装"更克制 |
| `SupportsAutoConfigure` | 明确区分"能自动配"与"只能给手动步骤" |
| `Configure()` | 文档明确"**总是幂等**"——*"calling twice with the same settings is safe and is what the bulk 'Configure All' path relies on"* |
| `GetManualSnippet()` + `GetInstallationSteps()` | **手动兜底**：自动配置不适用时给 JSON/TOML 片段与有序步骤 |
| `SupportsSkills` + `GetSkillInstallPath()` | **不是每个 agent 都该装 skill**；基类默认 `false`/`null`，只有 ClaudeCode/ClaudeDesktop/**Codex** 覆写为 true |
| `Unregister()` | 与安装对称的卸载（Codex TOML 尚未实现，文档标注为 best-effort） |

**写入是合并而非覆盖**：先读入既有配置对象再写回（`McpClientConfiguratorBase.cs:405,1202`），并**先从所有 scope 移除旧注册再添加**，以防陈旧配置冲突（`:913-914,1015-1016` 注释引用 issue #664）。

> 这条正好回应我上一轮提的担忧：我们对 Codex 直接**覆盖** `~/.codex/AGENTS.md`。unity-mcp 的做法是**写进客户端的配置文件并合并**，而不是覆盖一个共享的通用文件。

### 5.2 skill 安装：远程同步 + 明确 scope

- `MCPForUnity/Editor/Setup/SkillSyncService.cs`（31.5KB）：`SkillSubdir = ".claude/skills/unity-mcp-skill"`，用 **GitHub API 读远端目录树做增量同步（不 clone 仓库）**，scope 键 = `repoUrl|branch|subdir`；只报告 `+added ~updated -deleted`。
- `McpForUnitySkillInstaller.cs`（268 行）：一个安装窗口，四个可配项（Repo URL / Branch / CLI / Install Dir）持久化到 `EditorPrefs`；`GetDefaultInstallDir` = `~/.claude/skills/unity-mcp-skill` 或 `~/.codex/skills/unity-mcp-skill`（`CliOptions = { codex, claude }`）。
- 日志做了 **ANSI 转义清理**（`SanitizeLogLine`）再显示。

**与我们对比**：我们从 pip 包内 symlink/copy（**版本锁定、离线可用、确定性强**）；它从 GitHub 拉（可独立更新，但需网络、且可能与已装包版本漂移）。**我们的方式在确定性上更好**，可借的是 `SupportsSkills` 概念、scope 键、以及"只报告变更"的输出。

### 5.3 双份文件 drift-guard 测试 ⭐（XS，直接命中我们仓库）

`Server/tests/test_skill_copies_in_sync.py`（**33 行**），其 docstring 是一份事后复盘：

> *"The agent skill lives in two directories that must hold the same files. `.claude/skills/unity-mcp-skill/` is the copy the Unity editor installs … `unity-mcp-skill/` at the repo root is the copy users download by hand. **Edits landed in one copy only, until the installed skill linked two reference files it did not ship.**"*

```python
def _files(root):
    # Line endings are normalised: core.autocrlf decides them per checkout, not per edit.
    return {p.relative_to(root).as_posix(): p.read_bytes().replace(b"\r\n", b"\n") ...}

problems  = [f"only in unity-mcp-skill/: {n}" ...]
problems += [f"only in .claude/skills/unity-mcp-skill/: {n}" ...]
problems += [f"differs between the copies: {n}" ...]
```

**我们正好有 3 个双份文件**：`skill/SKILL.md`、`skill/references/COMMANDS.md`、`skill/references/EXTENDING.md`（另一份在 `skill/src/agents_unity_bridge/skill/`）。本次会话我就**手动同步改过 `SKILL.md` 的两份**——纯靠人工纪律，一旦漏改一份，装到 agent 里的就是旧的。这个 33 行测试直接适用（已核验：当前两份 `SKILL.md` sha256 一致，均 702 行 CRLF）。

同类守卫还有：`test_manifest_tools.py`（`manifest.json` 必须与注册的工具**双向**一致，注释记录"六个 asset_gen 工具曾经因为没人比对两个列表而漏登记"）、`test_tool_test_symmetry.py`（每个工具模块必须有测试引用，且**隔离名单"只能变小"**，第二个参数化测试断言名单里没有过期条目）。

### 5.4 Skill 写法（可直接搬到我们的 `SKILL.md`）

| 模式 | 出处 | 内容 |
|---|---|---|
| **Template Notice** | `unity-mcp-skill/SKILL.md:10-16` | 明说示例是**可复用模板**，"may be inaccurate across Unity versions, package setups, and project-specific conventions"，要求先验证目标再套用、把名称/枚举/属性当占位符 |
| **Resource-First 工作流** | `:18-28` | "**Always read relevant resources before using tools**"，并给出 5 步顺序（查状态 → 理解场景 → 找目标 → 行动 → 验证） |
| **Error Recovery 表** | `:274-281` | 症状 → 原因 → 解法（busy/`stale_file`/连接丢失/静默失败） |
| **Pagination Pattern** | `:248-261` | `next_cursor` 循环的完整示例 |
| **信任层级** | `:200` | `reflection > project assets > docs` |
| **反冗余调用** | `:37-51` | 创建/编辑脚本已自动触发导入+编译，"`refresh_unity` 是多余的" |
| **工具缺失 ≠ 故障** | `:30-33` | "If a tool you need is not in your tool list, it is not necessarily missing"（解释分组可见性） |
| **能力可用性先行** | `:199-200` | UI 前先读 `project/info` 探测 uGUI/TMP/Input System |
| **参考文档组织** | `references/*.md` | 4 个文件**各带 TOC**；`workflows.md` 以 "Setup & Verification" 就绪检查开篇；显式记录坑（URI 必须转义：`prefab/Assets%2FPrefabs%2FPlayer.prefab`） |

---

## 6. 工程 / CI / 测试基建（我们最大的空白）

### 6.1 免许可 Roslyn 编译门禁 ⭐⭐（"皇冠明珠"）

`tools/compile-check.sh`（8KB）：**从不启动编辑器**，只把 Unity 安装目录当作参考 DLL 来源，然后直接调 Unity 自带的 Roslyn：

- `CSC="$UNITY_DATA/DotNetSdkRoslyn/csc.dll"`、`DOTNET="$UNITY_DATA/NetCoreRuntime/dotnet"`。
- 每个 asmdef 每平台生成一份 `.rsp`：`-target:library -langversion:9.0 -nostdlib+ -preferreduilang:en-US -nowarn:CS1701,CS1702 -out:...`，再附上源码目录下所有 `*.cs`。
- **版本宏按精确发布阶梯计算**（`UNITY_RELEASES`）——因为"在 2021.3 上定义 `UNITY_2022_1_OR_NEWER` 会编错 `#if` 分支"。
- **参考清单从 Unity 生成的 `.csproj` 捕获，而非 glob**（`Editor/Data` 下有整套 .NET 4.8 BCL 与 Unity 特意不引用的 vendored 库，如 `ExCSS.Unity` 重定义 `System.Tuple`）。
- `LIBCACHE/` 从 `ProjectTemplates/libcache/**/ScriptAssemblies/` 取 `UnityEngine.UI.dll` / TestRunner DLL —— **无需导入工程**。
- 输出程序集名必须精确匹配（因为 `InternalsVisibleTo` 按程序集名授权）。
- 规模：`Runtime` + `Editor` × `win/osx/linux` = 6 次编译，**约 1 分钟/版本**。
- CI（`.github/workflows/compile-check.yml`）：`pull_request` 触发、**完全没有 `secrets:`**，所以**fork PR 上也是真实信号**；从**公开** `packages.unity.com` 取 Newtonsoft/nunit DLL，用公开的 `unityci/editor:ubuntu-<ver>-base-3` 镜像只读参考程序集。

**对我们的意义（重要）**：我们 `AGENTS.md` 写着 *"Phase 6: CI/CD integration (deferred - licensing discussion needed)"*，且 C# 侧**完全没有 CI**。而 **编译门禁 + 多版本编译矩阵根本不需要 Unity 许可**：公开镜像 + 公开 DLL + Unity 自带 Roslyn 即可，且能在 fork PR 上跑。这把"需要讨论许可"的阻塞**从源头绕开了**。

> 注意其已知缺口（照抄时要补）：它只编 `Runtime` + `Editor`，**测试 asmdef 未被编译检查**；`USE_ROSLYN` 未在 `compile-defines.txt` 中，相关分支被编掉。我们可以补第三份 manifest 覆盖自己的 `Tests/Editor`（`UNITY_INCLUDE_TESTS` 已在 defines 里）。

### 6.2 多版本矩阵单一真源

`tools/unity-versions.json`：`defaultVersion` + `versions[]` 带 `role = floor|lts|rolling` 与"该版本覆盖哪些 `#if` 分支"的说明；版本**钉到精确补丁号、不用浮动 tag**；并有一个显式的 **`$coverageGap`** 字段**承认哪些分支未被覆盖**（因暂无对应 GameCI 镜像）。

**值得学的不是矩阵本身，而是"矩阵记录自己的盲区"**。

### 6.3 兼容 shim 政策（写成文档 + 标记类）

`MCPForUnity/Runtime/Helpers/UnityCompatShims.cs` 是一个**故意留空的标记类**，唯一职责是承载清单与政策，让任何 shim 里按 F12 都能跳到它：

- **何时加**：API 被 `[Obsolete]` 且调用点不能直接删；**或 ≥3 处调用点需要同一 API 的版本门禁**；**或**未来版本已宣布改名/移除。
- **什么不该放**：热路径引擎 API（`Transform.position`、`Vector3.*`、`GetComponent<T>`）、Unity 未威胁的 API（`Mathf`、`Quaternion`、大部分 `AssetDatabase`）、编辑器内部未文档化 API —— *"those should break loudly so the package maintainers notice."*
- **模式**：能用 `#if UNITY_*_OR_NEWER` 静态分派就用；新 API 在尚未面向的版本里、或旧 API 可能被移除（CS0619）时，用**运行时反射 + 缓存 `MethodInfo`/`PropertyInfo`**；**fail-soft：缺失即 no-op，绝不抛**。

我们支持 2021.3+ 且 shim 会持续累积，**一份 40 行的政策 + 标记类**能防止 shim 蔓延。

### 6.4 其余工程项（多为 XS）

| 项 | 说明 |
|---|---|
| **退出码分类学** | `tools/local_harness.py:21-33`：`0` 全部阻塞腿通过 / `1` 阻塞腿回归 / `2` 桥不可达或启动失败 / `3` 工程编译不过 / `4` 无 Unity 许可或 Hub 席位 / `5` 编辑器二进制或版本找不到；聚合取**最高严重度**，优先级 `5>4>3>2>1` 让基础设施故障压过测试故障 |
| **NUnit XML 门禁** | `tools/check_unity_test_results.py` 让本地跑的 67 个测试在 CI 里算数（XS） |
| **版本单源** | `tools/update_versions.py`：以 `package.json` 为源，改写 manifest / pyproject / uv.lock / README / i18n README；`uv.lock` 用**字节往返**以免改换行；注释记录了它防的具体事故（lock 停在 10.1.0 而 pyproject 已是 10.2.0，整整几个发布周期） |
| **许可门禁作为 job 级 `if`** | 不是 step 级——step 级 `skipped` 对 job 结论无贡献，曾出现"编了零个文件却显示绿色"；因此 fork PR 被诚实标为 Skipped 并写明原因 |
| **元测试** | `tools/tests/`（10 模块 3395 行）断言 **CI 工作流不变量**，如每个 runner step 必须 pin 40 位 SHA + `vX.Y.Z` cliVersion + `githubToken: ""` |
| **`.gitattributes`** | 对行敏感清单强制 `eol=lf`——CR 会让 `-define:FOO` 定义错符号、让 `find -name` 匹配不到 |
| **opt-in git hooks** | `tools/install-hooks.sh` 幂等、`--force`/`--uninstall`；`pre-push` 仅在推送路径匹配时跑编译矩阵（浅克隆用空树 SHA 兜底） |
| **`uv sync --locked`** | lock 与 pyproject 不一致就**失败**，而非静默重解析 |
| **MCPB 打包** | `manifest.json`（`manifest_version 0.3` + `server.entry_point` + 机器可读 `tools[]` 目录）+ `.mcpbignore` 分区注释排除 |
| **`mcp_source.py`** | 交互式切换 `Packages/manifest.json` 的包源（upstream main/beta、当前分支、本地 `file:`）——Unity 包项目开发循环里很实用 |
| **i18n 文档配对** | `docs/i18n/README-zh.md` ↔ `website/docs/...` 顶部互相链接语言切换 |

---

## 7. ⚠️ 顺带发现的我们自身潜在缺陷（与本轮借鉴无关，但建议修）

**事实（已独立核验）**：

- 我们的 `package/Editor/AgentsBridge.cs:11` 是 **`[InitializeOnLoad]`**；
- 静态构造 `:42-75` 在 `:70` **无条件**执行 `EditorApplication.update += PollForCommands;`，且先调用了 `EnsureDirectoryExists()`（`:68`）与 **`CleanupOldResponses()`（`:69`）**；
- **没有任何 AssetImportWorker 守卫**。

**风险**：Unity 的 **AssetImportWorker 子进程共享 `[InitializeOnLoad]`**。若该子进程运行了我们的静态构造，它会：
1. `EnsureDirectoryExists()` —— 无害；
2. `CleanupOldResponses()` —— **可能删掉主编辑器正要读取的响应文件**（竞态）；
3. `PollForCommands` —— **可能消费并删除 `command.json`**，导致主编辑器永远看不到该命令，而 CLI 一直轮询到超时。

**unity-mcp 的对策**（`MCPForUnity/Editor/Services/StartupConfigRewrite.cs:25-28, 62-85`）：

```csharp
// AssetImportWorker subprocesses share [InitializeOnLoad] but don't host MCP and
if (IsRunningInAssetImportWorker()) return;
...
// 缓存 bool：查 IsAssetImportWorkerProcess + 命令行里的 -importWorker
```

**修法（XS）**：静态构造开头加同样的守卫，命中即 `return`（不注册轮询、不做清理）。本轮按你的口径**未改任何代码**。

---

## 8. 与上一轮 Locus 结论的交叉印证（重要）

两个**互相独立**的大型项目收敛到同一套实践，说明这不是某个项目的偏好，而是"让 AI 可靠驱动 Unity"的必要工程：

| 实践 | Locus | unity-mcp |
|---|---|---|
| 输出有界化 + 截断标记 | `PropertyPaging`（`childrenTruncated`） | `page_size`/`cursor`/`nextCursor`/`totalCount`/`truncated` |
| 机器可读就绪 / 错误分类 | `editor_busy:` / `revision_conflict:` | `ready_for_tools` / `blocking_reasons` / `recommended_retry_after_ms`；`{error, hint, data.reason, retry_after_ms}` |
| 陈旧检测（乐观并发） | `revision_conflict` + expected bytes | `stale_file` + `expected/current_sha256`；`get_sha` |
| 能力探测 / 优雅降级 | `AccessProbe`（measure, never assume） | Tier1/Tier2 + `ping` + `project/info` |
| 截图 / 视觉验证 | `unity_capture_viewport` | `manage_camera` screenshot + `include_image` |
| 长任务进度 job + poll | `unity_test_start/status/cancel` | `run_tests` → `job_id` + `get_test_job` + `clear_stuck` |
| 集中式 busy 守卫 | 忙时直接报错、**不排队** | `preflight`（声明式、**fail-open**） |
| 域重载存活状态 | `SessionState` 持久寄存器 | `SessionState` 编译边沿 + job 状态 |

**我们目前这 8 项一项都没有。** 建议把它们作为后续主线，而不是零散地加功能。

---

## 9. 建议落地路线图

```
第一批（XS~S，纯收益，不动架构）
  §5.3  双份文件 drift-guard 测试（3 个双份文件，33 行）
  §7    AssetImportWorker 守卫（潜在缺陷）
  §3.2  错误信封：hint / reason / retry_after_ms
  §3.5  分页契约：page_size / cursor / nextCursor / totalCount / hasMore
  §3.6  参数宽容归一（snake/camel、字符串化数组、InvariantCulture）
  §3.7  get-status 增补 project_hash / unity_version（检测连错编辑器）

第二批（S~M，需改 Unity C# 侧）
  §3.1  就绪门禁 + preflight 守卫（声明式、fail-open、有界等待）
  §3.3  重载感知重试（单 deadline + 状态相关退避上限 + grace）
  §3.4  域重载安全异步等待 + 编译边沿存 SessionState
  §6.1  免许可 Roslyn 编译门禁 + §6.2 多版本矩阵（解掉"需讨论许可"的阻塞）
  §6.3  兼容 shim 政策（标记类 + 40 行政策）

第三批（M~L，功能扩展）
  §4.1  截图 / 视觉验证（含 token 预算与 surround 多视角）
  §4.2  batch_execute（一次往返多条命令，上限可配且在状态里公布）
  §4.7  job_id + poll（run-tests 不再阻塞；含 clear_stuck 逃逸口）
  §4.3  unity_reflect / unity_docs（API 发现 + 信任层级）
  §4.5  只读资源面（editor/state、instances、project/info…）
  §5.1  客户端配置器架构（接口 + 基类 + 注册表；合并而非覆盖）
  §5.4  SKILL.md 工程化（Template Notice / Error Recovery 表 / 信任层级）

第四批（需单独拍板，属"创作型扩张"）
  §4.4  execute_menu_item（低成本逃生口，需开关）
  §4.8  脚本编辑 + 校验（create/apply_edits/validate/get_sha）
  §4.6  能力分级 + ping 探测
  创作型工具（GameObject / 场景 / 资源 / 材质…）—— 与现定位冲突，不建议
```

---

## 10. 明确**不建议**借鉴

| 项 | 原因 |
|---|---|
| WebSocket / keep-alive / 重连调度 / 远程 URL 安全门禁 | 我们的文件协议是**刻意的取舍**（无端口、无端点、无宿主进程） |
| 三层各自实现（MCP tools + CLI + C# 各写一遍） | CLAUDE.md 自己承认三层"**不是**互相生成的"，加一个工具要改三处 + 两套测试；这正是它需要三个 drift guard 的原因。**只借守卫，不借重复** |
| FastMCP 会话级工具分组可见性 | 需要 monkeypatch FastMCP 私有 `MiddlewareServerSession.__aenter__` + `mcp._transforms` 切片手术；CLI 根本没有 tool list 可过滤 |
| `client_id` 会话隔离 | **文档还写着，代码已废弃**：peer 提供的 `client_id` 会把多客户端塌缩到同一条记录（issue #1023），现改用服务端 `ctx.session_id`。可留的教训是"身份必须由服务端分配，不能来自调用方" |
| 11 个 service 接口 + `MCPServiceLocator` | 70k 行 + GUI + 测试规模才配得上，对我们过重 |
| 24 个客户端配置器**全量照搬** | 我们只面向 6 个 agent；借架构与关键成员，不必抄 24 份 |
| `execute_code`（内存 Roslyn/CodeDom） | 其自带文档承认黑名单"**不是完整沙箱**"，且每次唯一编译会泄漏一个 assembly 直到重载 —— 与我们已定的"不纳入动态 C# 执行"一致 |
| 遥测（`threading.Timer(1.0, ...)` 延迟启动） | 本地开发工具不合适，且会迫使测试加 stub |
| `focus_nudge`（激活 OS 窗口解除 Unity 限流） | 平台相关且重；仅当"编辑器失焦时测试真的挂住"再考虑 |
| legacy stdio TCP 传输（端口发现 / `FRAMING=1` / 16MiB buffer / 手写 JSON 完整性修补） | 我们的原子文件 + UUID 方案**严格更简单**且已避开这一整类问题 |
| 重复的小写 `JsonProperty` 别名 | 为反射式测试保留的 API 表面，不具通用性 |

---

## 11. 证据核验说明

标注 **✅** 的条目由主 Agent **直接阅读源码核验**；**○** 来自子系统深挖报告（关键论断已抽样复核）。

| 条目 | 核验 |
|---|---|
| 许可（MIT / CoplayDev 2025） | ✅ 读 `LICENSE` |
| `preflight` 守卫全部细节（fail-open / 声明式 / busy 载荷） | ✅ 通读 `preflight.py`（110 行） |
| 就绪门禁字段与阈值（`>2000ms` 判 stale） | ✅ 读 `editor_state.py:160-229` |
| `MCPResponse` 的 `hint` 封闭词表 | ✅ 读 `models.py:1-40` |
| `stale_file` 带 expected/current sha256 | ✅ grep `ManageScript.cs:555` |
| drift-guard 测试（含 CRLF 归一化与复盘注释） | ✅ 通读 `test_skill_copies_in_sync.py`（33 行） |
| 24 个客户端配置器 + 接口契约 | ✅ 枚举目录 + 通读 `IMcpClientConfigurator.cs` |
| 合并而非覆盖 + 先移除旧注册 | ✅ grep `McpClientConfiguratorBase.cs`（405/913/1015/1202 行） |
| `SkillSyncService` 远程同步 + scope 键 | ✅ grep `SkillSyncService.cs:17,75,116` |
| `McpForUnitySkillInstaller` 四可配项 + 默认目录 | ✅ 通读（268 行） |
| `TypeCache.GetTypesWithAttribute` 自动发现 + 确定性排序 + 9 秒注释 | ✅ 读 `MCPForUnity/Editor/Tools/CommandRegistry.cs:74-98` |
| `batch_execute` 上限可配且在 editor state 公布 | ✅ grep `BatchExecute.cs` / `EditorStateCache.cs:264-265` |
| 截图 `include_image` 产 base64 PNG | ✅ grep `ScreenshotUtility.cs:51` |
| 25 个资源 URI | ✅ 枚举 `Server/src/services/resources/` |
| `SkillSyncService` 的 C# 侧导入器与默认路径 | ✅ 读 `McpForUnitySkillInstaller.cs` |
| **我们自身的 import-worker 缺失** | ✅ 读 `AgentsBridge.cs:11,42-75` + 对比 `StartupConfigRewrite.cs:25-28,62-85` |
| 我们两份 `SKILL.md` 当前一致（sha256 相同 / 702 行 CRLF） | ✅ 直接比对 |
| CI/工具链细节（compile-check.sh 机制、退出码、元测试、版本单源） | ○ 深挖报告（已核对 `tools/` 文件清单与 `compile-check.sh` 存在性、8KB 体积） |
| Python 侧重试/分页/多实例细节 | ○ 深挖报告（已复核 `preflight.py`、`editor_state.py`、`models.py`） |
| C# 侧异步/分组/分页/job 细节 | ○ 深挖报告（已复核 `CommandRegistry.cs` TypeCache 与 `Pagination.cs` 存在性） |

**本轮未改任何代码**，`agents-unity-bridge` 仓库只新增本文档。
