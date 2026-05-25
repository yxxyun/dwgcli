# AGENTS.md — dwgcli

DWG/DXF CLI tool built on .NET 10 + ACadSharp. Single-project repo, no tests, no CI.

## Commands

```bash
dotnet run --project src/dwgcli -- <command> [args]
dotnet build -c Release          # output: src/dwgcli/bin/Release/net10.0/dwgcli.exe
```

**Registered commands** (see `CommandBuilder.cs` for the full list):
`info`, `get`, `dump`, `query`, `set`, `add`, `remove`, `purge`, `batch`, `new`, `block import`, `stats`

> **`stats` command exists** but is not yet documented in README. Shows entity counts by type, layer, and block. Usage: `dwgcli stats <file> [--json]`

## Architecture

```
src/
├── dwgcli/                             # CLI 工具
│   ├── Program.cs                      # Entry point
│   ├── CommandBuilder.cs + partials    # 命令注册
│   └── Core/
│       ├── IDwgHandler.cs              # 文档操作接口
│       ├── DwgHandler.cs + 6 partials  # 核心实现
│       ├── DwgNode.cs / BatchItem.cs   # 数据模型
│       └── ...                         # 辅助模块
│
├── dwgcli-mcp/                         # MCP Server
│   ├── Program.cs                      # 4 unified tools + 9 Obsolete + shorthand
│   ├── DwgHelper.cs                    # 执行中间件
│   └── DwgComAutomation.cs             # CAD COM 自动化（可选，需 AutoCAD）
│
└── tests/                              # xUnit 测试
```

## MCP Server（dwgcli-mcp）

```
src/dwgcli-mcp/
├── Program.cs                    # 4 unified tools + 9 Obsolete + shorthand
├── DwgHelper.cs                  # ExecuteRead/ExecuteWrite + JSON 输出
└── DwgComAutomation.cs           # CAD COM 自动化（可选，需 AutoCAD）
```

**MCP Tools 清单**：
- `dwg_query` — 统一读工具（info/get/query/dump/stats）
- `dwg_edit` — 统一写工具（set/add/remove/purge）
- `dwg_shorthand` — 简写格式批量操作
- `dwg_cad` — CAD 自动化工具（可选，需 AutoCAD/ZWCAD/GstarCAD/BricsCAD）→ 截图/PNG导出/PDF打印/打开图纸

**Key pattern**: `DwgComAutomation` 使用 `GetShared()` 共享单例，MCP 会话内复用 COM 连接，避免每次调用都启动/关闭 CAD。自动检测已安装的 CAD（AutoCAD → ZWCAD → GstarCAD → BricsCAD 优先级），支持 `cadType` 参数指定。无 CAD 时静默降级。

## CLI Architecture

**Key pattern**: `CommandBuilder` is a `static partial class`. Each command lives in its own `CommandBuilder.{Name}.cs` file. All commands share a single `--json` option passed from `BuildRootCommand()`.

## Critical Behaviors

### Save auto-upgrades old DWG versions
`DwgHandler.Save()` auto-upgrades any DWG older than AC1021 to AC1027 before writing. Always creates a `.bak` backup before overwriting.

### `--json` output envelope
All commands wrap output in `{ "success": true/false, "data": ..., "message": ... }`. Read-only commands (`info`, `get`, `dump`, `query`, `stats`) serialize `DwgNode` into `data`. Mutating commands (`set`, `add`, `remove`, `purge`, `new`, `block import`) use `WrapEnvelopeText(message)` which produces `{ "success": true, "data": "message", "message": "message" }`.

### Batch command
`batch` executes multiple commands in a single open/save cycle. Input via `--commands` (inline JSON), `--input` (file), or stdin. Supports `--stop-on-error`. The batch executor in `CommandBuilder.cs` (`ExecuteBatchItem`) handles all command dispatch — **adding a new command requires updating this switch too**.

### `block import` requires `MarkModified()`
The `block import` command manipulates `handler.Document` directly (not through handler methods), so it must call `handler.MarkModified()` before `handler.Save()`. This is a gotcha if you add similar direct-document commands.

### `Add` supports `attrs` field
`BatchItem` has an `attrs` field (Dictionary<string, string>) for attribute definitions on Insert entities. The `handler.Add()` signature accepts an optional `attributes` parameter.

## Adding a New Command

1. Create `CommandBuilder.{Name}.cs` with a `private static Command Build{Name}Command(Option<bool> jsonOption)` method
2. Register it in `CommandBuilder.BuildRootCommand()`: `rootCommand.Add(Build{Name}Command(jsonOption));`
3. Add the command to `ExecuteBatchItem()` switch in `CommandBuilder.cs`
4. If it mutates the document, add the operation to `IDwgHandler` + `DwgHandler`
5. Update README.md command reference

## Dependencies

### dwgcli（CLI）
- **ACadSharp 3.4.9** — DWG/DXF read/write. In-memory only (no file locks after read).
- **System.CommandLine 3.0.0-preview** — CLI parsing. Uses `cmd.SetAction()` pattern (not the older `Handler.SetHandler`).

### dwgcli-mcp（MCP Server）
- **ModelContextProtocol 1.3.0** — MCP SDK
- **System.Drawing.Common 9.x** — 截图（仅 Windows）
- **ACadSharp 3.4.9** — DWG/DXF 读写（通过 dwgcli 项目引用）

## Conventions

- Namespace: `DwgCli` (root), `DwgCli.Core` (core types)
- All handler methods throw on error; `SafeRun()` in CommandBuilder catches and formats
- Read-only commands open with `editable: false`; mutating commands use `editable: true`
- Color parsing supports: ACI index (1-255), `#RRGGBB`, named colors (red/yellow/green/cyan/blue/magenta/white/black), `byLayer`, `byBlock`
