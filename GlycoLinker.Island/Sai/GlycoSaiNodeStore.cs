using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GlycoLinker.Island.Automations;
using Glycoprotein.Glycosylation;
using Microsoft.Extensions.Logging;

namespace GlycoLinker.Island.Sai;

public sealed class GlycoSaiNodeStore : ObservableObject {
    readonly Config _config;
    readonly object _eventGate = new();
    readonly Dictionary<string, BeaconInfo> _recycled = new(StringComparer.Ordinal);
    Dictionary<string, BeaconInfo> _online = new(StringComparer.Ordinal);
    Dictionary<string, BeaconInfo> _saved;
    volatile BeaconInfo[] _published;
    GlycoBridge? _bridge;
    string? _errorMessage;

    public GlycoSaiNodeStore(Config config) {
        _config = config;
        _saved = Detach(config.SavedSaiNodes ?? []);
        _published = Sorted(_saved);
        UnsavedNodes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasUnsavedNodes));
        SavedNodes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSavedNodes));
        RefreshRows();
    }

    public IReadOnlyList<BeaconInfo> SavedSnapshot => _published;
    public ObservableCollection<GlycoSaiNodeRow> UnsavedNodes { get; } = [];
    public ObservableCollection<GlycoSaiNodeRow> SavedNodes { get; } = [];
    public bool HasUnsavedNodes => UnsavedNodes.Count != 0;
    public bool HasSavedNodes => SavedNodes.Count != 0;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public string? ErrorMessage {
        get => _errorMessage;
        private set {
            if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError));
        }
    }
    public event Action? DefinitionsChanged;

    // Capture and enqueue under one gate: an expiration cannot erase a received beacon
    // before the UI has processed it. Initial attachment uses the same FIFO path.
    public void Attach(GlycoBridge? bridge) {
        lock (_eventGate) {
            if (ReferenceEquals(_bridge, bridge) && bridge != null) return;
            if (_bridge != null) _bridge.SnapshotChanged -= OnSnapshotChanged;
            _bridge = bridge;
            if (_bridge != null) _bridge.SnapshotChanged += OnSnapshotChanged;
            QueueSnapshot();
        }
    }

    void OnSnapshotChanged() {
        lock (_eventGate) QueueSnapshot();
    }

    void QueueSnapshot() {
        BeaconInfo[] snapshot = Sorted(Detach(_bridge?.Snapshot ?? []));
        Dispatcher.UIThread.Post(() => Reconcile(snapshot));
    }

    // Production callers run on the UI thread; standalone smoke callers may use one thread.
    public void Reconcile(IReadOnlyList<BeaconInfo> online) {
        _online = Detach(online);
        foreach (string id in _recycled.Keys.ToArray()) {
            if (_online.TryGetValue(id, out BeaconInfo? beacon)) _recycled[id] = beacon;
        }
        Dictionary<string, BeaconInfo> candidate = new(_saved, StringComparer.Ordinal);
        foreach (string id in _saved.Keys) {
            if (_online.TryGetValue(id, out BeaconInfo? beacon)) candidate[id] = beacon;
        }
        TryCommit(candidate);
        RefreshRows();
    }

    public bool SaveNode(string gid) {
        if (_saved.ContainsKey(gid)) {
            ErrorMessage = null;
            return true;
        }
        if (!_online.TryGetValue(gid, out BeaconInfo? beacon) && !_recycled.TryGetValue(gid, out beacon)) {
            RefreshRows();
            return false;
        }
        Dictionary<string, BeaconInfo> candidate = new(_saved, StringComparer.Ordinal) { [gid] = beacon };
        if (!TryCommit(candidate)) return false;
        _recycled.Remove(gid);
        RefreshRows();
        return true;
    }

    public bool UnsaveNode(string gid) {
        if (!_saved.TryGetValue(gid, out BeaconInfo? beacon)) {
            ErrorMessage = null;
            return true;
        }
        Dictionary<string, BeaconInfo> candidate = new(_saved, StringComparer.Ordinal);
        candidate.Remove(gid);
        if (!TryCommit(candidate)) return false;
        if (!_online.ContainsKey(gid)) _recycled[gid] = beacon;
        RefreshRows();
        return true;
    }

    bool TryCommit(Dictionary<string, BeaconInfo> candidate) {
        BeaconInfo[] snapshot = Sorted(candidate);
        if (JsonElement.DeepEquals(JsonSerializer.SerializeToElement(_published), JsonSerializer.SerializeToElement(snapshot))) return true;
        List<BeaconInfo> previous = _config.SavedSaiNodes;
        try {
            _config.SavedSaiNodes = snapshot.ToList();
            _config.Save();
        } catch (Exception e) {
            _config.SavedSaiNodes = previous;
            ErrorMessage = $"保存 SAI 节点失败: {e.Message}";
            ServicesCaptureService.Logger?.LogError(e, "保存 SAI 节点失败");
            return false;
        }
        _saved = candidate;
        _published = snapshot;
        ErrorMessage = null;
        RefreshRows();
        DefinitionsChanged?.Invoke();
        return true;
    }

    void RefreshRows() {
        UpdateRows(SavedNodes, _saved.Values, true);
        Dictionary<string, BeaconInfo> unsaved = new(_recycled, StringComparer.Ordinal);
        foreach ((string id, BeaconInfo beacon) in _online) unsaved[id] = beacon;
        foreach (string id in _saved.Keys) unsaved.Remove(id);
        UpdateRows(UnsavedNodes, unsaved.Values, false);
    }

    void UpdateRows(ObservableCollection<GlycoSaiNodeRow> rows, IEnumerable<BeaconInfo> beacons, bool saved) {
        Dictionary<string, GlycoSaiNodeRow> existing = rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
        int index = 0;
        foreach (BeaconInfo beacon in beacons.OrderBy(b => b.Id, StringComparer.Ordinal)) {
            if (!existing.TryGetValue(beacon.Id, out GlycoSaiNodeRow? row)) {
                row = new GlycoSaiNodeRow(beacon.Id);
                rows.Insert(index, row);
            } else {
                int oldIndex = rows.IndexOf(row);
                if (oldIndex != index) rows.Move(oldIndex, index);
            }
            row.Vendor = beacon.Vendor ?? "";
            row.IsOnline = _online.ContainsKey(beacon.Id);
            row.IsSaved = saved;
            index++;
        }
        while (rows.Count > index) rows.RemoveAt(rows.Count - 1);
    }

    static BeaconInfo[] Sorted(Dictionary<string, BeaconInfo> nodes) =>
        nodes.Values.OrderBy(b => b.Id, StringComparer.Ordinal).ToArray();

    static Dictionary<string, BeaconInfo> Detach(IEnumerable<BeaconInfo> nodes) {
        Dictionary<string, BeaconInfo> result = new(StringComparer.Ordinal);
        foreach (BeaconInfo? node in nodes) {
            if (node == null || string.IsNullOrEmpty(node.Id) || node.Fields == null) continue;
            Field[] fields = node.Fields.Where(f => f != null && f.Id != null)
                .OrderBy(f => f.Id, StringComparer.Ordinal).Select(CloneField).ToArray();
            result[node.Id] = node with { Fields = fields };
        }
        return result;
    }

    static Field CloneField(Field field) => field switch {
        Field.Method method => method with {
            QuerySchema = method.QuerySchema?.Clone(), ReceiptSchema = method.ReceiptSchema?.Clone()
        },
        Field.Event evt => evt with { CallArgSchema = evt.CallArgSchema?.Clone() },
        _ => field with { }
    };
}

public sealed class GlycoSaiNodeRow(string id) : ObservableObject {
    string _vendor = "";
    bool _isOnline;
    bool _isSaved;
    public string Id { get; } = id;
    public string Vendor { get => _vendor; set => SetProperty(ref _vendor, value); }
    public bool IsOnline {
        get => _isOnline;
        set {
            if (!SetProperty(ref _isOnline, value)) return;
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(MoveGlyph));
        }
    }
    public bool IsSaved {
        get => _isSaved;
        set {
            if (!SetProperty(ref _isSaved, value)) return;
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(MoveGlyph));
        }
    }
    public IBrush StatusBrush => IsOnline ? Brushes.Green : Brushes.Red;
    public string StatusText => IsOnline ? "在线" : "离线";
    public string MoveGlyph => IsSaved ? "\uE109" : "\uE131";
}
