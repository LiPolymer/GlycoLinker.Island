using System.Text.Json;
using Glycoprotein.Glycosylation;
using Glycoprotein.HostedService;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GlycoLinker.Island.Automations;

public record BeaconInfo(string Id, IReadOnlyList<Field> Fields);

public class GlycoBridge : IHostedService {
    public static GlycoBridge? Instance;

    readonly GlycoService _gx;
    readonly ILogger<GlycoBridge> _logger;
    readonly object _lock = new();
    readonly Dictionary<string, HashSet<Action>> _triggers = [];
    readonly Dictionary<string, (string? FriendlyName, string? Description)> _metas = [];
    readonly Dictionary<(string Gid, string Fid), HashSet<Action>> _eventSubs = [];
    readonly Dictionary<string, (string? FriendlyName, string? Description)> _eventFieldMetas = [];
    readonly object _snapshotLock = new();
    List<BeaconInfo> _snapshot = [];

    public event Action? SnapshotChanged;

    public GlycoBridge(GlycoService gx, ILogger<GlycoBridge> logger) {
        _gx = gx;
        _logger = logger;
        Instance = this;
        _gx.OnDiscovered += _ => RebuildSnapshot();
        _gx.OnExpired += _ => RebuildSnapshot();
        _gx.OnChanged += _ => RebuildSnapshot();
    }

    public GlycoService Service => _gx;

    public IReadOnlyList<BeaconInfo> Snapshot {
        get {
            lock (_snapshotLock) return _snapshot.ToArray();
        }
    }

    public IReadOnlyList<string> DiscoveredNodeIds => Snapshot.Select(b => b.Id).ToArray();
    
    public void Register(string fid, Action fire, string? friendlyName = null, string? description = null) {
        if (string.IsNullOrWhiteSpace(fid)) {
            _logger.LogWarning("拒绝注册空 fid 的触发器");
            return;
        }
        bool isFirst;
        bool metaChanged;
        lock (_lock) {
            if (!_triggers.TryGetValue(fid, out HashSet<Action>? set)) {
                set = [];
                _triggers[fid] = set;
                isFirst = true;
            } else {
                isFirst = false;
            }
            set.Add(fire);

            (string?, string?) meta = (Normalize(friendlyName), Normalize(description));
            metaChanged = isFirst || !_metas.TryGetValue(fid, out (string?, string?) prev) || prev != meta;
            _metas[fid] = meta;
        }
        if (isFirst) {
            _logger.LogInformation("首次注册触发器字段 [Fid={Fid}], 正在暴露 Glycoprotein Action", fid);
            AddTriggerField(fid, friendlyName, description);
            return;
        }
        if (!metaChanged) {
            _logger.LogInformation("已注册 {fid}", fid);
            return;
        }
        _logger.LogInformation("触发器字段元数据更新 [Fid={Fid}]", fid);
        try {
            AddTriggerField(fid, friendlyName, description);
        } catch (Exception e) {
            _logger.LogWarning(e, "更新字段 [Fid={Fid}] 元数据失败", fid);
        }
    }

    void AddTriggerField(string fid, string? friendlyName, string? description) {
        _gx.AddAction(new Field.Method {
            Id = fid,
            FriendlyName = Normalize(friendlyName),
            Description = Normalize(description)
        }, () => InvokeAll(fid));
        TryRefreshBeacon();
    }

    static string? Normalize(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public void Unregister(string fid, Action fire) {
        bool empty;
        lock (_lock) {
            if (!_triggers.TryGetValue(fid, out HashSet<Action>? set)) return;
            set.Remove(fire);
            empty = set.Count == 0;
            if (empty) {
                _triggers.Remove(fid);
                _metas.Remove(fid);
            }
        }
        if (!empty) return;
        _logger.LogInformation("字段 [Fid={Fid}] 的触发器已全部卸载, 正在注销字段", fid);
        try {
            _gx.RemoveField(fid);
        } catch (Exception e) {
            _logger.LogWarning(e, "注销字段 [Fid={Fid}] 失败", fid);
        }
    }

    void InvokeAll(string fid) {
        _logger.LogInformation("Glycoprotein Call [{fid}] Acquired", fid);
        Action[] fires;
        lock (_lock) {
            fires = _triggers.TryGetValue(fid, out HashSet<Action>? set) ? set.ToArray() : [];
        }
        foreach (Action fire in fires) {
            try {
                _logger.LogInformation("Firing [{fid}]", fid);
                fire();
                _logger.LogInformation("Fired [{fid}]", fid);
            } catch (Exception e) {
                _logger.LogError(e, "触发器回调执行失败 [Fid={Fid}]", fid);
            }
        }
    }

    public void SubscribeEvent(string gid, string fid, Action fire) {
        if (string.IsNullOrWhiteSpace(gid) || string.IsNullOrWhiteSpace(fid)) {
            _logger.LogWarning("拒绝订阅空 Gid/Fid 的事件 [Gid={Gid}, Fid={Fid}]", gid, fid);
            return;
        }
        (string Gid, string Fid) key = (gid.Trim(), fid.Trim());
        bool isFirst;
        lock (_lock) {
            if (!_eventSubs.TryGetValue(key, out HashSet<Action>? set)) {
                set = [];
                _eventSubs[key] = set;
                isFirst = true;
            } else {
                isFirst = false;
            }
            set.Add(fire);
        }
        if (!isFirst) {
            _logger.LogInformation("已订阅事件 [Gid={Gid}, Fid={Fid}]", key.Gid, key.Fid);
            return;
        }
        _logger.LogInformation("首次订阅事件 [Gid={Gid}, Fid={Fid}], 正在注册 Glycoprotein 事件监听", key.Gid, key.Fid);
        _gx.OnEventRaw(key.Gid, key.Fid, args => DispatchEvent(key.Gid, key.Fid, args));
    }

    public void UnsubscribeEvent(string gid, string fid, Action fire) {
        lock (_lock) {
            if (!_eventSubs.TryGetValue((gid.Trim(), fid.Trim()), out HashSet<Action>? set)) return;
            set.Remove(fire);
            if (set.Count == 0) _eventSubs.Remove((gid.Trim(), fid.Trim()));
        }
        _logger.LogInformation("已退订事件 [Gid={Gid}, Fid={Fid}]", gid, fid);
    }

    void DispatchEvent(string gid, string fid, JsonElement? args) {
        Action[] fires;
        lock (_lock) {
            fires = _eventSubs.TryGetValue((gid, fid), out HashSet<Action>? set) ? set.ToArray() : [];
        }
        if (fires.Length == 0) return;
        _logger.LogInformation("Glycoprotein Event [Gid={Gid}, Fid={Fid}] Acquired, Args={Args}", gid, fid, args?.GetRawText());
        foreach (Action fire in fires) {
            try {
                fire();
            } catch (Exception e) {
                _logger.LogError(e, "事件回调执行失败 [Gid={Gid}, Fid={Fid}]", gid, fid);
            }
        }
    }

    public void RegisterEventField(string fid, string? friendlyName = null, string? description = null) {
        if (string.IsNullOrWhiteSpace(fid)) return;
        fid = fid.Trim();
        (string?, string?) meta = (Normalize(friendlyName), Normalize(description));
        bool isNew;
        bool metaChanged;
        lock (_lock) {
            isNew = !_eventFieldMetas.TryGetValue(fid, out (string?, string?) prev);
            metaChanged = isNew || prev != meta;
            _eventFieldMetas[fid] = meta;
        }
        if (!metaChanged) {
            _logger.LogInformation("事件字段已注册 [Fid={Fid}]", fid);
            return;
        }
        if (isNew) _logger.LogInformation("注册事件字段 [Fid={Fid}]", fid);
        else _logger.LogInformation("更新事件字段元数据 [Fid={Fid}]", fid);
        _gx.AddEvent(new Field.Event {
            Id = fid,
            FriendlyName = Normalize(friendlyName),
            Description = Normalize(description)
        });
        TryRefreshBeacon();
    }

    public void UnregisterEventField(string fid) {
        if (string.IsNullOrWhiteSpace(fid)) return;
        fid = fid.Trim();
        lock (_lock) {
            if (!_eventFieldMetas.Remove(fid)) return;
        }
        _logger.LogInformation("注销事件字段 [Fid={Fid}]", fid);
        try {
            _gx.RemoveField(fid);
        } catch (Exception e) {
            _logger.LogWarning(e, "注销事件字段 [Fid={Fid}] 失败", fid);
        }
    }

    public async Task EmitEventAsync(string fid, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(fid)) {
            throw new InvalidOperationException("事件字段 ID (Fid) 不能为空");
        }
        fid = fid.Trim();
        bool known;
        lock (_lock) {
            known = _eventFieldMetas.ContainsKey(fid);
        }
        if (!known) RegisterEventField(fid);
        await _gx.EmitEventAsync(fid, ct);
    }

    void RebuildSnapshot() {
        lock (_snapshotLock) {
            _snapshot = _gx.Presenters.Select(b => new BeaconInfo(b.Id, b.Fields)).ToList();
        }
        SnapshotChanged?.Invoke();
    }

    void TryRefreshBeacon() {
        try {
            _gx.RefreshBeacon();
        } catch (Exception e) {
            _logger.LogWarning(e, "刷新 beacon 失败 (节点可能尚未启动), 将在启动时自动广播");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        return Task.CompletedTask;
    }
}
