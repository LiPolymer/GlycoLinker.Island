using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using CommunityToolkit.Mvvm.ComponentModel;
using Glycoprotein.Glycosylation;
using Microsoft.Extensions.Logging;

namespace GlycoLinker.Island.Automations.Actions;

public sealed record NodeOption(string Id, string? Vendor, int FieldCount) {
    public string Detail {
        get {
            string fields = FieldCount == 1 ? "1 个字段" : $"{FieldCount} 个字段";
            return Vendor is { Length: > 0 } v ? $"{v} · {fields}" : fields;
        }
    }
    public bool HasDetail => true;
    public override string ToString() => Id;
}

public sealed record FieldOption(string Id, string? FriendlyName, string? Description) {
    public string DisplayName => FriendlyName is { Length: > 0 } f ? f : Id;
    public bool HasFriendlyName => FriendlyName is { Length: > 0 };
    public bool HasDescription => Description is { Length: > 0 };
    public override string ToString() => Id;
}

public partial class GlycoCallSettings : ActionSettingsControlBase<GlycoCallConfig> {
    public ObservableCollection<NodeOption> NodeIds { get; } = [];
    public ObservableCollection<FieldOption> FieldIds { get; } = [];

    readonly List<ParamField> _paramFields = [];
    bool _suppressPayloadSync;
    bool _suppressAutoOpen;
    string? _lastCommittedGid;
    string? _lastCommittedFid;

    public GlycoCallSettings() {
        InitializeComponent();
        DataContext = this;
        FidBox.ItemFilter = FilterFieldOption;
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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnAttachedToVisualTree(e);
        if (GlycoBridge.Instance != null) GlycoBridge.Instance.SnapshotChanged += OnSnapshotChanged;
        _lastCommittedGid = Settings.TargetGid.Trim();
        _lastCommittedFid = Settings.Fid.Trim();
        RefreshSnapshot();
        RefreshFields();
        RebuildParamForm();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnDetachedFromVisualTree(e);
        if (GlycoBridge.Instance != null) GlycoBridge.Instance.SnapshotChanged -= OnSnapshotChanged;
    }

    void OnSnapshotChanged() {
        Dispatcher.UIThread.Post(() => {
            RefreshSnapshot();
            RefreshFields();
            RebuildParamForm();
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
        string gid = Settings.TargetGid.Trim();
        if (gid == _lastCommittedGid) return;
        _lastCommittedGid = gid;
        RefreshFields();
        RebuildParamForm();
    }

    void FidBox_OnGotFocus(object? sender, FocusChangedEventArgs e) => TryAutoOpenDropDown(FidBox, FieldIds.Count);

    void FidBox_OnLostFocus(object? sender, RoutedEventArgs e) => CommitFid();

    void FidBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) {
        if (e.AddedItems.Count == 0) return;
        _suppressAutoOpen = true;
        Dispatcher.UIThread.Post(() => _suppressAutoOpen = false);
        Dispatcher.UIThread.Post(CommitFid);
    }

    void TryAutoOpenDropDown(AutoCompleteBox box, int itemCount) {
        if (_suppressAutoOpen || box.IsDropDownOpen || itemCount == 0) return;
        box.IsDropDownOpen = true;
    }

    void CommitFid() {
        string fid = Settings.Fid.Trim();
        if (fid == _lastCommittedFid) return;
        _lastCommittedFid = fid;
        RebuildParamForm();
    }

    void RefreshSnapshot() {
        NodeIds.Clear();
        foreach (BeaconInfo beacon in GlycoBridge.Instance?.Snapshot ?? []) {
            NodeIds.Add(new NodeOption(beacon.Id,beacon.Vendor,beacon.Fields.Count));
        }
    }

    void RefreshFields() {
        FieldIds.Clear();
        GlycoBridge? bridge = GlycoBridge.Instance;
        if (bridge == null) return;
        string gid = Settings.TargetGid.Trim();
        if (gid.Length == 0) return;
        BeaconInfo? beacon = bridge.Snapshot.FirstOrDefault(b => b.Id == gid);
        if (beacon == null) return;
        foreach (Field field in beacon.Fields) {
            if (field is Field.Event) continue;
            FieldIds.Add(new FieldOption(field.Id, field.FriendlyName, field.Description));
        }
        if (FieldIds.Count == 1 && Settings.Fid.Trim().Length == 0) {
            Settings.Fid = FieldIds[0].Id;
            _lastCommittedFid = FieldIds[0].Id;
        }
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
        if (field is Field.Event) {
            ParamNoteText("该字段为 Event 类型, 无法通过本行动调用, 请改选 Method 字段。");
            JsonParamBox.IsVisible = false;
            return;
        }
        if (field is not Field.Method method || method.QuerySchema is not JsonElement schemaEl) {
            if (field is Field.Method) ParamNoteText("该字段为无参 Action, 无需参数。");
            ClearPayloadJson();
            return;
        }

        try {
            if (JsonNode.Parse(schemaEl.GetRawText()) is not JsonObject schemaRoot) return;
            if (schemaRoot["properties"] is not JsonObject props || props.Count == 0) {
                ParamNoteText("该字段无需参数。");
                ClearPayloadJson();
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
                ParamForm.Children.Add(pf.Row);
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

    void ClearPayloadJson() {
        JsonParamBox.IsVisible = false;
        JsonParamError.IsVisible = false;
        JsonParamError.Text = "";
        if (Settings.PayloadJson.Length > 0) Settings.PayloadJson = "";
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
        string? title = ps["title"] is JsonValue tv && tv.TryGetValue<string>(out string? t) ? t : null;
        string? description = ps["description"] is JsonValue dv && dv.TryGetValue<string>(out string? d) ? d : null;
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
                    MinWidth = 220,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
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
                        MinWidth = 220,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
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
                    Text = current is JsonValue cv2 && cv2.TryGetValue<string>(out string? o) ? o : "",
                    MinWidth = 220,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
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

        Control row = BuildParamRow(name, title, description, control);
        return new ParamField(name, row, required, readValue, defaultValue);
    }

    static StackPanel BuildParamRow(string name, string? title, string? description, Control control) {
        StackPanel label = new() {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6,
            MinWidth = 110,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        if (title is { Length: > 0 }) {
            label.Children.Add(new TextBlock {
                Text = title,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            });
            label.Children.Add(new TextBlock {
                Text = name,
                FontSize = 11,
                Opacity = 0.6,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            });
        } else {
            label.Children.Add(new TextBlock {
                Text = name,
                Opacity = 0.8,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            });
        }
        StackPanel row = new() {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            Spacing = 2,
            Children = {
                new StackPanel {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    Children = { label, control }
                }
            }
        };
        if (description is { Length: > 0 }) {
            row.Children.Add(new TextBlock {
                Text = description,
                FontSize = 11,
                Opacity = 0.7,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
        }
        return row;
    }

    void JsonParamBox_OnTextChanged(object? sender, TextChangedEventArgs e) {
        string? error = null;
        if (!string.IsNullOrWhiteSpace(JsonParamBox.Text)) {
            try {
                JsonNode.Parse(JsonParamBox.Text);
            } catch (JsonException je) {
                error = $"JSON 无效: {je.Message}";
            }
        }
        JsonParamError.Text = error ?? "";
        JsonParamError.IsVisible = error != null;
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
        Control Row,
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
