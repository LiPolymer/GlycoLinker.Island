using Avalonia.Interactivity;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;

namespace GlycoLinker.Island;

[HidePageTitle]
[SettingsPageInfo("glycolinker.master","GlycoLinker","\uEA37","\uEA36")]
public partial class SettingsPage : SettingsPageBase {
    string _initialGid;

    public SettingsPage() {
        InitializeComponent();
        DataContext = this;
        _initialGid = Config.Gid;
    }

    public Config Config => Config.Instance!;

    public string SocketDirectory => Path.Combine(Path.GetTempPath(), "glycoprotein");

    void CheckGidChanged() {
        if (Config.Gid == _initialGid) return;
        _initialGid = Config.Gid;
        RequestRestart();
    }

    void GidTextBox_OnLostFocus(object? sender, RoutedEventArgs e) {
        CheckGidChanged();
    }

    void ButtonRegenerateGid_OnClick(object? sender, RoutedEventArgs e) {
        Config.Gid = $"classIsland-{Guid.NewGuid().ToString().Split('-')[1]}";
        CheckGidChanged();
    }
}
