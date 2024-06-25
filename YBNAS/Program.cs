// See https://aka.ms/new-console-template for more information
using Flurl.Http;
using NLog;
using System.Net;
using System.Text.Json.Nodes;
using YBNAS;
using System.Text.Json;
using CommandLine;
using Error = YBNAS.Error;
using System.Diagnostics;
using System.Text;

var asm = System.Reflection.Assembly.GetExecutingAssembly();
string appName = asm.GetName().Name!;
string appVer = asm.GetName().Version!.ToString();
string appTitle = $"{appName} v{appVer}";
string tempPath = Path.Combine(Path.GetTempPath(), appName);

Console.Title = appTitle;
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
Console.WriteLine($"{appTitle} 由 Hollis 编写，源代码、许可证、版本更新及项目说明见 https://github.com/bianyukun1213/YBNAS。");

DateTimeOffset curDateTime = DateTimeOffset.Now;
List<SignInTask> tasks = [];
Dictionary<string, int> retries = [];
Dictionary<string, long> lastSuccesses = [];

int tasksRunning = 0;
int tasksComplete = 0;
int tasksSkipped = 0;
int tasksWaiting = tasks.Count;
int tasksAborted = 0;

try
{
    logger.Debug("清理临时文件……");
    Directory.CreateDirectory(tempPath);
    DirectoryInfo dir = new(tempPath);
    FileInfo[] files = dir.GetFiles("result_*.json", SearchOption.TopDirectoryOnly);
    foreach (var item in files)
        File.Delete(item.FullName);
}
catch (Exception ex)
{
    logger.Warn(ex, "清理临时文件时出错。");
}

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
    JsonNode confRoot = JsonNode.Parse(configStr)!;
    Config.AutoSignIn = confRoot["AutoSignIn"].Deserialize<bool>();
    logger.Debug($"配置 AutoSignIn: {Config.AutoSignIn}。");
    Config.AutoExit = confRoot["AutoExit"].Deserialize<bool>();
    logger.Debug($"配置 AutoExit: {Config.AutoExit}。");
    Config.Execute = confRoot["Execute"].Deserialize<string>() ?? string.Empty;
    logger.Debug($"配置 Execute: {Config.Execute}。");
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
    if (Config.ExpireIn < 0 && Config.ExpireIn != -1)
    {
        logger.Warn("配置 ExpireIn 无效，将使用内置值 0。");
        Config.ExpireIn = 0;
    }
    logger.Debug($"配置 ExpireIn: {Config.ExpireIn}。");
    Config.SignInConfigs = confRoot["SignInConfigs"].Deserialize<List<SignInConfig>>() ?? [];
    if (Config.SignInConfigs.Count == 0)
    {
        logger.Warn("配置 SignInConfigs 为空。");
        PrintExitMsg();
        return -1;
    }
    foreach (SignInConfig conf in Config.SignInConfigs)
    {
        logger.Debug($"解析签到配置 {conf}……");
        string getSignInConfigSkippedStr(string reason)
        {
            string nameAndDesc = string.Empty;
            string name = conf.Name ?? string.Empty;
            string description = conf.Description ?? string.Empty;
            if (!string.IsNullOrEmpty(name.Trim()) && !string.IsNullOrEmpty(description.Trim()))
            {
                nameAndDesc = $"{name}，{description}";
            }
            else
            {
                if (!string.IsNullOrEmpty(name.Trim()))
                    nameAndDesc += name.Trim();
                if (!string.IsNullOrEmpty(description.Trim()))
                    nameAndDesc += description.Trim();
            }
            return $"第 {Config.SignInConfigs.IndexOf(conf) + 1} 条签到配置{(!string.IsNullOrEmpty(nameAndDesc) ? "（" + nameAndDesc + "）" : string.Empty)}{reason}，将跳过解析。";
        }
        if (!conf.Enable)
        {
            logger.Info(getSignInConfigSkippedStr("未启用"));
            continue;
        }
        if (string.IsNullOrEmpty(conf.Account?.Trim()))
        {
            logger.Warn(getSignInConfigSkippedStr("账号为空"));
            continue;
        }
        if (string.IsNullOrEmpty(conf.Password?.Trim()))
        {
            logger.Warn(getSignInConfigSkippedStr("密码为空"));
            continue;
        }
        if (conf.Position?.Length != 2)
        {
            logger.Warn(getSignInConfigSkippedStr("签到坐标格式错误"));
            continue;
        }
        if (string.IsNullOrEmpty(conf.Address?.Trim()))
        {
            logger.Warn(getSignInConfigSkippedStr("签到地址为空"));
            continue;
        }
        if (!string.IsNullOrEmpty(conf.Photo?.Trim()))
        {
            try
            {
                FileInfo fileInfo = new(conf.Photo);
                if ((!fileInfo.Extension.Equals(".jpg", StringComparison.CurrentCultureIgnoreCase) && !fileInfo.Extension.Equals(".jpeg", StringComparison.CurrentCultureIgnoreCase)) || fileInfo.Length == 0)
                {
                    logger.Warn(getSignInConfigSkippedStr("照片文件无效"));
                    continue;
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, getSignInConfigSkippedStr("照片文件读取失败"));
                continue;
            }
        }
        if (conf.TimeSpan?.Count != 4)
        {
            logger.Warn(getSignInConfigSkippedStr("签到时间段格式错误"));
            continue;
        }
        int confBegTime = conf.TimeSpan[0] * 60 + conf.TimeSpan[1];
        int confEndTime = conf.TimeSpan[2] * 60 + conf.TimeSpan[3];
        int curTime = curDateTime.Hour * 60 + curDateTime.Minute;
        if (curTime < confBegTime || curTime > confEndTime)
        {
            logger.Info(getSignInConfigSkippedStr("签到时间段不包含当前时间"));
            continue;
        }
        if (!lastSuccesses.TryGetValue(conf.Account, out long lastSuccess))
            lastSuccess = 0;
        //lastSuccess = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 100;
        SignInTask task = new(
            conf.Name ?? string.Empty,
            conf.Description ?? string.Empty,
            conf.Account,
            conf.Password,
            conf.Position,
            conf.Address,
            conf.Photo ?? string.Empty, // 此处 conf.Photo 可能为空。
            conf.Reason ?? string.Empty,
            conf.Outside,
            conf.TimeSpan[0],
            conf.TimeSpan[1],
            conf.TimeSpan[2],
            conf.TimeSpan[3],
            lastSuccess,
            conf.Device
            );
        tasks.Add(task);
        retries.Add(task.TaskId, 0);
        UpdateStatus();
    }
    if (Config.Shuffle)
    {
        Random rd = new();
        tasks = [.. tasks.OrderBy(task => rd.Next())];
    }
    logger.Info($"共 {Config.SignInConfigs.Count} 条签到配置，{tasks.Count} 条可用且已解析。");
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

if (!Config.AutoSignIn)
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
    tasksRunning = tasks.Count(x => x.Status == SignInTask.TaskStatus.Running);
    tasksComplete = tasks.Count(x => x.Status == SignInTask.TaskStatus.Complete);
    tasksSkipped = tasks.Count(x => x.Status == SignInTask.TaskStatus.Skipped);
    tasksWaiting = tasks.Count(x => x.Status == SignInTask.TaskStatus.Waiting);
    tasksAborted = tasks.Count(x => x.Status == SignInTask.TaskStatus.Aborted);
    Console.Title = $"{appTitle} | {Config.SignInConfigs.Count} 签到配置，{tasks.Count} 已解析：{tasksRunning} 运行，{tasksComplete} 完成，{tasksSkipped} 跳过，{tasksWaiting} 等待，{tasksAborted} 中止";
}

void RunNextTask()
{
    SignInTask? one = tasks.Find(x => x.Status == SignInTask.TaskStatus.Waiting);
    var res = one?.Run();
}

void St_OnRun(SignInTask task, Error err)
{
    UpdateStatus();
}

void St_OnComplete(SignInTask task, Error err)
{
    if (!lastSuccesses.TryAdd(task.Account, task.LastSuccess))
        lastSuccesses[task.Account] = task.LastSuccess;
    UpdateStatus();
    RunNextTask();
}

void St_OnSkip(SignInTask task, Error err)
{
    UpdateStatus();
    RunNextTask();
}

void St_OnError(SignInTask task, Error err)
{
    UpdateStatus();
    bool succ = retries.TryGetValue(task.TaskId, out int curRetries);
    if (!succ)
        return;
    string logPrefix = task.GetLogPrefix();
    if (logPrefix.EndsWith('）'))
        logPrefix += " ";
    if (curRetries < Config.MaxRetries)
    {
        logger.Warn($"{logPrefix}出错，将进行第 {++curRetries} 次重试。");
        retries[task.TaskId] = curRetries; // 在重试任务运行前增加重试次数。
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

string[] exec = Config.Execute.Split(' ');
if (!string.IsNullOrEmpty(exec[0]))
{
    try
    {
        string execArgs = string.Empty;
        if (exec.Length > 1)
        {
            string result = JsonSerializer.Serialize(tasks, ServiceOptions.jsonSerializerOptions);
            string resultTemp = string.Empty;
            if (Config.Execute.Contains("{%RESULT_TEMP%}"))
            {
                resultTemp = Path.Combine(tempPath, $"result_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.json");
                File.WriteAllText(resultTemp, result);
            }
            StringBuilder sb = new(execArgs);
            for (int i = 1; i < exec.Length; i++)
                sb.Append(exec[i].Replace("{%RESULT_TEMP%}", resultTemp)
                    .Replace("{%SIGN_IN_CONFIG_COUNT%}", Config.SignInConfigs.Count.ToString())
                    .Replace("{%TASK_COUNT%}", tasks.Count.ToString())
                    .Replace("{%TASKS_RUNNING%}", tasksRunning.ToString())
                    .Replace("{%TASKS_COMPLETE%}", tasksComplete.ToString())
                    .Replace("{%TASKS_SKIPPED%}", tasksSkipped.ToString())
                    .Replace("{%TASKS_WAITING%}", tasksWaiting.ToString())
                    .Replace("{%TASKS_ABORTED%}", tasksAborted.ToString()) + ' ');
            execArgs = sb.ToString().Trim();
        }
        logger.Debug($"运行程序：{exec[0]} {execArgs}。");
        Process process = new();
        ProcessStartInfo startInfo = new(exec[0], execArgs)
        {
            UseShellExecute = true
        };
        process.StartInfo = startInfo;
        process.Start();
        process.Close();
    }
    catch (Exception ex)
    {
        logger.Error(ex, "运行程序时出错。");
    }
}

logger.Info("已尝试运行所有任务。");
if (tasksAborted > 0)
{
    logger.Warn($"----中止（{tasksAborted}/{tasks.Count}）----");
    foreach (var item in tasks.FindAll(x => x.Status == SignInTask.TaskStatus.Aborted))
    {
        logger.Warn($"{item.GetLogPrefix()}：{item.StatusText}。");
    }
}
if (tasksSkipped > 0)
{
    logger.Info($"----跳过（{tasksSkipped}/{tasks.Count}）----");
    foreach (var item in tasks.FindAll(x => x.Status == SignInTask.TaskStatus.Skipped))
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
