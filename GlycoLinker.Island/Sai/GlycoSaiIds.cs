namespace GlycoLinker.Island.Sai;

/// <summary>
/// SAI 集成使用的分类、积木 id 与前缀。
/// </summary>
public static class GlycoSaiIds {
    /// <summary>SAI 里显示的分类名（同时是分类唯一标识）。</summary>
    public const string CategoryName = "GlycoLinker";

    /// <summary>调用 Glycoprotein Action 的行动积木 id。</summary>
    public const string CallActionId = "glycolinker.sai.action.call";

    /// <summary>分发 Glycoprotein 事件的行动积木 id。</summary>
    public const string EmitActionId = "glycolinker.sai.action.emit";

    /// <summary>被前缀处理器接管的数据积木 id 前缀。</summary>
    public const string DataPrefix = "glycolinker.sai.data.";

    /// <summary>被前缀处理器接管的规则积木 id 前缀。</summary>
    public const string RulePrefix = "glycolinker.sai.rule.";

    /// <summary>节点列表数据积木 id。</summary>
    public const string NodesDataId = DataPrefix + "nodes";

    /// <summary>节点字段数据积木 id。</summary>
    public const string FieldsDataId = DataPrefix + "fields";

    /// <summary>节点存在规则积木 id。</summary>
    public const string NodeExistsRuleId = RulePrefix + "nodeExists";

    /// <summary>字段存在规则积木 id。</summary>
    public const string FieldExistsRuleId = RulePrefix + "fieldExists";

    /// <summary>节点动态下拉框 id。</summary>
    public const string NodesDropdownId = "glycolinker.sai.nodes";

    /// <summary>本插件原生「调用 Glycoprotein Action」行动 id（wrapper 的目标）。</summary>
    public const string CallTargetActionId = "glycolinker.action.glycoCall";

    /// <summary>本插件原生「分发 Glycoprotein 事件」行动 id（wrapper 的目标）。</summary>
    public const string EmitTargetActionId = "glycolinker.action.glycoEmitter";

    /// <summary>按保存信标生成的行动积木 id 前缀。</summary>
    public const string DynCallPrefix = "glycolinker.sai.dyn.call.";

    /// <summary>
    /// 把 gid/fid 编码为可嵌入积木 id 的片段；<c>.</c> 是 id 内的层级分隔符，必须转义。
    /// </summary>
    public static string Encode(string s) => Uri.EscapeDataString(s).Replace(".", "%2E");
}
