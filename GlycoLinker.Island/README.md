# GlycoLinker.Island
*A0.01*

> 本插件需要 ClassIsland 2.1.1.1 来运行, 其为开发构建.

> 下面暂时是AI夏季吧写的 不想写说是

**GlycoLinker** 是一个将 [ClassIsland](https://classisland.tech) 接入 [Glycoprotein](https://gitlab.com/LiPolymer/Glycoprotein) ([Github](https://github.com/LiPolymer/Glycoprotein)) 节点网络的插件, 让 ClassIsland 的自动化能力可以与外部节点双向联动。

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

### 自动化平台: Glycoprotein 积木 (SuperAutoIsland)

安装 [SuperAutoIsland](https://github.com/lrsgzs/SuperAutoIsland) (SAI, 需要 `0.3.9.2` 及以上) 后, 本插件会向 SAI 注册 **GlycoLinker** 积木分类, 让 Glycoprotein 能力可以在 SAI 的 Blockly 自动化里使用:

- **调用 Glycoprotein Action**: 目标节点为实时下拉框, 字段 ID 与参数 JSON (可空) 手填; 执行时复用本插件的调用行动 (10 秒超时、错误包装)
- **分发 Glycoprotein 事件**: 填写事件字段 ID 与可选的友好名称、描述, 执行前自动注册该事件字段再广播
- **数据积木**: 节点列表、节点字段 (输出 JSON 字符串; 指定节点不存在时输出 `{"error": "..."}`)
- **规则积木**: 节点存在、字段存在
- **动态行动积木**: 从用户保存的信标生成「调用 <节点> / <字段>」行动, 每个保存节点的每个 Method 字段一块, 不设数量截断。节点离线仍保留类型; 在线新信标真实删除字段或用户取消保存节点时才撤销对应定义。

插件用 SAI 的 `ICategoryProvider` 注册分类, 用前缀处理器接管数据/规则积木 (`glycolinker.sai.data.` / `glycolinker.sai.rule.`)。六个固定积木和实时节点下拉框的语义不变。只有保存成员或保存信标内容变化才通知 SAI 重建 (600ms 防抖); 单纯上下线只更新节点状态。SAI 缺失或兼容注册失败时跳过新导航, 保留原 GlycoLinker 设置页和原生行动/触发器。

#### 保存节点

SAI 兼容接口注册成功后, 在 ClassIsland 设置中打开 **GlycoLinker · SAI 节点**:

- 页面按说明、节点列表、刷新提示竖向排列, 沿用设置窗口标题, 不重复显示标题。未保存/已保存节点分为两张卡片并显示数量; 内容宽度小于 720 时改为上下排列。左侧为未保存在线节点及本会话收回的离线节点, 右侧为已保存节点; 每行右侧使用带文字的「保存 / 取消保存」按钮。
- 节点 Id 过长时省略显示, 悬停查看完整 Id; 第二行斜体显示非空 Vendor。绿色/红色圆点与「在线 / 离线」文字同时表示状态, 无 Vendor 的节点也保持按钮居中。列表高度受限并独立滚动, 空列表显示下一步提示; Blockly 刷新说明位于页面底部。
- 节点以 Id/Gid 区分 (Ordinal), 不以 Vendor 或字段 Fid 区分。配置初始保存列表为空, 发现节点不会自动保存。
- 保存完整 Method/Event 信标及参数/返回/事件 Schema, 在线同 Id 信标更新会立即同步保存, 离线保留最后成功保存版本。写盘失败不移动节点、不发布新定义, 页面显示错误。
- 离线取消保存的节点只在本会话左侧保留, 可立即移回; 关闭设置页不会清除, 重启应用会丢弃。普通未保存在线节点过期后直接从左侧消失。
- 取消保存意味着撤销该节点动态积木定义, 即使节点仍显示在本会话左侧。离线积木定义保留不代表离线节点可以接受网络调用。
- 注册发生在启动完成时; 若设置窗口已被提前创建, 关闭并重新打开以读取当前导航注册表。

#### 动态参数表单

- QuerySchema 按原生 `GlycoCallSettings` 的 properties 解析逻辑展开, 支持 .NET 导出的根 `type: ["object", "null"]`, 不要求根 type 必须是字符串 `"object"`。属性的 type 字符串/数组以及 anyOf、oneOf 中的 type 取第一个非 null 类型; string / integer / number / boolean 生成控件, string enum 生成下拉框。
- 任一属性含 object / array、无可展开的简单类型或 string enum 含非字符串时, 整块回退到完整 **PayloadJson**, 不保留半展开表单或嵌套属性的局部 JSON。空 enum 无可选值, 也回退完整 JSON。标签采用 Schema title 并保留原属性名, 必填标 `*`, description 放入积木提示; 参数键使用安全编码, 网络 JSON 恢复原属性名。
- 可选参数用 **omit / value** 模式; 可空参数另有 **null**。空字符串是合法值, 只有 omit 才表示省略。类型匹配的 default 映射到控件默认值, 超 JS 安全范围的整数 default 使用局部 JSON 保留精度。
- 无 Schema、缺少 properties 或空 properties 的 Method 不生成参数输入, 调用时不传 payload, 与原生表单清空参数的行为一致。非对象根 Schema 使用完整 **PayloadJson**。JSON 的 value 输入必须是非空有效 JSON; 缺必需值、非法模式/enum token、非整数等通过 SAI 原错误通路报错, 不降级为空参数。完整 Schema 验证与 10 秒网络超时仍由原生调用管线负责。

> SAI 前端只在页面加载时拉取一次积木列表。修改保存列表或信标后, 需要重新打开/刷新 Blockly 页面; 本插件不提供前端实时推送。

#### 升级已有 SAI 项目

先备份 SAI 项目, 再升级插件。先在新设置页保存旧项目使用的节点, 再打开旧 Blockly 项目, 避免缺少类型定义。此前未保存且已经离线的节点需等待上线才能取得完整信标。

动态积木 Id 保持不变, 但参数直接切换到 Schema 表单: **旧 PayloadJson 参数连接需手工重填**, 不提供 JSON 覆盖优先级或旧参数兼容层。六个固定积木的 JSON 输入不变。
