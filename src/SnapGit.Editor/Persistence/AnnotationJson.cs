using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SnapGit.Editor.Annotations;

namespace SnapGit.Editor.Persistence;

/// <summary>JSON (de)serialisation of an annotation list. Polymorphic via the attributes on <see cref="Annotation"/>.</summary>
public static class AnnotationJson
{
    public const int CurrentVersion = 1;

    private static readonly string[] ComputedProperties = ["Bounds", "HandlePoints", "IsDegenerate"];

    private static readonly JsonSerializerOptions Options = CreateOptions();

    private sealed record Envelope(int Version, List<Annotation> Annotations);

    public static string Serialize(IEnumerable<Annotation> annotations) =>
        JsonSerializer.Serialize(new Envelope(CurrentVersion, annotations.ToList()), Options);

    public static List<Annotation> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        var envelope = JsonSerializer.Deserialize<Envelope>(json, Options);
        if (envelope is null) return [];
        if (envelope.Version > CurrentVersion)
            throw new NotSupportedException($"This file was written by a newer SnapGit (document version {envelope.Version}).");
        return envelope.Annotations ?? [];
    }

    private static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Converters =
        {
            new JsonStringEnumConverter(),
            new ColorJsonConverter(),
            new PointFJsonConverter(),
            new RectangleFJsonConverter(),
        },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                typeInfo =>
                {
                    // Derived values must not be written (or read back).
                    foreach (var name in ComputedProperties)
                    {
                        var property = typeInfo.Properties.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                        if (property is not null) typeInfo.Properties.Remove(property);
                    }
                },
            },
        },
    };

    private sealed class ColorJsonConverter : JsonConverter<Color>
    {
        public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.GetString() ?? "#00FFFFFF";
            return Color.FromArgb(Convert.ToInt32(text.TrimStart('#'), 16));
        }

        public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) =>
            writer.WriteStringValue($"#{value.ToArgb():X8}");
    }

    private sealed class PointFJsonConverter : JsonConverter<PointF>
    {
        public override PointF Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var values = JsonSerializer.Deserialize<float[]>(ref reader, options) ?? [0, 0];
            return new PointF(values[0], values[1]);
        }

        public override void Write(Utf8JsonWriter writer, PointF value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
            writer.WriteEndArray();
        }
    }

    private sealed class RectangleFJsonConverter : JsonConverter<RectangleF>
    {
        public override RectangleF Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var values = JsonSerializer.Deserialize<float[]>(ref reader, options) ?? [0, 0, 0, 0];
            return new RectangleF(values[0], values[1], values[2], values[3]);
        }

        public override void Write(Utf8JsonWriter writer, RectangleF value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
            writer.WriteNumberValue(value.Width);
            writer.WriteNumberValue(value.Height);
            writer.WriteEndArray();
        }
    }
}
