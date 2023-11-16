using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YBNAS
{
    struct SigninConfig
    {
        public string? Account { get; set; }
        public string? Password { get; set; }
        public Device Device { get; set; } // C# struct 是值类型，不会是 null。
        public List<double>? Position { get; set; }
        public string? Address { get; set; }
        public List<int>? TimeSpan { get; set; }
        public override readonly string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
    static internal class Config
    {
        public static bool AutoExit { get; set; }
        public static int MaxRunningTasks { get; set; }
        public static int MaxRetries { get; set; }
        public static bool RandomDelay { get; set; }
        public static List<SigninConfig>? SigninConfigs { get; set; }
    }
}
