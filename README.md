#  Agents Unity Bridge

[English](README.md) | [中文](README.zh.md)

![Unity 2021.3+](https://img.shields.io/badge/Unity-2021.3%2B-black.svg)
![Python 3.8+](https://img.shields.io/badge/Python-3.8%2B-blue.svg)

[![GitHub release](https://img.shields.io/github/v/release/WarrenMondeville/agents-unity-bridge)](https://github.com/WarrenMondeville/agents-unity-bridge/releases)
[![CI](https://github.com/WarrenMondeville/agents-unity-bridge/actions/workflows/test-skill.yml/badge.svg)](https://github.com/WarrenMondeville/agents-unity-bridge/actions/workflows/test-skill.yml)

[![codecov](https://codecov.io/gh/WarrenMondeville/agents-unity-bridge/graph/badge.svg?token=3PHF2GXHON)](https://codecov.io/gh/WarrenMondeville/agents-unity-bridge)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)



File-based bridge enabling AI agents to trigger Unity Editor operations in a running editor instance.

## ✨ Features

- **Run Tests** — Execute EditMode or PlayMode tests
- **Compile** — Trigger script compilation
- **Refresh** — Force asset database refresh
- **Get Status** — Check editor compilation/update state
- **Get Console Logs** — Retrieve Unity console output
- **Play Mode Control** — Play, pause, and step through frames
- **Build** — Direct builds or custom build pipelines
- **Asset Dependency Analysis** — Dependencies, references, unused assets, path tracing, search, and asset info
- **Prefab Management** — Inspect prefab metadata and hierarchy, create prefabs from scene objects
- **Asset Inspector Dump** — Dump Inspector-visible serialized field values of prefabs, assets, and scenes

## 🚀 Quick Start

### 1. Add the Unity Package

In Unity: `Window > Package Manager > + > Add package from git URL...`

```
https://github.com/WarrenMondeville/agents-unity-bridge.git?path=package
```

### 2. Install the CLI & Skill

In Unity, open `Tools > Unity Bridge > Open Installer`, then click **Install Python CLI** and **Install Skill**.

Or install manually from a terminal:

```bash
pip install "git+https://github.com/WarrenMondeville/agents-unity-bridge.git#subdirectory=skill"
agents-unity-bridge install-skill
```

> If `agents-unity-bridge` is not on your PATH, use `python -m agents_unity_bridge.cli` instead.

### 3. Use It

Open your AI agent in your Unity project directory:

```
"Run the Unity tests"
"Check for compilation errors"
"Show me the error logs"
```

Or use the CLI directly:

```bash
agents-unity-bridge run-tests --mode EditMode
agents-unity-bridge compile
agents-unity-bridge get-console-logs --limit 10
```

### Updating

```bash
agents-unity-bridge update
```

## ⚙️ How It Works

```
AI agents → agents-unity-bridge CLI → .agents-unity-bridge/command.json → Unity Editor → response.json
```

1. AI agent (or you) runs `agents-unity-bridge` commands
2. The CLI writes commands to `.agents-unity-bridge/command.json`
3. Unity Editor polls and executes commands
4. Results appear in `.agents-unity-bridge/response-{id}.json`

Each Unity project has its own `.agents-unity-bridge/` directory, enabling multi-project support.

## 📚 Documentation

- [Installation Options](docs/INSTALLATION.md) — Alternative installation methods
- [Usage Guide](docs/USAGE.md) — Command formats and response details
- [Architecture](docs/ARCHITECTURE.md) — Project structure and design
- [Skill Reference](skill/SKILL.md) — AI agent skill documentation
- [Command Reference](skill/references/COMMANDS.md) — Complete command specification
