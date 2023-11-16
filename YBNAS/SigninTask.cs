using com.github.xiangyuecn.rsacsharp;
using Flurl;
using Flurl.Http;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace YBNAS
{
    struct User
    {
        public string? UniversityName { get; set; }
        public string? UniversityId { get; set; }
        public string? PersonName { get; set; }
        public string PersonId { get; set; } // PersonId 总不该是 null 吧……
        public override readonly string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    struct Device
    {
        public string? Code { get; set; }
        public string? PhoneModel { get; set; }
        public override readonly string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    struct SigninInfo
    {
        public bool FromServer { get; set; }
        public int State { get; set; } // 不该是 null。
        public long BeginTime { get; set; } // 不该是 null。
        public long EndTime { get; set; } // 不该是 null。
        public bool ShouldSigninToday { get; set; } // 不该是 null。
        public override readonly string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    internal class SigninTask
    {
        public enum TaskStatus
        {
            Waiting,
            Running,
            Complete,
            Skipped,
            Aborted
        }

        private readonly string _taskGuid = Guid.NewGuid().ToString()[..4];
        private TaskStatus _status;

        private readonly string _account = "";
        private readonly string _password = "";
        private readonly string _position = "";
        private readonly string _address = "";
        private readonly int _beginHour = 0;
        private readonly int _beginMin = 0;
        private readonly int _endHour = 0;
        private readonly int _endMin = 0;

        private User _user;
        private Device _device;

        //private int _runCount = 0; 现在重试次数变为可配置项，需要更清晰地追踪重试次数。打算在 Program.cs 里实现。

        public delegate void RunHandler(SigninTask st);
        public event RunHandler? OnRun;
        public delegate void CompleteHandler(SigninTask st);
        public event CompleteHandler? OnComplete;
        public delegate void SkipHandler(SigninTask st);
        public event SkipHandler? OnSkip;
        public delegate void ErrorHandler(SigninTask st);
        public event ErrorHandler? OnError;

        private CookieJar _jar = new();
        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        public string TaskGuid { get { return _taskGuid; } }
        public TaskStatus Status { get { return _status; } }

        public string Account { get { return _account; } }
        //public string Password { get { return _password; } }
        public string Position { get { return _position; } }
        public string Address { get { return _address; } }
        public int BeginHour { get { return _beginHour; } }
        public int BeginMin { get { return _beginMin; } }
        public int EndHour { get { return _endHour; } }
        public int EndMin { get { return _endMin; } }

        public User User { get { return _user; } }
        public Device Device { get { return _device; } }

        //public int RunCount { get { return _runCount; } } 现在重试次数变为可配置项，需要更清晰地追踪重试次数。打算在 Program.cs 里实现。

        public SigninTask(string account, string password, string position, string address, int beginHour, int beginMin, int endHour, int endMin, Device device = new())
        {
            _logger.Debug($"任务 {_taskGuid} - 构造 SigninTask。");
            _account = account;
            _password = password;
            _position = position;
            _address = address;
            _beginHour = beginHour;
            _beginMin = beginMin;
            _endHour = endHour;
            _endMin = endMin;
            _user = new User();
            _device = device;
            _status = TaskStatus.Waiting;
        }

        private static int GetRandom(int minValue, int maxValue)
        {
            Random rd = new(Guid.NewGuid().GetHashCode()); // 用 GUID 作种子，高频调用也随机。
            return rd.Next(minValue, maxValue);
        }

        public async Task Run()
        {
            if (_status == TaskStatus.Running)
            {
                _logger.Warn($"任务 {_taskGuid} - 当前任务已在运行，忽略本次运行请求。");
                return;
            }
            try
            {
                //_runCount++;
                _status = TaskStatus.Running;
                _logger.Debug($"任务 {_taskGuid} - 开始运行。");
                OnRun?.Invoke(this);
                _jar = new CookieJar(); // 存 cookie。任务失败重试，使用新 cookie。
                _logger.Debug($"任务 {_taskGuid} - 新建 CookieJar。");
                string csrfToken = Guid.NewGuid().ToString("N");
                string rsaPubKey = await GetRsaPubKey();
                await Login(rsaPubKey);
                string vr = await GetVr();
                string userAgent = "yiban_android"; // 校本化应用需要的 UA，先使用 Android 端。
                _user = await Auth(vr, csrfToken, userAgent);
                if (string.IsNullOrEmpty(_user.PersonId)) // 校本化认证失败。
                {
                    _status = TaskStatus.Aborted;
                    OnError?.Invoke(this);
                    return;
                }
                _logger.Info($"任务 {_taskGuid} - 认证成功，{_user.PersonName}同学！");
                if (_user.UniversityName != "黑龙江科技大学")
                    if (string.IsNullOrEmpty(_user.UniversityName))
                        _logger.Warn($"任务 {_taskGuid} - {_user.PersonName}学校信息缺失，本程序可能不适用，不过我会试试。");
                    else
                        _logger.Warn($"任务 {_taskGuid} - 本程序可能不适用于{_user.PersonName}学校{_user.UniversityName}，不过我会试试。");
                if (string.IsNullOrEmpty(_device.Code) || string.IsNullOrEmpty(_device.PhoneModel))
                {
                    _logger.Info($"任务 {_taskGuid} - {_user.PersonName}未提供合适的设备信息，将从接口获取。");
                    _device = await GetDevice(csrfToken, userAgent); // 未提供合适的设备信息，从接口获取。
                    if (_device.PhoneModel != string.Empty && _device.Code != string.Empty)
                        _logger.Info($"任务 {_taskGuid} - {_user.PersonName}使用的是 {_device.PhoneModel}（{_device.Code}），好眼光！");
                    else
                        _logger.Info($"任务 {_taskGuid} - {_user.PersonName}设备信息缺失。Do you guys not have phones?");
                }
                if (!string.IsNullOrEmpty(_device.PhoneModel) && _device.PhoneModel.Contains("iPhone"))
                    userAgent = "yiban_iOS"; // 如果是 iPhone，让之后的请求携带 iOS 客户端的 UA。
                // 签到之前必须先获取签到信息，可能会设定 cookie，否则会判非法签到。
                SigninInfo info = await GetSigninInfo(csrfToken, userAgent);
                if (!info.FromServer)
                {
                    _status = TaskStatus.Aborted;
                    _logger.Error($"任务 {_taskGuid} - 运行出错。");
                    OnError?.Invoke(this);
                    return;
                }
                if (info.State == 3) // 表示已签到。
                {
                    _logger.Info($"任务 {_taskGuid} - {_user.PersonName}今天已签到，将跳过。");
                    _status = TaskStatus.Skipped;
                    _logger.Debug($"任务 {_taskGuid} - 跳过运行。");
                    OnSkip?.Invoke(this);
                    return;
                }
                if (info.State != 0) // 因为其他原因不适宜签到。已知请假审批通过可能会标为“无需签到”，此时再签就会提示非法签到。但我不知道“无需签到”具体的 State 值；毕竟我通常不请假，直接跑。
                {
                    _logger.Info($"任务 {_taskGuid} - {_user.PersonName}今天无需签到或无法签到，（签到信息 State 值为 {info.State}。）将跳过。");
                    _status = TaskStatus.Skipped;
                    _logger.Debug($"任务 {_taskGuid} - 跳过运行。");
                    OnSkip?.Invoke(this);
                    return;
                }
                long curTimeSeconds = DateTimeOffset.Now.ToUnixTimeSeconds();
                if (curTimeSeconds < info.BeginTime || curTimeSeconds > info.EndTime)
                {
                    _logger.Info($"任务 {_taskGuid} - 不在{_user.PersonName}学校{_user.UniversityName}要求的签到时间段内，将跳过。"); // 最好让用户一眼知道是哪个人在哪个学校因为未到时间签到失败。
                    _status = TaskStatus.Skipped;
                    _logger.Debug($"任务 {_taskGuid} - 跳过运行。");
                    OnSkip?.Invoke(this);
                    return;
                }
                // 延迟。
                if (Config.RandomDelay![0] != 0)
                {
                    int sec = Config.RandomDelay![0];
                    if (Config.RandomDelay![0] != Config.RandomDelay![1])
                        sec = GetRandom(Config.RandomDelay![0], Config.RandomDelay![1] + 1);
                    _logger.Info($"任务 {_taskGuid} - 延迟 {sec} 秒签到……");
                    await Task.Delay(sec * 1000);
                }
                bool signinStatus = await Signin(csrfToken, _device, userAgent);
                if (!signinStatus) // 签到失败。
                {
                    _status = TaskStatus.Aborted;
                    _logger.Error($"任务 {_taskGuid} - 运行出错。");
                    OnError?.Invoke(this);
                    return;
                }
                _logger.Info($"任务 {_taskGuid} - {_user.PersonName}签到成功！Have a safe day.");
                _status = TaskStatus.Complete;
                _logger.Debug($"任务 {_taskGuid} - 运行完成。");
                OnComplete?.Invoke(this);
            }
            catch (Exception ex)
            {
                _status = TaskStatus.Aborted;
                _logger.Error(ex, $"任务 {_taskGuid} - 运行出错。"); // NLog 推荐这样传递异常信息。
                OnError?.Invoke(this);
                //throw;
            }
        }

        private async Task<string> GetRsaPubKey()
        {
            _logger.Info($"任务 {_taskGuid} - 获取 RSA 加密公钥……");
            var reqGetRsaPubKey = "https://oauth.yiban.cn/" // 留出 BaseUrl，Flurl.Http 给相同域的请求复用同一个 HttpClient。
                .AppendPathSegment("code/html") // 在此附加路径。
                .SetQueryParams(new { client_id = "95626fa3080300ea" /* 不知道是啥，写死的。 */, redirect_uri = "https://f.yiban.cn/iapp7463" })
                .WithHeaders(new { Origin = "https://c.uyiban.com", User_Agent = "YiBan", AppVersion = "5.0" }) // User_Agent 会自动变成 User-Agent。 
                                                                                                                //.WithHeaders(new DefaultHeaders()) 把 header 提取成一个默认的结构体，行不通……抓包发现没有这些数据。
                .WithCookies(out _jar); // 存入 cookie，供以后的请求携带。
            _logger.Debug($"任务 {_taskGuid} - 发送请求：{reqGetRsaPubKey.Url}……");
            string rsaPubKeyContent = await reqGetRsaPubKey.GetStringAsync();
            _logger.Debug($"任务 {_taskGuid} - 收到响应：{rsaPubKeyContent}。");
            string keyBegPatt = "-----BEGIN PUBLIC KEY-----";
            string keyEndPatt = "-----END PUBLIC KEY-----";
            int keyBegPattPos = rsaPubKeyContent.IndexOf(keyBegPatt);
            int keyEndPattPos = rsaPubKeyContent.IndexOf(keyEndPatt);
            string rsaPubKey = rsaPubKeyContent.Substring(keyBegPattPos, keyEndPattPos - keyBegPattPos + keyEndPatt.Length);
            _logger.Debug($"任务 {_taskGuid} - 取得 RSA 加密公钥：{rsaPubKey}。");
            return rsaPubKey;
        }

        private async Task Login(string rsaPubKey)
        {
            _logger.Info($"任务 {_taskGuid} - 加密密码……");
            var pem = RSA_PEM.FromPEM(rsaPubKey);
            var rsa = new RSA_Util(pem);
            string pwdEncoded = rsa.Encode(_password);
            _logger.Info($"任务 {_taskGuid} - 登录……");
            var reqLogin = "https://oauth.yiban.cn/"
                .AppendPathSegment("code/usersure")
                .WithHeaders(new { Origin = "https://c.uyiban.com", User_Agent = "YiBan", AppVersion = "5.0" })
                .WithCookies(_jar);
            var loginBody = new { oauth_uname = _account, oauth_upwd = pwdEncoded, client_id = "95626fa3080300ea", redirect_uri = "https://f.yiban.cn/iapp7463" };
            _logger.Debug($"任务 {_taskGuid} - 发送请求：{reqLogin.Url}，loginBody：{JsonConvert.SerializeObject(loginBody)}……");
            string loginContent = await reqLogin.PostUrlEncodedAsync(loginBody).ReceiveString();
            _logger.Debug($"任务 {_taskGuid} - 收到响应：{loginContent}。");
        }

        private async Task<string> GetVr()
        {
            _logger.Info($"任务 {_taskGuid} - 获取认证参数……");
            var reqGetVr = "https://f.yiban.cn/"
                .AppendPathSegment("iframe/index")
                .SetQueryParams(new { act = "iapp7463" })
                .WithHeaders(new { Origin = "https://c.uyiban.com", User_Agent = "YiBan", AppVersion = "5.0" })
                .WithCookies(_jar);
            _logger.Debug($"任务 {_taskGuid} - 发送请求：{reqGetVr.Url}……");
            var resGetVr = await reqGetVr.WithAutoRedirect(false).GetAsync(); // 不要重定向，以便从响应头读取 verify_request。
            _logger.Debug($"任务 {_taskGuid} - 收到响应。");
            string vr = "";
            foreach (var (name, value) in resGetVr.Headers)
            {
                if (name == "Location") // 在响应头的 Location 里找 verify_request。
                {
                    // 不知道为啥，调用 Flurl 解析 url 不好使。
                    //var location = new Url(value);
                    //vr = (string)location.QueryParams.FirstOrDefault("verify_request");
                    string location = value;
                    string vrBegPatt = "verify_request=";
                    string vrEndPatt = "&yb_uid";
                    int vrBegPattPos = location.IndexOf(vrBegPatt);
                    int vrEndPattPos = location.IndexOf(vrEndPatt);
                    vr = location[(vrBegPattPos + vrBegPatt.Length)..vrEndPattPos];
                    break;
                }
            }
            _logger.Debug($"任务 {_taskGuid} - 提取的认证参数（verify_request）：{vr}。");
            return vr;
        }

        private async Task<User> Auth(string vr, string csrfToken, string userAgent)
        {
            _logger.Info($"任务 {_taskGuid} - 进行校本化认证……");
            var reqAuth = "https://api.uyiban.com/"
                .AppendPathSegment("base/c/auth/yiban")
                .SetQueryParams(new { verifyRequest = vr, CSRF = csrfToken })
                .WithHeaders(new { Origin = "https://c.uyiban.com" /* 认证 origin 是 c…… */, User_Agent = userAgent /* 认证 UA 包含 yiban_android。 */, AppVersion = "5.0", Cookie = $"csrf_token={csrfToken}" }) // 还需在 cookie 中提供 csrf_token。
                .WithCookies(_jar);
            _logger.Debug($"任务 {_taskGuid} - 发送请求：{reqAuth.Url}……");
            var authContent = await reqAuth.GetStringAsync();
            _logger.Debug($"任务 {_taskGuid} - 收到响应：{authContent}");
            JsonNode authResNode = JsonNode.Parse(authContent!)!;
            int authResCode = authResNode["code"].Deserialize<int>();
            string authResMsg = authResNode["msg"].Deserialize<string>()!;
            if (authResCode != 0)
            {
                _logger.Error($"任务 {_taskGuid} - 校本化认证失败，服务端返回消息：{authResMsg}。");
                return new User();
            }
            JsonNode authResData = authResNode["data"]!;
            User user = new()
            {
                UniversityName = authResData["UniversityName"].Deserialize<string>(),
                UniversityId = authResData["UniversityId"].Deserialize<string>(),
                PersonName = authResData["PersonName"].Deserialize<string>(),
                PersonId = authResData["PersonId"].Deserialize<string>()!
            };
            _logger.Debug($"任务 {_taskGuid} - 解析出用户信息：{user}。");
            return user;
        }

        private async Task<Device> GetDevice(string csrfToken, string userAgent)
        {
            _logger.Info($"任务 {_taskGuid} - 获取授权设备……");
            var reqGetDevice = "https://api.uyiban.com/"
                .AppendPathSegment("device/student/index/getState")
                .SetQueryParams(new { CSRF = csrfToken })
                .WithHeaders(new { Origin = "https://app.uyiban.com" /* 获取设备 origin 是 app…… */, User_Agent = userAgent /* 获取设备 UA 包含 yiban_android，如果是 iOS，则为 yiban_iOS。 */, AppVersion = "5.0", Cookie = $"csrf_token={csrfToken}" }) // 还需在 cookie 中提供 csrf_token。
                .WithCookies(_jar);
            _logger.Debug($"任务 {_taskGuid} - 发送请求：{reqGetDevice.Url}……");
            var deviceContent = await reqGetDevice.GetStringAsync();
            _logger.Debug($"任务 {_taskGuid} - 收到响应：{deviceContent}");
            JsonNode deviceResNode = JsonNode.Parse(deviceContent!)!;
            int deviceResCode = deviceResNode["code"].Deserialize<int>();
            string deviceResMsg = deviceResNode["msg"].Deserialize<string>()!;
            if (deviceResCode != 0)
            {
                _logger.Error($"任务 {_taskGuid} - 获取授权设备失败，服务端返回消息：{deviceResMsg}。");
                return new Device();
            }
            JsonNode deviceResData = deviceResNode["data"]!;
            Device device = new()
            {
                Code = deviceResData["Code"].Deserialize<string>(),
                PhoneModel = deviceResData["PhoneModel"].Deserialize<string>()
            };
            _logger.Debug($"任务 {_taskGuid} - 解析出授权设备：{device}。");
            return device;
        }

        private async Task<SigninInfo> GetSigninInfo(string csrfToken, string userAgent)
        {
            _logger.Info($"任务 {_taskGuid} - 获取签到信息……");
            var reqSigninInfo = "https://api.uyiban.com/"
                .AppendPathSegment("nightAttendance/student/index/signPosition")
                .SetQueryParams(new { CSRF = csrfToken })
                .WithHeaders(new { Origin = "https://app.uyiban.com" /* 获取签到信息 origin 是 app…… */, User_Agent = userAgent /* 获取签到信息 UA 包含 yiban_android，如果是 iOS，则为 yiban_iOS。 */, AppVersion = "5.0", Cookie = $"csrf_token={csrfToken}" }) // 还需在 cookie 中提供 csrf_token。
                .WithCookies(_jar);
            _logger.Debug($"任务 {_taskGuid} - 发送请求：{reqSigninInfo.Url}……");
            var infoContent = await reqSigninInfo.GetStringAsync();
            _logger.Debug($"任务 {_taskGuid} - 收到响应：{infoContent}");
            JsonNode infoResNode = JsonNode.Parse(infoContent!)!;
            int infoResCode = infoResNode["code"].Deserialize<int>();
            string infoResMsg = infoResNode["msg"].Deserialize<string>()!;
            if (infoResCode != 0)
            {
                _logger.Error($"任务 {_taskGuid} - 获取签到信息失败，服务端返回消息：{infoResMsg}。");
                return new();
            }
            JsonNode infoResData = infoResNode["data"]!;
            SigninInfo signinInfo = new()
            {
                FromServer = true /* 标记已从服务器获取到签到信息，区别于默认的签到信息。 */,
                State = infoResData["State"].Deserialize<int>(),
                BeginTime = infoResData["Range"]!["StartTime"].Deserialize<long>(),
                EndTime = infoResData["Range"]!["EndTime"].Deserialize<long>(),
                ShouldSigninToday = infoResData["Range"]!["SignDay"].Deserialize<int>() != 0
            };
            _logger.Debug($"任务 {_taskGuid} - 解析出签到信息：{signinInfo}。");
            return signinInfo;
        }

        private async Task<bool> Signin(string csrfToken, Device device, string userAgent)
        {
            _logger.Info($"任务 {_taskGuid} - 晚点签到，启动！");
            var reqSignin = "https://api.uyiban.com/"
                .AppendPathSegment("nightAttendance/student/index/signIn")
                .SetQueryParams(new { CSRF = csrfToken })
                .WithHeaders(new { Origin = "https://app.uyiban.com" /* 签到 origin 是 app…… */, User_Agent = userAgent /* 签到 UA 包含 yiban_android，如果是 iOS，则为 yiban_iOS。 */, AppVersion = "5.0", Cookie = $"csrf_token={csrfToken}" }) // 还需在 cookie 中提供 csrf_token。
                .WithCookies(_jar);
            var signinBody = new { OutState = "1", device.Code, device.PhoneModel /* 经测试只要 PhoneModel 对上即可。 */, SignInfo = JsonConvert.SerializeObject(new { Reason = "", AttachmentFileName = "", LngLat = _position, Address = _address }) }; // SignInfo 是字符串。
            _logger.Debug($"任务 {_taskGuid} - 发送请求：{reqSignin.Url}，SigninBody：{JsonConvert.SerializeObject(signinBody)}……");
            string signinContent = await reqSignin.PostUrlEncodedAsync(signinBody).ReceiveString();
            _logger.Debug($"任务 {_taskGuid} - 收到响应：{signinContent}。");
            JsonNode signinResNode = JsonNode.Parse(signinContent!)!;
            int signinResCode = signinResNode["code"].Deserialize<int>();
            string signinResMsg = signinResNode["msg"].Deserialize<string>()!;
            if (signinResCode != 0)
            {
                _logger.Error($"任务 {_taskGuid} - 签到失败，服务端返回消息：{signinResMsg}。");
                return false;
            }
            return true;
        }
    }
}
