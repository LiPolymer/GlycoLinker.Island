using System.ComponentModel;
using Avalonia.Threading;
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

    [ObservableProperty]
    string _friendlyName = "";

    [ObservableProperty]
    string _description = "";
}

[TriggerInfo("glycolinker.trigger.glycoInvoke", "Glycoprotein 调用", "\uEDC7")]
// ReSharper disable once ClassNeverInstantiated.Global
public class GlycoTrigger : TriggerBase<GlycoTriggerConfig> {
    static readonly TimeSpan UpdateDebounce = TimeSpan.FromMilliseconds(600);

    string? _registeredFid;
    DispatcherTimer? _updateTimer;

    public override void Loaded() {
        if (GlycoBridge.Instance == null) {
            ServicesCaptureService.Logger?.LogWarning("GlycoBridge 未初始化, 触发器 [Fid={Fid}] 注册失败", Settings.Fid);
            return;
        }
        _registeredFid = Settings.Fid;
        GlycoBridge.Instance.Register(Settings.Fid, Trigger, Settings.FriendlyName, Settings.Description);
        Settings.PropertyChanged += OnSettingsChanged;
    }

    public override void UnLoaded() {
        Settings.PropertyChanged -= OnSettingsChanged;
        _updateTimer?.Stop();
        _updateTimer = null;
        if (_registeredFid != null) GlycoBridge.Instance?.Unregister(_registeredFid, Trigger);
        _registeredFid = null;
    }

    void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) {
        if (_registeredFid == null) return;
        if (e.PropertyName is not (nameof(GlycoTriggerConfig.Fid)
            or nameof(GlycoTriggerConfig.FriendlyName)
            or nameof(GlycoTriggerConfig.Description))) return;
        _updateTimer ??= new DispatcherTimer { Interval = UpdateDebounce };
        _updateTimer.Tick += OnUpdateTimerTick;
        _updateTimer.Stop();
        _updateTimer.Start();
    }

    void OnUpdateTimerTick(object? sender, EventArgs e) {
        _updateTimer?.Stop();
        ApplyRegistration();
    }

    void ApplyRegistration() {
        GlycoBridge? bridge = GlycoBridge.Instance;
        if (bridge == null || _registeredFid == null) return;
        string fid = Settings.Fid.Trim();
        if (string.IsNullOrWhiteSpace(fid)) return;
        if (fid != _registeredFid) {
            bridge.Unregister(_registeredFid, Trigger);
            _registeredFid = fid;
        }
        bridge.Register(fid, Trigger, Settings.FriendlyName, Settings.Description);
    }
}
