# dwgcli 实战反馈 & 改进建议

> 来自真实场景：处理 ~75K 实体的 DCS 接线图 DWG，读取、查询、修改 202 个 PAGE1 页码占位符（含坐标对齐）。

---

## 1. `set` 命令 — 增加 insertPoint 支持

**现状：** 仅支持修改 `startpoint`/`endpoint`（Line）、`center`/`radius`（Circle/Arc）、`text`/`height`（Text），
不支持修改 Text/MText/Insert 的 `insertPoint` 坐标。

**场景：** 页码文字和"总页数"文字 X 坐标差了 18.7，导致上下不对齐，需要把页码的 insertPoint 改到跟总页数一致。

**建议：** 增加 `insertpoint`/`insertPoint`（大小写不敏感）属性支持，作用于 TextEntity、MText、Insert 实体。

**已在 `DwgHandler.cs:1036~1046`（ApplyEntityProps 方法）实现，搜索 "insertpoint" 可定位，待 review 纳入主线。**

---

## ~~2. JSON 输出编码 — 改用 UTF-8~~ ✅ 跳过

**结论：** `Console.OutputEncoding = UTF8` 已在 Program.cs 设置。
UTF-16LE 是 PowerShell 重定向/管道的生态问题，不是 dwgcli 的 bug。
用 `> file.json` 时在 PS 前面加 `$OutputEncoding = [Console]::OutputEncoding` 可解。

---

## 3. 大结果集 JSON 中的控制字符

**现状：** `query "hastext=true" --json` 返回大 JSON（12MB+），AutoCAD 文本内容中
包含 `\P`、`\pxql;` 等格式控制符，导致标准 JSON 解析器报错：
```
json.decoder.JSONDecodeError: Invalid control character at: line 10490 column 24
```

**建议：** 输出 JSON 前对实体 `text` 字段做转义清理，确保输出是合法的标准 JSON。

---

## 4. 增加 `--out` / `-o` 输出到文件

**现状：** 大量数据只能 stdout，需要 shell 重定向。

**问题：** 配合 UTF-16 编码一起用，重定向经常出乱子。

**建议：** `info`、`get`、`query`、`dump`、`stats` 等读命令增加 `--out <file>` / `-o <file>`
选项，直接写入文件，文件名后缀决定编码（`.json` 用 UTF-8）。

---

## 5. query 增加 `text=` 搜索条件

**现状：** 只有 `hastext=true/false`，无法精确搜索文字内容。

**场景：** 想搜所有 `text == "PAGE1"` 的实体，只能 `hastext=true` 全量拉回 2.6 万条再本地过滤，
浪费带宽和时间。

**建议：** 增加 `text=` 条件，支持：
- `text=PAGE1` — 精确匹配
- `text*=PAGE` — 模糊匹配（LIKE 风格）
- 可选大小写敏感开关

---

## 6. query 增加 `limit` / 分页

**现状：** 匹配多少就返回多少，无上限。`hastext=true` 返回 26,286 条，12MB JSON。

**场景：** 先看看有没有匹配的、大概多少条，没必要全量拉回。

**建议：** 加 `limit=50` 限制返回条数，或者纯统计模式 `count=true` 只返回数量不返实体数据。

---

## 7. batch 结果输出

**现状：** batch 把每个子命令的详细结果全量输出，202 条命令打印 200 多行。

**建议：** 增加 `--summary-only` 选项，只输出 `succeeded/failed/total` 统计摘要，减少刷屏。

---

## 8. 文档小修

**现状：** README.md 和 DOCS.md 里没有 `insertPoint` 相关说明，`set` 命令支持的属性列表需要补充。
