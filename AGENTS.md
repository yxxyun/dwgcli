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
src/dwgcli/
├── Program.cs              # Entry point — forces UTF8 output, dispatches to CommandBuilder
├── CommandBuilder.cs       # Root command registration + SafeRun + batch executor + ParsePropsArray
├── CommandBuilder.*.cs     # One partial class file per command
└── Core/
    ├── IDwgHandler.cs      # Interface: GetInfo, Get, Dump, Query, Set, Add, Remove, Purge, Stats, Save
    ├── DwgHandler.cs       # ~1550 lines — all CRUD logic on ACadSharp CadDocument
    ├── DwgHandlerFactory.cs # Opens .dwg (DwgReader) or .dxf (DxfReader), returns IDwgHandler
    ├── DwgNode.cs          # Tree node model: path, type, properties, children
    ├── BatchItem.cs        # Batch command model + KnownFields validation set
    └── OutputFormatter.cs  # Text/JSON formatting + WrapEnvelope helpers
```

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

- **ACadSharp 3.4.9** — DWG/DXF read/write. In-memory only (no file locks after read).
- **System.CommandLine 3.0.0-preview** — CLI parsing. Uses `cmd.SetAction()` pattern (not the older `Handler.SetHandler`).

## Conventions

- Namespace: `DwgCli` (root), `DwgCli.Core` (core types)
- All handler methods throw on error; `SafeRun()` in CommandBuilder catches and formats
- Read-only commands open with `editable: false`; mutating commands use `editable: true`
- Color parsing supports: ACI index (1-255), `#RRGGBB`, named colors (red/yellow/green/cyan/blue/magenta/white/black), `byLayer`, `byBlock`
