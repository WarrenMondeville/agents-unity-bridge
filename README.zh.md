# Agents Unity Bridge

![Unity 2021.3+](https://img.shields.io/badge/Unity-2021.3%2B-black.svg)
![Python 3.8+](https://img.shields.io/badge/Python-3.8%2B-blue.svg)

[![PyPI](https://img.shields.io/pypi/v/harness-unity-bridge)](https://pypi.org/project/harness-unity-bridge/)
[![GitHub release](https://img.shields.io/github/v/release/WarrenMondeville/harness-unity-bridge)](https://github.com/WarrenMondeville/harness-unity-bridge/releases)
[![CI](https://github.com/WarrenMondeville/harness-unity-bridge/actions/workflows/test-skill.yml/badge.svg)](https://github.com/WarrenMondeville/harness-unity-bridge/actions/workflows/test-skill.yml)

[![PyPI Downloads](https://img.shields.io/pypi/dm/harness-unity-bridge)](https://pypi.org/project/harness-unity-bridge/)
[![codecov](https://codecov.io/gh/WarrenMondeville/harness-unity-bridge/graph/badge.svg?token=3PHF2GXHON)](https://codecov.io/gh/WarrenMondeville/harness-unity-bridge)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)

一个基于文件协议（file-based）的桥接器，让 **DeepSeek Harness** 能够在**正在运行**的 Unity Editor 实例中触发各类操作。

## ✨ 功能特性

- **运行测试** — 执行 EditMode 或 PlayMode 测试
- **编译** — 触发脚本编译
- **刷新** — 强制刷新资源数据库（Asset Database）
- **查询状态** — 检查编辑器编译 / 更新状态
- **获取控制台日志** — 拉取 Unity Console 输出
- **Play Mode 控制** — 播放、暂停、单帧步进
- **构建项目** — 直接构建或调用自定义构建方法
- **资源依赖分析** — 依赖追踪、引用查找、未使用资源检测、依赖路径追踪、资源搜索、资源信息
- **预制体管理** — 查看预制体元信息与层级，从场景对象创建预制体
- **资源字段导出** — 导出预制体/资源/场景的 Inspector 可见序列化字段值

## 🚀 快速开始

### 1. 安装

**macOS / Linux / Git Bash：**
```bash
curl -sSL https://raw.githubusercontent.com/WarrenMondeville/harness-unity-bridge/main/install.sh | bash
```

**Windows（PowerShell）：**
```powershell
irm https://raw.githubusercontent.com/WarrenMondeville/harness-unity-bridge/main/install.ps1 | iex
```

安装脚本会完成以下事情：
- 安装 `agents-unity-bridge` 这个 pip 包（提供 `agents-unity-bridge` 命令行工具）
- 将 Python 的 scripts 目录加入 PATH
- 把 DeepSeek Harness 技能安装到 `~/.dsh/skills/unity-bridge/`

### 2. 添加到你的 Unity 工程

在 Unity 中：`Window > Package Manager > + > Add package from git URL...`

```
https://github.com/WarrenMondeville/harness-unity-bridge.git?path=package
```

### 3. 使用

在 Unity 工程目录中打开 DeepSeek Harness，直接自然地提出要求即可：

```
“运行 Unity 测试”
“检查有没有编译错误”
“把错误日志给我看看”
```

也可以直接使用命令行工具：

```bash
agents-unity-bridge run-tests --mode EditMode
agents-unity-bridge compile
agents-unity-bridge get-console-logs --limit 10
```

其它常用命令：

```bash
agents-unity-bridge get-status                    # 查看编辑器状态
agents-unity-bridge refresh                       # 刷新资源数据库
agents-unity-bridge play / pause / step           # 控制 Play Mode
agents-unity-bridge build --target Android        # 构建项目
agents-unity-bridge get-dependencies --asset Assets/Foo.prefab --recursive   # 正向依赖
agents-unity-bridge find-references --asset Assets/Foo.mat                    # 反向引用
agents-unity-bridge find-unused-assets                                       # 未使用资源
agents-unity-bridge trace-path --from Assets/A.prefab --to Assets/D.fbx      # 依赖路径
agents-unity-bridge search-assets --query "Player" --type Prefab             # 搜索资源
agents-unity-bridge get-asset-info --asset Assets/Foo.prefab                 # 资源信息
agents-unity-bridge manage-prefabs --action get-info --prefab-path Assets/Prefabs/Foo.prefab   # 预制体信息
agents-unity-bridge manage-prefabs --action create --object MyObj --prefab-path Assets/Prefabs/Foo.prefab  # 创建预制体
agents-unity-bridge dump-asset --asset Assets/Prefabs/Foo.prefab                                           # 导出资源字段
agents-unity-bridge health-check                  # 检查桥接环境是否就绪
```

### 更新

```bash
agents-unity-bridge update
```

## ⚙️ 工作原理

```
DeepSeek Harness → agents-unity-bridge CLI → .agents-unity-bridge/command.json → Unity Editor → response.json
```

1. DeepSeek Harness（或你自己）运行 `agents-unity-bridge` 命令
2. CLI 把命令写入 `.agents-unity-bridge/command.json`
3. Unity Editor 轮询并执行该命令
4. 结果写入 `.agents-unity-bridge/response-{id}.json`

每个 Unity 工程都有自己的 `.agents-unity-bridge/` 目录，因此支持多工程并行。

**为什么用文件协议而不是网络？**
- 无需任何网络配置，不存在端口冲突
- 不受防火墙限制
- 每个工程独立目录，天然支持多工程
- 调试直观：直接查看 JSON 文件即可

## 🎯 技能（Skill）说明

DeepSeek Harness 通过名为 `unity-bridge` 的技能来“感知”如何操控 Unity。安装后：

- 技能文件位于 `~/.dsh/skills/unity-bridge/SKILL.md`
- 在工程目录中询问「运行测试」「检查编译错误」等，Harness 会自动加载该技能并调用 CLI
- 手动安装 / 卸载技能：`agents-unity-bridge install-skill` / `agents-unity-bridge uninstall-skill`

## 📚 文档

- [安装说明](docs/INSTALLATION.md) — 各种替代安装方式
- [使用指南](docs/USAGE.md) — 命令格式与响应详情
- [架构设计](docs/ARCHITECTURE.md) — 项目结构与设计
- [技能参考](skill/SKILL.md) — DeepSeek Harness 技能文档
- [命令参考](skill/references/COMMANDS.md) — 完整命令规范

## 🧩 自定义命令

参见 [skill/references/EXTENDING.md](skill/references/EXTENDING.md)，了解如何为你的工程添加自定义命令。

## 📄 许可证

[Apache 2.0](LICENSE)
