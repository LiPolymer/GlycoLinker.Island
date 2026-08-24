using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace GlycoLinker.Island.Automations.Triggers;

public partial class GlycoTriggerSettings : TriggerSettingsControlBase<GlycoTriggerConfig> {
    public GlycoTriggerSettings() {
        InitializeComponent();
        DataContext = this;
    }

    public string LocalGid => Config.Instance?.Gid ?? "";
}

// ReSharper disable once ClassNeverInstantiated.Global
public partial class GlycoTriggerConfig : ObservableRecipient {
    [ObservableProperty]
    string _fid = "glycolinker-fire";
}

[TriggerInfo("glycolinker.trigger.glycoInvoke", "Glycoprotein 调用", "\uEDC7")]
// ReSharper disable once ClassNeverInstantiated.Global
public class GlycoTrigger : TriggerBase<GlycoTriggerConfig> {
    public override void Loaded() {
        if (GlycoBridge.Instance == null) {
            ServicesCaptureService.Logger?.LogWarning("GlycoBridge 未初始化, 触发器 [Fid={Fid}] 注册失败", Settings.Fid);
            return;
        }
        GlycoBridge.Instance.Register(Settings.Fid, Trigger);
    }

    public override void UnLoaded() {
        GlycoBridge.Instance?.Unregister(Settings.Fid, Trigger);
    }
}
