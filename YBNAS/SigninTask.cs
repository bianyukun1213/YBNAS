﻿using com.github.xiangyuecn.rsacsharp;
using Flurl;
using Flurl.Http;
using Flurl.Http.Content;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace YBNAS
{
    internal enum Error
    {
        Unknown = -1,
        Ok,
        LoginFailed,
        VrInvalid,
        UserInvalid,
        SigninInfoInvalid,
        SigninFailed
    }

    internal struct User
    {
        public string? UniversityName { get; set; } // User 是服务器返回的结果，不知道这些值是否能是空。
        public string? UniversityId { get; set; }
        public string? PersonName { get; set; }
        public string PersonId { get; set; } // PersonId 总不该是 null 吧……
        public override readonly string ToString()
        {
            return JsonSerializer.Serialize(this, ServiceOptions.jsonSerializerOptions);
        }
    }

    internal struct Device
    {
        public string? Code { get; set; } // Device 是服务器返回的结果，不知道这些值是否能是空。
        public string? PhoneModel { get; set; }
        public override readonly string ToString()
        {
            return JsonSerializer.Serialize(this, ServiceOptions.jsonSerializerOptions);
        }
    }

    internal struct SigninPhotoInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public long Size { get; set; }
        public override readonly string ToString()
        {
            return JsonSerializer.Serialize(this, ServiceOptions.jsonSerializerOptions);
        }
    }

    internal struct SigninInfo
    {
        public bool IsServerRes { get; set; }
        public int State { get; set; } // 不该是 null。
        public long BeginTime { get; set; } // 不该是 null。
        public long EndTime { get; set; } // 不该是 null。
        public bool ShouldSigninToday { get; set; } // 不该是 null。
        public override readonly string ToString()
        {
            return JsonSerializer.Serialize(this, ServiceOptions.jsonSerializerOptions);
        }
    }

    internal struct YibanNightAttendanceSigninApiRes
    {
        public int Code { get; set; }
        public string Msg { get; set; }
        public JsonNode Data { get; set; }
        public override readonly string ToString()
        {
            return JsonSerializer.Serialize(this, ServiceOptions.jsonSerializerOptions);
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
        private string _statusText;

        private string _name = "";
        private readonly string _account = "";
        private readonly string _password = "";
        private readonly List<double> _position = [0.0, 0.0];
        private readonly string _address = "";
        private readonly string _photo = "";
        private readonly string _reason = "";
        private readonly int _beginHour = 0;
        private readonly int _beginMin = 0;
        private readonly int _endHour = 0;
        private readonly int _endMin = 0;

        private User _user;
        private Device _device;

        //private int _runCount = 0; 现在重试次数变为可配置项，需要更清晰地追踪重试次数。打算在 Program.cs 里实现。

        public delegate void RunHandler(SigninTask st, Error err);
        public event RunHandler? OnRun;
        public delegate void CompleteHandler(SigninTask st, Error err);
        public event CompleteHandler? OnComplete;
        public delegate void SkipHandler(SigninTask st, Error err);
        public event SkipHandler? OnSkip;
        public delegate void ErrorHandler(SigninTask st, Error err);
        public event ErrorHandler? OnError;

        public CookieJar _jar = new();
        public static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        public string TaskGuid { get { return _taskGuid; } }
        public TaskStatus Status { get { return _status; } }
        public string StatusText { get { return _statusText; } }

        public string Name { get { return _name; } }
        public string Account { get { return _account; } }
        public string Password { get { return _password; } }
        public List<double> Position { get { return _position; } }
        public string Address { get { return _address; } }
        public string Photo { get { return _photo; } }
        public string Reason { get { return _reason; } }
        public int BeginHour { get { return _beginHour; } }
        public int BeginMin { get { return _beginMin; } }
        public int EndHour { get { return _endHour; } }
        public int EndMin { get { return _endMin; } }

        public User User { get { return _user; } }
        public Device Device { get { return _device; } }

        //public int RunCount { get { return _runCount; } } 现在重试次数变为可配置项，需要更清晰地追踪重试次数。打算在 Program.cs 里实现。

        public SigninTask(string name, string account, string password, List<double> position, string address, string photo, string reason, int beginHour, int beginMin, int endHour, int endMin, Device device = new())
        {
            _name = name;
            _account = account;
            _password = password;
            if (position.Count == 2)
                _position = position;
            _address = address;
            _photo = photo;
            _reason = reason;
            _beginHour = beginHour;
            _beginMin = beginMin;
            _endHour = endHour;
            _endMin = endMin;
            _user = new User();
            _device = device;
            _status = TaskStatus.Waiting;
            _statusText = "等待";
            _logger.Debug($"{GetLogPrefix()}：构造完成。");
        }

        public string GetLogPrefix()
        {
            return "任务 " + _taskGuid + (!string.IsNullOrEmpty(_name.Trim()) ? "（" + _name + "）" : "");
        }

        private static int GetRandom(int minValue, int maxValue)
        {
            Random rd = new(Guid.NewGuid().GetHashCode()); // 用 GUID 作种子，高频调用也随机。
            return rd.Next(minValue, maxValue);
        }

        private static YibanNightAttendanceSigninApiRes ParseYibanNightAttendanceSigninApiRes(JsonNode node)
        {
            int code = node["code"].Deserialize<int>();
            string msg = node["msg"].Deserialize<string>()!;
            JsonNode data = node["data"]!;
            return new YibanNightAttendanceSigninApiRes { Code = code, Msg = msg, Data = data };
        }

        public async Task Run()
        {
            if (_status == TaskStatus.Running)
            {
                _logger.Warn($"{GetLogPrefix()}：当前任务已在运行，忽略本次运行请求。");
                return;
            }
            try
            {
                //_runCount++;
                _status = TaskStatus.Running;
                _statusText = "正在运行";
                _logger.Debug($"{GetLogPrefix()}：开始运行。");
                OnRun?.Invoke(this, Error.Ok);
                _jar = new CookieJar(); // 存 cookie。任务失败重试，使用新 cookie。
                _logger.Debug($"{GetLogPrefix()}：新建 CookieJar。");
                string csrfToken = Guid.NewGuid().ToString("N");
                string rsaPubKey = await GetRsaPubKey();
                bool loginSucceeded = await Login(rsaPubKey);
                if (!loginSucceeded)
                {
                    _status = TaskStatus.Aborted;
                    _statusText = "登录失败";
                    //_logger.Error($"任务 {_taskGuid}：运行出错。");
                    OnError?.Invoke(this, Error.LoginFailed);
                    return;
                }
                string vr = await GetVr();
                if (string.IsNullOrEmpty(vr))
                {
                    _status = TaskStatus.Aborted;
                    _statusText = "获取认证参数失败";
                    //_logger.Error($"任务 {_taskGuid}：运行出错。");
                    OnError?.Invoke(this, Error.VrInvalid);
                    return;
                }
                string userAgent = "yiban_android"; // 校本化应用需要的 UA，先使用 Android 端。
                _user = await Auth(vr, csrfToken, userAgent);
                if (string.IsNullOrEmpty(_user.PersonId)) // 校本化认证失败。
                {
                    _status = TaskStatus.Aborted;
                    _statusText = "校本化认证失败";
                    //_logger.Error($"任务 {_taskGuid}：运行出错。");
                    OnError?.Invoke(this, Error.UserInvalid);
                    return;
                }
                _name = string.IsNullOrEmpty(_name.Trim()) ? (string.IsNullOrEmpty(_user.PersonName) ? "" : _user.PersonName) : _name;
                _logger.Info($"{GetLogPrefix()}：认证成功。");
                if (_user.UniversityName != "黑龙江科技大学")
                    if (string.IsNullOrEmpty(_user.UniversityName))
                        _logger.Warn($"{GetLogPrefix()}：学校信息缺失，本程序可能不适用，不过我会试试。");
                    else
                        _logger.Warn($"{GetLogPrefix()}：本程序可能不适用于{_name}学校{_user.UniversityName}，不过我会试试。");
                if (string.IsNullOrEmpty(_device.Code) || string.IsNullOrEmpty(_device.PhoneModel))
                {
                    _logger.Info($"{GetLogPrefix()}：未提供合适的设备信息，将从接口获取。");
                    _device = await GetDevice(csrfToken, userAgent); // 未提供合适的设备信息，从接口获取。
                    if (_device.PhoneModel != string.Empty && _device.Code != string.Empty)
                        _logger.Info($"{GetLogPrefix()}：绑定的设备是 {_device.PhoneModel}（{_device.Code}）。");
                    else
                        _logger.Info($"{GetLogPrefix()}：设备信息缺失。Do you guys not have phones? 可能会签到失败，不过我会试试。");
                }
                if (!string.IsNullOrEmpty(_device.PhoneModel) && _device.PhoneModel.Contains("iPhone"))
                    userAgent = "yiban_iOS"; // 如果是 iPhone，让之后的请求携带 iOS 客户端的 UA。
                // 签到之前必须先获取签到信息，可能会设定 cookie，否则会判非法签到。
                SigninInfo info = await GetSigninInfo(csrfToken, userAgent);
                if (!info.IsServerRes)
                {
                    _status = TaskStatus.Aborted;
                    _statusText = "获取签到信息失败";
                    //_logger.Error($"任务 {_taskGuid}：运行出错。");
                    OnError?.Invoke(this, Error.SigninInfoInvalid);
                    return;
                }
                if (info.State == 3) // 表示已签到。
                {
                    _logger.Info($"{GetLogPrefix()}：今天已签到，将跳过。");
                    _status = TaskStatus.Skipped;
                    _statusText = "今天已签到";
                    _logger.Debug($"{GetLogPrefix()}：跳过运行。");
                    OnSkip?.Invoke(this, Error.Ok);
                    return;
                }
                if (info.State == 2) // 无需签到，可能已请假。
                {
                    _logger.Info($"{GetLogPrefix()}：今天无需签到，将跳过。");
                    _status = TaskStatus.Skipped;
                    _statusText = "今天无需签到";
                    _logger.Debug($"{GetLogPrefix()}：跳过运行。");
                    OnSkip?.Invoke(this, Error.Ok);
                    return;
                }
                if (info.State == 1)
                {
                    _logger.Info($"{GetLogPrefix()}：不在学校要求的签到时间段内，将跳过。"); // 最好让用户一眼知道是哪个人在哪个学校因为未到时间签到失败。
                    _status = TaskStatus.Skipped;
                    _statusText = "未到签到时间";
                    _logger.Debug($"{GetLogPrefix()}：跳过运行。");
                    OnSkip?.Invoke(this, Error.Ok);
                    return;
                }
                if (info.State != 0) // 因为其他原因不适宜签到。
                {
                    _logger.Info($"{GetLogPrefix()}：今天无需签到或无法签到，（签到信息 State 值为 {info.State}。）将跳过。");
                    _status = TaskStatus.Skipped;
                    _statusText = "今天无需或无法签到";
                    _logger.Debug($"{GetLogPrefix()}：跳过运行。");
                    OnSkip?.Invoke(this, Error.Ok);
                    return;
                }
                // 延迟。
                if (Config.RandomDelay![0] != 0)
                {
                    int sec = Config.RandomDelay![0];
                    if (Config.RandomDelay![0] != Config.RandomDelay![1])
                        sec = GetRandom(Config.RandomDelay![0], Config.RandomDelay![1] + 1);
                    _logger.Info($"{GetLogPrefix()}：延迟 {sec} 秒签到……");
                    await Task.Delay(sec * 1000);
                }

                SigninPhotoInfo signinPhotoInfo = new SigninPhotoInfo();
                if (!string.IsNullOrEmpty(_photo))
                {
                    FileInfo photoFileInfo = new(_photo);
                    signinPhotoInfo.Name = $"yiban_camera_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.jpg";
                    signinPhotoInfo.Type = "image/jpeg";
                    signinPhotoInfo.Size = photoFileInfo.Length;
                }
                bool signinStatus = await Signin(csrfToken, _device, signinPhotoInfo, userAgent);
                if (!signinStatus) // 签到失败。
                {
                    _status = TaskStatus.Aborted;
                    _statusText = "签到失败";
                    //_logger.Error($"任务 {_taskGuid}：运行出错。");
                    OnError?.Invoke(this, Error.SigninFailed);
                    return;
                }
                _logger.Info($"{GetLogPrefix()}：签到成功！Have a safe day.");
                _status = TaskStatus.Complete;
                _statusText = "完成";
                _logger.Debug($"{GetLogPrefix()}：运行完成。");
                OnComplete?.Invoke(this, Error.Ok);
            }
            catch (Exception ex)
            {
                _status = TaskStatus.Aborted;
                _statusText = "运行出错";
                _logger.Error(ex, $"{GetLogPrefix()}：运行出错。"); // NLog 推荐这样传递异常信息。
                OnError?.Invoke(this, Error.Unknown);
                //throw;
            }
        }

        private async Task<string> GetRsaPubKey()
        {
            _logger.Info($"{GetLogPrefix()}：获取 RSA 加密公钥……");
            var reqGetRsaPubKey = "https://oauth.yiban.cn/" // 留出 BaseUrl，Flurl.Http 给相同域的请求复用同一个 HttpClient。
                .AppendPathSegment("code/html") // 在此附加路径。
                .SetQueryParams(new { client_id = "95626fa3080300ea" /* 不知道是啥，写死的。 */, redirect_uri = "https://f.yiban.cn/iapp7463" })
                .WithHeaders(new { Origin = "https://c.uyiban.com", User_Agent = "YiBan", AppVersion = "5.0" }) // User_Agent 会自动变成 User-Agent。 
                                                                                                                //.WithHeaders(new DefaultHeaders()) 把 header 提取成一个默认的结构体，行不通……抓包发现没有这些数据。
                .WithCookies(out _jar); // 存入 cookie，供以后的请求携带。
            _logger.Debug($"{GetLogPrefix()}：发送请求：{reqGetRsaPubKey.Url}……");
            string rsaPubKeyContent = await reqGetRsaPubKey.GetStringAsync();
            _logger.Debug($"{GetLogPrefix()}：收到响应：{rsaPubKeyContent}。");
            string keyBegPatt = "-----BEGIN PUBLIC KEY-----";
            string keyEndPatt = "-----END PUBLIC KEY-----";
            int keyBegPattPos = rsaPubKeyContent.IndexOf(keyBegPatt);
            int keyEndPattPos = rsaPubKeyContent.IndexOf(keyEndPatt);
            string rsaPubKey = rsaPubKeyContent.Substring(keyBegPattPos, keyEndPattPos - keyBegPattPos + keyEndPatt.Length);
            _logger.Debug($"{GetLogPrefix()}：取得 RSA 加密公钥：{rsaPubKey}。");
            return rsaPubKey;
        }

        private async Task<bool> Login(string rsaPubKey)
        {
            _logger.Info($"{GetLogPrefix()}：加密密码……");
            var pem = RSA_PEM.FromPEM(rsaPubKey);
            var rsa = new RSA_Util(pem);
            string pwdEncoded = rsa.Encode(_password);
            _logger.Info($"{GetLogPrefix()}：登录……");
            var reqLogin = "https://oauth.yiban.cn/"
                .AppendPathSegment("code/usersure")
                .WithHeaders(new { Origin = "https://c.uyiban.com", User_Agent = "YiBan", AppVersion = "5.0" })
                .WithCookies(_jar);
            var loginBody = new { oauth_uname = _account, oauth_upwd = pwdEncoded, client_id = "95626fa3080300ea", redirect_uri = "https://f.yiban.cn/iapp7463" };
            _logger.Debug($"{GetLogPrefix()}：发送请求：{reqLogin.Url}，loginBody：{JsonSerializer.Serialize(loginBody, ServiceOptions.jsonSerializerOptions)}……");
            string loginContent = await reqLogin.PostUrlEncodedAsync(loginBody).ReceiveString();
            _logger.Debug($"{GetLogPrefix()}：收到响应：{loginContent}。");
            if (loginContent.Contains("error"))
            {
                _logger.Error($"{GetLogPrefix()}：登录失败，可能是用户名或密码错误。");
                return false;
            }
            return true;
        }

        private async Task<string> GetVr()
        {
            _logger.Info($"{GetLogPrefix()}：获取认证参数……");
            var reqGetVr = "https://f.yiban.cn/"
                .AppendPathSegment("iframe/index")
                .SetQueryParams(new { act = "iapp7463" })
                .WithHeaders(new { Origin = "https://c.uyiban.com", User_Agent = "YiBan", AppVersion = "5.0" })
                .WithCookies(_jar);
            _logger.Debug($"{GetLogPrefix()}：发送请求：{reqGetVr.Url}……");
            var resGetVr = await reqGetVr.WithAutoRedirect(false).GetAsync(); // 不要重定向，以便从响应头读取 verify_request。
            _logger.Debug($"{GetLogPrefix()}：收到响应。");
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
                    if (vrBegPattPos != -1 && vrEndPattPos != -1)
                        vr = location[(vrBegPattPos + vrBegPatt.Length)..vrEndPattPos];
                    break;
                }
            }
            _logger.Debug($"{GetLogPrefix()}：提取的认证参数（verify_request）：{vr}。");
            if (string.IsNullOrEmpty(vr))
                _logger.Error($"{GetLogPrefix()}：获取认证参数失败。");
            return vr;
        }

        private async Task<User> Auth(string vr, string csrfToken, string userAgent)
        {
            _logger.Info($"{GetLogPrefix()}：进行校本化认证……");
            var reqAuth = "https://api.uyiban.com/"
                .AppendPathSegment("base/c/auth/yiban")
                .SetQueryParams(new { verifyRequest = vr, CSRF = csrfToken })
                .WithHeaders(new { Origin = "https://c.uyiban.com" /* 认证 origin 是 c…… */, User_Agent = userAgent /* 认证 UA 包含 yiban_android。 */, AppVersion = "5.0", Cookie = $"csrf_token={csrfToken}" }) // 还需在 cookie 中提供 csrf_token。
                .WithCookies(_jar);
            _logger.Debug($"{GetLogPrefix()}：发送请求：{reqAuth.Url}……");
            var authContent = await reqAuth.GetStringAsync();
            _logger.Debug($"{GetLogPrefix()}：收到响应：{authContent}");
            YibanNightAttendanceSigninApiRes authRes = ParseYibanNightAttendanceSigninApiRes(JsonNode.Parse(authContent!)!);
            if (authRes.Code != 0)
            {
                _logger.Error($"{GetLogPrefix()}：校本化认证失败，服务端返回消息：{authRes.Msg}。");
                return new User();
            }
            JsonNode authResData = authRes.Data;
            User user = new()
            {
                UniversityName = authResData["UniversityName"].Deserialize<string>(),
                UniversityId = authResData["UniversityId"].Deserialize<string>(),
                PersonName = authResData["PersonName"].Deserialize<string>(),
                PersonId = authResData["PersonId"].Deserialize<string>()!
            };
            _logger.Debug($"{GetLogPrefix()}：解析出用户信息：{user}。");
            return user;
        }

        private async Task<Device> GetDevice(string csrfToken, string userAgent)
        {
            _logger.Info($"{GetLogPrefix()}：获取授权设备……");
            var reqGetDevice = "https://api.uyiban.com/"
                .AppendPathSegment("device/student/index/getState")
                .SetQueryParams(new { CSRF = csrfToken })
                .WithHeaders(new { Origin = "https://app.uyiban.com" /* 获取设备 origin 是 app…… */, User_Agent = userAgent /* 获取设备 UA 包含 yiban_android，如果是 iOS，则为 yiban_iOS。 */, AppVersion = "5.0", Cookie = $"csrf_token={csrfToken}" }) // 还需在 cookie 中提供 csrf_token。
                .WithCookies(_jar);
            _logger.Debug($"{GetLogPrefix()}：发送请求：{reqGetDevice.Url}……");
            var deviceContent = await reqGetDevice.GetStringAsync();
            _logger.Debug($"{GetLogPrefix()}：收到响应：{deviceContent}");
            YibanNightAttendanceSigninApiRes deviceRes = ParseYibanNightAttendanceSigninApiRes(JsonNode.Parse(deviceContent!)!);
            if (deviceRes.Code != 0)
            {
                _logger.Error($"{GetLogPrefix()}：获取授权设备失败，服务端返回消息：{deviceRes.Msg}。");
                return new Device();
            }
            JsonNode deviceResData = deviceRes.Data;
            Device device = new()
            {
                Code = deviceResData["Code"].Deserialize<string>(),
                PhoneModel = deviceResData["PhoneModel"].Deserialize<string>()
            };
            _logger.Debug($"{GetLogPrefix()}：解析出授权设备：{device}。");
            return device;
        }

        private async Task<SigninInfo> GetSigninInfo(string csrfToken, string userAgent)
        {
            _logger.Info($"{GetLogPrefix()}：获取签到信息……");
            var reqSigninInfo = "https://api.uyiban.com/"
                .AppendPathSegment("nightAttendance/student/index/signPosition")
                .SetQueryParams(new { CSRF = csrfToken })
                .WithHeaders(new { Origin = "https://app.uyiban.com" /* 获取签到信息 origin 是 app…… */, User_Agent = userAgent /* 获取签到信息 UA 包含 yiban_android，如果是 iOS，则为 yiban_iOS。 */, AppVersion = "5.0", Cookie = $"csrf_token={csrfToken}" }) // 还需在 cookie 中提供 csrf_token。
                .WithCookies(_jar);
            _logger.Debug($"{GetLogPrefix()}：发送请求：{reqSigninInfo.Url}……");
            var infoContent = await reqSigninInfo.GetStringAsync();
            _logger.Debug($"{GetLogPrefix()}：收到响应：{infoContent}");
            YibanNightAttendanceSigninApiRes infoRes = ParseYibanNightAttendanceSigninApiRes(JsonNode.Parse(infoContent!)!);
            if (infoRes.Code != 0)
            {
                _logger.Error($"{GetLogPrefix()}：获取签到信息失败，服务端返回消息：{infoRes.Msg}。");
                return new();
            }
            JsonNode infoResData = infoRes.Data;
            SigninInfo signinInfo = new()
            {
                IsServerRes = true /* 标记已从服务器获取到签到信息，区别于默认的签到信息。 */,
                State = infoResData["State"].Deserialize<int>(),
                BeginTime = infoResData["Range"]!["StartTime"].Deserialize<long>(),
                EndTime = infoResData["Range"]!["EndTime"].Deserialize<long>(),
                ShouldSigninToday = infoResData["Range"]!["SignDay"].Deserialize<int>() != 0
            };
            _logger.Debug($"{GetLogPrefix()}：解析出签到信息：{signinInfo}。");
            return signinInfo;
        }

        private async Task<bool> Signin(string csrfToken, Device device, SigninPhotoInfo signinPhotoInfo, string userAgent)
        {
            _logger.Info($"{GetLogPrefix()}：晚点签到，启动！");
            // 附加照片。
            string attachmentFilename = "";
            if (!string.IsNullOrEmpty(signinPhotoInfo.Name) && !string.IsNullOrEmpty(signinPhotoInfo.Type) && signinPhotoInfo.Size != 0)
            {
                var reqGetUploadUri = "https://api.uyiban.com/"
                    .AppendPathSegment("nightAttendance/student/index/uploadUri")
                    .SetQueryParams(new { name = signinPhotoInfo.Name })
                    .SetQueryParams(new { type = signinPhotoInfo.Type })
                    .SetQueryParams(new { size = signinPhotoInfo.Size })
                    .SetQueryParams(new { CSRF = csrfToken })
                    .WithHeaders(new { Origin = "https://app.uyiban.com" /* 签到 origin 是 app…… */, User_Agent = userAgent /* 签到 UA 包含 yiban_android，如果是 iOS，则为 yiban_iOS。 */, AppVersion = "5.0", Cookie = $"csrf_token={csrfToken}" }) // 还需在 cookie 中提供 csrf_token。
                    .WithCookies(_jar);
                _logger.Debug($"{GetLogPrefix()}：发送请求：{reqGetUploadUri.Url}……");
                var uploadUriContent = await reqGetUploadUri.GetStringAsync();
                _logger.Debug($"{GetLogPrefix()}：收到响应：{uploadUriContent}");
                YibanNightAttendanceSigninApiRes uploadUriRes = ParseYibanNightAttendanceSigninApiRes(JsonNode.Parse(uploadUriContent!)!);
                if (uploadUriRes.Code != 0)
                {
                    _logger.Error($"{GetLogPrefix()}：获取照片上传 URI 失败，服务端返回消息：{uploadUriRes.Msg}。");
                    return false;
                }
                attachmentFilename = uploadUriRes.Data["AttachmentFileName"].Deserialize<string>()!;
                string signedUrl = uploadUriRes.Data["signedUrl"].Deserialize<string>()!;
                var reqUploadPhoto = signedUrl
                    .WithHeaders(new { Content_Type = "image/jpeg", Origin = "https://app.uyiban.com" /* 签到 origin 是 app…… */, User_Agent = userAgent /* 签到 UA 包含 yiban_android，如果是 iOS，则为 yiban_iOS。 */, AppVersion = "5.0", Cookie = $"csrf_token={csrfToken}" }) // 还需在 cookie 中提供 csrf_token。
                    .WithCookies(_jar);
                _logger.Debug($"{GetLogPrefix()}：发送请求：{reqUploadPhoto.Url}……");
                var uploadPhotoContent = await reqUploadPhoto.PutAsync(new FileContent(_photo));
                _logger.Debug($"{GetLogPrefix()}：收到响应：HTTP {uploadPhotoContent.StatusCode}");
                if (uploadPhotoContent.StatusCode != 200)
                {
                    _logger.Error($"{GetLogPrefix()}：照片上传失败，服务端返回 HTTP 状态码：{uploadPhotoContent.StatusCode}。");
                    return false;
                }
                var reqGetDownloadUri = "https://api.uyiban.com/"
                    .AppendPathSegment("nightAttendance/student/index/downloadUri")
                    .SetQueryParams(new { AttachmentFileName = attachmentFilename })
                    .SetQueryParams(new { CSRF = csrfToken })
                    .WithHeaders(new { Origin = "https://app.uyiban.com" /* 签到 origin 是 app…… */, User_Agent = userAgent /* 签到 UA 包含 yiban_android，如果是 iOS，则为 yiban_iOS。 */, AppVersion = "5.0", Cookie = $"csrf_token={csrfToken}" }) // 还需在 cookie 中提供 csrf_token。
                    .WithCookies(_jar);
                _logger.Debug($"{GetLogPrefix()}：发送请求：{reqGetDownloadUri.Url}……");
                var downloadUriContent = await reqGetDownloadUri.GetStringAsync();
                _logger.Debug($"{GetLogPrefix()}：收到响应：{downloadUriContent}");
                YibanNightAttendanceSigninApiRes downloadUriRes = ParseYibanNightAttendanceSigninApiRes(JsonNode.Parse(downloadUriContent!)!);
                if (downloadUriRes.Code != 0)
                {
                    _logger.Error($"{GetLogPrefix()}：获取照片下载 URI 失败，服务端返回消息：{downloadUriRes.Msg}。");
                    return false;
                }
                JsonNode downloadUriResData = downloadUriRes.Data;
                _logger.Debug($"{GetLogPrefix()}：解析出照片下载 URI：{downloadUriResData.Deserialize<string>()!}。");
            }
            var reqSignin = "https://api.uyiban.com/"
                .AppendPathSegment("nightAttendance/student/index/signIn")
                .SetQueryParams(new { CSRF = csrfToken })
                .WithHeaders(new { Origin = "https://app.uyiban.com" /* 签到 origin 是 app…… */, User_Agent = userAgent /* 签到 UA 包含 yiban_android，如果是 iOS，则为 yiban_iOS。 */, AppVersion = "5.0", Cookie = $"csrf_token={csrfToken}" }) // 还需在 cookie 中提供 csrf_token。
                .WithCookies(_jar);
            var signinBody = new { OutState = "1", device.Code, device.PhoneModel /* 经测试只要 PhoneModel 对上即可。 */, SignInfo = JsonSerializer.Serialize(new { Reason = _reason, AttachmentFileName = attachmentFilename, LngLat = $"{_position[0]},{_position[1]}", Address = _address }) }; // SignInfo 是字符串。
            _logger.Debug($"{GetLogPrefix()}：发送请求：{reqSignin.Url}，SigninBody：{JsonSerializer.Serialize(signinBody, ServiceOptions.jsonSerializerOptions)}……");
            string signinContent = await reqSignin.PostUrlEncodedAsync(signinBody).ReceiveString();
            _logger.Debug($"{GetLogPrefix()}：收到响应：{signinContent}。");
            YibanNightAttendanceSigninApiRes siginRes = ParseYibanNightAttendanceSigninApiRes(JsonNode.Parse(signinContent!)!);
            if (siginRes.Code != 0)
            {
                _logger.Error($"{GetLogPrefix()}：签到失败，服务端返回消息：{siginRes.Msg}。");
                return false;
            }
            return true;
        }
    }
}
