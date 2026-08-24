using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using CommunityToolkit.Mvvm.ComponentModel;
using Glycoprotein.Glycosylation;
using Microsoft.Extensions.Logging;
using CIField = ClassIsland.Core.Controls.Field;

namespace GlycoLinker.Island.Automations.Actions;

public partial class GlycoCallSettings : ActionSettingsControlBase<GlycoCallConfig> {
    public ObservableCollection<string> NodeIds { get; } = [];
    public ObservableCollection<string> FieldIds { get; } = [];

    readonly List<ParamField> _paramFields = [];
    bool _suppressPayloadSync;

    public GlycoCallSettings() {
        InitializeComponent();
        DataContext = this;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnAttachedToVisualTree(e);
        if (GlycoBridge.Instance != null) GlycoBridge.Instance.SnapshotChanged += OnSnapshotChanged;
        Settings.PropertyChanged += OnConfigPropertyChanged;
        RefreshSnapshot();
        RefreshFields();
        RebuildParamForm();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnDetachedFromVisualTree(e);
        if (GlycoBridge.Instance != null) GlycoBridge.Instance.SnapshotChanged -= OnSnapshotChanged;
        Settings.PropertyChanged -= OnConfigPropertyChanged;
    }

    void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(GlycoCallConfig.TargetGid)) RefreshFields();
        if (e.PropertyName is nameof(GlycoCallConfig.TargetGid) or nameof(GlycoCallConfig.Fid)) RebuildParamForm();
    }

    void OnSnapshotChanged() {
        Dispatcher.UIThread.Post(() => {
            RefreshSnapshot();
            RefreshFields();
            RebuildParamForm();
        });
    }

    void RefreshSnapshot() {
        NodeIds.Clear();
        foreach (string id in GlycoBridge.Instance?.DiscoveredNodeIds ?? []) NodeIds.Add(id);
    }

    void RefreshFields() {
        FieldIds.Clear();
        GlycoBridge? bridge = GlycoBridge.Instance;
        if (bridge == null) return;
        string gid = Settings.TargetGid.Trim();
        if (gid.Length == 0) return;
        BeaconInfo? beacon = bridge.Snapshot.FirstOrDefault(b => b.Id == gid);
        if (beacon == null) return;
        foreach (Field field in beacon.Fields) FieldIds.Add(field.Id);
    }

    void ButtonRefresh_OnClick(object? sender, RoutedEventArgs e) {
        RefreshSnapshot();
        RefreshFields();
        RebuildParamForm();
    }

    Field? ResolveField() {
        GlycoBridge? bridge = GlycoBridge.Instance;
        if (bridge == null) return null;
        string gid = Settings.TargetGid.Trim();
        string fid = Settings.Fid.Trim();
        if (gid.Length == 0 || fid.Length == 0) return null;
        BeaconInfo? beacon = bridge.Snapshot.FirstOrDefault(b => b.Id == gid);
        return beacon?.Fields.FirstOrDefault(f => f.Id == fid);
    }

    void RebuildParamForm() {
        ParamForm.Children.Clear();
        _paramFields.Clear();
        ParamNote.IsVisible = false;
        JsonParamBox.IsVisible = true;

        Field? field = ResolveField();
        if (field is not Field.Method method || method.QuerySchema is not JsonElement schemaEl) {
            if (field is Field.Method) ParamNoteText("该字段为无参 Action, 无需参数。");
            return;
        }

        try {
            if (JsonNode.Parse(schemaEl.GetRawText()) is not JsonObject schemaRoot) return;
            if (schemaRoot["properties"] is not JsonObject props || props.Count == 0) {
                ParamNoteText("该字段无需参数。");
                return;
            }

            HashSet<string> required = [];
            if (schemaRoot["required"] is JsonArray requiredArr) {
                foreach (JsonNode? item in requiredArr) {
                    if (item?.GetValue<string>() is { } name) required.Add(name);
                }
            }

            JsonObject? payload = ParsePayloadObject();
            foreach ((string name, JsonNode? psNode) in props) {
                if (psNode is not JsonObject ps) return;
                ParamField? pf = BuildParamField(name, ps, required.Contains(name), payload);
                if (pf == null) return;
                _paramFields.Add(pf);
                ParamForm.Children.Add(pf.Field);
            }

            JsonParamBox.IsVisible = false;
        } catch (JsonException e) {
            ServicesCaptureService.Logger?.LogWarning(e, "解析 QuerySchema 失败, 回退到原始 JSON 模式");
        }
    }

    void ParamNoteText(string text) {
        ParamNote.Text = text;
        ParamNote.IsVisible = true;
    }

    JsonObject? ParsePayloadObject() {
        if (string.IsNullOrWhiteSpace(Settings.PayloadJson)) return null;
        try {
            return JsonNode.Parse(Settings.PayloadJson) as JsonObject;
        } catch (JsonException) {
            return null;
        }
    }

    ParamField? BuildParamField(string name, JsonObject ps, bool required, JsonObject? payload) {
        List<string> types = ResolveTypes(ps);
        if (types.Contains("object") || types.Contains("array")) return null;
        string? type = types.FirstOrDefault(t => t != "null");
        JsonNode? current = payload?[name];

        Control control;
        Func<JsonNode?> readValue;
        JsonNode? defaultValue;

        switch (type) {
            case "boolean": {
                CheckBox cb = new CheckBox {
                    Content = "",
                    IsChecked = current?.GetValue<bool>() == true
                };
                cb.IsCheckedChanged += (_, _) => SyncFormToPayload();
                control = cb;
                readValue = () => JsonValue.Create(cb.IsChecked == true);
                defaultValue = JsonValue.Create(false);
                break;
            }
            case "integer":
            case "number": {
                bool isInteger = type == "integer";
                NumericUpDown nud = new NumericUpDown {
                    Minimum = isInteger ? int.MinValue : decimal.MinValue,
                    Maximum = isInteger ? int.MaxValue : decimal.MaxValue,
                    Increment = isInteger ? 1 : 0.1m,
                    FormatString = isInteger ? "0" : "0.#####",
                    MinWidth = 200,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
                };
                if (current is JsonValue jv) {
                    try {
                        nud.Value = jv.GetValue<decimal>();
                    } catch (InvalidOperationException) {
                        // 忽略类型不匹配
                    }
                }
                nud.ValueChanged += (_, _) => SyncFormToPayload();
                control = nud;
                readValue = isInteger
                    ? () => nud.Value is { } v ? JsonValue.Create((int)decimal.Round(v)) : null
                    : () => nud.Value is { } v ? JsonValue.Create((double)v) : null;
                defaultValue = isInteger ? JsonValue.Create(0) : JsonValue.Create(0.0);
                break;
            }
            case "string": {
                if (ps["enum"] is JsonArray enumArr) {
                    List<string> options = [];
                    foreach (JsonNode? item in enumArr) {
                        if (item is not JsonValue ev || ev.TryGetValue<string>(out _) == false) return null;
                        options.Add(item.GetValue<string>());
                    }
                    if (!required) options.Insert(0, "");
                    ComboBox cb = new ComboBox {
                        ItemsSource = options,
                        SelectedIndex = current is JsonValue cv && cv.TryGetValue<string>(out string? cur)
                            ? options.IndexOf(cur)
                            : -1,
                        MinWidth = 200,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
                    };
                    cb.SelectionChanged += (_, _) => SyncFormToPayload();
                    control = cb;
                    readValue = () => cb.SelectedIndex < 0 || (string?)cb.SelectedItem is not { } s
                        ? null
                        : JsonValue.Create(s);
                    defaultValue = options.FirstOrDefault(o => o.Length > 0) is { } first
                        ? JsonValue.Create(first)
                        : JsonValue.Create("");
                    break;
                }
                TextBox tb = new TextBox {
                    Text = current is JsonValue cv2 && cv2.TryGetValue<string>(out string? t) ? t : "",
                    MinWidth = 200,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
                };
                tb.TextChanged += (_, _) => SyncFormToPayload();
                control = tb;
                readValue = () => string.IsNullOrEmpty(tb.Text) ? null : JsonValue.Create(tb.Text);
                defaultValue = JsonValue.Create("");
                break;
            }
            default:
                return null;
        }

        return new ParamField(name, new CIField { Label = name, Content = control }, required, readValue, defaultValue);
    }

    static List<string> ResolveTypes(JsonObject ps) {
        List<string> types = [];
        if (ps["type"] is JsonValue typeValue) {
            types.Add(typeValue.GetValue<string>());
        } else if (ps["type"] is JsonArray typeArray) {
            foreach (JsonNode? item in typeArray) {
                if (item?.GetValue<string>() is { } t) types.Add(t);
            }
        }

        foreach (string key in new[] { "anyOf", "oneOf" }) {
            if (ps[key] is not JsonArray branches) continue;
            foreach (JsonNode? branch in branches) {
                if (branch is JsonObject bo && bo["type"] is JsonValue bv) types.Add(bv.GetValue<string>());
            }
        }

        return types.Distinct().ToList();
    }

    void SyncFormToPayload() {
        if (_suppressPayloadSync) return;
        _suppressPayloadSync = true;
        try {
            JsonObject obj = [];
            foreach (ParamField pf in _paramFields) {
                JsonNode? node = pf.ReadValue();
                if (node == null) {
                    if (pf.Required) obj[pf.Name] = pf.DefaultValue;
                    continue;
                }
                obj[pf.Name] = node;
            }
            Settings.PayloadJson = obj.ToJsonString();
        } catch (Exception e) {
            ServicesCaptureService.Logger?.LogError(e, "同步参数表单到 PayloadJson 失败");
        } finally {
            _suppressPayloadSync = false;
        }
    }

    sealed record ParamField(
        string Name,
        CIField Field,
        bool Required,
        Func<JsonNode?> ReadValue,
        JsonNode DefaultValue);
}

// ReSharper disable once ClassNeverInstantiated.Global
public partial class GlycoCallConfig : ObservableRecipient {
    [ObservableProperty]
    string _targetGid = "";

    [ObservableProperty]
    string _fid = "";

    [ObservableProperty]
    string _payloadJson = "";
}

[ActionInfo("glycolinker.action.glycoCall", "调用 Glycoprotein Action", "\uEDC9")]
public class GlycoCallAction : ActionBase<GlycoCallConfig> {
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    protected override async Task OnInvoke() {
        await base.OnInvoke();
        GlycoBridge bridge = GlycoBridge.Instance ?? throw new InvalidOperationException("GlycoBridge 未初始化");
        Glycoprotein.HostedService.GlycoService gx = bridge.Service;

        string gid = Settings.TargetGid.Trim();
        string fid = Settings.Fid.Trim();
        if (gid.Length == 0 || fid.Length == 0) {
            throw new InvalidOperationException("目标节点 (Gid) 与字段 ID (Fid) 不能为空");
        }

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(InterruptCancellationToken);
        cts.CancelAfter(Timeout);
        try {
            if (string.IsNullOrWhiteSpace(Settings.PayloadJson)) {
                ServicesCaptureService.Logger?.LogInformation("调用 Glycoprotein Action: [{Gid}]/{Fid}", gid, fid);
                await gx.DoActionAsync(gid, fid, cts.Token);
            } else {
                JsonElement payload;
                try {
                    payload = JsonDocument.Parse(Settings.PayloadJson).RootElement;
                } catch (JsonException e) {
                    throw new InvalidOperationException($"参数 JSON 无效: {e.Message}", e);
                }
                ServicesCaptureService.Logger?.LogInformation("调用 Glycoprotein Action(带参): [{Gid}]/{Fid}", gid, fid);
                await gx.DoActionAsync(gid, fid, payload, cts.Token);
            }
        } catch (InvalidOperationException) {
            throw;
        } catch (Exception e) {
            ServicesCaptureService.Logger?.LogError(e, "调用 Glycoprotein Action 失败: [{Gid}]/{Fid}", gid, fid);
            throw new InvalidOperationException($"调用 Glycoprotein Action 失败: {e.Message}", e);
        }
    }
}
