#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SWSimTool.Persistence
{
    // The net48 on-document codec remains JavaScriptSerializer. This adapter is only
    // used by the portable host, where dictionary values must be ordinary CLR data.
    public sealed class PortableJsonSerializer
    {
        public int MaxJsonLength { get; set; } = 2097152;
        static readonly JsonSerializerOptions Options = CreateOptions();
        static JsonSerializerOptions CreateOptions()
        {
            var value = new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true,
                MaxDepth = 100, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            value.Converters.Add(new ClrObjectConverter());
            return value;
        }
        void Check(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            if (json.Length > MaxJsonLength) throw new InvalidOperationException("JSON input exceeds the configured length limit");
        }
        public string Serialize(object value)
        {
            var json = JsonSerializer.Serialize(value, Options); Check(json); return json;
        }
        public T Deserialize<T>(string json) { Check(json); return JsonSerializer.Deserialize<T>(json, Options); }
        public object DeserializeObject(string json) { return Deserialize<object>(json); }
        public T ConvertToType<T>(object value) { return Deserialize<T>(Serialize(value)); }
        sealed class ClrObjectConverter : JsonConverter<object>
        {
            public override object Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            {
                using (var document = JsonDocument.ParseValue(ref reader)) return Convert(document.RootElement);
            }
            static object Convert(JsonElement value)
            {
                switch (value.ValueKind)
                {
                    case JsonValueKind.Object:
                        var result = new Dictionary<string, object>(StringComparer.Ordinal);
                        foreach (var entry in value.EnumerateObject()) result.Add(entry.Name, Convert(entry.Value));
                        return result;
                    case JsonValueKind.Array:
                        var list = new List<object>(); foreach (var entry in value.EnumerateArray()) list.Add(Convert(entry)); return list.ToArray();
                    case JsonValueKind.String: return value.GetString();
                    case JsonValueKind.Number:
                        int integer; long large;
                        if (value.TryGetInt32(out integer)) return integer;
                        if (value.TryGetInt64(out large)) return large;
                        return value.GetDouble();
                    case JsonValueKind.True: return true;
                    case JsonValueKind.False: return false;
                    case JsonValueKind.Null: return null;
                    default: throw new JsonException("Unsupported JSON token");
                }
            }
            public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
            {
                if (value == null) writer.WriteNullValue();
                else if (value.GetType() == typeof(object)) { writer.WriteStartObject(); writer.WriteEndObject(); }
                else JsonSerializer.Serialize(writer, value, value.GetType(), options);
            }
        }
    }
}
#endif
