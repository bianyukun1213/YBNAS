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
using System.Text.Json;
using Flurl.Util;

var asm = System.Reflection.Assembly.GetExecutingAssembly();
string appVer = $"{asm.GetName().Name} v{asm.GetName().Version}";

Logger logger = NLog.LogManager.GetCurrentClassLogger(); // NLog 推荐 logger 声明成 static 的，不过这里不行。
logger.Info($"程序启动。");
logger.Info($"{appVer} 由 Hollis 编写，源代码及版本更新见 https://github.com/bianyukun1213/YBNAS。");

DateTime curDateTime = DateTime.Now;
List<SigninTask> tasks = [];
Dictionary<string, int> retries = [];
bool allTasksDone = false;

try
{
    string configPath = Path.GetDirectoryName(asm.Location)! + @"/config.json";
    if (!File.Exists(configPath))
    {
        logger.Fatal($"配置文件不存在。");
        PrintExitMsg();
        return -1;
    }
    string configStr = File.ReadAllText(configPath);
    logger.Debug($"解析配置字符串……");
    JsonNode confRoot = JsonNode.Parse(configStr)!;
    Config.AutoExit = confRoot["AutoExit"].Deserialize<bool>();
    logger.Debug($"配置 AutoExit: {Config.AutoExit}。");
    Config.MaxRunningTasks = confRoot["MaxRunningTasks"].Deserialize<int>();
    if (Config.MaxRunningTasks < 1)
    {
        logger.Warn($"配置 MaxRunningTasks 不应小于 1，将使用内置值 4。");
        Config.MaxRunningTasks = 4;
    }
    logger.Debug($"配置 MaxRunningTasks: {Config.MaxRunningTasks}。");
    Config.MaxRetries = confRoot["MaxRetries"].Deserialize<int>();
    if (Config.MaxRetries < 0)
    {
        logger.Warn($"配置 MaxRetries 不应小于 0，将使用内置值 3。");
        Config.MaxRetries = 3;
    }
    logger.Debug($"配置 MaxRetry: {Config.MaxRetries}。");
    Config.RandomDelay = confRoot["RandomDelay"].Deserialize<List<int>>();
    if (Config.RandomDelay == null ||
        Config.RandomDelay.Count != 2 ||
        Config.RandomDelay[0] < 0 ||
        Config.RandomDelay[1] < 0 ||
        Config.RandomDelay[0] > 120 ||
        Config.RandomDelay[1] > 120 ||
        (Config.RandomDelay[0] == 0 && Config.RandomDelay[1] != 0) ||
        Config.RandomDelay[0] > Config.RandomDelay[1])
    {
        logger.Warn($"配置 RandomDelay 无效，将使用内置值 [1, 10]。");
        Config.RandomDelay = [1, 10];
    }
    logger.Debug($"配置 RandomDelay: [{Config.RandomDelay[0]}, {Config.RandomDelay[1]}]。");
    Config.SigninConfigs = confRoot["SigninConfigs"].Deserialize<List<SigninConfig>>()!;
    if (Config.SigninConfigs == null)
    {
        logger.Fatal($"配置 SigninConfigs 为空。");
        PrintExitMsg();
        return -1;
    }
    foreach (SigninConfig conf in Config.SigninConfigs)
    {

        SigninConfig tempSc = JsonConvert.DeserializeObject<SigninConfig>(JsonConvert.SerializeObject(conf))!;
        tempSc.Password = "<已抹除>";
        logger.Debug($"解析签到配置 {tempSc}……"); // 在日志中抹除密码。
        if (string.IsNullOrEmpty(conf.Account))
            continue;
        if (string.IsNullOrEmpty(conf.Password))
            continue;
        if (conf.Position!.Count != 2)
            continue;
        if (string.IsNullOrEmpty(conf.Address))
            continue;
        if (conf.TimeSpan!.Count != 4)
            continue;
        int confBegTime = conf.TimeSpan![0] * 60 + conf.TimeSpan![1];
        int confEndTime = conf.TimeSpan![2] * 60 + conf.TimeSpan![3];
        int curTime = curDateTime.Hour * 60 + curDateTime.Minute;
        if (curTime < confBegTime || curTime > confEndTime)
            continue;
        SigninTask task = new(
            conf.Account is null ? "" : conf.Account,
            conf.Password is null ? "" : conf.Password,
            $"{conf.Position![0]},{conf.Position![1]}",
            conf.Address is null ? "" : conf.Address,
            conf.TimeSpan![0],
            conf.TimeSpan![1],
            conf.TimeSpan![2],
            conf.TimeSpan![3],
            conf.Device
            );
        tasks.Add(task);
        retries.Add(task.TaskGuid, 0);
    }
    logger.Info($"共 {Config.SigninConfigs.Count} 条签到配置，{tasks.Count} 条可用且已解析。");
    if (tasks.Count == 0)
    {
        logger.Warn($"当前时间下无可用签到配置。");
        PrintExitMsg();
        return 0;
    }
}
catch (Exception ex)
{
    logger.Fatal(ex, $"解析配置文件时出错。");
    PrintExitMsg();
    return -1;
    //throw;
}

int tasksRunning = 0;
int tasksComplete = 0;
int tasksSkipped = 0;
int tasksWaiting = tasks.Count;
int tasksAborted = 0;

UpdateStatus();

foreach (var item in tasks)
{
    item.OnRun += St_OnRun;
    item.OnComplete += St_OnComplete;
    item.OnSkip += St_OnComplete; // 目前跳过和完成同样处理。
    item.OnError += St_OnError;
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
    Console.Title = $"{appVer} | {tasksRunning} 运行，{tasksComplete} 完成，{tasksSkipped} 跳过，{tasksWaiting} 等待，{tasksAborted} 中止";
    if (tasksRunning == 0 && tasksWaiting == 0)
        allTasksDone = true;
}

void RunNextTask()
{
    SigninTask? one = tasks.Find(x => x.Status == SigninTask.TaskStatus.Waiting);
    var res = one?.Run();
}

void St_OnRun(SigninTask task)
{
    UpdateStatus();
}

void St_OnComplete(SigninTask task)
{
    UpdateStatus();
    RunNextTask();
}

void St_OnError(SigninTask task)
{
    UpdateStatus();
    bool succ = retries.TryGetValue(task.TaskGuid, out int curRetries);
    if (!succ)
        return;
    if (curRetries < Config.MaxRetries)
    {
        logger.Warn($"任务 {task.TaskGuid} 出错，将进行第 {++curRetries} 次重试。");
        var res = task.Run();
        retries[task.TaskGuid] = curRetries;
    }
    else
    {
        if (Config.MaxRetries == 0)
            logger.Warn($"任务 {task.TaskGuid} 出错且未开启重试，将运行下一项任务。");
        else
            logger.Warn($"任务 {task.TaskGuid} 在 {curRetries} 次重试后再次出错，将运行下一项任务。");
        RunNextTask();
    }
}

void PrintExitMsg()
{
    if (!Config.AutoExit)
    {
        logger.Info($"按任意键退出。");
        Console.ReadKey();
    }
    logger.Debug($"即将退出。");
}

while (!allTasksDone)
{
    await Task.Delay(1000);
}

logger.Info($"已完成所有任务的执行。");
PrintExitMsg();
return 0;
