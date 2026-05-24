# dwgcli — DWG Command-Line Interface

> 基于 ACadSharp 的 AutoCAD .dwg/.dxf 文件命令行工具，支持读取、查询、修改、创建和批量操作 CAD 文件。
> 专为 AI 代理和人类用户设计，`--json` 输出格式可直接供 AI 使用。

---

## 概述

dwgcli 是一个 .NET 命令行工具 + MCP Server，提供对 AutoCAD DWG/DXF 文件的程序化访问能力：

- **读取** — 查看文档元信息、图层、块定义、布局、实体列表
- **查询** — 按类型、图层、颜色、坐标范围、文本内容等条件搜索实体
- **导出** — 以 JSON、文本、CSV 或 Excel (.xlsx) 格式输出文档结构
- **修改** — 添加/删除实体、修改属性（颜色/图层/线宽/坐标）、图层开关/冻结/锁定
- **创建** — 新建空 DWG 文件、从源 DWG 导入块定义
- **批量** — 通过 JSON 脚本或**简写格式**批量执行多个操作
- **智能输入** — 颜色模糊匹配（`blu`→`blue`）、数字自动转换、坐标自动规范化

### 技术栈

- **.NET 10** (net10.0)
- **ACadSharp** v3.4.9 — 纯 C# CAD 文件读写库
- **System.CommandLine** — 命令行解析
- **ClosedXML** — Excel (.xlsx) 导出
- **ModelContextProtocol** v1.3.0 — MCP Server 集成（可选）

---

## 安装/构建

### 从源码构建

```bash
git clone <repo>
cd dwgcli/src/dwgcli
dotnet build
```

构建产物位于 `bin/Debug/net10.0/dwgcli.exe`。

### 直接运行

```bash
# 从项目目录
dotnet run -- <args>

# 或使用编译后的 exe
./bin/Debug/net10.0/dwgcli.exe <args>
```

---

## 全局选项

所有子命令均支持：

| 选项 | 类型 | 说明 |
|------|------|------|
| `--json` | bool | 以 JSON 信封格式输出（AI 友好，推荐） |

无参数时自动显示帮助信息。

---

## 命令参考

---

### 1. `info` — 查看文档摘要信息

**用途：** 快速了解 DWG 文件的基本信息：版本、作者、实体/图层/块/布局数量。

**语法：**

```bash
dwgcli info <file> [--out <file>] [--json]
```

**参数：**

| 参数 | 说明 |
|------|------|
| `file` | DWG/DXF 文件路径 |
| `--out` / `-o` | 写入文件（替代 stdout） |

---

### 2. `get` — 按路径访问文档结构

**用途：** 按路径精确访问文档的某个部分：根节点、图层、实体、块定义、布局等。

**语法：**

```bash
dwgcli get <file> <path> [--depth <n>] [--out <file>] [--json]
```

**支持路径：**

| 路径 | 返回内容 |
|------|----------|
| `/` | 根节点：文档属性 + 布局列表 |
| `/info` | 文件元信息（同 `info` 命令） |
| `/layers` | 所有图层列表 |
| `/layer/{name}` | 指定名称的图层（如 `/layer/0`、`/layer/Walls`） |
| `/layer/{index}` | 按序号访问图层（如 `/layer/0` 是第一个图层） |
| `/entities` | 模型空间所有实体列表 |
| `/entity/{handle}` | 指定句柄的实体（如 `/entity/1F3A`，16 进制） |
| `/blocks` | 所有块定义列表（排除 *Model_Space、*Paper_Space） |
| `/block/{name}` | 指定块定义 |
| `/layouts` | 所有布局列表 |
| `/layout/{name}` | 指定名称的布局（如 `/layout/Model`） |

---

### 3. `query` — 搜索实体

**用途：** 按条件搜索实体、图层或块定义。支持坐标范围过滤和 CSV 导出。

**语法：**

```bash
dwgcli query <file> <selector> [--format <json|csv>] [--out <file>] [--json]
```

**参数：**

| 参数 | 说明 |
|------|------|
| `file` | DWG/DXF 文件路径 |
| `selector` | 空格/逗号/分号分隔的 `key=value` 条件 |
| `--format` | 输出格式：`json`（默认）或 `csv` |
| `--out` / `-o` | 写入文件 |

**选择器语法：**

```
type=Line layer=Walls xMin=0 xMax=1000 yMin=0 yMax=1000
```

**实体搜索过滤条件（默认）：**

| 条件 | 说明 | 示例 |
|------|------|------|
| `type=` | 按实体类型匹配（ObjectName 或类名） | `type=Line`、`type=Insert` |
| `layer=` | 按图层名匹配 | `layer=0`、`layer=Walls` |
| `handle=` | 按 16 进制句柄精确匹配 | `handle=1F3A` |
| `color=` | 按 ACI 颜色索引或颜色名称匹配（支持模糊匹配） | `color=1`、`color=red` |
| `linetype=` | 按线型名匹配 | `linetype=Continuous` |
| `hastext=` | 是否为文本类实体 | `hastext=true` |
| `text=` | 按文本内容模糊搜索（大小写不敏感，包含匹配） | `text=PAGE1` |
| `xmin=`/`xmax=` | X 坐标范围过滤（使用实体中心点近似） | `xmin=13000` `xmax=14000` |
| `ymin=`/`ymax=` | Y 坐标范围过滤 | `ymin=0` `ymax=500` |
| `limit=` | 限制返回结果数（0=不返回任何数据） | `limit=50` |
| `count=true` | 仅返回匹配数量，不返回实体数据 | `count=true` |

**示例：搜索所有 Line 并过滤坐标范围**

```bash
dwgcli query drawing.dwg "type=Line xMin=13000 xMax=14000 yMin=5000 yMax=6000" --json
```

**示例：搜索特定图层上的 Insert**

```bash
dwgcli query drawing.dwg "type=Insert layer=EQU" --json
```

**示例：导出查询结果为 CSV**

```bash
dwgcli query drawing.dwg "type=Insert layer=EQU" --format csv --out inserts.csv
```

---

### 4. `dump` — 遍历文档结构树

**用途：** 输出完整的文档结构树（含图层、线型、文字样式、块定义、模型空间实体）。支持多种导出格式。

**语法：**

```bash
dwgcli dump <file> [--format <tree|batch|csv|excel>] [--depth <n>] [--out|-o <file>] [--json]
```

**参数：**

| 参数 | 说明 |
|------|------|
| `file` | DWG/DXF 文件路径 |
| `--format` | 输出格式：`tree`（默认，层级结构树）、`batch`（可重放的批量 JSON）、`csv`（CSV 表格）、`excel`（Excel .xlsx 文件） |
| `--depth` | 树深度，默认 10 |
| `--out` / `-o` | 写入文件。`--format excel` 时必填 |

**`csv` 格式：** 输出所有实体属性的 CSV 表格，自动收集所有属性键作为表头。

**`excel` 格式：** 导出为 .xlsx 文件（使用 ClosedXML），自动调整列宽。

**示例：导出为 CSV**

```bash
dwgcli dump drawing.dwg --format csv --out entities.csv
```

**示例：导出为 Excel**

```bash
dwgcli dump drawing.dwg --format excel --out drawing.xlsx
```

---

### 5. `stats` — 统计文档内容

**用途：** 按实体类型、图层、块引用三个维度汇总文档内容分布。

**语法：**

```bash
dwgcli stats <file> [--out <file>] [--json]
```

---

### 6. `add` — 添加实体或图层

**用途：** 向 DWG 文件中添加新实体或新图层。支持颜色模糊匹配（`red`、`blu` 等）。

**语法：**

```bash
# 添加图层
dwgcli add <file> /layers <name> [--prop key=value ...] [--json]

# 添加实体
dwgcli add <file> /entities <type> --prop key=value [--prop ...] [--attr TAG=VALUE ...] [--json]
```

**支持的实体类型和属性：**

| 类型 | 必填属性 | 可选属性 |
|------|----------|----------|
| `line` | `x1 y1 z1 x2 y2 z2` | `layer color linetype` |
| `circle` | `cx cy cz r` | `layer color linetype` |
| `arc` | `cx cy cz r startAngle endAngle` | `layer color linetype` |
| `text` | `x y z text` | `height`(默认2.5) `rotation`(默认0) `layer color linetype` |
| `mtext` | `x y z text` | `height`(默认2.5) `width` `layer color linetype` |
| `insert`/`blockref` | `block x y z` | `scaleX`(1) `scaleY`(1) `scaleZ`(1) `rotation`(0) `layer color linetype` |

---

### 7. `set` — 修改实体/图层/文档属性

**用途：** 修改文档元信息、图层属性或实体属性。支持图层开关/冻结/锁定操作。

**语法：**

```bash
dwgcli set <file> <path> --prop key=value [--prop ...] [--dry-run] [--json]
```

**支持的路径和可改属性：**

| 路径 | 可改属性 |
|------|----------|
| `/`（文档信息） | `author` `title` `subject` `comments` `keywords` |
| `/layer/{name}` | `name` `color` `linetype` `ison` `isfrozen` `islocked` `freeze` `thaw` `lock` `unlock` `toggleon` `show` `toggleoff` `hide` |
| `/entity/{handle}` | `layer` `color` `linetype` `lineweight` `transparency`(0-90) `material` `invisible` `linetypescale` |

**图层开关/冻结/锁定快捷操作：**

```bash
# 冻结图层
dwgcli set drawing.dwg /layer/Walls --prop freeze=true

# 锁定图层
dwgcli set drawing.dwg /layer/PIPE --prop lock=true

# 开关图层可见性
dwgcli set drawing.dwg /layer/EQU --prop toggleoff=true
```

**颜色值格式：**
- ACI 索引 (1-255)
- `#RRGGBB` 十六进制
- 命名颜色（支持 80+ 别名模糊匹配：red/yellow/green/cyan/blue/magenta/white/black/blu→blue 等）
- `ByLayer` / `ByBlock`

---

### 8. `remove` — 删除实体或图层

**用途：** 从文档中删除实体或图层。

**语法：**

```bash
dwgcli remove <file> <path> [--json]
```

**约束：** 不能删除 `0` 号图层或最后一个图层。

---

### 9. `purge` — 清理未使用项

**用途：** 删除文档中未被任何实体引用的图层、块定义和线型。

**语法：**

```bash
dwgcli purge <file> [--dry-run] [--json]
```

---

### 10. `batch` — 批量执行

**用途：** 在一个打开/保存周期内批量执行多个操作，支持 JSON 脚本和简写格式。

**语法：**

```bash
# JSON 方式
dwgcli batch <file> --input <json-file> [--stop-on-error] [--json]
dwgcli batch <file> --commands <json-string> [--stop-on-error] [--json]
dwgcli batch <file> [--stop-on-error] [--json]    # 从 stdin 读取

# 简写方式
dwgcli batch <file> --shorthand <shorthand-string> [--stop-on-error] [--json]
```

**简写格式说明：**

简写格式用管道符 `|` 分隔命令、参数和键值对，适合在聊天/对话中快速输入：

```
cmd|arg1|arg2|key=value|key=value
```

**支持的命令：**

| 简写命令 | 对应操作 | 示例 |
|----------|---------|------|
| `layer` | 添加图层 | `layer|Walls|color=red` |
| `line` | 画线 | `line|x1=0|y1=0|x2=100|y2=100|layer=Walls` |
| `circle` | 画圆 | `circle|cx=50|cy=50|r=30` |
| `arc` | 画弧 | `arc|cx=0|cy=0|r=50|start=0|end=90` |
| `text` | 添加文字 | `text|x=10|y=10|text=Hello|height=5` |
| `insert` | 插入块引用 | `insert|block=VALVE|x=500|y=300` |
| `set` | 修改属性 | `set|entity=1F3A|layer=Walls|color=red` |
| `remove` | 删除实体 | `remove|entity=1F3A` |
| `freeze` | 冻结图层 | `freeze|layer=Walls` |
| `thaw` | 解冻图层 | `thaw|layer=Walls` |
| `lock` | 锁定图层 | `lock|layer=PIPE` |
| `unlock` | 解锁图层 | `unlock|layer=PIPE` |

**示例：简写格式批量操作**

```bash
dwgcli batch drawing.dwg --shorthand "layer|Walls|color=red
line|x1=0|y1=0|x2=100|y2=100|layer=Walls
circle|cx=50|cy=50|r=30|layer=0"
```

---

### 11. `new` — 新建 DWG

**用途：** 创建一个新的空 DWG 文件（版本 AC1027）。

**语法：**

```bash
dwgcli new <file> [--json]
```

---

### 12. `block import` — 导入块定义

**用途：** 从源 DWG 文件中导入所有模型空间实体作为一个块定义到目标 DWG 中。

**语法：**

```bash
dwgcli block import <target> <source> --name <block-name> [--json]
```

---

## 配置（Config 级联搜索）

dwgcli 支持从以下路径**级联搜索**配置文件，后配置覆盖前配置：

1. `./dwgcli.json` — 当前目录（项目级）
2. `~/.dwgcli/config.json` — 用户目录（用户级）
3. 可执行文件所在目录
4. 内置默认值

### 配置项

```json
{
  "output": {
    "csvDelimiter": ",",
    "excelColumnWidth": 15,
    "dateTimeFormat": "yyyy-MM-dd HH:mm:ss",
    "coordinatePrecision": 3
  },
  "drawing": {
    "defaultTextHeight": 2.5,
    "backupOnSave": true,
    "defaultVersion": "AC1027"
  }
}
```

---

## 输出格式

### JSON 输出

所有结果通过统一信封包装。此外支持 **MCP UI 元数据**（`_meta` 字段），标注结果的类型（table/list/json）便于 AI 客户端渲染：

```json
{
  "success": true,
  "data": { ... },
  "_meta": {
    "ui": { "type": "table", "resourceUri": "drawing.dwg" }
  }
}
```

### CSV 输出

`dump --format csv` 和 `query --format csv` 输出标准 CSV 格式，自动收集所有实体的属性键作为表头。

### Excel 输出

`dump --format excel` 使用 ClosedXML 生成 .xlsx 文件，自动调整列宽，适合 Excel 直接打开。

### Text 输出

节点格式：每行一个节点，子节点按缩进层级排列。

变更类命令：输出简单文本消息。

---

## 实体类型支持列表

以下实体类型在查询/输出时被识别并提取专有属性：

| ACadSharp 类型 | ObjectName | 额外属性 |
|----------------|-----------|----------|
| **Line** | LINE | startPoint, endPoint, length, angle |
| **Circle** | CIRCLE | center, radius, diameter, circumference |
| **Arc** | ARC | center, radius, startAngle, endAngle, totalAngle |
| **Ellipse** | ELLIPSE | center, majorAxis, radiusRatio, startParam, endParam |
| **LwPolyline** | LWPOLYLINE | vertexCount, isClosed, elevation, width/startWidth |
| **Polyline2D/3D** | POLYLINE | vertexCount, isClosed, type |
| **Spline** | SPLINE | degree, controlPointCount, fitPointCount |
| **TextEntity** | TEXT | insertPoint, height, rotation, text |
| **MText** | MTEXT | insertPoint, height, width, rotation, text, **plainText**, lineCount |
| **Insert** | INSERT | insertPoint, blockName, scale, rotation, **sourceBlock**, attributes |
| **Hatch** | HATCH | patternName, isSolid, isAssociative |
| **Leader** | LEADER | vertexCount, arrowHeadEnabled |
| **RasterImage** | IMAGE | imageFile, pixelWidth, pixelHeight |
| **Dimension** 系列 | DIMENSION | measurement, various geometry fields |

所有实体通用属性：`handle`（16 进制句柄）、`layer`、`color`、`colorIndex`、`lineWeight`、`linetype`。

---

## 常见场景示例

### 1. 查看文件基本信息

```bash
dwgcli info drawing.dwg --json
```

### 2. 列出所有图层及其实体数量

```bash
dwgcli get drawing.dwg /layers --json
```

### 3. 按坐标范围查询某张分图的内容

```bash
dwgcli query drawing.dwg "xMin=13000 xMax=14000 yMin=5000 yMax=6000" --json
```

### 4. 统计不同块类型的数量

```bash
dwgcli stats drawing.dwg --json
```

### 5. 批量处理多个文件

```powershell
foreach ($file in Get-ChildItem *.dwg) {
  dwgcli stats $file.Name --json | Select-String "_total"
}
```

### 6. 将源文件作为块导入目标文件

```bash
dwgcli block import target.dwg source.dwg --name PUMP_SKID
```

### 7. 简写格式批量绘制

```bash
dwgcli batch drawing.dwg --shorthand "layer|Walls|color=red
line|x1=0|y1=0|x2=100|y2=100|layer=Walls"
```

### 8. 导出图纸所有内容为 Excel

```bash
dwgcli dump drawing.dwg --format excel --out drawing.xlsx
```

---

## 注意事项

### 只读模式 vs 编辑模式

- `info`、`get`、`query`、`dump`、`stats` — **只读**，不会修改文件
- `set`、`add`、`remove`、`purge`、`batch` — **编辑**，会修改文件
- `new` — 创建新文件
- `block import` — 修改目标文件

编辑命令会在写入前自动创建 `.bak` 备份文件（可在配置中关闭）。

### ACadSharp 格式支持范围

- **读取：** DWG (AC1009–AC1027, R12–2018), DXF (ASCII + Binary)
- **写入：** DWG (AC1021+, 即 AutoCAD 2007+); AC1027（AutoCAD 2018）最稳定
- **自动升级：** 低于 AC1021 的版本写入时会自动升级到 AC1027

### 颜色模糊匹配

dwgcli 支持 80+ 颜色别名模糊匹配，自动修正常见拼写错误：
- `blu` → `blue`、`gren` → `green`、`yelow` → `yellow`
- `magenta` → `magenta`、`cyn` → `cyan`
- 颜色索引 1-255 与命名颜色互通
- 输入无效颜色时返回最接近的匹配

---

## MCP Server

`dwgcli-mcp` 是 dwgcli 的 **Model Context Protocol** 服务器实现，让 AI 代理（如 Claude Desktop、Cursor、VS Code Copilot）直接调用 DWG 操作工具，无需拼接命令行。

### 概述

MCP Server 将 dwgcli 的核心功能通过 dispatch 机制暴露为统一工具。相比 CLI 方案：

- **结构化参数** — AI 直接传参，无需拼接命令行字符串
- **统一 JSON 响应** — 所有工具返回标准 JSON + `_meta` UI 元数据
- **共享核心代码** — 直接引用 ACadSharp 和 DwgHandler，与 CLI 行为一致
- **dispatch 模式** — `dwg_query`（读操作）+ `dwg_edit`（写操作）两个核心工具

### 构建和发布

```bash
cd src/dwgcli-mcp

# 开发运行
dotnet run

# 单文件发布
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

发布产物：`src/dwgcli-mcp/bin/Release/net10.0/win-x64/publish/dwgcli-mcp.exe`（约 76 MB，self-contained，无需 .NET runtime）。

### MCP Tools 清单

| Tool | 参数 | 说明 |
|------|------|------|
| `dwg_query` | `filePath`, `operations` | **统一读工具** — dispatch 支持 info/get/query/dump/stats |
| `dwg_edit` | `filePath`, `operations` | **统一写工具** — dispatch 支持 set/add/remove/purge/batch/layer |
| `dwg_shorthand` | `filePath`, `shorthand`, `stopOnError` | 简写格式批量操作（Phase 3 新增） |

**旧工具（已标记 `[Obsolete]`，保持兼容）：**

| Tool | 说明 |
|------|------|
| `dwg_info` | 委托到 `dwg_query` |
| `dwg_query` (旧) | 委托到 `dwg_query` |
| `dwg_get` | 委托到 `dwg_query` |
| `dwg_stats` | 委托到 `dwg_query` |
| `dwg_dump` | 委托到 `dwg_query` |
| `dwg_set` | 委托到 `dwg_edit` |
| `dwg_add` | 委托到 `dwg_edit` |
| `dwg_remove` | 委托到 `dwg_edit` |
| `dwg_purge` | 委托到 `dwg_edit` |

### `dwg_query` 使用示例

```json
{
  "filePath": "drawing.dwg",
  "operations": [
    { "action": "info" },
    { "action": "query", "selector": "type=Insert layer=EQU" }
  ]
}
```

### `dwg_edit` 使用示例

```json
{
  "filePath": "drawing.dwg",
  "operations": [
    { "action": "add", "parent": "/layers", "type": "layer", "props": { "name": "Walls", "color": "red" } },
    { "action": "add", "parent": "/entities", "type": "line", "props": { "x1": "0", "y1": "0", "x2": "100", "y2": "100", "layer": "Walls" } }
  ]
}
```

### `dwg_shorthand` 使用示例

```
layer|Walls|color=red
line|x1=0|y1=0|x2=100|y2=100|layer=Walls
freeze|layer=EQU
```

### 客户端配置示例

```json
{
  "mcpServers": {
    "dwgcli": {
      "command": "C:\\data\\code\\dwgcli\\src\\dwgcli-mcp\\bin\\Release\\net10.0\\win-x64\\publish\\dwgcli-mcp.exe"
    }
  }
}
```

### CLI vs MCP 选型建议

| 场景 | 推荐 |
|------|------|
| 脚本自动化、CI/CD、批处理 | **CLI** (`dwgcli.exe`) |
| AI 代理对话式查询/修改 DWG | **MCP** (`dwgcli-mcp.exe`) |
| 快速一次性查询 | 两者均可 |
| 多轮对话中频繁操作同一文件 | **MCP**（dispatch 模式更稳定） |

---

## 项目结构

```
src/
├── dwgcli/                              # CLI 工具
│   ├── Program.cs                       # 入口
│   ├── CommandBuilder.cs + partials     # 命令注册（Dictionary dispatch）
│   └── Core/
│       ├── IDwgHandler.cs               # 文档操作接口（10 个方法）
│       ├── DwgHandler.cs + 6 partials   # 核心实现（按功能拆分）
│       │   ├── DwgHandler.Info.cs       # Info/Get/Dump/Stats
│       │   ├── DwgHandler.Query.cs      # Query/ParseSelector
│       │   ├── DwgHandler.Crud.cs       # Set/Add/Remove/Purge
│       │   ├── DwgHandler.EntityProps.cs # 实体属性提取
│       │   ├── DwgHandler.Layout.cs     # 布局/视口管理
│       │   └── DwgHandler.Parsing.cs    # 坐标/颜色解析工具
│       ├── DwgNode.cs                   # 树节点模型
│       ├── BatchItem.cs                 # 批量操作模型
│       ├── InputValidator.cs            # 输入自动修正（颜色模糊匹配/数字转换）
│       ├── ShorthandParser.cs           # 简写格式解析器
│       ├── OutputFormatter.cs           # CSV/JSON/文本输出
│       ├── ExcelExporter.cs             # ClosedXML → .xlsx
│       ├── Config/
│       │   ├── DwgCliConfig.cs          # 配置模型
│       │   └── ConfigLoader.cs          # 级联搜索
│       └── Exceptions/
│           └── DwgExceptions.cs         # 9 个领域异常
│
├── dwgcli-mcp/                          # MCP Server（AI 代理集成）
│   ├── Program.cs                       # 2 unified tools + 9 Obsolete + shorthand
│   └── DwgHelper.cs                     # ExecuteRead/ExecuteWrite 中间件
│
└── tests/
    └── dwgcli.Tests/                    # xUnit 单元测试
        ├── dwgcli.Tests.csproj
        ├── InputValidatorTests.cs        # 45 测试
        ├── ParseColorTests.cs            # 17 测试
        └── UnitTest1.cs                  # 框架测试
```

## 依赖

- [ACadSharp](https://github.com/DomCR/ACadSharp) v3.4.9 — DWG/DXF 读写
- [System.CommandLine](https://github.com/dotnet/command-line-api) — CLI 解析
- [ClosedXML](https://github.com/ClosedXML/ClosedXML) — Excel 导出
- [ModelContextProtocol](https://github.com/modelcontextprotocol/csharp-sdk) — MCP SDK（仅 dwgcli-mcp）

## 许可

Apache-2.0
