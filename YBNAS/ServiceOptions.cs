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
}
