using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading.Tasks;

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
