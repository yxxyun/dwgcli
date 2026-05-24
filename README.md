# dwgcli

> 基于 ACadSharp 的 AutoCAD DWG/DXF 命令行工具 — 读取、查询、修改、导出、批量操作，全栈搞定。

`dwgcli` 是一个 .NET CLI 工具 + MCP Server，支持对 .dwg 文件的**读取、查询、修改、新建、批量操作**，所有命令支持 `--json` 输出，方便 AI 和脚本集成。

```bash
dwgcli info drawing.dwg
dwgcli get drawing.dwg /layers --depth 2
dwgcli query drawing.dwg "type=Line layer=0"
dwgcli stats drawing.dwg
dwgcli dump drawing.dwg --format excel --out drawing.xlsx
dwgcli batch drawing.dwg --shorthand "layer|Walls|color=red\nline|x1=0|y1=0|x2=100|y2=100"
```

## 快速开始

```bash
# 构建
dotnet build -c Release

# 查看文档信息
dwgcli info sample.dwg --json

# 查询所有 Insert 实体
dwgcli query sample.dwg "type=Insert" --json

# 按坐标范围过滤（多张图放在一个文件中的场景）
dwgcli query sample.dwg "type=Insert xMin=13000 xMax=14000" --json

# 统计图纸内容
dwgcli stats sample.dwg --json

# 导出为 Excel
dwgcli dump sample.dwg --format excel --out sample.xlsx

# 简写格式批量操作
dwgcli batch sample.dwg --shorthand "layer|Walls|color=red\nline|x1=0|y1=0|x2=100|y2=100|layer=Walls"

# 修改实体文字坐标
dwgcli set sample.dwg /entity/1F3A --prop insertPoint="18276,85000,0"

# 冻结图层
dwgcli set sample.dwg /layer/EQU --prop freeze=true

# 批量执行
dwgcli batch sample.dwg --input commands.json

# MCP Server（AI 代理集成）
cd src/dwgcli-mcp && dotnet run
```

## 命令一览

| 命令 | 用途 | 文档 |
|------|------|------|
| `info` | 查看文档元信息 | [DOCS.md](./DOCS.md#info) |
| `get` | 按路径浏览文档结构 | [DOCS.md](./DOCS.md#get) |
| `dump` | 遍历文档结构树（支持 CSV/Excel 导出） | [DOCS.md](./DOCS.md#dump) |
| `query` | 按条件搜索实体/图层/块 | [DOCS.md](./DOCS.md#query) |
| `stats` | 统计图纸内容分布 | [DOCS.md](./DOCS.md#stats) |
| `set` | 修改实体/图层属性（含开关/冻结/锁定） | [DOCS.md](./DOCS.md#set) |
| `add` | 添加实体或图层 | [DOCS.md](./DOCS.md#add) |
| `remove` | 删除实体或图层 | [DOCS.md](./DOCS.md#remove) |
| `purge` | 清理未使用的图层/块/线型 | [DOCS.md](./DOCS.md#purge) |
| `new` | 创建空白 DWG | [DOCS.md](./DOCS.md#new) |
| `block import` | 从外部 DWG 导入块定义 | [DOCS.md](./DOCS.md#block-import) |
| `batch` | 单周期批量执行（支持 JSON 和简写格式） | [DOCS.md](./DOCS.md#batch) |
| `mcp` | MCP Server（AI 代理集成） | [DOCS.md](./DOCS.md#mcp-server) |

> 完整命令参考、参数说明、JSON 示例、场景用例请查阅 **[DOCS.md](./DOCS.md)**

## 特色功能

- **CSV/Excel 导出** — `dump --format csv|excel` 直接出表格数据
- **简写格式批量** — `batch --shorthand "line|x1=0|y1=0|x2=100|y2=100"` 省 token
- **颜色模糊匹配** — `blu`→`blue`、`gren`→`green`，80+ 别名自动补全
- **Config 级联** — 当前目录 → 用户目录 → 程序目录 → 默认值
- **图层快捷操作** — `freeze`/`thaw`/`lock`/`unlock`/`toggleon`/`toggleoff`
- **MCP Unified Tools** — `dwg_query` + `dwg_edit` 两个 dispatch 工具搞定一切
- **76 个单元测试** — 纯函数全覆盖，无需 .dwg 文件即可验证

## 项目结构

```
src/
├── dwgcli/                              # CLI 工具
│   ├── Program.cs + 12 CommandBuilder*   # 命令注册（Dictionary dispatch）
│   └── Core/
│       ├── IDwgHandler.cs               # 文档操作接口
│       ├── DwgHandler.cs + 6 partials   # 核心实现（~2000 行）
│       ├── DwgNode.cs / BatchItem.cs    # 数据模型
│       ├── InputValidator.cs            # 输入自动修正
│       ├── ShorthandParser.cs           # 简写格式解析
│       ├── OutputFormatter.cs / ExcelExporter.cs  # 输出
│       ├── Config/                      # 级联配置
│       └── Exceptions/                  # 9 个领域异常
│
├── dwgcli-mcp/                          # MCP Server（2 unified + 9 Obsolete）
│
└── tests/
    └── dwgcli.Tests/                    # 76 个单元测试
```

## 依赖

- [ACadSharp](https://github.com/DomCR/ACadSharp) v3.4.9 — DWG/DXF 读写
- [System.CommandLine](https://github.com/dotnet/command-line-api) — CLI 解析
- [ClosedXML](https://github.com/ClosedXML/ClosedXML) — Excel 导出
- [ModelContextProtocol](https://github.com/modelcontextprotocol/csharp-sdk) — MCP SDK

## 许可

Apache-2.0
