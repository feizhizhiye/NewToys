using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NewToys.ScriptManager.Models;

/// <summary>
/// 宽松字符串转换器：manifest 是**给人手改的**，<c>"width": 110</c> 这种写数字的也该认
/// （读成 <c>"110"</c>，后续由 <c>SysInfoManifestService.NormalizeWidth</c> 统一校验）。
/// <para>
/// 🔴 没有它，一个数字列宽就会让整份 manifest 反序列化抛错 ⇒ 整页空态（NT-067 现场问题）。
/// </para>
/// </summary>
public sealed class LenientStringConverter : JsonConverter<string>
{
    /// <inheritdoc />
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? "",
            JsonTokenType.Number => reader.TryGetInt64(out var l)
                ? l.ToString(CultureInfo.InvariantCulture)
                : reader.GetDouble().ToString("0.###", CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => "",
            _ => throw new JsonException("宽度必须是字符串或数字（例：\"110\" / \"2*\" / \"Auto\"）"),
        };

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value ?? "");
}
