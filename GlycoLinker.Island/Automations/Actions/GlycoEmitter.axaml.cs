using System.ComponentModel;
using Avalonia;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace GlycoLinker.Island.Automations.Actions;

public partial class GlycoEmitterSettings : ActionSettingsControlBase<GlycoEmitterConfig> {
    static readonly TimeSpan UpdateDebounce = TimeSpan.FromMilliseconds(600);

    string? _registeredFid;
    DispatcherTimer? _updateTimer;

    public GlycoEmitterSettings() {
        InitializeComponent();
        DataContext = this;
    }

    public string LocalGid => Config.Instance?.Gid ?? "";

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnAttachedToVisualTree(e);
        Settings.PropertyChanged += OnSettingsChanged;
        _registeredFid = Settings.Fid.Trim();
        if (_registeredFid.Length > 0) {
            GlycoBridge.Instance?.RegisterEventField(_registeredFid, Settings.FriendlyName, Settings.Description);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnDetachedFromVisualTree(e);
        Settings.PropertyChanged -= OnSettingsChanged;
        _updateTimer?.Stop();
        _updateTimer = null;
    }

    void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName is not (nameof(GlycoEmitterConfig.Fid)
            or nameof(GlycoEmitterConfig.FriendlyName)
            or nameof(GlycoEmitterConfig.Description))) return;
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
        if (bridge == null) return;
        string fid = Settings.Fid.Trim();
        if (fid.Length == 0) {
            if (_registeredFid != null) bridge.UnregisterEventField(_registeredFid);
            _registeredFid = null;
            return;
        }
        if (fid == _registeredFid) {
            bridge.RegisterEventField(fid, Settings.FriendlyName, Settings.Description);
            return;
        }
        if (_registeredFid != null) bridge.UnregisterEventField(_registeredFid);
        _registeredFid = fid;
        bridge.RegisterEventField(fid, Settings.FriendlyName, Settings.Description);
    }
}

// ReSharper disable once ClassNeverInstantiated.Global
public partial class GlycoEmitterConfig : ObservableRecipient {
    [ObservableProperty]
    string _fid = "";

    [ObservableProperty]
    string _friendlyName = "";

    [ObservableProperty]
    string _description = "";
}

[ActionInfo("glycolinker.action.glycoEmitter", "分发 Glycoprotein 事件", "\uEDC8")]
public class GlycoEmitterAction : ActionBase<GlycoEmitterConfig> {
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    protected override async Task OnInvoke() {
        await base.OnInvoke();
        GlycoBridge bridge = GlycoBridge.Instance ?? throw new InvalidOperationException("GlycoBridge 未初始化");

        string fid = Settings.Fid.Trim();
        if (fid.Length == 0) {
            throw new InvalidOperationException("事件字段 ID (Fid) 不能为空");
        }

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(InterruptCancellationToken);
        cts.CancelAfter(Timeout);
        try {
            ServicesCaptureService.Logger?.LogInformation("分发 Glycoprotein 事件: [{Fid}]", fid);
            await bridge.EmitEventAsync(fid, cts.Token);
        } catch (InvalidOperationException) {
            throw;
        } catch (Exception e) {
            ServicesCaptureService.Logger?.LogError(e, "分发 Glycoprotein 事件失败: [{Fid}]", fid);
            throw new InvalidOperationException($"分发 Glycoprotein 事件失败: {e.Message}", e);
        }
    }
}
