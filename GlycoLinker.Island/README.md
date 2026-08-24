# GlycoLinker.Island

**GlycoLinker** 是一个将 [ClassIsland](https://classisland.tech) 接入 [Glycoprotein](https://glycoprotein.dev) 节点网络的插件, 让 ClassIsland 的自动化能力可以与外部节点双向联动。

## 功能

### 行动: 调用 Glycoprotein Action

在规则集中添加 **「调用 Glycoprotein Action」** 行动, 即可在自动化触发时调用外部 Glycoprotein 节点暴露的 Action:

- 通过自动补全选择目标节点 (Gid) 与字段 (Fid)
- 若目标字段带有 JSON Schema 参数, 会自动生成可视化参数表单, 无需手写 JSON
- 支持 `string` / `integer` / `number` / `boolean` / `enum` 类型的参数; 复杂参数可回退到原始 JSON 输入
- 调用带 10 秒超时, 失败会在行动项中显示错误

### 触发器: Glycoprotein 调用

添加 **「Glycoprotein 调用」** 触发器后, 本机会向网络暴露一个字段 (Fid)。外部节点对本机执行 `DoActionAsync(本机Gid, Fid)` 即可触发该触发器, 运行关联的工作流行动组。

## 使用前提

- 目标节点与本机必须处于同一 Glycoprotein 网络 (socket 目录: `%TEMP%\glycoprotein`)
- 各节点通过 Unix Domain Socket 自动发现彼此, 无需额外配置

## 常见问题

**为什么调用失败?**
- 检查目标节点 Gid / Fid 是否正确, 节点是否在线 (约 2 秒发现周期)
- 检查字段是否为 Action 或带参函数 (Event 字段不可通过本行动调用)

**触发器被调用但工作流未执行?**
- 确认【应用设置】->【自动化】已启用
- 确认工作流的行动组处于启用状态
