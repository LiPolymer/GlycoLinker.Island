# GlycoLinker.Island

> 下面暂时是AI夏季吧写的 不想写说是

**GlycoLinker** 是一个将 [ClassIsland](https://classisland.tech) 接入 [Glycoprotein](https://glycoprotein.dev) 节点网络的插件, 让 ClassIsland 的自动化能力可以与外部节点双向联动。

## 功能

### 行动: 调用 Glycoprotein Action

在规则集中添加 **「调用 Glycoprotein Action」** 行动, 即可在自动化触发时调用外部 Glycoprotein 节点暴露的 Action:

- 通过自动补全选择目标节点 (Gid) 与字段 (Fid)
- 若目标字段带有 JSON Schema 参数, 会自动生成可视化参数表单, 无需手写 JSON
- 参数类型可用标准特性标注易读名: `[Display(Name = "易读名", Description = "说明")]` 或 `[Description("易读名")]`, 表单会以易读名展示参数 (原名以小字保留)
- 支持 `string` / `integer` / `number` / `boolean` / `enum` 类型的参数; 复杂参数可回退到原始 JSON 输入
- 调用带 10 秒超时, 失败会在行动项中显示错误

### 行动: 分发 Glycoprotein 事件

在规则集中添加 **「分发 Glycoprotein 事件」** 行动, 即可以本机节点身份向网络广播一个事件:

- 填写要广播的结构域 ID (Fid), 事件无参数; 可为字段附加友好名称与描述, 便于网络上其他节点识别
- 配置 Fid 后即自动在本节点注册该事件字段并广播 (其他节点可通过「Glycoprotein 事件」触发器发现并订阅它), 修改 Fid 自动换注
- 订阅了 `[本机G节点ID, Fid]` 的节点 (通过 `OnEvent`) 会收到回调, 事件为即发即忘, 不等待响应

### 触发器: Glycoprotein 事件

添加 **「Glycoprotein 事件」** 触发器, 订阅指定源节点的 Event 结构域:

- 通过自动补全选择源节点 (Gid) 与事件字段 (Fid), 列表仅包含 Event 类型字段
- 当源节点广播对应结构域的事件时, 触发本工作流
- 修改订阅后自动生效 (防抖 600ms)

### 触发器: Glycoprotein 调用

添加 **「Glycoprotein 调用」** 触发器后, 本机会向网络暴露一个字段 (Fid)。外部节点对本机执行 `DoActionAsync(本机Gid, Fid)` 即可触发该触发器, 运行关联的工作流行动组。可为字段附加友好名称与描述, 便于网络上其他节点识别。

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
