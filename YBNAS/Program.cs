// See https://aka.ms/new-console-template for more information
using com.github.xiangyuecn.rsacsharp;
using Flurl;
using Flurl.Http;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using NLog;
using System;
using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using YBNAS;

var asm = System.Reflection.Assembly.GetExecutingAssembly();
string appVer = $"{asm.GetName().Name} v{asm.GetName().Version}";

Logger logger = NLog.LogManager.GetCurrentClassLogger(); // NLog 推荐 logger 声明成 static 的，不过这里不行。
logger.Info("程序启动。");
logger.Info($"{appVer} 由 Hollis 编写，源代码及版本更新见 https://github.com/bianyukun1213/YBNAS。");

DateTime curDateTime = DateTime.Now;
List<SingleConfig> confs;
List<SigninTask> tasks = new();

try
{
    string configPath = Path.GetDirectoryName(asm.Location)! + @"/config.json";
    //if (!File.Exists(configPath))
    //{
    //    logger.Error("配置文件不存在。");
    //    return;
    //}
    string configStr = File.ReadAllText(configPath);
    confs = JsonConvert.DeserializeObject<List<SingleConfig>>(configStr)!;
    foreach (SingleConfig config in confs)
    {
        logger.Debug($"解析配置 {config}……");
        if (string.IsNullOrEmpty(config.Account))
            continue;
        if (string.IsNullOrEmpty(config.Password))
            continue;
        if (config.Position!.Count != 2)
            continue;
        if (string.IsNullOrEmpty(config.Address))
            continue;
        if (config.TimeSpan!.Count != 4)
            continue;
        int confBegTime = config.TimeSpan![0] * 60 + config.TimeSpan![1];
        int confEndTime = config.TimeSpan![2] * 60 + config.TimeSpan![3];
        int curTime = curDateTime.Hour * 60 + curDateTime.Minute;
        if (curTime < confBegTime || curTime > confEndTime)
            continue;
        tasks.Add(new(
            config.Account is null ? "" : config.Account,
            config.Password is null ? "" : config.Password,
            $"{config.Position![0]},{config.Position![1]}",
            config.Address is null ? "" : config.Address,
            config.TimeSpan![0],
            config.TimeSpan![1],
            config.TimeSpan![2],
            config.TimeSpan![3],
            config.Device
            ));
    }
    logger.Info($"共 {confs.Count} 条配置，{tasks.Count} 条可用且已解析。");
    if (tasks.Count == 0)
    {
        logger.Warn($"当前时间下无可用配置。");
    }
}
catch (Exception ex)
{
    logger.Error(ex, $"解析配置文件时出错。");
    return;
    //throw;
}

int tasksRunning = 0;
int tasksComplete = 0;
int tasksWaiting = tasks.Count;
int tasksAborted = 0;

UpdateCount();

foreach (var item in tasks)
{
    item.OnRun += St_OnRun;
    item.OnComplete += St_OnComplete;
    item.OnError += St_OnError;
}

for (int i = 0; i < tasks.Count; i++)
{
    if (i > 3)
    {
        break;
    }
    var res = tasks[i].Run(); // 消除 CS4014 警告，https://learn.microsoft.com/zh-cn/dotnet/csharp/language-reference/compiler-messages/cs4014。
}

void UpdateCount()
{
    tasksRunning = tasks.Count(x => x.Status == SigninTask.TaskStatus.Running);
    tasksComplete = tasks.Count(x => x.Status == SigninTask.TaskStatus.Complete);
    tasksWaiting = tasks.Count(x => x.Status == SigninTask.TaskStatus.Waiting);
    tasksAborted = tasks.Count(x => x.Status == SigninTask.TaskStatus.Aborted);
    Console.Title = $"{appVer} | {tasksRunning} 运行，{tasksComplete} 完成，{tasksWaiting} 等待，{tasksAborted} 中止";
}

void RunNextTask()
{
    SigninTask? one = tasks.Find(x => x.Status == SigninTask.TaskStatus.Waiting);
    var res = one?.Run();
}

void St_OnRun(SigninTask task)
{
    UpdateCount();
}

void St_OnComplete(SigninTask task)
{
    UpdateCount();
    RunNextTask();
}

void St_OnError(SigninTask task)
{
    UpdateCount();
    if (task.RunCount < 2)
    {
        logger.Warn($"任务 {task.TaskGuid} 出错，将重试。");
        var res = task.Run();
    }
    else
    {
        logger.Warn($"任务 {task.TaskGuid} 出错且已多次运行，将跳过。");
        RunNextTask();
    }
}

Console.ReadLine();
