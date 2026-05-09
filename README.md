# dwgcli

**DWG/DXF 文件 CLI 工具** — 基于 .NET 10 和 ACadSharp 构建，支持读取、查询、修改 AutoCAD 图形文件。

```
dwgcli info drawing.dwg
dwgcli get drawing.dwg /layers --depth 2
dwgcli query drawing.dwg "type=Line layer=0"
dwgcli set drawing.dwg /layer/Walls --prop color=red
dwgcli add drawing.dwg /entities line --prop x1=0 y1=0 x2=100 y2=100
dwgcli batch drawing.dwg --commands '[{"command":"info"}]'
```

---

## 目录

- [概述](#概述)
- [安装](#安装)
- [命令参考](#命令参考)
  - [info](#info)
  - [get](#get)
  - [dump](#dump)
  - [query](#query)
  - [set](#set)
  - [add](#add)
  - [remove](#remove)
  - [purge](#purge)
  - [batch](#batch)
  - [new](#new)
  - [block import](#block-import)
- [拓扑图构建工作流](#拓扑图构建工作流)
  - [new](#new)
  - [block import](#block-import)
- [路径系统](#路径系统)
- [查询语法](#查询语法)
- [批处理脚本](#批处理脚本)
- [JSON 模式](#json-模式)
- [架构说明](#架构说明)
- [开发](#开发)

---

## 概述

dwgcli 是一个命令行工具，用于处理 AutoCAD DWG 和 DXF 文件。它提供了一套完整的 CRUD 操作：

- **读取**：查看文件元信息、文档结构树、分层浏览图层/实体/块
- **查询**：按条件搜索实体和图层
- **修改**：修改属性、添加/删除实体和图层、清理未使用的元素
- **批处理**：在一个打开/保存周期内执行多个操作，大幅提升性能

所有命令均支持 `--json` 输出，方便 AI 和脚本集成。

---
---

## 安装

### 前置条件

- .NET 10 SDK 或更高版本

### 构建

```bash
git clone <repo-url>
cd dwgcli
dotnet build -c Release
```

编译产物位于 `src/dwgcli/bin/Release/net10.0/dwgcli.exe`。

### 全局工具安装（可选）

```bash
dotnet pack -c Release
dotnet tool install --global --add-source ./nupkg dwgcli
```

---

## 命令参考

所有命令的通用选项：

| 选项 | 说明 |
|---|---|
| `--json` | 以 JSON 格式输出（AI 友好模式） |

---

### info

显示 DWG 文件的元信息：版本、作者、图层/实体/块数量等。

```bash
dwgcli info <file>
```

**参数：**

| 参数 | 说明 |
|---|---|
| `file` | DWG/DXF 文件路径 |

**示例：**

```bash
dwgcli info drawing.dwg
# fileName: drawing.dwg
# version: AC1027
# author: John Doe
# created: 2024-01-15 10:30:00
# entityCount: 1245
# layerCount: 12
# blockCount: 5
```

**JSON 输出：**

```bash
dwgcli info drawing.dwg --json
```

```json
{
  "success": true,
  "data": {
    "path": "/",
    "type": "dwg",
    "properties": {
      "fileName": "drawing.dwg",
      "version": "AC1027",
      "author": "John Doe",
      "created": "2024-01-15 10:30:00",
      "entityCount": 1245,
      "layerCount": 12,
      "blockCount": 5,
      "linetypeCount": 5,
      "textStyleCount": 2
    }
  }
}
```

---

### get

按路径获取文档节点，支持浏览文档结构树。

```bash
dwgcli get <file> [<path>] [--depth <n>]
```

**参数：**

| 参数 | 说明 | 默认值 |
|---|---|---|
| `path` | 文档路径，如 `/`、`/layers`、`/layer/0`、`/layer/Walls`、`/entities`、`/entity/{handle}`、`/blocks`、`/block/*Model_Space` | `/` |
| `--depth` | 包含子节点的深度（0=无子节点，1=直接子节点） | `1` |

**示例：**

```bash
dwgcli get drawing.dwg
dwgcli get drawing.dwg /layers --depth 2
dwgcli get drawing.dwg /entity/1F3
dwgcli get drawing.dwg /block/*Model_Space --depth 3
```

**JSON 输出：**

```bash
dwgcli get drawing.dwg /layers --depth 2 --json
```

```json
{
  "success": true,
  "data": {
    "path": "/layers",
    "type": "layers",
    "childCount": 3,
    "children": [
      {
        "path": "/layer/0",
        "type": "layer",
        "properties": {
          "name": "0",
          "color": "7",
          "isOn": true,
          "isFrozen": false,
          "isLocked": false
        }
      }
    ]
  }
}
```

---

### dump

导出完整的文档结构树，或生成可重放的批处理脚本。

```bash
dwgcli dump <file> [--format tree|batch] [--depth <n>] [--out <file>|-o <file>]
```

**参数：**

| 参数 | 说明 | 默认值 |
|---|---|---|
| `--format` | 输出格式：`tree`（结构树）或 `batch`（可重放批处理 JSON） | `tree` |
| `--depth` | 树的深度 | `10` |
| `--out` / `-o` | 输出到文件而非 stdout。指定 `-` 强制 stdout | - |

**示例：**

```bash
# 以树状结构查看完整文档
dwgcli dump drawing.dwg

# 生成重放脚本（JSON 数组）
dwgcli dump drawing.dwg --format batch > recreate.json

# 后续可用 batch 命令重放
dwgcli batch new_drawing.dwg --input recreate.json
```

**JSON 输出（tree 格式）：**

```bash
dwgcli dump drawing.dwg --depth 2 --json
```

```json
{
  "success": true,
  "data": {
    "path": "/",
    "type": "dwg",
    "children": [
      { "path": "/layers", "type": "layers", "childCount": 3 },
      { "path": "/entities", "type": "entities", "childCount": 50 }
    ]
  }
}
```

---

### query

按条件搜索实体、图层或块。

```bash
dwgcli query <file> "<selector>"
```

**selector 语法：** `key=value key=value`，多个条件用空格分隔（AND 逻辑）。

**示例：**

```bash
# 搜索所有 Line 实体
dwgcli query drawing.dwg "type=Line"

# 搜索指定图层上的圆
dwgcli query drawing.dwg "type=Circle layer=Walls"

# 搜索图层
dwgcli query drawing.dwg "target=layers name=Walls"

# 搜索块
dwgcli query drawing.dwg "target=blocks isDynamic=true"

# 搜索包含文字的实体
dwgcli query drawing.dwg "hasText=true"
```

**JSON 输出：**

```bash
dwgcli query drawing.dwg "type=Line" --json
```

```json
{
  "success": true,
  "data": {
    "matches": 2,
    "results": [
      {
        "path": "/entity/1F3",
        "type": "Line",
        "properties": {
          "handle": "1F3",
          "layer": "0",
          "startPoint": "0.000,0.000,0.000",
          "endPoint": "100.000,100.000,0.000"
        }
      }
    ]
  }
}
```

---

### set

修改图层、实体或文档摘要信息的属性。

```bash
dwgcli set <file> <path> --prop key=value [--prop key=value ...] [--dry-run]
```

**示例：**

```bash
# 修改文档信息
dwgcli set drawing.dwg / --prop author=Me --prop title="My Drawing"

# 修改图层属性
dwgcli set drawing.dwg /layer/Walls --prop color=red --prop isFrozen=false

# 修改实体属性
dwgcli set drawing.dwg /entity/1F3 --prop layer=Walls --prop color=#FF0000

# 预览修改
dwgcli set drawing.dwg /layer/Walls --prop color=blue --dry-run
```

**JSON 输出：**

```bash
dwgcli set drawing.dwg /layer/Walls --prop color=red --json
```

```json
{
  "success": true,
  "data": "Updated /layer/Walls: color=red",
  "message": "Updated /layer/Walls: color=red"
}
```

**可设置的实体属性：**

| 属性 | 适用 | 说明 |
|---|---|---|
| `layer` | 所有实体 | 图层名 |
| `color` | 所有实体 | 颜色值（索引、#RRGGBB 或名称） |
| `linetype` | 所有实体 | 线型名 |
| `lineweight` | 所有实体 | 线宽枚举值 |
| `transparency` | 所有实体 | 透明度 0-90（0=不透明） |
| `material` | 所有实体 | 材质名 |
| `invisible` | 所有实体 | true/false |
| `linetypescale` | 所有实体 | 线型缩放 |
| `startPoint` | Line | 起点 `x,y,z` |
| `endPoint` | Line | 终点 `x,y,z` |
| `center` | Circle/Arc | 圆心 `x,y,z` |
| `radius` | Circle/Arc | 半径 |
| `text` | Text/MText | 文字内容 |
| `height` | Text/MText | 文字高度 |

---

### add

向图形添加实体或图层。

```bash
dwgcli add <file> <parent> <type> --prop key=value [--prop key=value ...] [--attr tag=value ...]
```

**参数：**

| 参数 | 说明 |
|---|---|
| `parent` | 父路径。`/entities`（添加实体）或 `/layers`（添加图层） |
| `type` | 元素类型。实体：`line`、`circle`、`arc`、`text`、`mtext`、`insert`；表格：`layer` |
| `--attr` | 属性值（仅适用于 insert 类型），格式 `tag=value`，可重复。为块参照的 ATTDEF 提供替代值 |

**示例：**

```bash
# 添加直线
dwgcli add drawing.dwg /entities line --prop x1=0 y1=0 x2=100 y2=100

# 添加圆
dwgcli add drawing.dwg /entities circle --prop cx=50 cy=50 r=25 --prop color=red

# 添加文字
dwgcli add drawing.dwg /entities text --prop x=10 y=10 text="Hello" height=3.5

# 添加块参照
dwgcli add drawing.dwg /entities insert --prop block=MyBlock x=0 y=0

# 添加带属性值的块参照（仅 insert 类型支持 --attr）
dwgcli add drawing.dwg /entities insert --prop block=OS x=0 y=0 --attr TAG=OS-001 EN=Pump_A CN=1A INC=15'

# 添加图层
dwgcli add drawing.dwg /layers layer --prop name=Walls color=red
```

**JSON 输出：**

```bash
dwgcli add drawing.dwg /entities line --prop x1=0 y1=0 x2=100 y2=100 --json
```

```json
{
  "success": true,
  "data": "Added line at /entity/1F4",
  "message": "Added line at /entity/1F4"
}
```

**实体类型与属性对照表：**

| 类型 | 必需属性 | 可选属性 | 附加参数 |
|---|---|---|---|
| `line` | `x1`, `y1`, `x2`, `y2` | `z1`, `z2` | - |
| `circle` | `cx`, `cy`, `r` | `cz` | - |
| `arc` | `cx`, `cy`, `r`, `startAngle`, `endAngle` | `cz` | - |
| `text` | `x`, `y`, `text` | `z`, `height`(默认2.5), `rotation` | - |
| `mtext` | `x`, `y`, `text` | `z`, `height`(默认2.5), `width` | - |
| `insert` | `block`, `x`, `y` | `z`, `scaleX`/`scaleY`/`scaleZ`(默认1), `rotation` | `--attr tag=value` |

---

### remove

删除实体或图层。

```bash
dwgcli remove <file> <path>
```

**参数：**

| 参数 | 说明 |
|---|---|
| `file` | DWG/DXF 文件路径 |
| `path` | 要删除的元素路径。支持 `/entity/{handle}`（删除实体）和 `/layer/{name}`（删除图层） |

**删除规则与约束：**

- **删除实体**：通过十六进制句柄定位，支持从模型空间**和块定义**中删除实体
- **删除图层**：按图层名称删除，需满足以下条件：
  - 不能删除图层 `"0"`
  - 不能删除最后一个剩余图层（至少保留一个图层）
  - 删除图层时**不会**删除该图层上的实体（实体仍在，但可能因丢失图层引用而不可见）

**示例：**

```bash
# 按句柄删除模型空间中的实体（句柄可通过 get 或 query 获取）
dwgcli remove drawing.dwg /entity/1F3

# 删除块定义中的实体
dwgcli remove drawing.dwg /entity/2A1

# 删除图层（先确认图层上没有重要实体）
dwgcli remove drawing.dwg /layer/Walls
```

**JSON 输出：**

```bash
dwgcli remove drawing.dwg /entity/1F3 --json
```

```json
{
  "success": true,
  "data": "Removed /entity/1F3"
}
```

> **注意**：删除图层时如果该图层上仍有实体引用，这些实体在 AutoCAD 中将变为不可见（引用已删除的图层）。建议先使用 `query` 检查图层上的实体引用，必要时先用 `set` 将实体移至其他图层。

---

### purge

清理未使用的图层、块和线型，减少文件体积，提高图纸整洁度。

```bash
dwgcli purge <file> [--dry-run]
```

**参数：**

| 参数 | 说明 |
|---|---|
| `file` | DWG/DXF 文件路径 |
| `--dry-run` | 预览模式，仅显示将要清理的内容，不实际删除 |

**使用场景：**

- **图纸清理**：从外部导入的 DWG 常包含大量未使用的图层和块定义，使用 purge 快速清理冗余元素
- **文件瘦身**：删除废弃的块定义和线型可显著减小文件体积
- **CI/CD 质量检查**：在自动化流程中作为后处理步骤，确保输出图纸的整洁性

**示例：**

```bash
# 预览要清理的内容
dwgcli purge drawing.dwg --dry-run
# Dry-run: would purge 3 item(s): layer/TempLayer, block/ObsoleteBlock, linetype/PHANTOM

# 执行清理
dwgcli purge drawing.dwg
# Purged 3 item(s): layer/TempLayer, block/ObsoleteBlock, linetype/PHANTOM
```

**JSON 输出：**

```bash
dwgcli purge drawing.dwg --dry-run --json
```

```json
{
  "success": true,
  "data": "Dry-run: would purge 3 item(s): layer/TempLayer, block/ObsoleteBlock, linetype/PHANTOM"
}
```

**清理逻辑：**

| 清理目标 | 删除条件 | 保留项 |
|---|---|---|
| 图层 | 没有实体引用 | 图层 `"0"` |
| 块定义 | 没有 `Insert` 实体引用 | `*Model_Space`、`*Paper_Space` 及所有以 `*` 开头的系统块 |
| 线型 | 没有被实体使用 | `BYLAYER`、`BYBLOCK`、`Continuous` |

---

### batch

在一个打开/保存周期内执行多条命令，大幅提升批量修改性能。

```bash
dwgcli batch <file> --commands <json-array>
dwgcli batch <file> --input <json-file>
echo <json-array> | dwgcli batch <file>
```

**参数：**

| 参数 | 说明 |
|---|---|
| `--commands` | 内联 JSON 数组 |
| `--input` | 从 JSON 文件读取 |
| `--stop-on-error` | 遇到第一个错误即中止（默认继续并报告） |

**示例：**

```bash
# 内联批处理
dwgcli batch drawing.dwg --commands ^
  '[{"command":"info"},{"command":"query","selector":"type=Line"},{"command":"set","path":"/layer/Walls","props":{"color":"red"}}]'

# 从文件读取
dwgcli batch drawing.dwg --input commands.json

# 通过 stdin 管道
cat commands.json | dwgcli batch drawing.dwg

# 结合 dump --format batch 使用
dwgcli dump source.dwg --format batch > recreate.json
dwgcli batch target.dwg --input recreate.json
```

**批处理 JSON 格式（每个 item 的字段）：**

| 字段 | 类型 | 说明 | 适用命令 |
|---|---|---|---|
| `command` | string | **必填**。命令名 | 全部 |
| `path` | string | 路径 | get, set, remove, add |
| `parent` | string | 父路径 | add |
| `type` | string | 元素类型 | add |
| `selector` | string | 查询条件 | query |
| `depth` | int | 深度 | get, dump |
| `props` | object | 属性键值对 | set, add |
| `attrs` | object | 属性值键值对（仅 add insert 时有效） | add |

---

### new

从零创建一个空的 DWG 文件（AC1027 格式）。

```bash
dwgcli new <file>
```

**参数：**

| 参数 | 说明 |
|---|---|
| `file` | 输出的 DWG 文件路径 |

**默认内容：**

创建完成后，ACadSharp 库自动初始化以下默认表条目：

| 类别 | 默认条目 |
|---|---|
| 图层 | `0` |
| 块定义 | `*Model_Space`、`*Paper_Space` |
| 线型 | `BYLAYER`、`BYBLOCK`、`Continuous` |
| 文字样式 | `Standard` |

**示例：**

```bash
# 创建空图
dwgcli new blank.dwg
# Created empty DWG: blank.dwg

# 创建后可用 info 验证
dwgcli info blank.dwg
# layerCount: 1
# blockCount: 2
# linetypeCount: 3
# textStyleCount: 1
```

**JSON 输出：**

```bash
dwgcli new blank.dwg --json
```

```json
{
  "success": true,
  "data": "Created empty DWG: blank.dwg",
  "message": "Created empty DWG: blank.dwg"
}
```

---

### block import

将外部 DWG 文件模型空间中的实体导入为目标 DWG 中的命名块（BlockRecord），同时自动拷贝依赖的图层、线型、文字样式表。

```bash
dwgcli block import <target> <source> --name <block-name>
```

**参数：**

| 参数 | 说明 |
|---|---|
| `target` | 目标 DWG 文件（必须已存在，可用 `new` 命令创建） |
| `source` | 源 DWG 文件（符号图块） |
| `--name` | 导入后的块名称 |

**示例：**

```bash
# 创建空图并导入符号
dwgcli new panel.dwg
dwgcli block import panel.dwg CPU.dwg --name CPU
dwgcli block import panel.dwg IO.dwg --name IO
```

**JSON 输出：**

```bash
dwgcli block import panel.dwg CPU.dwg --name CPU --json
```

```json
{
  "success": true,
  "data": "Imported block 'CPU' from CPU.dwg into panel.dwg"
}
```

**导入规则与细节：**

1. **支持的实体类型** — 以下实体类型会被精确克隆到目标块定义中：

   | 类型 | 说明 |
   |---|---|
   | `Line` | 直线 |
   | `Circle` | 圆 |
   | `Arc` | 圆弧 |
   | `LwPolyline` | 轻量多段线 |
   | `TextEntity` | 单行文字 |
   | `MText` | 多行文字 |
   | `Insert` | 块参照（嵌套引用通过 MemberwiseClone 保留） |
   | `Hatch` | 填充图案 |
   | `AttributeDefinition` | 属性定义 |

   其他实体类型会被**静默跳过**（不报错，但不会被包含在块定义中）。

2. **依赖表条目自动拷贝**：
   - **图层**：源文件中所有图层拷贝到目标文件（目标中已有同名图层则不重复添加）
   - **线型**：源文件中所有线型拷贝到目标文件
   - **文字样式**：源文件中所有文字样式拷贝到目标文件

3. **块名冲突**：目标文件中已存在同名块时，命令**报错退出**，不会覆盖已有块定义

4. **嵌套块引用**：`Insert` 实体的 `Block` 引用通过 `MemberwiseClone` 保留，要求该块记录已在目标文档中存在（设计为在拓扑图工作流中先导入所有符号块，再排布）

---

## 拓扑图构建工作流

利用 `new` + `block import` + `batch` 三个命令，可以由外部 AI 或脚本灵活拼接网络拓扑图或柜面布置图。

### 典型流程

```bash
# 1. 创建空图
dwgcli new topology.dwg

# 2. 导入符号块定义
dwgcli block import topology.dwg CPU.dwg --name CPU
dwgcli block import topology.dwg IO.dwg  --name IO
dwgcli block import topology.dwg EX.dwg  --name EX
dwgcli block import topology.dwg OS.dwg  --name OS

# 3. AI 生成排布 JSON，用 batch 执行
dwgcli batch topology.dwg --input layout.json
```

### 排布 JSON 示例 (`layout.json`)

```json
[
  {"command":"add","path":"/layers","type":"layer","props":{"name":"连接线","color":"3"}},
  {"command":"add","path":"/entities","type":"insert","props":{"block":"CPU","x":"0","y":"0","scale":"1"}},
  {"command":"add","path":"/entities","type":"insert","props":{"block":"IO","x":"0","y":"-60","scale":"1"}},
  {"command":"add","path":"/entities","type":"insert","props":{"block":"EX","x":"80","y":"-30","scale":"1"}},
  {"command":"add","path":"/entities","type":"insert","props":{"block":"OS","x":"160","y":"0","scale":"1"}},
  {"command":"add","path":"/entities","type":"line","props":{"x1":"25","y1":"0","x2":"80","y2":"-28","layer":"连接线"}},
  {"command":"add","path":"/entities","type":"line","props":{"x1":"25","y1":"-60","x2":"80","y2":"-32","layer":"连接线"}},
  {"command":"add","path":"/entities","type":"line","props":{"x1":"80","y1":"-28","x2":"135","y2":"0","layer":"连接线"}}
]
```

### 设计思想

`dwgcli` 提供**底层原语**（创建图纸、导入块、添加实体），拓扑排布的逻辑由外部 AI 根据具体配置灵活生成。这样既能复用符号库，又能应对不同的布局需求——不把硬编码的规则塞进 CLI 中。

---

## 路径系统

dwgcli 使用类似文件系统的路径结构来定位文档中的元素：

```
/                   根节点（文档信息）
/info               文档信息（同 /）
/layers             图层列表
/layer/{name}       指定图层（/layer/Walls）
/layer/{index}      按索引访问（/layer/0）
/entities           模型空间实体列表
/entity/{handle}    按句柄访问实体（/entity/1F3）
/blocks             块定义列表
/block/{name}       指定块定义（/block/*Model_Space）
/linetypes          线型列表
/linetype/{name}    指定线型
/styles             文字样式列表
/style/{name}       指定文字样式
```

---

## 查询语法

查询使用 `key=value` 格式，多个条件用空格分隔（AND 逻辑）：

```
type=Line layer=0 color=1
```

**可用的查询键：**

| 键 | 适用目标 | 说明 |
|---|---|---|
| `target` | 全部 | 搜索范围：`entities`（默认）、`layers`、`blocks` |
| `type` | entities | 实体类型名，如 `Line`、`Circle`、`Arc`、`Insert` |
| `layer` | entities | 图层名 |
| `handle` | entities | 十六进制句柄 |
| `color` | entities, layers | ACI 颜色索引或 `#RRGGBB` |
| `linetype` | entities | 线型名 |
| `hasText` | entities | `true` 或 `false`，筛选有文本的实体 |
| `name` | layers, blocks | 名称精确匹配 |
| `isOn` | layers | `true`/`false` |
| `isFrozen` | layers | `true`/`false` |
| `isDynamic` | blocks | `true`/`false` |
| `isAnonymous` | blocks | `true`/`false` |

---

## JSON 模式

所有命令加上 `--json` 标志后，输出会包裹在标准响应信封中：

**成功响应：**
```json
{
  "success": true,
  "data": { ... }
}
```

**错误响应：**
```json
{
  "success": false,
  "message": "Error description"
}
```

批处理命令在 `--json` 模式下会输出详细的结果和摘要：
```json
{
  "success": true,
  "data": {
    "results": [
      { "index": 0, "success": true, "output": "..." },
      { "index": 1, "success": false, "error": "...", "item": { ... } }
    ],
    "summary": { "total": 2, "executed": 2, "succeeded": 1, "failed": 1 }
  }
}
```

---

## 架构说明

### 项目结构

```
dwgcli/
├── src/dwgcli/
│   ├── Program.cs                  # 入口点
│   ├── dwgcli.csproj               # 项目文件（.NET 10 + ACadSharp + System.CommandLine）
│   ├── CommandBuilder.cs           # 根命令构建 + SafeRun/Batch 执行框架
│   ├── CommandBuilder.Add.cs       # add 命令
│   ├── CommandBuilder.Batch.cs     # batch 命令
│   ├── CommandBuilder.Block.cs     # block import 命令
│   ├── CommandBuilder.Dump.cs      # dump 命令
│   ├── CommandBuilder.Get.cs       # get 命令
│   ├── CommandBuilder.Info.cs      # info 命令
│   ├── CommandBuilder.New.cs       # new 命令
│   ├── CommandBuilder.Purge.cs     # purge 命令
│   ├── CommandBuilder.Query.cs     # query 命令
│   ├── CommandBuilder.Remove.cs    # remove 命令
│   ├── CommandBuilder.Set.cs       # set 命令
│   └── Core/
│       ├── IDwgHandler.cs          # 文档操作接口
│       ├── DwgHandler.cs           # 核心实现（1540 行，封装 ACadSharp CadDocument）
│       ├── DwgHandlerFactory.cs    # 工厂：打开 DWG/DXF 文件
│       ├── DwgNode.cs              # 文档树节点模型
│       ├── BatchItem.cs            # 批处理命令数据模型
│       └── OutputFormatter.cs      # 文本/JSON 输出格式化器
└── README.md
```

### 关键依赖

- **[ACadSharp](https://github.com/DomCR/ACadSharp) v3.4.9** — 开源的 .NET DWG/DXF 库，负责文件读写和文档模型
- **[System.CommandLine](https://github.com/dotnet/command-line-api) v3.0.0-preview.2** — .NET 官方命令行解析库

### 设计模式

1. **Partial Class 命令注册**：`CommandBuilder` 作为 `static partial class`，每个命令一个子文件，保持代码组织清晰
2. **工厂 + 接口**：`DwgHandlerFactory.Open()` 返回 `IDwgHandler` 接口，支持 DWG/DXF 两种格式
3. **SafeRun 错误处理**：统一异常捕获，支持 JSON 错误信封
4. **批处理事务**：`batch` 命令在单个打开/保存周期内执行多条操作，避免重复 IO

---

## 开发

### 构建

```bash
dotnet build
```

### 运行

```bash
dotnet run --project src/dwgcli -- info drawing.dwg
```

### 添加新命令

1. 新建 `CommandBuilder.{Command}.cs`
2. 在 `partial class CommandBuilder` 中添加 `Build{Command}Command` 方法
3. 在 `BuildRootCommand()` 中注册新命令
4. 在 `IDwgHandler` 接口和 `DwgHandler` 实现中添加对应的操作方法
5. 在 `ExecuteBatchItem` 中添加批处理分支

---

## 许可

Apache-2.0
