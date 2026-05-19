# dwgcli — DWG Command-Line Interface

> 基于 ACadSharp 的 AutoCAD .dwg/.dxf 文件命令行工具，支持读取、查询、修改和创建 CAD 文件。
> 专为 AI 代理和人类用户设计，`--json` 输出格式可直接供 AI 使用。

---

## 概述

dwgcli 是一个 .NET 命令行工具，提供对 AutoCAD DWG/DXF 文件的程序化访问能力：

- **读取** — 查看文档元信息、图层、块定义、布局、实体列表
- **查询** — 按类型、图层、颜色、坐标范围等条件搜索实体
- **导出** — 以 JSON 或文本格式输出文档结构树
- **修改** — 添加/删除实体、修改属性、清理未使用项
- **创建** — 新建空 DWG 文件、从源 DWG 导入块定义
- **批量** — 通过 JSON 脚本批量执行多个操作

### 技术栈

- **.NET 10** (net10.0)
- **ACadSharp** v3.4.9 — 纯 C# CAD 文件读写库
- **System.CommandLine** — 命令行解析

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
dwgcli info <file> [--json]
```

**参数：**

| 参数 | 说明 |
|------|------|
| `file` | DWG/DXF 文件路径 |

**JSON 输出示例：**

```json
{
  "success": true,
  "data": {
    "path": "/",
    "type": "dwg",
    "properties": {
      "fileName": "drawing.dwg",
      "version": "AC1027",
      "author": "",
      "title": "",
      "subject": "",
      "comments": "",
      "keywords": "",
      "created": "2026-05-10 18:25:47",
      "modified": "2026-05-10 18:25:47",
      "entityCount": 10508,
      "layerCount": 47,
      "blockCount": 124,
      "linetypeCount": 8,
      "textStyleCount": 5,
      "layoutCount": 2,
      "layoutNames": "Model, Layout1"
    }
  }
}
```

> **注意：** 如果 DWG 解析过程中有警告（如不支持的实体类型），会显示 `warnings` 字段。

---

### 2. `get` — 按路径访问文档结构

**用途：** 按路径精确访问文档的某个部分：根节点、图层、实体、块定义、布局等。

**语法：**

```bash
dwgcli get <file> <path> [--depth <n>] [--json]
```

**参数：**

| 参数 | 说明 |
|------|------|
| `file` | DWG/DXF 文件路径 |
| `path` | 文档路径（见下方路径说明） |
| `--depth` | 子节点展开深度，默认 1，0 表示不展开子节点 |

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

**示例：列出所有图层及其实体数量**

```bash
dwgcli get drawing.dwg /layers --json
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
          "colorIndex": 7,
          "isOn": true,
          "isFrozen": false,
          "isLocked": false,
          "lineWeight": "Default",
          "plot": true,
          "linetype": "Continuous",
          "entityCount": 128
        }
      },
      {
        "path": "/layer/EQU",
        "type": "layer",
        "properties": {
          "name": "EQU",
          "color": "7",
          "isOn": false,
          "isFrozen": false,
          "isLocked": false,
          "lineWeight": "Default",
          "plot": true,
          "linetype": "Continuous",
          "entityCount": 255
        }
      }
    ]
  }
}
```

**示例：查询指定地块的布局及视口**

```bash
dwgcli get drawing.dwg /layout/Model --json
```

---

### 3. `query` — 搜索实体

**用途：** 按条件搜索实体、图层或块定义。支持坐标范围过滤。

**语法：**

```bash
dwgcli query <file> <selector> [--json]
```

**参数：**

| 参数 | 说明 |
|------|------|
| `file` | DWG/DXF 文件路径 |
| `selector` | 空格/逗号/分号分隔的 `key=value` 条件 |

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
| `color=` | 按 ACI 颜色索引匹配 | `color=1`（红色） |
| `linetype=` | 按线型名匹配 | `linetype=Continuous` |
| `hastext=` | 是否为文本类实体 | `hastext=true` |
| `xmin=`/`xmax=` | X 坐标范围过滤（使用实体中心点近似） | `xmin=13000` `xmax=14000` |
| `ymin=`/`ymax=` | Y 坐标范围过滤 | `ymin=0` `ymax=500` |

**图层搜索（`target=layers`）：**

| 条件 | 说明 |
|------|------|
| `target=layers` | 切换为搜索图层 |
| `name=` | 图层名 |
| `ison=` | 是否开启 |
| `isfrozen=` | 是否冻结 |
| `color=` | 图层颜色 |

**块定义搜索（`target=blocks`）：**

| 条件 | 说明 |
|------|------|
| `target=blocks` | 切换为搜索块定义 |
| `name=` | 块名 |
| `isdynamic=` | 是否为动态块 |
| `isanonymous=` | 是否为匿名块 |

**示例：搜索所有 Line 并过滤坐标范围**

```bash
dwgcli query drawing.dwg "type=Line xMin=13000 xMax=14000 yMin=5000 yMax=6000" --json
```

**示例：搜索特定图层上的 Insert**

```bash
dwgcli query drawing.dwg "type=Insert layer=EQU" --json
```

```json
{
  "success": true,
  "data": {
    "matches": 4,
    "results": [
      {
        "path": "/entity/4E",
        "type": "INSERT",
        "properties": {
          "handle": "4E",
          "layer": "EQU",
          "color": "256",
          "colorIndex": 256,
          "lineWeight": "ByLayer",
          "linetype": "ByLayer",
          "insertPoint": "25.500,11.118,0.000",
          "blockName": "DISPLAY_2",
          "scale": "1.000,1.000,1.000",
          "rotation": 0,
          "blockIsDynamic": false
        }
      }
    ]
  }
}
```

**示例：搜索图层**

```bash
dwgcli query drawing.dwg "target=layers name=0"
```

---

### 4. `dump` — 遍历文档结构树

**用途：** 输出完整的文档结构树（含图层、线型、文字样式、块定义、模型空间实体）。

**语法：**

```bash
dwgcli dump <file> [--format <tree|batch>] [--depth <n>] [--out|-o <file>] [--json]
```

**参数：**

| 参数 | 说明 |
|------|------|
| `file` | DWG/DXF 文件路径 |
| `--format` | 输出格式：`tree`（默认，层级结构树）或 `batch`（可重放的批量 JSON） |
| `--depth` | 树深度，默认 10 |
| `--out` / `-o` | 写入文件（`-` 表示 stdout） |

**`tree` 格式输出根节点：**

```
/ (dwg) fileName=drawing.dwg version=AC1027 entityCount=10508 ...
  /layers (layers)
    /layer/0 (layer) ...
    /layer/EQU (layer) ...
  /blocks (blocks)
    /block/OS (block) ...
  /entities (entities)
    /entity/1F3A (LINE) ...
    /entity/4E (INSERT) ...
```

**`batch` 格式：** 输出一个 JSON 数组，其中每条是可被 `batch` 命令回放的 `add` 操作。可用于迁移/复制文件内容。

**示例：将结构树保存到文件**

```bash
dwgcli dump drawing.dwg --out structure.txt
```

---

### 5. `stats` — 统计文档内容

**用途：** 按实体类型、图层、块引用三个维度汇总文档内容分布。

**语法：**

```bash
dwgcli stats <file> [--json]
```

**参数：**

| 参数 | 说明 |
|------|------|
| `file` | DWG/DXF 文件路径 |

**JSON 输出示例：**

```json
{
  "success": true,
  "data": {
    "path": "/stats",
    "type": "stats",
    "children": [
      {
        "path": "/stats/type",
        "type": "stats_entityTypes",
        "properties": {
          "LINE": 3200,
          "INSERT": 104,
          "LWPOLYLINE": 450,
          "CIRCLE": 89,
          "ARC": 67,
          "MTEXT": 210,
          "ATTDEF": 104,
          "_total": 4224
        }
      },
      {
        "path": "/stats/layer",
        "type": "stats_layers",
        "properties": {
          "EQU": 2550,
          "PIPE": 890,
          "0": 784
        }
      },
      {
        "path": "/stats/block",
        "type": "stats_blocks",
        "properties": {
          "IDEATAG01_PID_IC_D8": 48,
          "VALVE_GATE": 24,
          "OS": 12
        }
      }
    ]
  }
}
```

> `stats` 的块引用统计会自动解析匿名动态块（`*U` 开头的块名），显示其原始块定义名（`sourceBlock`）。

---

### 6. `add` — 添加实体或图层

**用途：** 向 DWG 文件中添加新实体或新图层。

**语法：**

```bash
# 添加图层
dwgcli add <file> /layers <name> [--prop key=value ...] [--json]

# 添加实体
dwgcli add <file> /entities <type> --prop key=value [--prop ...] [--attr TAG=VALUE ...] [--json]
```

**参数：**

| 参数 | 说明 |
|------|------|
| `file` | DWG/DXF 文件路径 |
| `parent` | `/layers` 或 `/entities` |
| `type` | 实体类型（见下方） |
| `--prop` | `key=value` 属性（可重复） |
| `--attr` | 块属性 `TAG=VALUE`（仅 `insert` 类型，可重复） |

**支持的实体类型和属性：**

| 类型 | 必填属性 | 可选属性 |
|------|----------|----------|
| `line` | `x1 y1 z1 x2 y2 z2` | `layer color linetype` |
| `circle` | `cx cy cz r` | `layer color linetype` |
| `arc` | `cx cy cz r startAngle endAngle` | `layer color linetype` |
| `text` | `x y z text` | `height`(默认2.5) `rotation`(默认0) `layer color linetype` |
| `mtext` | `x y z text` | `height`(默认2.5) `width` `layer color linetype` |
| `insert`/`blockref` | `block x y z` | `scaleX`(1) `scaleY`(1) `scaleZ`(1) `rotation`(0) `layer color linetype` |

**示例：添加一条线**

```bash
dwgcli add drawing.dwg /entities line `
  --prop x1=0 y1=0 z1=0 x2=100 y2=100 z2=0 `
  --prop layer=0 color=red
```

**示例：添加一个带属性的块引用**

```bash
dwgcli add drawing.dwg /entities insert `
  --prop block=VALVE_GATE x=500 y=300 z=0 `
  --attr TAG=VV-001 `
  --attr EN=PUMP_A
```

---

### 7. `set` — 修改实体/图层/文档属性

**用途：** 修改文档元信息、图层属性或实体属性。

**语法：**

```bash
dwgcli set <file> <path> --prop key=value [--prop ...] [--dry-run] [--json]
```

**参数：**

| 参数 | 说明 |
|------|------|
| `file` | DWG/DXF 文件路径 |
| `path` | 要修改的目标路径 |
| `--prop` | `key=value` 属性对（可重复） |
| `--dry-run` | 预览修改但不写入文件 |

**支持的路径和可改属性：**

| 路径 | 可改属性 |
|------|----------|
| `/`（文档信息） | `author` `title` `subject` `comments` `keywords` |
| `/layer/{name}` | `name` `color` `linetype` `ison` `isfrozen` `islocked` |
| `/entity/{handle}` | `layer` `color` `linetype` `lineweight` `transparency`(0-90) `material` `invisible` `linetypescale` |
| 实体几何（Line） | `startpoint` `endpoint` |
| 实体几何（Circle/Arc） | `center` `radius` |
| 实体几何（Text/MText） | `text` `height` |

**颜色值格式：** ACI 索引 (1-255)、`#RRGGBB` 十六进制、命名颜色（red/yellow/green/cyan/blue/magenta/white/black）或 `ByLayer`/`ByBlock`。

**示例：修改实体图层**

```bash
dwgcli set drawing.dwg /entity/1F3A --prop layer=Walls --prop color=red
```

---

### 8. `remove` — 删除实体或图层

**用途：** 从文档中删除实体或图层。

**语法：**

```bash
dwgcli remove <file> <path> [--json]
```

**参数：**

| 参数 | 说明 |
|------|------|
| `file` | DWG/DXF 文件路径 |
| `path` | `/entity/{handle}` 或 `/layer/{name}` |

**约束：**
- 不能删除最后一个图层
- 不能删除 `0` 号图层

**示例：**

```bash
dwgcli remove drawing.dwg /entity/1F3A
dwgcli remove drawing.dwg /layer/TEMP_LAYER
```

---

### 9. `purge` — 清理未使用项

**用途：** 删除文档中未被任何实体引用的图层、块定义和线型。

**语法：**

```bash
dwgcli purge <file> [--dry-run] [--json]
```

**参数：**

| 参数 | 说明 |
|------|------|
| `file` | DWG/DXF 文件路径 |
| `--dry-run` | 预览模式（不实际删除） |

**清理规则：**

- **图层：** 保留 `0` 号图层和所有有实体的图层
- **块定义：** 保留 `*Model_Space`、`*Paper_Space` 和所有被 `Insert` 引用的块
- **线型：** 保留 `BYLAYER`、`BYBLOCK`、`Continuous` 和所有被引用的线型

**示例：**

```bash
dwgcli purge drawing.dwg --dry-run --json
```

---

### 10. `batch` — 批量执行

**用途：** 在一个打开/保存周期内批量执行多个操作，适合自动化工作流。

**语法：**

```bash
dwgcli batch <file> --input <json-file> [--stop-on-error] [--json]
dwgcli batch <file> --commands <json-string> [--stop-on-error] [--json]
dwgcli batch <file> [--stop-on-error] [--json]    # 从 stdin 读取
```

**参数：**

| 参数 | 说明 |
|------|------|
| `file` | DWG/DXF 文件路径 |
| `--input` | JSON 批处理文件 |
| `--commands` | 内联 JSON 批处理字符串 |
| `--stop-on-error` | 遇到错误时中止 |

**BatchItem JSON 格式：**

```json
{
  "command": "info|get|dump|query|set|add|remove|purge",
  "path": "...",          // get/set/remove/add 的路径
  "parent": "...",        // add 的父路径（替代 path）
  "type": "line|...",     // add 的类型
  "selector": "...",      // query 的选择器
  "depth": 1,             // get/dump 的深度
  "props": {},            // set/add 的属性
  "attrs": {}             // add insert 的属性
}
```

**示例：**

```bash
dwgcli batch drawing.dwg --commands ^
  '[{"command":"add","parent":"/layers","type":"layer","props":{"name":"Walls","color":"red"}},{"command":"add","parent":"/entities","type":"line","props":{"x1":"0","y1":"0","z1":"0","x2":"100","y2":"100","z2":"0","layer":"Walls"}}]' ^
  --json
```

---

### 11. `new` — 新建 DWG

**用途：** 创建一个新的空 DWG 文件（版本 AC1027）。

**语法：**

```bash
dwgcli new <file> [--json]
```

**参数：**

| 参数 | 说明 |
|------|------|
| `file` | 输出 DWG 文件路径 |

**示例：**

```bash
dwgcli new new_drawing.dwg
```

---

### 12. `block import` — 导入块定义

**用途：** 从源 DWG 文件中导入所有模型空间实体作为一个块定义到目标 DWG 中。

**语法：**

```bash
dwgcli block import <target> <source> --name <block-name> [--json]
```

**参数：**

| 参数 | 说明 |
|------|------|
| `target` | 目标 DWG 文件 |
| `source` | 源 DWG 文件 |
| `--name` | 块定义名称（必填） |

**导入过程：**
1. 读取源 DWG 的所有模型空间实体
2. 将源文档中的图层、线型、文字样式复制到目标文档（如不存在）
3. 在目标文档中创建块记录
4. 克隆所有支持的实体类型到块中

**支持的克隆实体类型：** `Line`、`Circle`、`Arc`、`LwPolyline`、`MText`、`TextEntity`、`Insert`（含属性）、`Hatch`、`AttributeDefinition`

**示例：**

```bash
dwgcli block import target.dwg source.dwg --name PUMP_SKID
```

---

## 输出格式

### JSON 输出

所有结果通过一个统一信封包装：

**成功（含数据）：**

```json
{
  "success": true,
  "data": { ... }
}
```

**成功（含文本消息）：**

```json
{
  "success": true,
  "data": "Added line at /entity/1F3A"
}
```

**查询结果（获取多条）：**

```json
{
  "success": true,
  "data": {
    "matches": 5,
    "results": [ ... ]
  }
}
```

**错误：**

```json
{
  "success": false,
  "message": "Layer not found: 'InvalidLayer'"
}
```

**批量执行结果：**

```json
{
  "success": true,
  "data": {
    "results": [
      { "index": 0, "success": true, "output": "..." }
    ],
    "summary": {
      "total": 3,
      "executed": 3,
      "succeeded": 3,
      "failed": 0
    }
  }
}
```

### Text 输出

**节点格式：** 每行一个节点：

```
{path} ({type}) {text} key=value key=value children=N
```

- `text` 只有文本类实体才有
- `children=N` 在有子节点但不展开时显示
- 子节点按缩进层级排列

**变更类命令：** 输出简单文本消息，如 `"Updated /layer/Walls: color=red"`。

---

## 实体类型支持列表

以下实体类型在查询/输出时被识别并提取专有属性：

| ACadSharp 类型 | ObjectName | 额外属性 |
|----------------|-----------|----------|
| **Line** | LINE | startPoint, endPoint, length, angle |
| **Circle** | CIRCLE | center, radius, diameter, circumference |
| **Arc** | ARC | center, radius, startAngle, endAngle, totalAngle |
| **Ellipse** | ELLIPSE | center, majorAxis, majorAxisEndPoint, radiusRatio, startParam, endParam |
| **LwPolyline** | LWPOLYLINE | vertexCount, isClosed, elevation, width/startWidth |
| **Polyline2D** | POLYLINE | vertexCount, isClosed, type="Polyline2D" |
| **Polyline3D** | POLYLINE | vertexCount, isClosed, type="Polyline3D" |
| **Spline** | SPLINE | degree, controlPointCount, fitPointCount, isClosed, isPeriodic, isRational |
| **Point** | POINT | location, thickness |
| **TextEntity** | TEXT | insertPoint, height, rotation, text |
| **MText** | MTEXT | insertPoint, height, width, rotation, text, **plainText**, lineCount |
| **Insert** | INSERT | insertPoint, blockName, scale, rotation, blockIsDynamic, **sourceBlock**, attributes, attributeCount |
| **AttributeDefinition** | ATTDEF | tag, text, height, rotation, insertPoint, flags, isAttributeDefinition |
| **AttributeEntity** | ATTRIB | tag, text, height, rotation, insertPoint, flags, isAttributeReference |
| **Hatch** | HATCH | patternName, isSolid, isAssociative, elevation |
| **Leader** | LEADER | vertexCount, arrowHeadEnabled, pathType, creationType, hasHookline, horizontalDir, normal |
| **RasterImage** | IMAGE | imageFile, pixelWidth, pixelHeight, insertPoint, sizeU, sizeV, brightness, contrast, fade |
| **DimensionLinear** | DIMENSION | type="RotatedDimension", measurement, rotation, firstPoint, secondPoint, dimLinePoint, textMidPoint, text |
| **DimensionAligned** | DIMENSION | type="AlignedDimension", measurement, firstPoint, secondPoint, offset, dimLinePoint, textMidPoint, text |
| **DimensionAngular2Line** | DIMENSION | type="Angular2LineDimension", measurement, center, firstPoint, secondPoint, angleVertex, dimArcPoint, textMidPoint, text |
| **DimensionAngular3Pt** | DIMENSION | type="Angular3PointDimension", measurement, firstPoint, secondPoint, angleVertex, dimLinePoint, textMidPoint, text |
| **DimensionRadius** | DIMENSION | type="RadialDimension", measurement, center, radiusPoint, leaderLength, textMidPoint, text |
| **DimensionDiameter** | DIMENSION | type="DiametricDimension", measurement, center, radius, diameterPoint, dimLinePoint, leaderLength, textMidPoint, text |
| **DimensionOrdinate** | DIMENSION | type="OrdinateDimension", measurement, featureLocation, leaderEndpoint, dimLinePoint, isOrdinateTypeX, textMidPoint, text |
| **Dimension** (通用) | DIMENSION | type="Dimension", measurement, dimLinePoint, textMidPoint, text, style |

所有实体通用属性：`handle`（16进制句柄）、`layer`（图层名）、`color`（颜色）、`colorIndex`（ACI索引）、`lineWeight`（线宽）、`linetype`（线型名）。

坐标值格式：`"X.FFF,Y.FFF,Z.FFF"`（3位小数）。
角度值格式：度（十进制度数）。

---

## 常见场景示例

### 1. 查看文件基本信息

```bash
dwgcli info drawing.dwg --json
```

快速了解：版本号、实体总数、图层数、块数、布局数。

### 2. 列出所有图层及其实体数量

```bash
dwgcli get drawing.dwg /layers --json
```

每个图层显示 `entityCount`，可快速定位有内容的图层和空图层。

### 3. 按坐标范围查询某张分图的内容

```bash
dwgcli query drawing.dwg "xMin=13000 xMax=14000 yMin=5000 yMax=6000" --json
```

适用于多张图排列在同一个 Model Space 中的场景，按坐标范围提取某张分图的所有实体。

### 4. 提取所有仪表标签的 KKS 编码

```bash
dwgcli query drawing.dwg "type=Insert" --json
```

从 Insert 实体的 `attributes` 字段中提取 `KKS1=...; KKS2=...; CONTROLNAME=...` 等属性值。

### 5. 统计不同块类型的数量

```bash
dwgcli stats drawing.dwg --json
```

`/stats/block` 节点按块引用次数排序，可快速了解图纸使用了哪些标准图块。

### 6. 批量处理多个文件

```powershell
# 脚本化批量处理多个文件
foreach ($file in Get-ChildItem *.dwg) {
  dwgcli stats $file.Name --json | Select-String "_total"
}
```

### 7. 将源文件作为块导入目标文件

```bash
dwgcli block import target.dwg source.dwg --name PUMP_SKID
```

---

## 关键字段说明

### Insert 实体

| 字段 | 说明 |
|------|------|
| `blockName` | 块定义名称 |
| `blockIsDynamic` | 是否为动态块 |
| `sourceBlock` | **匿名动态块时显示原始块定义名。** 动态块被 Explode 后，`blockName` 可能显示为 `*U34992`，此时 `sourceBlock` 会给出原始名称如 `IDEATAG01_PID_IC_D8` |
| `attributes` | 块属性值，分号分隔的 `TAG=VALUE` 格式 |
| `attributeCount` | 属性数量 |

### MText 实体

| 字段 | 说明 |
|------|------|
| `text` | 原始文本（含 AutoCAD 格式控制符如 `\P`、`\pxqc;`、`\F...;`） |
| `plainText` | **去除格式控制符后的纯文本。** 格式符说明：`\P`=换行，`\pxqc;`=段落对齐居中，`\L`/`\l`=下划线开/关，`\O`/`\o`=上划线开/关，`\F{fontname;text}`=字体切换 |
| `lineCount` | 行数 |

### 图层

| 字段 | 说明 |
|------|------|
| `entityCount` | 该图层上的实体总数（含模型空间和块记录中的实体） |
| `isOn` | 是否可见 |
| `isFrozen` | 是否冻结 |
| `isLocked` | 是否锁定 |
| `plot` | 是否可打印 |
| `color` | `#RRGGBB`（真彩色）或 ACI 索引号 |
| `linetype` | 线型名 |

### 颜色值

- **ACI 索引：** 整数 1-255（1=红, 2=黄, 3=绿, 4=青, 5=蓝, 6=品红, 7=白/黑）
- **真彩色：** `#RRGGBB` 格式
- **特殊：** `ByLayer`（随层）、`ByBlock`（随块）

### 布局

| 字段 | 说明 |
|------|------|
| `minLimits`/`maxLimits` | 布局范围（XY坐标） |
| `viewports` | 视口描述列表，每个包含 center、width、height，可含 `paper` 标记 |

---

## 注意事项

### 只读模式 vs 编辑模式

- `info`、`get`、`query`、`dump`、`stats` — **只读**，不会修改文件
- `set`、`add`、`remove`、`purge`、`batch` — **编辑**，会修改文件
- `new` — 创建新文件
- `block import` — 修改目标文件

编辑命令会在写入前自动创建 `.bak` 备份文件。

### ACadSharp 格式支持范围

- **读取：** DWG (AC1009–AC1027, R12–2018), DXF (ASCII + Binary)
- **写入：** DWG (AC1021+, 即 AutoCAD 2007+); AC1027（AutoCAD 2018）最稳定
- **自动升级：** 低于 AC1021 的版本写入时会自动升级到 AC1027
- **DXF 写入：** 当前仅支持 DWG 写入

### 大文件处理建议

- `dump` 命令在超大文件（数万实体）时输出可能很长，建议配合 `--out` 写入文件
- `query` 返回的结果集会包含所有匹配实体，数量大时建议搭配 `xMin/xMax/yMin/yMax` 或其他过滤条件缩小范围
- `stats` 和 `info` 对大文件开销最小，是最快的"先了解"手段
- 批量操作建议使用 `batch` 命令在一个打开/保存周期内完成，减少重复 I/O

### 句柄（Handle）

- 实体句柄是 DWG 文件中唯一标识实体的 16 进制数字
- 可以通过 `get /entities` 列出所有实体句柄，或用 `query` 按条件搜索后查看
- 句柄在文件的整个生命周期内保持不变
- 格式：16 进制大写字母，如 `/entity/1F3A`
