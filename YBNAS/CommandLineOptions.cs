using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YBNAS
{
    internal class CommandLineOptions
    {
        [Option('c', "config", Required = false, HelpText = "设置配置文件读取路径。")]
        public string? ConfigPath { get; set; }
    }
}
