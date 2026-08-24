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
    readonly object _snapshotLock = new();
    List<BeaconInfo> _snapshot = [];

    public event Action? SnapshotChanged;

    public GlycoBridge(GlycoService gx, ILogger<GlycoBridge> logger) {
        _gx = gx;
        _logger = logger;
        Instance = this;
        _gx.OnDiscovered += _ => RebuildSnapshot();
        _gx.OnExpired += _ => RebuildSnapshot();
    }

    public GlycoService Service => _gx;

    public IReadOnlyList<BeaconInfo> Snapshot {
        get {
            lock (_snapshotLock) return _snapshot.ToArray();
        }
    }

    public IReadOnlyList<string> DiscoveredNodeIds => Snapshot.Select(b => b.Id).ToArray();
    
    public void Register(string fid, Action fire) {
        if (string.IsNullOrWhiteSpace(fid)) {
            _logger.LogWarning("拒绝注册空 fid 的触发器");
            return;
        }
        bool isFirst;
        lock (_lock) {
            if (!_triggers.TryGetValue(fid, out HashSet<Action>? set)) {
                set = [];
                _triggers[fid] = set;
                isFirst = true;
            } else {
                isFirst = false;
            }
            set.Add(fire);
            _logger.LogInformation("已注册 {fid}", fid);
        }
        if (!isFirst) return;
        _logger.LogInformation("首次注册触发器字段 [Fid={Fid}], 正在暴露 Glycoprotein Action", fid);
        _gx.AddAction(fid, () => InvokeAll(fid));
        TryRefreshBeacon();
    }

    public void Unregister(string fid, Action fire) {
        bool empty;
        lock (_lock) {
            if (!_triggers.TryGetValue(fid, out HashSet<Action>? set)) return;
            set.Remove(fire);
            empty = set.Count == 0;
            if (empty) _triggers.Remove(fid);
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
