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
    internal struct SigninConfig
    {
        public string Name { get; set; }
        public string Account { get; set; }
        public string Password { get; set; }
        public Device Device { get; set; } // C# struct 是值类型，不会是 null。
        public List<double> Position { get; set; }
        public string Address { get; set; }
        public List<int> TimeSpan { get; set; }
        public override readonly string ToString()
        {
            return JsonSerializer.Serialize(this, ServiceOptions.jsonSerializerOptions);
        }
    }
    internal static class Config
    {
        public static bool AutoExit { get; set; }
        public static string Proxy { get; set; } = string.Empty;
        public static bool Shuffle { get; set; }
        public static int MaxRunningTasks { get; set; }
        public static int MaxRetries { get; set; }
        public static List<int> RandomDelay { get; set; } = [];
        public static List<SigninConfig> SigninConfigs { get; set; } = [];
    }
}
