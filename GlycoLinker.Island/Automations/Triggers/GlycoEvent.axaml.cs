using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using CommunityToolkit.Mvvm.ComponentModel;
using GlycoLinker.Island.Automations.Actions;
using Glycoprotein.Glycosylation;
using Microsoft.Extensions.Logging;

namespace GlycoLinker.Island.Automations.Triggers;

public partial class GlycoEventSettings : TriggerSettingsControlBase<GlycoEventConfig> {
    public ObservableCollection<NodeOption> NodeIds { get; } = [];
    public ObservableCollection<FieldOption> FieldIds { get; } = [];

    bool _suppressAutoOpen;
    string? _lastCommittedGid;

    public GlycoEventSettings() {
        InitializeComponent();
        DataContext = this;
        FidBox.ItemFilter = FilterFieldOption;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnAttachedToVisualTree(e);
        if (GlycoBridge.Instance != null) GlycoBridge.Instance.SnapshotChanged += OnSnapshotChanged;
        _lastCommittedGid = Settings.SourceGid.Trim();
        RefreshSnapshot();
        RefreshFields();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnDetachedFromVisualTree(e);
        if (GlycoBridge.Instance != null) GlycoBridge.Instance.SnapshotChanged -= OnSnapshotChanged;
    }

    void OnSnapshotChanged() {
        Dispatcher.UIThread.Post(() => {
            RefreshSnapshot();
            RefreshFields();
        });
    }

    void GidBox_OnGotFocus(object? sender, FocusChangedEventArgs e) => TryAutoOpenDropDown(GidBox, NodeIds.Count);

    void GidBox_OnLostFocus(object? sender, RoutedEventArgs e) => CommitGid();

    void GidBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) {
        if (e.AddedItems.Count == 0) return;
        _suppressAutoOpen = true;
        Dispatcher.UIThread.Post(() => _suppressAutoOpen = false);
        Dispatcher.UIThread.Post(CommitGid);
    }

    void CommitGid() {
        string gid = Settings.SourceGid.Trim();
        if (gid == _lastCommittedGid) return;
        _lastCommittedGid = gid;
        RefreshFields();
    }

    void FidBox_OnGotFocus(object? sender, FocusChangedEventArgs e) => TryAutoOpenDropDown(FidBox, FieldIds.Count);

    void TryAutoOpenDropDown(AutoCompleteBox box, int itemCount) {
        if (_suppressAutoOpen || box.IsDropDownOpen || itemCount == 0) return;
        box.IsDropDownOpen = true;
    }

    void RefreshSnapshot() {
        NodeIds.Clear();
        foreach (BeaconInfo beacon in GlycoBridge.Instance?.Snapshot ?? []) {
            NodeIds.Add(new NodeOption(beacon.Id, beacon.Fields.Count));
        }
    }

    void RefreshFields() {
        FieldIds.Clear();
        GlycoBridge? bridge = GlycoBridge.Instance;
        if (bridge == null) return;
        string gid = Settings.SourceGid.Trim();
        if (gid.Length == 0) return;
        BeaconInfo? beacon = bridge.Snapshot.FirstOrDefault(b => b.Id == gid);
        if (beacon == null) return;
        foreach (Field field in beacon.Fields) {
            if (field is not Field.Event) continue;
            FieldIds.Add(new FieldOption(field.Id, field.FriendlyName, field.Description));
        }
    }

    static bool FilterFieldOption(string? search, object? item) {
        if (item is not FieldOption option) return false;
        if (string.IsNullOrEmpty(search)) return true;
        return Match(option.Id, search)
            || (option.FriendlyName is { Length: > 0 } f && Match(f, search))
            || (option.Description is { Length: > 0 } d && Match(d, search));
    }

    static bool Match(string source, string search) =>
        source.Contains(search, StringComparison.OrdinalIgnoreCase);
}

// ReSharper disable once ClassNeverInstantiated.Global
public partial class GlycoEventConfig : ObservableRecipient {
    [ObservableProperty]
    string _sourceGid = "";

    [ObservableProperty]
    string _fid = "";
}

[TriggerInfo("glycolinker.trigger.glycoEvent", "Glycoprotein 事件", "\uEDC6")]
// ReSharper disable once ClassNeverInstantiated.Global
public class GlycoEventTrigger : TriggerBase<GlycoEventConfig> {
    static readonly TimeSpan UpdateDebounce = TimeSpan.FromMilliseconds(600);

    (string Gid, string Fid)? _subscribed;
    DispatcherTimer? _updateTimer;

    public override void Loaded() {
        if (GlycoBridge.Instance == null) {
            ServicesCaptureService.Logger?.LogWarning("GlycoBridge 未初始化, 事件订阅 [Gid={Gid}, Fid={Fid}] 失败", Settings.SourceGid, Settings.Fid);
            return;
        }
        Settings.PropertyChanged += OnSettingsChanged;
        SubscribeCurrent();
    }

    public override void UnLoaded() {
        Settings.PropertyChanged -= OnSettingsChanged;
        _updateTimer?.Stop();
        _updateTimer = null;
        UnsubscribeCurrent();
    }

    void SubscribeCurrent() {
        string gid = Settings.SourceGid.Trim();
        string fid = Settings.Fid.Trim();
        if (gid.Length == 0 || fid.Length == 0) return;
        GlycoBridge.Instance!.SubscribeEvent(gid, fid, Trigger);
        _subscribed = (gid, fid);
    }

    void UnsubscribeCurrent() {
        if (_subscribed is not { } key) return;
        GlycoBridge.Instance?.UnsubscribeEvent(key.Gid, key.Fid, Trigger);
        _subscribed = null;
    }

    void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName is not (nameof(GlycoEventConfig.SourceGid) or nameof(GlycoEventConfig.Fid))) return;
        _updateTimer ??= new DispatcherTimer { Interval = UpdateDebounce };
        _updateTimer.Tick += OnUpdateTimerTick;
        _updateTimer.Stop();
        _updateTimer.Start();
    }

    void OnUpdateTimerTick(object? sender, EventArgs e) {
        _updateTimer?.Stop();
        ApplySubscription();
    }

    void ApplySubscription() {
        if (GlycoBridge.Instance == null) return;
        (string, string)? next = null;
        string gid = Settings.SourceGid.Trim();
        string fid = Settings.Fid.Trim();
        if (gid.Length > 0 && fid.Length > 0) next = (gid, fid);
        if (next == _subscribed) return;
        UnsubscribeCurrent();
        if (next is { } key) {
            GlycoBridge.Instance.SubscribeEvent(key.Item1, key.Item2, Trigger);
            _subscribed = key;
        }
    }
}
