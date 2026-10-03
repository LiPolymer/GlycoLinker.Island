using System.Text.Json;
using ClassIsland.Core.Icons;
using ClassIsland.Shared.Models.Automation;
using GlycoLinker.Island.Automations;
using Microsoft.Extensions.Logging;
using SuperAutoIsland.Interface.Metadata;
using SuperAutoIsland.Interface.Services;
using SuperAutoIsland.Interface.Services.Automations;
using GlycoField = Glycoprotein.Glycosylation.Field;

namespace GlycoLinker.Island.Sai;

/// <summary>
/// GlycoLinker 分类的积木提供方。Build 只读取已发布的保存定义，不依赖在线状态。
/// </summary>
public class GlycoSaiCategoryProvider(GlycoSaiNodeStore nodes) : ICategoryProvider {
    public CategoryMetadata Metadata => new(GlycoSaiIds.CategoryName) {
        Icon = ("Glycoprotein", FluentIcons.VehicleCarRegular)
    };

    public void Build(BlocksRegister it) {
        it.AddLabel("Glycoprotein")
            .AddBlock<SaiGlycoCallBlock>()
            .AddBlock<SaiGlycoEmitBlock>()
            .AddBlock(new BlockMetadata(GlycoSaiIds.NodesDataId) {
                Kind = BlockKind.Data,
                Name = "Glycoprotein 节点列表",
                Icon = ("节点列表", FluentIcons.TextAddRegular),
                DataOutput = "String"
            })
            .AddBlock(new BlockMetadata(GlycoSaiIds.FieldsDataId) {
                Kind = BlockKind.Data,
                Name = "Glycoprotein 节点字段",
                Icon = ("节点字段", FluentIcons.TextEditStyleRegular),
                DataOutput = "String",
                Fields = { ["Gid"] = BasicFields.Text("节点 ID") }
            })
            .AddBlock(new BlockMetadata(GlycoSaiIds.NodeExistsRuleId) {
                Kind = BlockKind.Rule,
                Name = "Glycoprotein 节点存在",
                Icon = ("节点", FluentIcons.TagMultipleRegular),
                Fields = { ["Gid"] = BasicFields.Text("节点 ID") }
            })
            .AddBlock(new BlockMetadata(GlycoSaiIds.FieldExistsRuleId) {
                Kind = BlockKind.Rule,
                Name = "Glycoprotein 字段存在",
                Icon = ("字段", FluentIcons.TagMultipleRegular),
                Fields = { ["Gid"] = BasicFields.Text("节点 ID"), ["Fid"] = BasicFields.Text("字段 ID") }
            });

        // 保存信标提供稳定定义；下线不移除工作区使用的积木类型。
        List<SaiGlycoDynCallBlock> dynamicBlocks = BuildDynamicBlocks();
        if (dynamicBlocks.Count == 0) return;
        it.AddLabel("网络字段 (动态)");
        foreach (SaiGlycoDynCallBlock block in dynamicBlocks) {
            FieldsRegister fields = new();
            block.GetFields(fields);
            it.Blocks[block.Id] = block;
            it.AddBlock(new BlockMetadata(block.Id) {
                Kind = BlockKind.Action,
                Name = block.Name,
                Icon = block.Icon,
                Tooltip = block.Tooltip,
                Fields = fields.Fields
            });
        }
    }

    /// <summary>按保存定义生成动态调用积木；gid/fid 按序数排序，不截断。</summary>
    List<SaiGlycoDynCallBlock> BuildDynamicBlocks() {
        List<SaiGlycoDynCallBlock> result = [];
        foreach (BeaconInfo beacon in nodes.SavedSnapshot.OrderBy(b => b.Id, StringComparer.Ordinal)) {
            string vendor = beacon.Vendor is { Length: > 0 } v ? v : beacon.Id;
            foreach (GlycoField.Method field in beacon.Fields.OfType<GlycoField.Method>()
                         .OrderBy(f => f.Id, StringComparer.Ordinal)) {
                string fieldName = field.FriendlyName is { Length: > 0 } fn ? fn : field.Id;
                result.Add(new SaiGlycoDynCallBlock(beacon.Id, field, $"调用 {vendor} / {fieldName}"));
            }
        }
        return result;
    }
}

/// <summary>
/// 调用 Glycoprotein Action 的行动积木。字段 id 与 <c>GlycoCallConfig</c> 属性同名，
/// 因此 wrapper 把设置原样转交给原生 glycoCall 行动即可（复用其超时与错误包装）。
/// </summary>
public class SaiGlycoCallBlock : ActionBlockBase {
    public override string Id => GlycoSaiIds.CallActionId;
    public override string Name => "调用 Glycoprotein Action";
    public override (string, string) Icon => ("Glycoprotein 调用", FluentIcons.AirplaneTakeOffRegular);

    public override void GetFields(FieldsRegister it) {
        it.AddField("TargetGid", BasicFields.DynamicDropdown("目标节点", GlycoSaiIds.NodesDropdownId))
            .AddField("Fid", BasicFields.Text("字段 ID"))
            .AddField("PayloadJson", BasicFields.Text("参数 JSON（可空）"));
    }

    public override ActionItem Wrapper(ActionItem actionItem) => new() {
        Id = GlycoSaiIds.CallTargetActionId,
        Settings = GlycoSaiHandlers.ToSettingsElement(actionItem.Settings)
    };
}

/// <summary>
/// 分发 Glycoprotein 事件的行动积木。运行前先注册事件字段以保住友好名称与描述，
/// 再把设置转交给原生 glycoEmitter 行动。
/// </summary>
public class SaiGlycoEmitBlock : ActionBlockBase {
    public override string Id => GlycoSaiIds.EmitActionId;
    public override string Name => "分发 Glycoprotein 事件";
    public override (string, string) Icon => ("Glycoprotein 事件", FluentIcons.AirplaneLandingRegular);

    public override void GetFields(FieldsRegister it) {
        it.AddField("Fid", BasicFields.Text("事件字段 ID"))
            .AddField("FriendlyName", BasicFields.Text("友好名称"))
            .AddField("Description", BasicFields.Text("描述"));
    }

    public override ActionItem Wrapper(ActionItem actionItem) {
        if (GlycoSaiHandlers.ReadString(actionItem.Settings, "Fid") is { } fid &&
            GlycoBridge.Instance is { } bridge) {
            try {
                bridge.RegisterEventField(fid,
                                          GlycoSaiHandlers.ReadString(actionItem.Settings, "FriendlyName"),
                                          GlycoSaiHandlers.ReadString(actionItem.Settings, "Description"));
            } catch (Exception e) {
                ServicesCaptureService.GLogger?.LogWarning(e, "注册事件字段 [{Fid}] 失败", fid);
            }
        }
        return new ActionItem {
            Id = GlycoSaiIds.EmitTargetActionId,
            Settings = GlycoSaiHandlers.ToSettingsElement(actionItem.Settings)
        };
    }
}

/// <summary>
/// 针对单个保存节点字段的行动积木。gid/fid 固定在积木 id 上。
/// </summary>
public sealed class SaiGlycoDynCallBlock : ActionBlockBase {
    readonly string _gid;
    readonly string _fid;
    readonly string _id;
    readonly string _name;
    readonly string _tooltip;
    readonly GlycoSaiSchema _schema;

    public SaiGlycoDynCallBlock(string gid, GlycoField.Method method, string name) {
        _gid = gid;
        _fid = method.Id;
        _id = GlycoSaiIds.DynCallPrefix + GlycoSaiIds.Encode(gid) + "." + GlycoSaiIds.Encode(_fid);
        _name = name;
        _schema = GlycoSaiSchema.Create(method.QuerySchema);
        _tooltip = string.Join("\n", new[] { method.Description ?? "", _schema.Tooltip }.Where(s => s.Length != 0));
    }

    public override string Id => _id;
    public override string Name => _name;
    public override (string, string) Icon => ("Glycoprotein 调用", FluentIcons.AirplaneTakeOffRegular);

    public override string Tooltip => _tooltip;
    public override void GetFields(FieldsRegister it) => _schema.GetFields(it);

    public override ActionItem Wrapper(ActionItem actionItem) => new() {
        Id = GlycoSaiIds.CallTargetActionId,
        Settings = GlycoSaiHandlers.BuildCallSettings(_gid, _fid, _schema.BuildPayload(
            actionItem.Settings is JsonElement element ? element : JsonSerializer.SerializeToElement(actionItem.Settings)))
    };
}
