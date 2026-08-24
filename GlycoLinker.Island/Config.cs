using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClassIsland.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GlycoLinker.Island;

public partial class Config: ObservableObject {
    public static Config? Instance;
    public static string? SaveDist;
    static JsonSerializerOptions? _jso;
    public Config() {
        PropertyChanged += Save;
    }
    public static Config Load() {
        if (File.Exists(SaveDist)) return JsonSerializer.Deserialize<Config>(File.ReadAllText(SaveDist))!;
        Config nCfg = new Config {
            Gid = $"classIsland-{Guid.NewGuid().ToString().Split('-')[1]}"
        };
        nCfg.Save();
        return nCfg;
    }
    void Save(object? sender,PropertyChangedEventArgs e) {
        Save();
    }
    public void Save() {
        _ = SaveDist ?? throw new InvalidOperationException("SaveDist is not set.");
        File.WriteAllText(SaveDist,JsonSerializer.Serialize(this,_jso ??= new JsonSerializerOptions {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));
    }

    [ObservableProperty]
    string _gid = "glycoLink-undefined";
}