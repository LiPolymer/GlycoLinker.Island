using System.Text.Encodings.Web;
using System.Text.Json;
using GlycoLinker.Island.Automations;
using Microsoft.Extensions.Logging;
using SuperAutoIsland.Interface.Metadata;
using GlycoField = Glycoprotein.Glycosylation.Field;

namespace GlycoLinker.Island.Sai;

/// <summary>
/// SAI 数据/规则积木的前缀处理器，以及积木设置与 JSON 之间的转换工具。
/// </summary>
public static class GlycoSaiHandlers {
    static readonly JsonSerializerOptions JsonOptions = new() {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 前缀处理器入口：只处理落在 <see cref="GlycoSaiIds.DataPrefix"/> 与
    /// <see cref="GlycoSaiIds.RulePrefix"/> 下的积木 id，其余返回 Handled=false 交回 SAI 原逻辑。
    /// </summary>
    public static (bool Handled, object? Result) Handle(BlockKind kind, string id, JsonElement settings) {
        try {
            return id switch {
                GlycoSaiIds.NodesDataId => (true, (object)NodesJson()),
                GlycoSaiIds.FieldsDataId => (true, (object)FieldsJson(settings)),
                GlycoSaiIds.NodeExistsRuleId => (true, (object)NodeExists(settings)),
                GlycoSaiIds.FieldExistsRuleId => (true, (object)FieldExists(settings)),
                _ => (false, null)
            };
        } catch (Exception e) {
            ServicesCaptureService.GLogger?.LogError(e, "SAI 积木 {Id} 处理失败", id);
            return (true, kind == BlockKind.Rule ? (object)false : "???");
        }
    }

    /// <summary>读取积木设置中的字符串字段；缺失、类型不支持或为空白时返回 null。</summary>
    public static string? ReadString(JsonElement settings, string name) {
        if (settings.ValueKind != JsonValueKind.Object || !settings.TryGetProperty(name, out JsonElement value)) {
            return null;
        }
        string? text = value.ValueKind switch {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
        text = text?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>读取积木设置（object 形式，SAI 传入的通常是 JsonElement）中的字符串字段。</summary>
    public static string? ReadString(object? settings, string name) =>
        settings is JsonElement element ? ReadString(element, name) : null;

    /// <summary>把行动项设置还原为 JsonElement（ClassIsland 行动管线的设置形态）。</summary>
    public static object ToSettingsElement(object? settings) {
        string json = settings is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(settings ?? new { });
        return JsonSerializer.Deserialize<object>(json)!;
    }

    /// <summary>为动态积木构造原生 glycoCall 行动的设置；键名与 GlycoCallConfig 属性一致。</summary>
    public static object BuildCallSettings(string gid, string fid, string? payloadJson) =>
        JsonSerializer.Deserialize<object>(JsonSerializer.Serialize(new {
            TargetGid = gid,
            Fid = fid,
            PayloadJson = payloadJson ?? ""
        }))!;

    /// <summary>当前发现的节点列表（JSON 数组字符串）。</summary>
    public static string NodesJson() {
        GlycoBridge? bridge = GlycoBridge.Instance;
        if (bridge == null) return "[]";
        IReadOnlyList<BeaconInfo> snapshot = bridge.Snapshot;
        if (snapshot.Count == 0) return "[]";
        return JsonSerializer.Serialize(snapshot.Select(b => new {
            id = b.Id,
            vendor = b.Vendor,
            fields = b.Fields.Count
        }), JsonOptions);
    }

    /// <summary>指定节点的字段列表（JSON 数组字符串；节点不存在时返回带 error 的对象）。</summary>
    public static string FieldsJson(JsonElement settings) {
        string? gid = ReadString(settings, "Gid");
        if (gid == null) return "[]";
        GlycoBridge? bridge = GlycoBridge.Instance;
        if (bridge == null) return "[]";
        BeaconInfo? node = bridge.Snapshot.FirstOrDefault(b => b.Id == gid);
        if (node == null) return JsonSerializer.Serialize(new { error = $"节点 {gid} 不存在" }, JsonOptions);
        return JsonSerializer.Serialize(node.Fields.Select(f => new {
            id = f.Id,
            name = f.FriendlyName,
            description = f.Description,
            kind = f is GlycoField.Method ? "method" : "event",
            hasQuerySchema = f is GlycoField.Method m && m.QuerySchema is { } schema && schema.ValueKind != JsonValueKind.Null
        }), JsonOptions);
    }

    /// <summary>节点是否存在。</summary>
    public static bool NodeExists(JsonElement settings) {
        string? gid = ReadString(settings, "Gid");
        if (gid == null) return false;
        return GlycoBridge.Instance is { } bridge && bridge.Snapshot.Any(b => b.Id == gid);
    }

    /// <summary>节点上的字段是否存在。</summary>
    public static bool FieldExists(JsonElement settings) {
        string? gid = ReadString(settings, "Gid");
        string? fid = ReadString(settings, "Fid");
        if (gid == null || fid == null) return false;
        if (GlycoBridge.Instance is not { } bridge) return false;
        BeaconInfo? node = bridge.Snapshot.FirstOrDefault(b => b.Id == gid);
        return node != null && node.Fields.Any(f => f.Id == fid);
    }

    /// <summary>节点动态下拉框的选项（显示内容, 后台值）。</summary>
    public static Task<List<(string, string)>> GetNodeOptionsAsync() {
        GlycoBridge? bridge = GlycoBridge.Instance;
        if (bridge == null) return Task.FromResult(new List<(string, string)>());
        return Task.FromResult(bridge.Snapshot
            .Select(b => (b.Vendor is { Length: > 0 } v ? $"{b.Id} · {v}" : b.Id, b.Id))
            .ToList());
    }
}
