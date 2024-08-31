using System.Text.Encodings.Web;
using System.Text.Json;

namespace YBNAS
{
    internal static class ServiceOptions
    {
        public static readonly JsonSerializerOptions jsonSerializerOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // 不转义中文字符和 HTML 敏感字符。
        };
    }

    // 不再需要。现在通过 Replace Layout Renderer 抹除密码。
    //internal class PasswordJsonConverter : JsonConverter<string>
    //{
    //    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    //    {
    //        return reader.GetString()!;
    //    }

    //    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    //    {
    //        writer.WriteStringValue("<已抹除>");
    //    }
    //}
}
