// See https://aka.ms/new-console-template for more information
using Flurl.Http;
using NLog;
using System.Net;
using System.Text.Json.Nodes;
using YBNAS;
using System.Text.Json;
using CommandLine;
using Error = YBNAS.Error;
using Json.Schema;

var asm = System.Reflection.Assembly.GetExecutingAssembly();
string appVer = $"{asm.GetName().Name} v{asm.GetName().Version}";

Console.Title = appVer;
Logger logger = LogManager.GetCurrentClassLogger(); // NLog 推荐 logger 声明成 static 的，不过这里不行。
string configPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "config.json");
string configSchemaPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "config.json.schema");
string cachePath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "cache");

var parser = new Parser(config =>
{
    config.AutoVersion = false;
    config.AutoHelp = false;
});
parser.ParseArguments<CommandLineOptions>(args).WithParsed(o =>
{
    if (!string.IsNullOrEmpty(o.ConfigPath))
    {
        configPath = o.ConfigPath;
        Console.WriteLine("已从命令行参数取得配置文件的读取路径。");
    }
    if (!string.IsNullOrEmpty(o.CachePath))
    {
        cachePath = o.CachePath;
        Console.WriteLine("已从命令行参数取得缓存文件的读取路径。");
    }
    if (!string.IsNullOrEmpty(o.LogPath))
    {
        GlobalDiagnosticsContext.Set("logPath", o.LogPath);
        Console.WriteLine("已从命令行参数取得日志的写入路径。");
    }
});

logger.Debug("程序启动。");
Console.WriteLine($"{appVer} 由 Hollis 编写，源代码、许可证、版本更新及项目说明见 https://github.com/bianyukun1213/YBNAS。");

DateTimeOffset curDateTime = DateTimeOffset.Now;
List<SigninTask> tasks = [];
Dictionary<string, int> retries = [];
Dictionary<string, long> lastSuccesses = [];

int tasksRunning = 0;
int tasksComplete = 0;
int tasksSkipped = 0;
int tasksWaiting = tasks.Count;
int tasksAborted = 0;

try
{
    logger.Debug($"缓存文件是 {cachePath}。");
    if (!File.Exists(cachePath))
    {
        logger.Debug("缓存文件不存在。");
    }
    else
    {
        string cacheStr = File.ReadAllText(cachePath);
        var successes = JsonNode.Parse(cacheStr)!.Deserialize<Dictionary<string, long>>();
        if (successes != null)
            lastSuccesses = successes;
    }
}
catch (Exception ex)
{
    logger.Warn(ex, "解析缓存文件时出错。");
}

try
{
    UpdateStatus();
    logger.Info($"配置文件是 {configPath}。");
    if (!File.Exists(configPath))
    {
        logger.Fatal("配置文件不存在。");
        PrintExitMsg();
        return -1;
    }
    string configStr = File.ReadAllText(configPath);
    logger.Debug("解析配置字符串……");
    //    var schema = await JsonSchema.FromFileAsync(configSchemaPath);
    //    var errors = schema.Validate("""
    //{
    //  "AutoSignin": true,
    //  "AutoExit": false,
    //  "Proxy": "",
    //  "Shuffle": true,
    //  "MaxRunningTasks": 4,
    //  "MaxRetries": 3,
    //  "RandomDelay": [
    //    0,
    //    0
    //  ],
    //  "ExpireIn": "",
    //  "SigninConfigs": [
    //    {
    //      "Enable": true,
    //      "Name": "xiaojia",
    //      "Account": "13009700568",
    //      "Password": "dnggitgn001213",
    //      "Position": [
    //        118.403892,
    //        24.973737
    //      ],
    //      "Address": "0",
    //      "Photo": "",
    //      "Reason": "",
    //      "TimeSpan": 000
    //    }
    //  ]
    //}
    //""");
    //    if (errors.Count > 0)
    //    {
    //        //logger.Error("配置字符串格式错误：");
    //        foreach (var error in errors)
    //        {
    //            //Console.WriteLine(error.Path + ": " + error.Kind);
    //            //Console.WriteLine(error.Path+"@"+error.LineNumber+":"+error.LineNumber + ": " + error.Kind);
    //            string line = ":" + error.LineNumber + ":" + error.LinePosition;
    //            //logger.Error($"{error.Kind} at {error.Path}{(error.HasLineInfo ? line : string.Empty)}");
    //            logger.Error($"配置字符串格式错误：{error}");
    //        }
    //        PrintExitMsg();
    //        return -1;
    //    }




    JsonNode confRoot = JsonNode.Parse(configStr)!;


    var mySchema = JsonSchema.FromFile(configSchemaPath);
    var res = mySchema.Evaluate(confRoot);
    foreach (var item in res.Details)
    {
        Console.WriteLine(item);
    }

    Config.AutoSignin = confRoot["AutoSignin"].Deserialize<bool>();
    logger.Debug($"配置 AutoSignin: {Config.AutoSignin}。");
    Config.AutoExit = confRoot["AutoExit"].Deserialize<bool>();
    logger.Debug($"配置 AutoExit: {Config.AutoExit}。");
    Config.Proxy = confRoot["Proxy"].Deserialize<string>() ?? string.Empty;
    if (!string.IsNullOrEmpty(Config.Proxy) &&
        !Config.Proxy.StartsWith("http://") &&
        !Config.Proxy.StartsWith("https://") &&
        !Config.Proxy.StartsWith("socks4://") &&
        !Config.Proxy.StartsWith("socks4a://") &&
        !Config.Proxy.StartsWith("socks5://"))
    {
        logger.Warn("配置 Proxy 无效，将使用内置值空字符串。");
        Config.Proxy = string.Empty;
    }
    FlurlHttp.Clients.WithDefaults(builder => builder
    .ConfigureInnerHandler(hch =>
    {
        if (!string.IsNullOrEmpty(Config.Proxy))
            hch.Proxy = new WebProxy(Config.Proxy);
        hch.UseProxy = !string.IsNullOrEmpty(Config.Proxy); // 经测试会默认使用代理，若 Proxy 未填写则是系统代理。
    }));
    logger.Debug($"配置 Proxy: {(string.IsNullOrEmpty(Config.Proxy) ? "<空字符串>" : Config.Proxy)}。");
    Config.Shuffle = confRoot["Shuffle"].Deserialize<bool>();
    logger.Debug($"配置 Shuffle: {Config.Shuffle}。");
    Config.MaxRunningTasks = confRoot["MaxRunningTasks"].Deserialize<int>();
    if (Config.MaxRunningTasks < 1)
    {
        logger.Warn("配置 MaxRunningTasks 不应小于 1，将使用内置值 4。");
        Config.MaxRunningTasks = 4;
    }
    logger.Debug($"配置 MaxRunningTasks: {Config.MaxRunningTasks}。");
    Config.MaxRetries = confRoot["MaxRetries"].Deserialize<int>();
    if (Config.MaxRetries < 0)
    {
        logger.Warn("配置 MaxRetries 不应小于 0，将使用内置值 3。");
        Config.MaxRetries = 3;
    }
    logger.Debug($"配置 MaxRetries: {Config.MaxRetries}。");
    Config.RandomDelay = confRoot["RandomDelay"].Deserialize<List<int>>() ?? [];
    if (Config.RandomDelay.Count != 2 ||
        Config.RandomDelay[0] < 0 ||
        Config.RandomDelay[1] < 0 ||
        Config.RandomDelay[0] > 120 ||
        Config.RandomDelay[1] > 120 ||
        (Config.RandomDelay[0] == 0 && Config.RandomDelay[1] != 0) ||
        Config.RandomDelay[0] > Config.RandomDelay[1])
    {
        logger.Warn("配置 RandomDelay 无效，将使用内置值 [1, 10]。");
        Config.RandomDelay = [1, 10];
    }
    logger.Debug($"配置 RandomDelay: [{Config.RandomDelay[0]}, {Config.RandomDelay[1]}]。");
    Config.ExpireIn = confRoot["ExpireIn"].Deserialize<int>();
    if (Config.ExpireIn < 0)
    {
        logger.Warn("配置 ExpireIn 不应小于 0，将使用内置值 0。");
        Config.ExpireIn = 0;
    }
    logger.Debug($"配置 ExpireIn: {Config.ExpireIn}。");
    Config.SigninConfigs = confRoot["SigninConfigs"].Deserialize<List<SigninConfig>>() ?? [];
    if (Config.SigninConfigs.Count == 0)
    {
        logger.Warn("配置 SigninConfigs 为空。");
        PrintExitMsg();
        return -1;
    }
    foreach (SigninConfig conf in Config.SigninConfigs)
    {
        SigninConfig tempSc = JsonSerializer.Deserialize<SigninConfig>(JsonSerializer.Serialize(conf, ServiceOptions.jsonSerializerOptions))!;
        tempSc.Password = "<已抹除>";
        logger.Debug($"解析签到配置 {tempSc}……"); // 在日志中抹除密码。
        string getSigninConfigSkippedStr(string reason)
        {
            return $"第 {Config.SigninConfigs.IndexOf(conf) + 1} 条签到配置{(string.IsNullOrEmpty(conf.Name.Trim()) ? string.Empty : "（" + conf.Name + "）")}{reason}，将跳过解析。";
        }
        if (!conf.Enable)
        {
            logger.Info(getSigninConfigSkippedStr("未启用"));
            continue;
        }
        if (string.IsNullOrEmpty(conf.Account?.Trim()))
        {
            logger.Warn(getSigninConfigSkippedStr("账号为空"));
            continue;
        }
        if (string.IsNullOrEmpty(conf.Password?.Trim()))
        {
            logger.Warn(getSigninConfigSkippedStr("密码为空"));
            continue;
        }
        if (conf.Position?.Count != 2)
        {
            logger.Warn(getSigninConfigSkippedStr("签到坐标格式错误"));
            continue;
        }
        if (string.IsNullOrEmpty(conf.Address?.Trim()))
        {
            logger.Warn(getSigninConfigSkippedStr("签到地址为空"));
            continue;
        }
        if (!string.IsNullOrEmpty(conf.Photo?.Trim()))
        {
            try
            {
                FileInfo fileInfo = new(conf.Photo);
                if ((!fileInfo.Extension.Equals(".jpg", StringComparison.CurrentCultureIgnoreCase) && !fileInfo.Extension.Equals(".jpeg", StringComparison.CurrentCultureIgnoreCase)) || fileInfo.Length == 0)
                {
                    logger.Warn(getSigninConfigSkippedStr("照片文件无效"));
                    continue;
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, getSigninConfigSkippedStr("照片文件读取失败"));
                continue;
            }
        }
        if (conf.TimeSpan?.Count != 4)
        {
            logger.Warn(getSigninConfigSkippedStr("签到时间段格式错误"));
            continue;
        }
        int confBegTime = conf.TimeSpan[0] * 60 + conf.TimeSpan[1];
        int confEndTime = conf.TimeSpan[2] * 60 + conf.TimeSpan[3];
        int curTime = curDateTime.Hour * 60 + curDateTime.Minute;
        if (curTime < confBegTime || curTime > confEndTime)
        {
            logger.Info(getSigninConfigSkippedStr("签到时间段不包含当前时间"));
            continue;
        }
        if (!lastSuccesses.TryGetValue(conf.Account, out long lastSuccess))
            lastSuccess = 0;
        //lastSuccess = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 100;
        SigninTask task = new(
            conf.Name,
            conf.Account,
            conf.Password,
            conf.Position,
            conf.Address,
            conf.Photo ?? string.Empty, // 此处 conf.Photo 可能为空。
            conf.Reason,
            conf.Outside,
            conf.TimeSpan[0],
            conf.TimeSpan[1],
            conf.TimeSpan[2],
            conf.TimeSpan[3],
            lastSuccess,
            conf.Device
            );
        tasks.Add(task);
        retries.Add(task.TaskGuid, 0);
        UpdateStatus();
    }
    if (Config.Shuffle)
    {
        Random rd = new();
        tasks = [.. tasks.OrderBy(task => rd.Next())];
    }
    logger.Info($"共 {Config.SigninConfigs.Count} 条签到配置，{tasks.Count} 条可用且已解析。");
    if (tasks.Count == 0)
    {
        logger.Warn("当前时间下无可用签到配置。");
        PrintExitMsg();
        return 0;
    }
}
catch (Exception ex)
{
    logger.Fatal(ex, "解析配置文件时出错。");
    PrintExitMsg();
    return -1;
}

foreach (var item in tasks)
{
    item.OnRun += St_OnRun;
    item.OnComplete += St_OnComplete;
    item.OnSkip += St_OnSkip;
    item.OnError += St_OnError;
}

if (!Config.AutoSignin)
{
    Console.WriteLine("按任意键开始签到。");
    Console.ReadKey(true); // true 不显示按下的按键。
}

for (int i = 0; i < tasks.Count; i++)
{
    if (i >= Config.MaxRunningTasks) // 应用初始同时运行任务数限制。
        break;
    var res = tasks[i].Run(); // 消除 CS4014 警告，https://learn.microsoft.com/zh-cn/dotnet/csharp/language-reference/compiler-messages/cs4014。
}

void UpdateStatus()
{
    tasksRunning = tasks.Count(x => x.Status == SigninTask.TaskStatus.Running);
    tasksComplete = tasks.Count(x => x.Status == SigninTask.TaskStatus.Complete);
    tasksSkipped = tasks.Count(x => x.Status == SigninTask.TaskStatus.Skipped);
    tasksWaiting = tasks.Count(x => x.Status == SigninTask.TaskStatus.Waiting);
    tasksAborted = tasks.Count(x => x.Status == SigninTask.TaskStatus.Aborted);
    Console.Title = $"{appVer} | {Config.SigninConfigs.Count} 签到配置，{tasks.Count} 已解析：{tasksRunning} 运行，{tasksComplete} 完成，{tasksSkipped} 跳过，{tasksWaiting} 等待，{tasksAborted} 中止";
}

void RunNextTask()
{
    SigninTask? one = tasks.Find(x => x.Status == SigninTask.TaskStatus.Waiting);
    var res = one?.Run();
}

void St_OnRun(SigninTask task, Error err)
{
    UpdateStatus();
}

void St_OnComplete(SigninTask task, Error err)
{
    long curTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    if (!lastSuccesses.TryAdd(task.Account, curTimestamp))
        lastSuccesses[task.Account] = curTimestamp;
    UpdateStatus();
    RunNextTask();
}

void St_OnSkip(SigninTask task, Error err)
{
    UpdateStatus();
    RunNextTask();
}

void St_OnError(SigninTask task, Error err)
{
    UpdateStatus();
    bool succ = retries.TryGetValue(task.TaskGuid, out int curRetries);
    if (!succ)
        return;
    string logPrefix = task.GetLogPrefix();
    if (logPrefix.EndsWith('）'))
        logPrefix += " ";
    if (curRetries < Config.MaxRetries)
    {
        logger.Warn($"{logPrefix}出错，将进行第 {++curRetries} 次重试。");
        retries[task.TaskGuid] = curRetries; // 在重试任务运行前增加重试次数。
        var res = task.Run();
    }
    else
    {
        if (Config.MaxRetries == 0)
            logger.Warn($"{logPrefix}出错且未开启重试，将运行下一项任务。");
        else
            logger.Warn($"{logPrefix}在 {curRetries} 次重试后再次出错，将运行下一项任务。");
        RunNextTask();
    }
}

void PrintExitMsg()
{
    if (!Config.AutoExit)
    {
        Console.WriteLine("按任意键退出。");
        Console.ReadKey(true);
    }
}

while (!(tasksRunning == 0 && tasksWaiting == 0))
{
    await Task.Delay(1000);
}

try
{
    File.WriteAllText(cachePath, JsonSerializer.Serialize(lastSuccesses, ServiceOptions.jsonSerializerOptions));
}
catch (Exception ex)
{
    logger.Error(ex, "写入缓存文件时出错。");
}

logger.Info("已尝试运行所有任务。");
if (tasksAborted > 0)
{
    logger.Warn($"----中止（{tasksAborted}/{tasks.Count}）----");
    foreach (var item in tasks.FindAll(x => x.Status == SigninTask.TaskStatus.Aborted))
    {
        logger.Warn($"{item.GetLogPrefix()}：{item.StatusText}。");
    }
}
if (tasksSkipped > 0)
{
    logger.Info($"----跳过（{tasksSkipped}/{tasks.Count}）----");
    foreach (var item in tasks.FindAll(x => x.Status == SigninTask.TaskStatus.Skipped))
    {
        logger.Info($"{item.GetLogPrefix()}：{item.StatusText}。");
    }
}
if (tasksSkipped > 0)
    logger.Info("--------------");
else if (tasksAborted > 0)
    logger.Warn("--------------");
PrintExitMsg();
return 0;
