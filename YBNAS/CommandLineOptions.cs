using CommandLine;

namespace YBNAS
{
    internal class CommandLineOptions
    {
        [Option('c', "config-path", Required = false, HelpText = "设置配置文件的读取路径。")]
        public string? ConfigPath { get; set; }
        [Option('k', "cache-path", Required = false, HelpText = "设置缓存文件的读取路径。")]
        public string? CachePath { get; set; }
        [Option('l', "log-path", Required = false, HelpText = "设置日志的写入路径。")]
        public string? LogPath { get; set; }
    }
}
