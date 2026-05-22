# dwgcli

> 基于 ACadSharp 的 AutoCAD DWG/DXF 命令行工具

`dwgcli` 是一个 .NET CLI 工具，支持对 .dwg 文件的**读取、查询、修改、新建**操作，所有命令支持 `--json` 输出，方便 AI 和脚本集成。

```bash
dwgcli info drawing.dwg
dwgcli get drawing.dwg /layers --depth 2
dwgcli query drawing.dwg "type=Line layer=0"
dwgcli stats drawing.dwg
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

# 搜索包含特定文字的实体
dwgcli query sample.dwg "text=PAGE1" --json

# 限制查询结果数（先看几条）
dwgcli query sample.dwg "type=MText limit=5" --json

# 仅统计匹配数量
dwgcli query sample.dwg "type=Insert count=true"

# 大结果写入文件（避免管道编码问题）
dwgcli query sample.dwg "hastext=true" --out result.json

# 修改实体文字坐标
dwgcli set sample.dwg /entity/1F3A --prop insertPoint="18276,85000,0"

# 统计图纸内容
dwgcli stats sample.dwg --json

# 批量执行
dwgcli batch sample.dwg --input commands.json
```

## 命令一览

| 命令 | 用途 | 文档 |
|------|------|------|
| `info` | 查看文档元信息 | [DOCS.md](./DOCS.md#info) |
| `get` | 按路径浏览文档结构 | [DOCS.md](./DOCS.md#get) |
| `dump` | 遍历文档结构树 | [DOCS.md](./DOCS.md#dump) |
| `query` | 按条件搜索实体/图层/块 | [DOCS.md](./DOCS.md#query) |
| `stats` | 统计图纸内容分布 | [DOCS.md](./DOCS.md#stats) |
| `set` | 修改实体/图层属性 | [DOCS.md](./DOCS.md#set) |
| `add` | 添加实体或图层 | [DOCS.md](./DOCS.md#add) |
| `remove` | 删除实体或图层 | [DOCS.md](./DOCS.md#remove) |
| `purge` | 清理未使用的图层/块/线型 | [DOCS.md](./DOCS.md#purge) |
| `new` | 创建空白 DWG | [DOCS.md](./DOCS.md#new) |
| `block import` | 从外部 DWG 导入块定义 | [DOCS.md](./DOCS.md#block-import) |
| `batch` | 单周期批量执行多条命令 | [DOCS.md](./DOCS.md#batch) |
| `mcp` | MCP Server（AI 代理集成） | [DOCS.md](./DOCS.md#mcp-server) |

> 完整命令参考、参数说明、JSON 示例、场景用例请查阅 **[DOCS.md](./DOCS.md)**

## 项目结构

```
src/
├── dwgcli/                    # CLI 工具
│   ├── Program.cs             # 入口
│   ├── CommandBuilder*.cs     # 命令注册（每个命令一个文件）
│   └── Core/
│       ├── IDwgHandler.cs     # 文档操作接口
│       ├── DwgHandler.cs      # 核心实现（~1970 行）
│       ├── DwgNode.cs         # 树节点模型
│       └── OutputFormatter.cs # 文本/JSON 输出
│
└── dwgcli-mcp/                # MCP Server（AI 代理集成）
    ├── Program.cs             # 9 个 MCP tools
    └── DwgHelper.cs           # 内部辅助类
```

## 依赖

- [ACadSharp](https://github.com/DomCR/ACadSharp) v3.4.9 — DWG/DXF 读写
- [System.CommandLine](https://github.com/dotnet/command-line-api) — CLI 解析

## 许可

Apache-2.0
