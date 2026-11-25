using System.Text.Json;
using System.Text.Json.Serialization;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Client;

// NSwag turns a nullable enum reference (OpenAPI 3.1 "oneOf: [null, ViewKind]") into an empty class: ViewDesign.inheritsFrom
// becomes InheritsFrom and DesignProblem.view becomes View, and their JSON value, a string such as "Mobile", would not
// deserialize. These partials give both classes the view they name and read and write it as that string.

/// <summary>The view a view inherits from (<see cref="ViewDesign.InheritsFrom"/>).</summary>
[JsonConverter(typeof(ViewKindRefConverter<InheritsFrom>))]
public partial class InheritsFrom : IViewKindRef
{
    public ViewKind Value { get; set; }

    public static InheritsFrom Of(ViewKind view) => new() { Value = view };
}

/// <summary>The view a problem is about (<see cref="DesignProblem.View"/>); null on the problem for the whole design.</summary>
[JsonConverter(typeof(ViewKindRefConverter<View>))]
public partial class View : IViewKindRef
{
    public ViewKind Value { get; set; }
}

public interface IViewKindRef
{
    ViewKind Value { get; set; }
}

/// <summary>Reads and writes a view reference as its name ("Mobile").</summary>
public sealed class ViewKindRefConverter<T> : JsonConverter<T> where T : class, IViewKindRef, new()
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.String && Enum.TryParse<ViewKind>(reader.GetString(), ignoreCase: true, out var view))
            return new T { Value = view };
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number) && Enum.IsDefined((ViewKind)number))
            return new T { Value = (ViewKind)number };
        throw new JsonException($"'{(reader.TokenType == JsonTokenType.String ? reader.GetString() : reader.TokenType.ToString())}' is not a view.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value.ToString());
}

public partial class ContentDesignerClient
{
    /// <summary>Enums travel by name, as the API writes them (ViewKind lists in ApplyRequest, CheckRequest, ...).</summary>
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) =>
        settings.Converters.Add(new JsonStringEnumConverter());
}
