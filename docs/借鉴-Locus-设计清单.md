# 借鉴 Locus 设计清单（agents-unity-bridge）

> 研究对象：`D:\AI\locus\locus_unity`（Unity 包 `com.farlocus.locus`，命名空间 `Locus`，v0.1.0）
> 研究目的：找出 Locus 中**值得 agents-unity-bridge 借鉴**的设计
> 本文性质：**只做调研与记录，不含任何代码改动**
> 研究方式：通读关键源码 + 三个子系统深挖（传输/生命周期、代码执行与扩展、安全与健壮性）

---

## 0. 结论速览

**Locus 的传输层不可借鉴**（Rust 原生 broker + Windows 命名管道 + 进程内 detour，数万行，解决的是"管道在 domain reload 后存活"，文件桥天然没有这个问题）。

**但它包在传输外面的那套工程实践极其值得借鉴**：重载存活的状态机、编译 epoch 归因、幂等键、输出有界化、机器可判读的错误分类、以及教科书级的 Skill 写法。其中大部分是**几十行的低成本改动**。

| # | 借鉴点 | 价值 | 成本 | 我们的对应位置 |
|---|---|---|---|---|
| 1 | 原子写：fsync + 唯一临时名 + CAS 冲突检测 + 窄重试 | 高 | S | `cli.py:164-171` |
| 2 | 编译/重载收敛协议（session / generation / serial） | 高 | M | `compile` 的 busy 判定 |
| 3 | 前缀式错误分类（`editor_busy:` 等） | 高 | XS | `cli.py` 错误路径 |
| 4 | 日志游标（只返回本次命令产生的日志） | 高 | S | `get-console-logs` |
| 5 | 输出有界化 + 显式截断标记 | 高 | S | `dump-asset` |
| 6 | 长任务：进度 / 心跳 / 幂等键 | 中高 | M | `run-tests` / `build` |
| 7 | SaveGuard：拒绝连带保存用户脏资产 | 中高 | S | 触发保存的路径 |
| 8 | Skill 工程：`tools:` 声明 + 边界写法 + 清单 | 高 | S | `skill/SKILL.md` |
| 9 | 稳定对象身份（GlobalObjectId + stale 报错） | 中 | S | 句柄传递 |
| 10 | 跨帧状态机 / TickDebugger（`WaitUntil`） | 中 | L | 暂无 |
| 11 | 截图能力（`unity_capture_viewport`） | 中 | M | 暂无 |
| 12 | 动态 C# 执行（`unity_execute`） | — | XL | **已决定不纳入**（见 §5） |

成本记号：XS < 1h，S ≈ 半天，M ≈ 1~3 天，L ≈ 1 周，XL ≈ 数周。

---

## 1. ⚠️ 许可前提（务必先读）

| 项目 | 许可证 |
|---|---|
| **agents-unity-bridge**（本仓库） | **Apache-2.0** |
| **Locus**（`com.farlocus.locus`） | **GPL-3.0-or-later** |

**结论：可以借鉴思想、架构、模式、协议语义；不要逐字复制 Locus 的代码。**

GPL-3.0 是强 copyleft：把 GPL 代码逐字并入 Apache-2.0 项目会产生许可冲突（整体将被迫以 GPL-3.0 分发）。本清单中的每一条都描述为**模式与语义**，落地时应自行重写实现，不要 copy-paste。

（思路、算法、协议设计本身不受版权保护，受保护的是具体表达。）

---

## 2. 架构对照

| 维度 | agents-unity-bridge | Locus |
|---|---|---|
| 传输 | 文件：`command.json` ↔ `response-{uuid}.json` | Windows 命名管道 + Rust broker DLL（`Editor/LocusBridge.Native.cs`） |
| 信封 | `{id, action, params}` / 响应文件 | `PipeEnvelope {id, reply_to, type, ok, message, error, processId, processPath}` |
| 并发 | **单槽**，同时只能一条命令 | 多请求并发（`id` → `reply_to`），上限 32 |
| 反向通道 | 无（只 Agent → Unity） | 有：事件通道 `SendEventToRust`（如 `unity-send-to-locus`） |
| 命令面 | 17 个固定命令（`Editor/AgentsBridge.cs:49-65`） | ~10 个固定动词 + 无限可达（`unity_execute` 跑任意 C#） |
| 扩展方式 | 改 C# → 重编译 Unity 包 | Skill 目录带 `.cs` → 运行时编译热插拔，零重编译 |
| 规模 | `cli.py` ≈ 1.7k 行 | `ExecuteCodeAsync` 单文件 2133 行，总量数万行 |
| 长任务 | 同步阻塞 + 超时重试 | 持久化 run state + 进度轮询 + 心跳 + 幂等 |

**关键判断**：Locus 用数万行解决"在 Unity 里安全地跑任意代码"；我们不需要跟。但它顺带沉淀的**可靠性工程**与传输方式无关，可以低成本移植。

---

## 3. 借鉴清单

### 3.1 正确性类（最高优先）

#### 借鉴 1：原子写 —— 我们目前有真实缺陷 ⭐

**我们现在的实现**（`skill/src/agents_unity_bridge/cli.py:164-171`）：

```python
temp_file = command_file.with_suffix(".tmp")            # ← 固定名 command.tmp
temp_file.write_text(json.dumps(command, indent=2))     # ← 无 fsync
temp_file.replace(command_file)                         # ← 无重试、无冲突检测
```

**Locus 的做法**（`Editor/LocusAssetApiAtomicFile.cs`，96 行）比我们多四件事：

1. **唯一临时名 + 独占创建**：`.locus-{guid:N}.tmp` + `FileMode.CreateNew`（拒绝覆盖已存在的临时文件）。
   - 我们的 `command.tmp` 是**固定名**：两个 CLI 进程 / 两个 Agent 并发写会互相踩临时文件，可能发布出半截内容。
2. **rename 前 `stream.Flush(true)`**（强制落盘）。
   - 我们 `write_text` 后直接 replace：崩溃时可能发布出**零长度 / 半截的 `command.json`**，Unity 侧解析失败。
3. **CAS 冲突检测**：rename 前重读目标、与 `expected` 字节 `SequenceEqual`；不等则抛 `revision_conflict: external change preserved at <path>`——**保留**外部改动而不是覆盖。
   - 我们单槽 `command.json` 是**后写覆盖**：并发时先到的 Agent 命令被静默吞掉，它只能等到超时。
4. **只对真正的共享冲突重试**：Windows 用 `MoveFileExW(temp, target, REPLACE_EXISTING|WRITE_THROUGH)`；仅在 win32 32/33 上按 `{25,50,100,200}ms` 退避。遇到 5（ACCESS_DENIED）时先用**非破坏性** `CreateFileW(..., DELETE, share=0x7, OPEN_EXISTING)` 探测是否真是共享冲突——是则重试，否则立即抛出，**不把 ACL / 只读错误掩盖成重试**。另用 `\\?\` 前缀规避 MAX_PATH。
   临时文件在 `finally` 中清理，且只删自己生成的那个。

**对我们的改法（S）**：Python 侧写临时文件后 `f.flush(); os.fsync(f.fileno())` 再 `os.replace`；临时名带 uuid；Windows 上加"仅共享冲突重试"的包装；`finally` 只删自己的临时文件。

---

#### 借鉴 2：编译 / 重载收敛协议 —— 我们"busy/compiling guard"的严格版 ⭐

我们目前靠"看 `isCompiling` + 超时重试"来猜。Locus 用三个独立单调量把它变成**可判定**的（`Editor/LocusBridge.cs:2013-2045`）：

```
get_reload_state → { session_id, domain_generation, converged_serial, is_compiling, is_updating }
```

- `session_id`：进程级 ID，存在 `SessionState` → 客户端能识别**编辑器已重启**（即使没看到旧进程退出）。
- `domain_generation`：每次 AppDomain 重载都变化。
- `converged_serial`：**仅在编译成功后推进**，并在下一个 domain 里被 `OnAfterAssemblyReload` 消费 → 语义是"新程序集已真正加载"，而**不是**"编译结束"。
- 组合判读：generation 变而 serial 不变 = **无编译的重载**（例如进入 PlayMode）。

配套的 **epoch 归因**（`LocusBridge.cs:116-127, 795-809, 850-852`）解决一个很实际的问题：**用户手按 Ctrl+R 触发的编译正在飞，与"我们请求的编译"必须区分**——`started < target` 的编译完成事件不得当作我们的请求完成，否则会提前汇报"已应用"。

还有一个**不需要任何 API 的巧招**（`LocusBridge.cs:153-160`）：编译成功后把静态帧计数置 0；若真的发生 domain reload，静态字段会随 AppDomain 一起消失，因此 100 帧后**还在计数 ⇒ 重载根本没发生**。这能抓住 Unity 最难查的失败模式之一。

**对我们的价值**：`compile` 现在"写完命令就返回"，"已接受"与"已开始"是混同的；编译排在长时间 import 之后时，与卡死无法区分。
**改法（M）**：响应加 `epoch` / `generation` 字段；Python 侧把"缺响应"分类为 *busy/reloading* 而不是一律超时。

---

#### 借鉴 3：前缀式错误分类（XS，近乎零成本）

我们 `cli.py` 只有 `EXIT_SUCCESS / EXIT_ERROR / EXIT_TIMEOUT`，错误信息全是自由文本（已确认没有任何 retryable / busy 分类）。

Locus 统一使用**机器可判读的 snake_case 前缀 / 码**：

```
editor_busy:      revision_conflict:   unsupported_capability:
invalid_path:     limit_exceeded:      persist_failed:
```
以及状态码 `awaiting_reload`、`unsupported_prefab_inheritance`、`recompile_result_operation_mismatch`、`not_run`、`stale_element`、`renderdoc_module_conflict`。

**价值**：Agent 能据此写可靠的条件分支（"可重试" vs "放弃并上报"），而不必解析中文错误串。

---

#### 借鉴 4：失败要响，不能挂

- 未知命令 → 明确 `unknown message type: X`，而不是静默（`LocusBridge.cs:2304-2308`）。
- 启动握手：`NativeProtocolVersion = 1` + `bridge_capabilities`（`LocusBridge.Native.cs:32, 49-50`）。
- 忙时**直接报错，不排队**（`LocusBridge.cs:1869-1874`）。

**对我们的价值**：文件桥最糟的失败模式就是"没有解释的超时"。今天 CLI 与 Unity 包版本不匹配会表现为超时；应当表现为一句明确的"版本不匹配"。

---

### 3.2 Agent 体验类（投入产出比最高）

#### 借鉴 5：输出有界化 + 显式截断标记 ⭐

`Editor/LocusBridge.PropertyPaging.cs`（**29 行**）：

```csharp
int count = Math.Min(prop.arraySize - start,
                     request.maxArrayItems > 0 ? Math.Min(request.maxArrayItems, 1024) : 64);
int depth = request.maxDepth > 0 ? Math.Min(request.maxDepth, 16) : 4;
snapshot.childrenTruncated = start + count < prop.arraySize;   // ← 显式告知"还有更多"
```

Locus 的 Skill 文档也反复强调："投影当前判断所需字段，避免返回完整对象"；出现 `__truncated` / `<max-*>` 时缩小投影。控制台还有 `matchedCount` / `uniqueCount` / `truncated` 三件套 + 6 万字符预算。

**我们的问题**：`dump-asset` 会把整个 Inspector 序列化字段树一次性吐出——大 Prefab / 场景会爆 token，且 Agent 不知道被截断。
**改法（S）**：加 `--max-items` / `--max-depth` / `--offset`，响应带 `truncated: true` 与总数。

---

#### 借鉴 6：日志游标 —— Locus 自己都没有，我们可以反超 ⭐

Locus 的控制台捕获做得很扎实（`Editor/LocusBridge.Console.cs`）：

- `StartGettingEntries()` / `EndGettingEntries()` **必须成对**（try/finally），否则破坏 Unity 内部读取状态——这是最重要的一条。
- 按 `level + "\0" + message` 去重并计次（`GetEntryCount` 折叠进 occurrence）。
- 取**尾部** N 条，上限 200；响应带 `matchedCount` / `uniqueCount` / `truncated`。
- 6 万字符预算，超长时前向裁剪（首条可部分裁剪）；标题截断到 96 字符。
- 严格校验 filter（只允许 error/warn/info），并把 assert/exception/fatal 归一化到 error。

**但它没有游标**：日志无法归属到产生它的那条命令（只有 execute-code 通过注入 `//__LOCUS_EXECUTION_ID__:` 标记做了关联）。

**对我们的价值**：`get-console-logs` 现在返回整个控制台快照，Agent 难以判断"哪些错误是我这次改动引入的"。
**改法（S）**：加日志游标（执行前记录 entry 计数，只返回其后的条目）或注入 marker。

---

#### 借鉴 7：稳定对象身份 + stale 报错

`Editor/LocusBridge.PropertyIdentity.cs`（**40 行**）：

- 优先 `GlobalObjectId`（跨会话、跨重载稳定）；
- 运行时对象回退到 `runtime:{session}:{instanceId}` + `WeakReference` 表；
- 解析失败抛明确错误 `Unity property target no longer exists: <value>`。

配合 Skill 中的 `stale_element` → "重新定位"纪律。

**对我们的价值**：我们只能按资产路径 / GUID 引用对象，跨 domain reload 的句柄不稳定。这是最小可用的稳定引用方案。

---

#### 借鉴 8：事务化修改 + 拒绝覆盖第三方

- **预览 / 回滚**：`SetStyles` 返回 `UIStylePreview`，校验失败 `Rollback()`；merge 流程是 `plan.preview()` → `apply(expected_plan_hash=...)`，计划过期返回 `stale`。
- **回滚只撤销自己的字节**：磁盘写入失败时，只恢复"当前内容 == 我自己写入的 after 镜像"的文件；若当前内容是第三种，则报 `recovery_required:` 而**不覆盖**（`Editor/LocusBridge.AssetApi.cs:186-245`）。

**对我们的价值**：`manage-prefabs --action create` 这类写操作值得加 plan-hash 或前后镜像保护，避免与用户 / 其他工具并发时互相覆盖。

---

#### 借鉴 9：SaveGuard —— 拒绝连带保存（64 行，很巧）

`Editor/LocusAssetApiSaveGuard.cs` 实现 `AssetModificationProcessor.OnWillSaveAssets`，在桥接操作期间**只放行自己的目标路径**：

- 起因：`SaveScene` / `OpenScene` / `ImportAsset` 内部可能触发**全局 `SaveAssets`**，把用户其他无关的脏资产一起写盘。
- `Allowed == null` → 正常编辑器行为（放行）；
- `Protect()` → 空集合（全部否决，用户脏状态保留）；
- `AllowOnly(path)` → 只放行目标；`Scope` 用锁 + 栈式保存/恢复；`.meta` 归一化。

**对我们的价值**：只要我们有任何触发 Unity 保存 / 导入的路径，这是防"副作用写盘"的廉价保险。

---

### 3.3 长任务与跨帧

#### 借鉴 10：进度 / 心跳 / 幂等键（我们目前完全没有）

- **基于"无输出"的超时**：30s 内**任何** print / progress 都重置空闲计时器；超时报 *"timed out after N seconds without print/progress output"*，而不是墙钟超时。
- **客户端心跳**：轮询 `execute_code_progress` 本身就是存活信号；客户端 120s 不轮询则**取消**工作 → 杀掉"CLI 被 Ctrl-C 了但编辑器还在编译/导入"的僵尸任务。
- **幂等键**：`execution_id` + SHA256 请求签名（`Editor/ExecuteCodeAsync/LocusBridge.ExecuteCodeAsync.cs:425-450`，已核验）。重复提交**加入**在途/已完成结果而不是重跑副作用；同 id 不同签名直接拒绝；完成项保留 10 分钟 / 最多 256 条。

**对我们的价值**：`run-tests` / `build` 超时后，Agent 唯一动作是"重发"——而重发会**真的再跑一遍构建**。幂等键 + 进度通道是根治手段。

---

#### 借鉴 11：跨帧状态机（能力缺口，成本高）

- `unity_run_states`：`start` / `update` / `end` 三段式，配 `ctx.Print` / `ctx.Fail` / `ctx.Goto` / `ctx.Done`，用于"等条件 → Begin → Trigger → LastCapture → End"这类跨帧流程（`skills/graphics-debugger/SKILL.md` 有完整范例）。
- `TickDebugger`（`Editor/LocusBridge.TickDebugger.cs`）可从执行的 C# 里 `await` 帧边界：`WaitUntil(...)`、`BreakWhen(...)`、`WaitBefore/WaitAfter/WaitAt`、`Next()`、`StepFrame()`、`ResumeGame()`、`SwitchToMainThread()`。

**对我们的价值**：我们现在一条命令只在一次 `EditorApplication.update` 内跑完，"等玩家血量掉到 50% 再暂停并 dump 状态"这类需求做不了。属**能力缺口**，实现成本 L（需要异步运行时 + 主线程泵）。

---

#### 借鉴 12：截图能力（能力缺口，成本中等）

`Editor/LocusBridge.CaptureViewport.cs` 能把 Game View / EditorWindow 渲染成 PNG（对应 `unity_capture_viewport` 工具）。UI / 渲染类工作中"看一眼"远胜文本描述——我们完全没有这个能力。

---

### 3.4 Skill 工程（与本仓库刚完成的 install-skill 最相关）

Locus 的 5 个 Skill（`skills/{graphics-debugger,merge,plugin,ui-toolkit,view}`）是**教科书级**的，以下几条可直接搬到 `skill/SKILL.md`：

1. **`tools:` 前置声明**。Locus frontmatter 是 `summary:` + `tools:`：
   ```yaml
   tools: [unity_execute, unity_run_states, bash, read, edit]
   ```
   即"这个 Skill 需要哪些工具"→ 能力/权限自描述。（我们目前用 Claude 风格 `name` / `description` / `whenToUse`。）

2. **"何时用 / 何时*不*用"写进 description**：
   > *"Load when plugin or 插件 means a Locus app extension... **Ignore Unity project plugins.**"*

   本仓库 AGENTS.md 也要求"写清边界而非罗列能力"，但我们 `skill/SKILL.md:3-10` 基本只写了"能做什么"。

3. **明确的反例清单**：
   > *"Avoid marketing-style hero areas, decorative gradients, heavy shadows, oversized cards, colorful chip clusters, and continuous animation."*

4. **禁止凭记忆即兴**：
   > *"Do not improvise GitHub or registry mechanics from memory."*
   要求先 `knowledge_query` 定位文档、再 `read` 返回的物理路径（渐进式披露）。

5. **别过度验证、别重复确认**：
   > *"不额外设置'必须工作区干净''必须全量扫描/编译/测试'的入口条件"*、
   > *"preview、validate、check_dependencies 当前共享静态预览，不连续调用三遍"*、
   > *"不在每一步重新征求确认"*。

6. **分级验证**：`validate(level="static")` vs `validate(level="unity")`，按改动规模选。

7. **`skill.json` 清单**（`locus.skill.v1`）：`id` / `version`（独立版本号）/ `injectMode: "excerpt"` / `command.trigger: "/graphics-debugger"` / `argumentHint` / `capabilities`（声明该 Skill 携带的 C# / Python API）。

8. **多个小 Skill，而非一个大 Skill**：Locus 5 个 Skill 各 49~260 行，按能力切分；我们是一个 702 行的 `SKILL.md`。我们已有 `references/COMMANDS.md`，但主文件仍偏"命令手册"。

---

## 4. 明确**不建议**借鉴

| 项 | 原因 |
|---|---|
| 命名管道 + Rust broker DLL + P/Invoke + detour | 数万行复杂度；它解决的问题（管道在重载后存活）文件桥天然没有 |
| `LocusMessagePump.cs` / `LocusMessageProxy.cs` | **不是 IPC**——是重载后给 Unity 引擎类型热补消息（`Update` / `OnTriggerEnter`），属另一个问题 |
| 进程内 Roslyn + 重载前排空编译 | 只因它要执行任意 C# 才存在 |
| AccessProbe 的 Mono JIT 逐格探测 | 与热重载强相关；只有"测量而非假设"与"INCONCLUSIVE 金丝雀"两点思想值得学 |
| `ExecuteCodeAsync` / `PropertyTree` / `RunStates` 等巨型引擎 | 是整套功能，不是可移植的模式 |
| 16 / 32 这类并发上限数值 | 数值来自管道；值得学的是"有上限 + 明确 busy 错误"这个模式 |
| SHA256 派生管道名 / 项目键控传输身份 | 管道专属 |

---

## 5. 决策记录：动态 C# 执行（`unity_execute`）不纳入

**决策：不纳入，保持固定命令面。**（2026-02 决策）

理由：

- 固定命令**可审计、可测试、自文档化**（`get-status`、`run-tests`）；任意 C# 是**未经审查的写入路径**，而 Locus 基本没有沙箱（仅 `allowUnsafe:false`）——在真实游戏仓库上这是执行与代码审查风险。
- 真实成本不是 Roslyn（就一个 DLL），而是**约 2000 行的支撑层**：取消、进度、心跳、幂等、主线程泵、重载排空、Skill 热插拔。bug 都长在那里。
- `execute_code` 缺少类型索引与 Skill 就**不可发现**；固定字典则天然可发现。

**若未来重启该议题**：只作为**显式开关的 gated 扩展**（每会话 / 每次调用 opt-in，并记录提交的源码），且直接照搬 Locus 的超时 / 取消 / 幂等 / 锁设计，不要自行发明。

---

## 6. 建议落地路线图（供后续排期）

```
第一批（1~2 天，纯收益，不动架构）
  借鉴 1  原子写 fsync + 唯一临时名 + 窄重试          cli.py:164-171
  借鉴 3  错误前缀分类（editor_busy / revision_conflict …）
  借鉴 6  日志游标                                   get-console-logs
  借鉴 5  dump-asset 输出有界化 + truncated 标记
  借鉴 8  SKILL.md 加 tools 声明 + 边界 / 反例

第二批（3~5 天，需改 Unity C# 侧）
  借鉴 2  收敛协议（session_id / generation / serial）   compile 的 busy 判定
  借鉴 9  SaveGuard（仅在我们触发保存时）
  借鉴 4  未知命令 / 版本握手明确报错

第三批（1~2 周，能力扩展）
  借鉴 12 截图 unity_capture_viewport
  借鉴 10 进度 / 心跳 / 幂等键（run-tests / build）
  借鉴 7  稳定对象身份

按需（高风险，暂不做）
  借鉴 11 跨帧状态机 / TickDebugger
  §5      动态 C# 执行（gated）
```

---

## 7. 证据与核验说明

标注 **✅** 的条目由主 Agent **直接阅读源码核验**；标注 **○** 的来自子系统深挖报告（已对关键论断抽样复核）。

| 条目 | 核验 |
|---|---|
| 借鉴 1 原子写全部细节 | ✅ 通读 `LocusAssetApiAtomicFile.cs`（96 行） |
| 借鉴 2 `get_reload_state` 载荷与语义 | ✅ `LocusBridge.cs:2013-2045` |
| 借鉴 2 静态帧计数检测重载未发生 | ✅ `LocusBridge.cs:153-160` |
| 借鉴 2 busy 是报错而非排队 | ✅ `LocusBridge.cs:1869-1874` |
| 借鉴 3 错误码字面量 | ✅ grep 全 `Editor/**/*.cs` |
| 借鉴 5 分页与截断标记 | ✅ 通读 `LocusBridge.PropertyPaging.cs`（29 行） |
| 借鉴 6 控制台细节 / 无游标 | ○ `LocusBridge.Console.cs` 深挖 |
| 借鉴 7 稳定身份 | ✅ 通读 `LocusBridge.PropertyIdentity.cs`（40 行） |
| 借鉴 9 SaveGuard | ✅ 通读 `LocusAssetApiSaveGuard.cs`（64 行） |
| 借鉴 10 幂等 SHA256 签名 | ✅ `ExecuteCodeAsync.cs:425-450` |
| 借鉴 10 心跳 / 空闲超时数值 | ○ 深挖报告 |
| 借鉴 11 TickDebugger API 面 | ✅ grep 公共方法 |
| 借鉴 12 截图能力 | ✅ grep `CaptureViewport` |
| §3.4 Skill 写法与清单 | ✅ 通读 5 个 `SKILL.md` + 5 个 `skill.json` |
| 我们自身的现状（17 命令 / 无错误分类 / 固定 `.tmp`） | ✅ 直接阅读本仓库源码 |

**我们自身现状的关键证据**：

- 17 个固定命令：`package/Editor/AgentsBridge.cs:49-65`
- 原子写的缺陷：`skill/src/agents_unity_bridge/cli.py:164-171`
- 无 retryable / busy 分类：grep `retryable|transient|editor_busy|error_code` 仅命中 `EXIT_TIMEOUT = 2`
- `SKILL.md` frontmatter 仅 `name` / `description` / `whenToUse`：`skill/SKILL.md:1-11`

---

## 附录：Locus 文件索引（按借鉴价值）

| 文件 | 规模 | 借鉴价值 |
|---|---|---|
| `Editor/LocusAssetApiAtomicFile.cs` | 96 行 | ★★★ 原子写范式 |
| `Editor/LocusAssetApiSaveGuard.cs` | 64 行 | ★★★ 连带保存防护 |
| `Editor/LocusBridge.PropertyPaging.cs` | 29 行 | ★★★ 输出有界化 |
| `Editor/LocusBridge.PropertyIdentity.cs` | 40 行 | ★★★ 稳定身份 |
| `Editor/LocusBridge.cs` | 2341 行 | ★★★ 收敛协议 / epoch / 持久寄存器 |
| `Editor/LocusBridge.Console.cs` | 23KB | ★★ 日志捕获 |
| `Editor/Testing/UnityTestRunLiveness.cs` | 3.6KB | ★★ 测试存活性 |
| `Editor/Testing/LocusUnityTestService.cs` | 32KB | ★★ 持久 run state |
| `Editor/ExecuteCodeAsync/*` | 83KB+ | ★ 幂等 / 超时 / 心跳（引擎本身不建议抄） |
| `Editor/LocusBridge.TickDebugger.cs` | 45KB | ★ 跨帧（成本高） |
| `Editor/LocusBridge.CaptureViewport.cs` | 63KB | ★ 截图（能力缺口） |
| `skills/*/SKILL.md`、`skills/*/skill.json` | 49~260 行 | ★★★ Skill 工程范式 |
| `Editor/LocusBridge.Native.cs`、`LocusMessagePump.cs`、`LocusMessageProxy.cs` | — | ✗ 不建议借鉴 |
