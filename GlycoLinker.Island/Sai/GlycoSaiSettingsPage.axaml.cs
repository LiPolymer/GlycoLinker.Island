using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;

namespace GlycoLinker.Island.Sai;

public partial class GlycoSaiSettingsPage : SettingsPageBase {
    readonly GlycoSaiNodeStore _nodes;
    bool _isCompact;

    // Avalonia's compiled resource loader requires this constructor; keyed DI uses the injected one.
    public GlycoSaiSettingsPage() : this(IAppHost.GetService<GlycoSaiNodeStore>()) { }

    public GlycoSaiSettingsPage(GlycoSaiNodeStore nodes) {
        _nodes = nodes;
        InitializeComponent();
        DataContext = nodes;
    }

    void Page_OnSizeChanged(object? sender, SizeChangedEventArgs e) {
        bool compact = e.NewSize.Width < 720;
        if (compact == _isCompact) return;
        _isCompact = compact;
        NodeColumns.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "*,*");
        NodeColumns.RowDefinitions = new RowDefinitions(compact ? "Auto,Auto" : "Auto");
        Grid.SetColumn(SavedPanel, compact ? 0 : 1);
        Grid.SetRow(SavedPanel, compact ? 1 : 0);
    }

    void SaveNode_OnClick(object? sender, RoutedEventArgs e) {
        if (sender is Button { Tag: string id }) _nodes.SaveNode(id);
    }

    void UnsaveNode_OnClick(object? sender, RoutedEventArgs e) {
        if (sender is Button { Tag: string id }) _nodes.UnsaveNode(id);
    }
}
