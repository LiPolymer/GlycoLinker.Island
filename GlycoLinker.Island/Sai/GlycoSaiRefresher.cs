using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SuperAutoIsland.Interface.Services;

namespace GlycoLinker.Island.Sai;

/// <summary>
/// 监听保存定义变化并通知 SAI 重建积木列表；在线状态变化不重建。
/// 注意：SAI 前端只在页面加载时拉取一次积木列表，刷新后的变化需重新打开自动化页面才可见。
/// </summary>
public static class GlycoSaiRefresher {
    static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(600);

    static ISaiServer? _sai;
    static DispatcherTimer? _timer;
    static GlycoSaiNodeStore? _nodes;

    public static void Attach(ISaiServer sai, GlycoSaiNodeStore nodes) {
        _sai = sai;
        if (ReferenceEquals(_nodes, nodes)) return;
        if (_nodes != null) _nodes.DefinitionsChanged -= OnDefinitionsChanged;
        _nodes = nodes;
        _nodes.DefinitionsChanged += OnDefinitionsChanged;
    }

    static void OnDefinitionsChanged() => Dispatcher.UIThread.Post(() => {
        _timer ??= CreateTimer();
        _timer.Stop();
        _timer.Start();
    });

    static DispatcherTimer CreateTimer() {
        DispatcherTimer timer = new() { Interval = Debounce };
        timer.Tick += (_, _) => {
            timer.Stop();
            try {
                _sai?.NotifyCategoryUpdated();
                ServicesCaptureService.Logger?.LogInformation("保存定义变化, 已通知 SuperAutoIsland 重建积木 (保存节点数 {Count})",
                                                             _nodes?.SavedSnapshot.Count ?? 0);
            } catch (Exception e) {
                ServicesCaptureService.Logger?.LogError(e, "通知 SuperAutoIsland 重建积木失败");
            }
        };
        return timer;
    }
}
