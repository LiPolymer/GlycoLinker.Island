using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SuperAutoIsland.Interface.Metadata;
using SuperAutoIsland.Interface.Services;

namespace GlycoLinker.Island.Sai;

// One captured descriptor drives both the Blockly fields and the native JSON payload.
internal sealed class GlycoSaiSchema {
    enum RootKind { None, Object, Json }
    enum ValueKind { String, Number, Integer, Boolean, Enum, Json }
    readonly RootKind _root;
    readonly Parameter[] _parameters;
    public string Tooltip { get; }

    GlycoSaiSchema(RootKind root, Parameter[] parameters, string tooltip) {
        _root = root;
        _parameters = parameters;
        Tooltip = tooltip;
    }

    public static GlycoSaiSchema Create(JsonElement? schema) {
        if (schema is not { } root)
            return new(RootKind.None, [], "");
        string description = Text(root, "description") ?? "";
        if (root.ValueKind != JsonValueKind.Object)
            return new(RootKind.Json, [], description);
        // Match GlycoCallSettings: the .NET exporter uses type: ["object", "null"].
        if (!root.TryGetProperty("properties", out JsonElement properties) ||
            properties.ValueKind != JsonValueKind.Object || properties.GetPropertyCount() == 0)
            return new(RootKind.None, [], description);
        HashSet<string> required = new(StringComparer.Ordinal);
        if (root.TryGetProperty("required", out JsonElement names) && names.ValueKind == JsonValueKind.Array) {
            foreach (JsonElement name in names.EnumerateArray()) {
                if (name.ValueKind == JsonValueKind.String) required.Add(name.GetString()!);
            }
        }
        List<Parameter> parameters = [];
        List<string> tips = [];
        if (description.Length != 0) tips.Add(description);
        foreach (JsonProperty property in properties.EnumerateObject()) {
            string? type = ResolveType(property.Value, out bool nullable);
            if (type is not ("string" or "integer" or "number" or "boolean"))
                return new(RootKind.Json, [], description);
            if (type == "string" && Has(property.Value, "enum") &&
                property.Value.GetProperty("enum") is { ValueKind: JsonValueKind.Array } options &&
                (options.GetArrayLength() == 0 || options.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.String)))
                return new(RootKind.Json, [], description);
            Parameter parameter = new(property.Name, property.Value, required.Contains(property.Name), type, nullable);
            parameters.Add(parameter);
            string? tip = Text(property.Value, "description");
            if (!string.IsNullOrEmpty(tip)) tips.Add($"{parameter.Label}: {tip}");
        }
        return new(RootKind.Object, parameters.ToArray(), string.Join("\n", tips));
    }

    public void GetFields(FieldsRegister fields) {
        if (_root == RootKind.Json) fields.AddField("PayloadJson", BasicFields.Text("参数 JSON（Schema 无法展开）"));
        foreach (Parameter parameter in _parameters) parameter.Register(fields);
    }

    public string BuildPayload(JsonElement settings) {
        if (_root == RootKind.None) return "";
        if (_root == RootKind.Json) {
            string text = ReadText(Read(settings, "PayloadJson", "PayloadJson"), "PayloadJson");
            ValidateJson(text, "PayloadJson");
            return text;
        }
        JsonObject payload = new();
        foreach (Parameter parameter in _parameters) parameter.Write(payload, settings);
        return payload.ToJsonString();
    }

    sealed class Parameter {
        readonly string _name;
        readonly string _valueKey;
        readonly string _modeKey;
        readonly bool _required;
        readonly bool _nullable;
        readonly ValueKind _kind;
        readonly JsonElement[] _enum;
        readonly string[] _enumTokens;
        readonly JsonElement? _default;
        readonly int _enumDefault;
        public string Label { get; }

        public Parameter(string name, JsonElement schema, bool required, string type, bool nullable) {
            _name = name;
            string hex = Convert.ToHexString(Encoding.UTF8.GetBytes(name));
            _valueKey = "p_" + hex;
            _modeKey = "m_" + hex;
            _required = required;
            string? title = Text(schema, "title");
            Label = (string.IsNullOrEmpty(title) ? name : $"{title}（{name}）") + (required ? " *" : "");
            _nullable = nullable;
            _enum = [];
            _enumTokens = [];
            _enumDefault = 0;
            _kind = type switch {
                "string" => ValueKind.String,
                "number" => ValueKind.Number,
                "integer" => ValueKind.Integer,
                "boolean" => ValueKind.Boolean,
                _ => ValueKind.Json
            };
            JsonElement? defaultValue = Has(schema, "default") ? schema.GetProperty("default").Clone() : null;
            if (type == "string" && Has(schema, "enum") && schema.GetProperty("enum").ValueKind == JsonValueKind.Array) {
                _enum = schema.GetProperty("enum").EnumerateArray().Select(v => v.Clone()).ToArray();
                _enumTokens = Enumerable.Range(0, _enum.Length).Select(i => $"e{i}").ToArray();
                _kind = ValueKind.Enum;
                if (defaultValue is { } enumDefault) {
                    int match = Array.FindIndex(_enum, v => JsonElement.DeepEquals(v, enumDefault));
                    if (match >= 0) { _enumDefault = match; _default = enumDefault; }
                }
            } else if (defaultValue is { } value) {
                if (_kind == ValueKind.Integer && value.ValueKind == JsonValueKind.Number && IsInteger(value) &&
                    (!value.TryGetDecimal(out decimal integer) || integer is > 9007199254740991m or < -9007199254740991m))
                    _kind = ValueKind.Json;
                if ((value.ValueKind == JsonValueKind.Null && _nullable) || Matches(value, type, _kind)) _default = value;
            }
        }

        public void Register(FieldsRegister fields) {
            if (!_required || _nullable) {
                List<(string, string)> modes = [("填写值", "value")];
                if (!_required) modes.Add(("省略", "omit"));
                if (_nullable) modes.Add(("null", "null"));
                string first = _default is { ValueKind: JsonValueKind.Null } ? "null" : _default.HasValue || _required ? "value" : "omit";
                int index = modes.FindIndex(m => m.Item2 == first);
                (string, string) selected = modes[index];
                modes.RemoveAt(index);
                modes.Insert(0, selected);
                fields.AddField(_modeKey, BasicFields.Dropdown(Label + " · 模式", modes));
            }
            switch (_kind) {
                case ValueKind.String:
                    fields.AddField(_valueKey, BasicFields.Text(Label, _default is { ValueKind: JsonValueKind.String } text ? text.GetString()! : ""));
                    break;
                case ValueKind.Number:
                case ValueKind.Integer:
                    fields.AddField(_valueKey, BasicFields.Number(Label, _default is { ValueKind: JsonValueKind.Number } number ? number.GetDouble() : 0));
                    break;
                case ValueKind.Boolean:
                    fields.AddField(_valueKey, BasicFields.Boolean(Label, _default is { ValueKind: JsonValueKind.True }));
                    break;
                case ValueKind.Enum:
                    List<(string, string)> options = [];
                    AddOption(_enumDefault);
                    for (int i = 0; i < _enum.Length; i++) if (i != _enumDefault) AddOption(i);
                    fields.AddField(_valueKey, BasicFields.Dropdown(Label, options));
                    void AddOption(int i) => options.Add((_enum[i].ValueKind == JsonValueKind.String ? _enum[i].GetString()! : _enum[i].GetRawText(), _enumTokens[i]));
                    break;
                default:
                    fields.AddField(_valueKey, BasicFields.Text(Label + " JSON", _default?.GetRawText() ?? ""));
                    break;
            }
        }

        public void Write(JsonObject payload, JsonElement settings) {
            string mode = _required ? "value" : "omit";
            if (settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty(_modeKey, out JsonElement modeValue)) {
                mode = ReadText(modeValue, _name);
            }
            switch (mode) {
                case "omit" when !_required: return;
                case "null" when _nullable: payload[_name] = null; return;
                case "value": break;
                default: throw Invalid(_name, "模式无效");
            }
            JsonElement value = Read(settings, _valueKey, _name);
            JsonNode? node;
            switch (_kind) {
                case ValueKind.String:
                    node = JsonValue.Create(ReadText(value, _name));
                    break;
                case ValueKind.Boolean:
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid(_name, "需要布尔值");
                    node = JsonValue.Create(value.GetBoolean());
                    break;
                case ValueKind.Number:
                case ValueKind.Integer:
                    if (value.ValueKind != JsonValueKind.Number) throw Invalid(_name, "需要数字");
                    if (_kind == ValueKind.Integer && !IsInteger(value)) throw Invalid(_name, "需要整数，不能包含小数");
                    node = JsonNode.Parse(value.GetRawText());
                    break;
                case ValueKind.Enum:
                    string token = ReadText(value, _name);
                    int index = Array.IndexOf(_enumTokens, token);
                    if (index < 0) throw Invalid(_name, "枚举选项无效");
                    node = JsonNode.Parse(_enum[index].GetRawText());
                    break;
                default:
                    node = ParseJson(ReadText(value, _name), _name);
                    break;
            }
            if (node == null && !_nullable) throw Invalid(_name, "不允许 null");
            payload[_name] = node;
        }
    }

    static bool Matches(JsonElement value, string? type, ValueKind kind) => kind switch {
        ValueKind.String => value.ValueKind == JsonValueKind.String,
        ValueKind.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        ValueKind.Number => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) && double.IsFinite(number),
        ValueKind.Integer => value.ValueKind == JsonValueKind.Number && IsInteger(value),
        ValueKind.Json => type switch {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "integer" => value.ValueKind == JsonValueKind.Number && IsInteger(value),
            _ => value.ValueKind != JsonValueKind.Null
        },
        _ => false
    };

    static string? ResolveType(JsonElement schema, out bool nullable) {
        List<string> types = [];
        if (Has(schema, "type")) AddTypes(schema.GetProperty("type"), types);
        foreach (string key in new[] { "anyOf", "oneOf" }) {
            if (!Has(schema, key) || schema.GetProperty(key) is not { ValueKind: JsonValueKind.Array } branches) continue;
            foreach (JsonElement branch in branches.EnumerateArray()) {
                if (Has(branch, "type") && branch.GetProperty("type") is { ValueKind: JsonValueKind.String } type)
                    types.Add(type.GetString()!);
            }
        }
        nullable = types.Contains("null");
        if (types.Contains("object") || types.Contains("array")) return null;
        return types.FirstOrDefault(t => t != "null");
    }

    static void AddTypes(JsonElement type, List<string> types) {
        if (type.ValueKind == JsonValueKind.String) {
            types.Add(type.GetString()!);
        } else if (type.ValueKind == JsonValueKind.Array) {
            foreach (JsonElement item in type.EnumerateArray()) {
                if (item.ValueKind == JsonValueKind.String) types.Add(item.GetString()!);
            }
        }
    }

    static bool IsInteger(JsonElement value) {
        // Inspect JSON's decimal digits; decimal/double conversion can round tiny fractions to zero.
        ReadOnlySpan<char> raw = value.GetRawText();
        int e = raw.IndexOfAny('e', 'E');
        ReadOnlySpan<char> mantissa = e < 0 ? raw : raw[..e];
        long exponent = 0;
        if (e >= 0 && !long.TryParse(raw[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
            exponent = raw[e + 1] == '-' ? long.MinValue : long.MaxValue;
        int dot = mantissa.IndexOf('.');
        int fraction = dot < 0 ? 0 : mantissa.Length - dot - 1;
        if (exponent >= fraction) return true;
        int zeros = 0;
        bool allZero = true;
        for (int i = mantissa.Length - 1; i >= 0; i--) {
            char digit = mantissa[i];
            if (digit is '.' or '-') continue;
            if (digit != '0') { allZero = false; break; }
            zeros++;
        }
        return allZero || (exponent >= fraction - (long)zeros);
    }

    static bool Has(JsonElement obj, string key) => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out _);
    static string? Text(JsonElement obj, string key) => Has(obj, key) && obj.GetProperty(key).ValueKind == JsonValueKind.String ? obj.GetProperty(key).GetString() : null;
    static JsonElement Read(JsonElement settings, string key, string name) {
        if (settings.ValueKind != JsonValueKind.Object || !settings.TryGetProperty(key, out JsonElement value)) throw Invalid(name, "缺少参数值");
        return value;
    }
    static string ReadText(JsonElement value, string name) {
        if (value.ValueKind != JsonValueKind.String) throw Invalid(name, "需要文本值");
        return value.GetString()!;
    }
    static JsonNode? ParseJson(string text, string name) {
        if (string.IsNullOrWhiteSpace(text)) throw Invalid(name, "JSON 不能为空");
        try { return JsonNode.Parse(text); }
        catch (JsonException e) { throw Invalid(name, $"JSON 格式错误: {e.Message}"); }
    }
    static void ValidateJson(string text, string name) {
        if (string.IsNullOrWhiteSpace(text)) throw Invalid(name, "JSON 不能为空");
        try { using JsonDocument document = JsonDocument.Parse(text); }
        catch (JsonException e) { throw Invalid(name, $"JSON 格式错误: {e.Message}"); }
    }
    static InvalidOperationException Invalid(string name, string reason) => new($"SAI 参数 [{name}] 无效: {reason}");
}
