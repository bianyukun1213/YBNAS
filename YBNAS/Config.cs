﻿using System.Text.Json;
using System.Text.Json.Serialization;

namespace YBNAS
{
    internal struct SignInConfig
    {
        public bool Enable { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Account { get; set; }
        public string Password { get; set; }
        public Device Device { get; set; } // C# struct 是值类型，不会是 null。
        public double[] Position { get; set; }
        public string Address { get; set; }
        public bool Outside { get; set; }
        public string Photo { get; set; }
        public string Reason { get; set; }
        public List<int> TimeSpan { get; set; }
        public override readonly string ToString()
        {
            return JsonSerializer.Serialize(this, ServiceOptions.jsonSerializerOptions);
        }
    }

    internal static class Config
    {
        public static bool AutoSignIn { get; set; }
        public static bool AutoExit { get; set; }
        public static string Execute { get; set; } = string.Empty;
        public static string Proxy { get; set; } = string.Empty;
        public static bool Shuffle { get; set; }
        public static int MaxRunningTasks { get; set; }
        public static int MaxRetries { get; set; }
        public static List<int> RandomDelay { get; set; } = [];
        public static long ExpireIn { get; set; }
        public static List<SignInConfig> SignInConfigs { get; set; } = [];
    }
}
