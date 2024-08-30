using com.github.xiangyuecn.rsacsharp;
using Flurl;
using Flurl.Http;
using Flurl.Http.Content;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace YBNAS
{
    internal enum Error
    {
        Unknown = -1,
        Ok,
        LoginParamsInvalid,
        LoginFailed,
        VerifyrequestInvalid,
        UserInvalid,
        SignInInfoInvalid,
        PhotoUploadFailed,
        SignInFailed
    }

    internal struct LoginParams
    {
        public string RsaPubKey { get; set; }
        public string Pageuse { get; set; }
        public override readonly string ToString()
        {
            return JsonSerializer.Serialize(this, ServiceOptions.jsonSerializerOptions);
        }
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

    internal struct SignInPhotoInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public long Size { get; set; }
        public override readonly string ToString()
        {
            return JsonSerializer.Serialize(this, ServiceOptions.jsonSerializerOptions);
        }
    }

    internal struct UploadedPhotoInfo
    {
        public string AttachmentFileName { get; set; } // 上传接口给的 AttachmentFileName，filename 里的 N 大写，跟官方一致。
        public string DownloadUri { get; set; } // 我自定义的字段，存下载接口返回的地址。
        public override readonly string ToString()
        {
            return JsonSerializer.Serialize(this, ServiceOptions.jsonSerializerOptions);
        }
    }

    internal struct SignInInfo
    {
        public bool IsServerRes { get; set; }
        public int State { get; set; } // 不该是 null。
        public long BeginTime { get; set; } // 不该是 null。
        public long EndTime { get; set; } // 不该是 null。
        public bool ShouldSignInToday { get; set; } // 不该是 null。
        public int IsNeedPhoto { get; set; } // 这是字段原名……猜测 0 代表不需提交照片，1 代表总需，2 代表范围外需。
        public override readonly string ToString()
        {
            return JsonSerializer.Serialize(this, ServiceOptions.jsonSerializerOptions);
        }
    }

    internal struct YibanNightAttendanceSignInApiRes
    {
        public int Code { get; set; }
        public string Msg { get; set; }
        public JsonNode Data { get; set; }
        public override readonly string ToString()
        {
            return JsonSerializer.Serialize(this, ServiceOptions.jsonSerializerOptions);
        }
    }

    internal class SignInTask
    {
        public enum TaskStatus
        {
            Waiting,
            Running,
            Complete,
            Skipped,
            Aborted
        }

        private readonly string _taskId = string.Empty;
        private TaskStatus _status;
        private string _statusText;

        private string _name = string.Empty;
        private string _description = string.Empty; // 不要只读。
        private readonly string _account = string.Empty;
        private readonly string _password = string.Empty;
        private readonly double[] _position = [0.0, 0.0];
        private readonly string _address = string.Empty;
        private readonly bool _outside = false;
        private readonly string _photo = string.Empty;
        private readonly string _reason = string.Empty;
        private readonly int _beginHour = 0;
        private readonly int _beginMin = 0;
        private readonly int _endHour = 0;
        private readonly int _endMin = 0;

        private readonly string _csrfToken = string.Empty;
        private string _appVersion = string.Empty; // 不要 readonly。iOS 可能有另一套版本号。
        private string _userAgent = string.Empty;

        private long _lastSuccess = 0;

        private User _user;
        private Device _device;

        public delegate void RunHandler(SignInTask st, Error err);
        public event RunHandler? OnRun;
        public delegate void CompleteHandler(SignInTask st, Error err);
        public event CompleteHandler? OnComplete;
        public delegate void SkipHandler(SignInTask st, Error err);
        public event SkipHandler? OnSkip;
        public delegate void ErrorHandler(SignInTask st, Error err);
        public event ErrorHandler? OnError;

        private CookieJar _jar = new();
        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        public string TaskId { get { return _taskId; } }
        public TaskStatus Status { get { return _status; } }
        public string StatusText { get { return _statusText; } }

        public string Name { get { return _name; } }
        public string Description { get { return _description; } }
        public string Account { get { return _account; } }
        [JsonConverter(typeof(PasswordJsonConverter))]
        public string Password { get { return _password; } }
        public double[] Position { get { return _position; } }
        public string Address { get { return _address; } }
        public bool Outside { get { return _outside; } }
        public string Photo { get { return _photo; } }
        public string Reason { get { return _reason; } }
        public int BeginHour { get { return _beginHour; } }
        public int BeginMin { get { return _beginMin; } }
        public int EndHour { get { return _endHour; } }
        public int EndMin { get { return _endMin; } }

        public long LastSuccess { get { return _lastSuccess; } }

        public User User { get { return _user; } }
        public Device Device { get { return _device; } }

        public SignInTask(
            string name,
            string description,
            string account,
            string password,
            double[] position,
            string address,
            string photo,
            string reason,
            bool outside,
            int beginHour,
            int beginMin,
            int endHour,
            int endMin,
            long lastSuccess,
            Device device = new()
            )
        {
            _taskId = Guid.NewGuid().ToString()[..8];
            _status = TaskStatus.Waiting;
            _statusText = "等待";

            _name = name;
            _description = description;
            _account = account;
            _password = password;
            if (position.Length == 2)
                _position = position;
            _address = address;
            _photo = photo;
            _reason = reason;
            _outside = outside;
            _beginHour = beginHour;
            _beginMin = beginMin;
            _endHour = endHour;
            _endMin = endMin;

            _lastSuccess = lastSuccess;

            _csrfToken = Guid.NewGuid().ToString("N");
            _appVersion = "5.1.2";
            _userAgent = $"yiban_android/{_appVersion}"; // 校本化应用需要的 UA，先使用 Android 端。

            _user = new();
            _device = device;

            _logger.Debug($"{GetLogPrefix()}：构造完成。");
        }

        public string GetLogPrefix()
        {
            string nameAndDesc = string.Empty;
            if (!string.IsNullOrEmpty(_name.Trim()) && !string.IsNullOrEmpty(_description.Trim()))
            {
                nameAndDesc = $"{_name}，{_description}";
            }
            else
            {
                if (!string.IsNullOrEmpty(_name.Trim()))
                    nameAndDesc += _name.Trim();
                if (!string.IsNullOrEmpty(_description.Trim()))
                    nameAndDesc += _description.Trim();
            }
            return "任务 " + _taskId + (!string.IsNullOrEmpty(nameAndDesc) ? "（" + nameAndDesc + "）" : string.Empty);
        }

        private static int GetRandom(int minValue, int maxValue)
        {
            Random rd = new(Guid.NewGuid().GetHashCode()); // 用 GUID 作种子，高频调用也随机。
            return rd.Next(minValue, maxValue);
        }

        private static YibanNightAttendanceSignInApiRes ParseYibanNightAttendanceSignInApiRes(JsonNode node)
        {
            int code = node["code"].Deserialize<int>();
            string msg = node["msg"].Deserialize<string>()!;
            JsonNode data = node["data"]!;
            return new() { Code = code, Msg = msg, Data = data };
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
                _status = TaskStatus.Running;
                _statusText = "正在运行";
                _logger.Debug($"{GetLogPrefix()}：开始运行。");
                OnRun?.Invoke(this, Error.Ok);
                DateTimeOffset curDateTime = DateTimeOffset.UtcNow;
                long curTimestamp = curDateTime.ToUnixTimeSeconds();
                DateTimeOffset lastSuccessDateTime = DateTimeOffset.FromUnixTimeSeconds(_lastSuccess);
                if (Config.ExpireIn == -1 ||
                    (curDateTime.Date == lastSuccessDateTime.Date &&
                    curTimestamp - _lastSuccess > 0 &&
                    curTimestamp - _lastSuccess < Config.ExpireIn))
                {
                    _logger.Info($"{GetLogPrefix()}：{Config.ExpireIn} 秒内曾成功签到，将跳过。");
                    _status = TaskStatus.Skipped;
                    _statusText = $"{Config.ExpireIn} 秒内曾成功签到";
                    _logger.Debug($"{GetLogPrefix()}：跳过运行。");
                    OnSkip?.Invoke(this, Error.Ok);
                    return;
                }
                _jar = new(); // 存 cookie。任务失败重试，使用新 cookie。
                _logger.Debug($"{GetLogPrefix()}：新建 CookieJar。");
                LoginParams loginParams = await GetLoginParams();
                if (string.IsNullOrEmpty(loginParams.RsaPubKey) || string.IsNullOrEmpty(loginParams.Pageuse))
                {
                    _status = TaskStatus.Aborted;
                    _statusText = "获取登录参数失败";
                    OnError?.Invoke(this, Error.LoginParamsInvalid);
                    return;
                }
                bool loginSucceeded = await LogIn(loginParams.RsaPubKey, loginParams.Pageuse);
                if (!loginSucceeded)
                {
                    _status = TaskStatus.Aborted;
                    _statusText = "登录失败";
                    OnError?.Invoke(this, Error.LoginFailed);
                    return;
                }
                string vr = await GetVerifyrequest();
                if (string.IsNullOrEmpty(vr))
                {
                    _status = TaskStatus.Aborted;
                    _statusText = "获取认证参数失败";
                    OnError?.Invoke(this, Error.VerifyrequestInvalid);
                    return;
                }
                _user = await Auth(vr);
                if (string.IsNullOrEmpty(_user.PersonId)) // 校本化认证失败。
                {
                    _status = TaskStatus.Aborted;
                    _statusText = "校本化认证失败";
                    OnError?.Invoke(this, Error.UserInvalid);
                    return;
                }
                _name = string.IsNullOrEmpty(_name.Trim()) ? (string.IsNullOrEmpty(_user.PersonName) ? string.Empty : _user.PersonName) : _name;
                _logger.Info($"{GetLogPrefix()}：认证成功。");
                //if (_user.UniversityName != "黑龙江科技大学")
                //    if (string.IsNullOrEmpty(_user.UniversityName))
                //        _logger.Warn($"{GetLogPrefix()}：学校信息缺失，本程序可能不适用，不过我会试试。");
                //    else
                //        _logger.Warn($"{GetLogPrefix()}：本程序可能不适用于{_name}学校{_user.UniversityName}，不过我会试试。");
                if (string.IsNullOrEmpty(_device.Code) || string.IsNullOrEmpty(_device.PhoneModel))
                {
                    _logger.Info($"{GetLogPrefix()}：未提供合适的设备信息，将从接口获取。");
                    _device = await GetDevice(); // 未提供合适的设备信息，从接口获取。
                    if (!string.IsNullOrEmpty(_device.PhoneModel) && !string.IsNullOrEmpty(_device.Code))
                        _logger.Info($"{GetLogPrefix()}：绑定的设备是 {_device.PhoneModel}（{_device.Code}）。");
                    else
                        //_logger.Info($"{GetLogPrefix()}：设备信息缺失。Do you guys not have phones? 可能会签到失败，不过我会试试。");
                        _logger.Info($"{GetLogPrefix()}：设备信息缺失。");
                }
                if (!string.IsNullOrEmpty(_device.PhoneModel) && (_device.PhoneModel.Contains("iPhone") || _device.PhoneModel.Contains("iPad")))
                    _userAgent = $"yiban_iOS/{_appVersion}"; // 如果是 iPhone 或 iPad，让之后的请求携带 iOS 客户端的 UA。
                // 签到之前必须先获取签到信息，可能会设定 cookie，否则会判非法签到。
                SignInInfo info = await GetSignInInfo();
                if (!info.IsServerRes)
                {
                    _status = TaskStatus.Aborted;
                    _statusText = "获取签到信息失败";
                    OnError?.Invoke(this, Error.SignInInfoInvalid);
                    return;
                }
                if (info.State == 4) // 表示签到状态已被更改。
                {
                    _logger.Info($"{GetLogPrefix()}：签到状态已被更改，无法签到，将跳过。");
                    _status = TaskStatus.Skipped;
                    _statusText = "无法签到，因为签到状态已被更改";
                    _logger.Debug($"{GetLogPrefix()}：跳过运行。");
                    OnSkip?.Invoke(this, Error.Ok);
                    return;
                }
                if (info.State == 3) // 表示已签到。
                {
                    _logger.Info($"{GetLogPrefix()}：已签到，将跳过。");
                    _status = TaskStatus.Skipped;
                    _statusText = "已签到";
                    _logger.Debug($"{GetLogPrefix()}：跳过运行。");
                    OnSkip?.Invoke(this, Error.Ok);
                    return;
                }
                if (info.State == 2) // 无需签到，可能已请假。
                {
                    _logger.Info($"{GetLogPrefix()}：无需签到，将跳过。");
                    _status = TaskStatus.Skipped;
                    _statusText = "无需签到";
                    _logger.Debug($"{GetLogPrefix()}：跳过运行。");
                    OnSkip?.Invoke(this, Error.Ok);
                    return;
                }
                if (info.State == 1)
                {
                    _logger.Info($"{GetLogPrefix()}：不在学校要求的签到时间段内，无法签到，将跳过。"); // 最好让用户一眼知道是哪个人在哪个学校因为未到时间签到失败。
                    _status = TaskStatus.Skipped;
                    _statusText = "无法签到，因为不在签到时间段内";
                    _logger.Debug($"{GetLogPrefix()}：跳过运行。");
                    OnSkip?.Invoke(this, Error.Ok);
                    return;
                }
                if (info.State != 0) // 因为其他原因不适宜签到。
                {
                    _logger.Info($"{GetLogPrefix()}：无需签到或无法签到，（原因未知，State 值为 {info.State}。）将跳过。");
                    _status = TaskStatus.Skipped;
                    _statusText = $"无需签到或无法签到，原因未知，State 值为 {info.State}";
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
                UploadedPhotoInfo uploadedPhotoInfo = new();
                if (!string.IsNullOrEmpty(_photo) && (info.IsNeedPhoto == 1 || (info.IsNeedPhoto == 2 && _outside))) // 0 不需，1 总需，2 校外需。
                {
                    FileInfo photoFileInfo = new(_photo);
                    SignInPhotoInfo signinPhotoInfo = new()
                    {
                        Name = $"yiban_camera_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.jpg",
                        Type = "image/jpeg",
                        Size = photoFileInfo.Length
                    };
                    uploadedPhotoInfo = await UploadPhoto(signinPhotoInfo);
                    if (string.IsNullOrEmpty(uploadedPhotoInfo.AttachmentFileName) && string.IsNullOrEmpty(uploadedPhotoInfo.DownloadUri))
                    {
                        _status = TaskStatus.Aborted;
                        _statusText = "照片上传失败";
                        OnError?.Invoke(this, Error.PhotoUploadFailed);
                        return;
                    }
                }
                bool signinStatus = await SignIn(_device, uploadedPhotoInfo);
                if (!signinStatus) // 签到失败。
                {
                    _status = TaskStatus.Aborted;
                    _statusText = "签到失败";
                    OnError?.Invoke(this, Error.SignInFailed);
                    return;
                }
                _logger.Info($"{GetLogPrefix()}：签到成功！Have a safe day.");
                _status = TaskStatus.Complete;
                _statusText = "完成";
                _lastSuccess = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                _logger.Debug($"{GetLogPrefix()}：运行完成。");
                OnComplete?.Invoke(this, Error.Ok);
            }
            catch (Exception ex)
            {
                _status = TaskStatus.Aborted;
                _statusText = "运行出错";
                _logger.Error(ex, $"{GetLogPrefix()}：运行出错。"); // NLog 推荐这样传递异常信息。
                OnError?.Invoke(this, Error.Unknown);
            }
        }

        private async Task<LoginParams> GetLoginParams()
        {
            _logger.Info($"{GetLogPrefix()}：获取登录参数……");
            var reqGetLoginParams = "https://oauth.yiban.cn/" // 留出 BaseUrl，Flurl.Http 给相同域的请求复用同一个 HttpClient。
                .AppendPathSegment("code/html") // 在此附加路径。
                .SetQueryParams(new { client_id = "95626fa3080300ea" /* 校本化的应用 Id。 */, redirect_uri = "https://f.yiban.cn/iapp7463" })
                .WithHeaders(new { User_Agent = _userAgent }) // User_Agent 会自动变成 User-Agent。此处确实需要携带正确 UA，否则 cookie 不正确，不能获取认证参数。
                                                              //.WithHeaders(new DefaultHeaders()) 把 header 提取成一个默认的结构体，行不通……抓包发现没有这些数据。
                .WithCookies(out _jar); // 存入 cookie，供以后的请求携带。
            _logger.Debug($"{GetLogPrefix()}：发送请求：{reqGetLoginParams.Url}……");
            string loginParamsContent = await reqGetLoginParams.GetStringAsync();
            _logger.Debug($"{GetLogPrefix()}：收到登录参数响应。");
            string rsaPubKey = new Regex(@"-----BEGIN PUBLIC KEY-----(.*\n)+-----END PUBLIC KEY-----").Match(loginParamsContent).Value ?? string.Empty;
            if (string.IsNullOrEmpty(rsaPubKey))
                _logger.Error($"{GetLogPrefix()}：获取登录参数失败，RSA 加密公钥为空。");
            else
                _logger.Debug($"{GetLogPrefix()}：取得 RSA 加密公钥：{rsaPubKey}。");
            string pageuse = new Regex(@"var page_use = '(.+)';").Match(loginParamsContent)?.Groups[1]?.Value ?? string.Empty;
            if (string.IsNullOrEmpty(pageuse))
                _logger.Error($"{GetLogPrefix()}：获取登录参数失败，AJAX 签名（page_use）为空。");
            else
                _logger.Debug($"{GetLogPrefix()}：取得 AJAX 签名（page_use）：{pageuse}。");
            return new LoginParams { RsaPubKey = rsaPubKey, Pageuse = pageuse };
        }

        private async Task<bool> LogIn(string rsaPubKey, string ajaxSign)
        {
            _logger.Info($"{GetLogPrefix()}：加密密码……");
            var pem = RSA_PEM.FromPEM(rsaPubKey);
            var rsa = new RSA_Util(pem);
            string pwdEncoded = rsa.Encode(_password);
            _logger.Info($"{GetLogPrefix()}：登录……");
            var reqLogIn = "https://oauth.yiban.cn/"
                .AppendPathSegment("code/usersure")
                .SetQueryParams(new { ajax_sign = ajaxSign })
                .WithHeaders(new { User_Agent = _userAgent })
                .WithCookies(_jar);
            var logInBody = new { oauth_uname = _account, oauth_upwd = pwdEncoded, client_id = "95626fa3080300ea", redirect_uri = "https://f.yiban.cn/iapp7463" };
            _logger.Debug($"{GetLogPrefix()}：发送请求：{reqLogIn.Url}，logInBody：{JsonSerializer.Serialize(logInBody, ServiceOptions.jsonSerializerOptions)}……");
            string logInContent = await reqLogIn.PostUrlEncodedAsync(logInBody).ReceiveString();
            _logger.Debug($"{GetLogPrefix()}：收到登录响应：{logInContent}。");
            if (logInContent.Contains("error"))
            {
                _logger.Error($"{GetLogPrefix()}：登录失败，可能是用户名或密码错误。");
                return false;
            }
            return true;
        }

        private async Task<string> GetVerifyrequest()
        {
            _logger.Info($"{GetLogPrefix()}：获取认证参数……");
            var reqGetVr = "https://f.yiban.cn/"
                .AppendPathSegment("iframe/index")
                .SetQueryParams(new { act = "iapp7463" })
                .WithHeaders(new { User_Agent = _userAgent })
                .WithCookies(_jar);
            _logger.Debug($"{GetLogPrefix()}：发送请求：{reqGetVr.Url}……");
            var resGetVr = await reqGetVr.WithAutoRedirect(false).GetAsync(); // 不要重定向，以便从响应头读取 verify_request。
            _logger.Debug($"{GetLogPrefix()}：收到认证参数响应。");
            string vr = string.Empty;
            foreach (var (name, value) in resGetVr.Headers)
            {
                if (name == "Location") // 在响应头的 Location 里找 verify_request。
                {
                    _logger.Debug($"{GetLogPrefix()}：查找到 Location：{value}。");
                    vr = new Regex(@"verify_request=(.+)&yb_uid").Match(value)?.Groups[1]?.Value ?? string.Empty;
                    break;
                }
            }
            if (string.IsNullOrEmpty(vr))
                _logger.Error($"{GetLogPrefix()}：获取认证参数失败。");
            else
                _logger.Debug($"{GetLogPrefix()}：取得认证参数（verify_request）：{vr}。");
            return vr;
        }

        private async Task<User> Auth(string vr)
        {
            _logger.Info($"{GetLogPrefix()}：进行校本化认证……");
            var reqAuth = "https://api.uyiban.com/"
                .AppendPathSegment("base/c/auth/yiban")
                .SetQueryParams(new { verifyRequest = vr, CSRF = _csrfToken })
                .WithHeaders(new { Origin = "https://c.uyiban.com" /* 认证 origin 是 c…… */, User_Agent = _userAgent /* 认证 UA 包含 yiban_android。 */, Cookie = $"csrf_token={_csrfToken}" }) // 还需在 cookie 中提供 csrf_token。
                .WithCookies(_jar);
            _logger.Debug($"{GetLogPrefix()}：发送请求：{reqAuth.Url}……");
            var authContent = await reqAuth.GetStringAsync();
            _logger.Debug($"{GetLogPrefix()}：收到校本化认证响应：{authContent}。");
            YibanNightAttendanceSignInApiRes authRes = ParseYibanNightAttendanceSignInApiRes(JsonNode.Parse(authContent!)!);
            if (authRes.Code != 0)
            {
                _logger.Error($"{GetLogPrefix()}：校本化认证失败，服务端返回消息：{authRes.Msg}。");
                return new();
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

        private async Task<Device> GetDevice()
        {
            _logger.Info($"{GetLogPrefix()}：获取授权设备……");
            var reqGetDevice = "https://api.uyiban.com/"
                .AppendPathSegment("device/student/index/getState")
                .SetQueryParams(new { CSRF = _csrfToken })
                .WithHeaders(new { Origin = "https://app.uyiban.com" /* 获取设备 origin 是 app…… */, User_Agent = _userAgent /* 获取设备 UA 包含 yiban_android，如果是 iOS，则为 yiban_iOS。 */, Cookie = $"csrf_token={_csrfToken}" }) // 还需在 cookie 中提供 csrf_token。
                .WithCookies(_jar);
            _logger.Debug($"{GetLogPrefix()}：发送请求：{reqGetDevice.Url}……");
            var deviceContent = await reqGetDevice.GetStringAsync();
            _logger.Debug($"{GetLogPrefix()}：收到授权设备响应：{deviceContent}。");
            YibanNightAttendanceSignInApiRes deviceRes = ParseYibanNightAttendanceSignInApiRes(JsonNode.Parse(deviceContent!)!);
            if (deviceRes.Code != 0)
            {
                _logger.Error($"{GetLogPrefix()}：获取授权设备失败，服务端返回消息：{deviceRes.Msg}。");
                return new();
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

        private async Task<SignInInfo> GetSignInInfo()
        {
            _logger.Info($"{GetLogPrefix()}：获取签到信息……");
            var reqSignInInfo = "https://api.uyiban.com/"
                .AppendPathSegment("nightAttendance/student/index/signPosition")
                .SetQueryParams(new { CSRF = _csrfToken })
                .WithHeaders(new { Origin = "https://app.uyiban.com" /* 获取签到信息 origin 是 app…… */, User_Agent = _userAgent /* 获取签到信息 UA 包含 yiban_android，如果是 iOS，则为 yiban_iOS。 */, Cookie = $"csrf_token={_csrfToken}" }) // 还需在 cookie 中提供 csrf_token。
                .WithCookies(_jar);
            _logger.Debug($"{GetLogPrefix()}：发送请求：{reqSignInInfo.Url}……");
            var infoContent = await reqSignInInfo.GetStringAsync();
            _logger.Debug($"{GetLogPrefix()}：收到签到信息响应：{infoContent}。");
            YibanNightAttendanceSignInApiRes infoRes = ParseYibanNightAttendanceSignInApiRes(JsonNode.Parse(infoContent!)!);
            if (infoRes.Code != 0)
            {
                _logger.Error($"{GetLogPrefix()}：获取签到信息失败，服务端返回消息：{infoRes.Msg}。");
                return new();
            }
            JsonNode infoResData = infoRes.Data;
            SignInInfo signinInfo = new()
            {
                IsServerRes = true /* 标记已从服务器获取到签到信息，区别于默认的签到信息。 */,
                State = infoResData["State"].Deserialize<int>(),
                BeginTime = infoResData["Range"]!["StartTime"].Deserialize<long>(),
                EndTime = infoResData["Range"]!["EndTime"].Deserialize<long>(),
                ShouldSignInToday = infoResData["Range"]!["SignDay"].Deserialize<int>() != 0,
                IsNeedPhoto = infoResData["IsNeedPhoto"].Deserialize<int>()
            };
            _logger.Debug($"{GetLogPrefix()}：解析出签到信息：{signinInfo}。");
            return signinInfo;
        }

        private async Task<UploadedPhotoInfo> UploadPhoto(SignInPhotoInfo signinPhotoInfo)
        {
            _logger.Info($"{GetLogPrefix()}：上传照片……");
            if (!string.IsNullOrEmpty(signinPhotoInfo.Name) && !string.IsNullOrEmpty(signinPhotoInfo.Type) && signinPhotoInfo.Size != 0)
            {
                var reqGetUploadUri = "https://api.uyiban.com/"
                    .AppendPathSegment("nightAttendance/student/index/uploadUri")
                    .SetQueryParams(new { name = signinPhotoInfo.Name, type = signinPhotoInfo.Type, size = signinPhotoInfo.Size, CSRF = _csrfToken })
                    .WithHeaders(new { Origin = "https://app.uyiban.com" /* 签到 origin 是 app…… */, User_Agent = _userAgent /* 签到 UA 包含 yiban_android，如果是 iOS，则为 yiban_iOS。 */, Cookie = $"csrf_token={_csrfToken}" }) // 还需在 cookie 中提供 csrf_token。
                    .WithCookies(_jar);
                _logger.Debug($"{GetLogPrefix()}：发送请求：{reqGetUploadUri.Url}……");
                var uploadUriContent = await reqGetUploadUri.GetStringAsync();
                _logger.Debug($"{GetLogPrefix()}：收到照片上传 URI 响应：{uploadUriContent}。");
                YibanNightAttendanceSignInApiRes uploadUriRes = ParseYibanNightAttendanceSignInApiRes(JsonNode.Parse(uploadUriContent!)!);
                if (uploadUriRes.Code != 0)
                {
                    _logger.Error($"{GetLogPrefix()}：获取照片上传 URI 失败，服务端返回消息：{uploadUriRes.Msg}。");
                    return new();
                }
                // 附加照片。
                string attachmentFilename = uploadUriRes.Data["AttachmentFileName"].Deserialize<string>()!;
                string signedUrl = uploadUriRes.Data["signedUrl"].Deserialize<string>()!;
                var reqUploadPhoto = signedUrl
                    .WithHeaders(new { Content_Type = "image/jpeg", Origin = "https://app.uyiban.com" /* 签到 origin 是 app…… */, User_Agent = _userAgent /* 签到 UA 包含 yiban_android，如果是 iOS，则为 yiban_iOS。 */ });
                //.WithCookies(_jar);
                _logger.Debug($"{GetLogPrefix()}：发送请求：{reqUploadPhoto.Url}……");
                var uploadPhotoContent = await reqUploadPhoto.PutAsync(new FileContent(_photo));
                _logger.Debug($"{GetLogPrefix()}：收到照片上传响应：HTTP {uploadPhotoContent.StatusCode}。");
                if (uploadPhotoContent.StatusCode != 200)
                {
                    _logger.Error($"{GetLogPrefix()}：照片上传失败，服务端返回 HTTP 状态码：{uploadPhotoContent.StatusCode}。");
                    return new();
                }
                var reqGetDownloadUri = "https://api.uyiban.com/"
                    .AppendPathSegment("nightAttendance/student/index/downloadUri")
                    .SetQueryParams(new { AttachmentFileName = attachmentFilename, CSRF = _csrfToken })
                    .WithHeaders(new { Origin = "https://app.uyiban.com" /* 签到 origin 是 app…… */, User_Agent = _userAgent /* 签到 UA 包含 yiban_android，如果是 iOS，则为 yiban_iOS。 */, Cookie = $"csrf_token={_csrfToken}" }) // 还需在 cookie 中提供 csrf_token。
                    .WithCookies(_jar);
                _logger.Debug($"{GetLogPrefix()}：发送请求：{reqGetDownloadUri.Url}……");
                var downloadUriContent = await reqGetDownloadUri.GetStringAsync();
                _logger.Debug($"{GetLogPrefix()}：收到照片下载 URI 响应：{downloadUriContent}。");
                YibanNightAttendanceSignInApiRes downloadUriRes = ParseYibanNightAttendanceSignInApiRes(JsonNode.Parse(downloadUriContent!)!);
                if (downloadUriRes.Code != 0)
                {
                    _logger.Error($"{GetLogPrefix()}：获取照片下载 URI 失败，服务端返回消息：{downloadUriRes.Msg}。");
                    return new();
                }
                JsonNode downloadUriResData = downloadUriRes.Data;
                string downloadUri = downloadUriResData.Deserialize<string>()!;
                _logger.Debug($"{GetLogPrefix()}：解析出照片下载 URI：{downloadUri}。");
                return new() { AttachmentFileName = attachmentFilename, DownloadUri = downloadUri };
            }
            return new();
        }

        private async Task<bool> SignIn(Device device, UploadedPhotoInfo uploadedPhotoInfo)
        {
            _logger.Info($"{GetLogPrefix()}：晚点签到，启动！");
            // 附加照片。
            var reqSignIn = "https://api.uyiban.com/"
                .AppendPathSegment("nightAttendance/student/index/signIn")
                .SetQueryParams(new { CSRF = _csrfToken })
                .WithHeaders(new { Origin = "https://app.uyiban.com" /* 签到 origin 是 app…… */, User_Agent = _userAgent /* 签到 UA 包含 yiban_android，如果是 iOS，则为 yiban_iOS。 */, Cookie = $"csrf_token={_csrfToken}" }) // 还需在 cookie 中提供 csrf_token。
                .WithCookies(_jar);
            string signInContent;
            if (_outside) // 是否在校外。校内外带照片签到，AttachmentFileName 的位置不同。
            {
                var signInBody = new { OutState = "1", device.Code, device.PhoneModel /* 经测试只要 PhoneModel 对上即可。 */, SignInfo = JsonSerializer.Serialize(new { Reason = _reason, uploadedPhotoInfo.AttachmentFileName, LngLat = $"{_position[0]},{_position[1]}", Address = _address }) }; // SignInfo 是字符串。                                                                                                                                                                                                                                                                            //var signinBody = new { AttachmentFileName = attachmentFilename, OutState = "1", device.Code, device.PhoneModel /* 经测试只要 PhoneModel 对上即可。 */, SignInfo = JsonSerializer.Serialize(new { Reason = _reason, AttachmentFileName = attachmentFilename, LngLat = $"{_position[0]},{_position[1]}", Address = _address }) }; // SignInfo 是字符串。
                _logger.Debug($"{GetLogPrefix()}：发送请求：{reqSignIn.Url}，signInBody：{JsonSerializer.Serialize(signInBody, ServiceOptions.jsonSerializerOptions)}……");
                signInContent = await reqSignIn.PostUrlEncodedAsync(signInBody).ReceiveString();
            }
            else
            {
                var signInBody = new { uploadedPhotoInfo.AttachmentFileName, OutState = "1", device.Code, device.PhoneModel /* 经测试只要 PhoneModel 对上即可。 */, SignInfo = JsonSerializer.Serialize(new { Reason = "", AttachmentFileName = "", LngLat = $"{_position[0]},{_position[1]}", Address = _address }) }; // SignInfo 是字符串。
                _logger.Debug($"{GetLogPrefix()}：发送请求：{reqSignIn.Url}，signInBody：{JsonSerializer.Serialize(signInBody, ServiceOptions.jsonSerializerOptions)}……");
                signInContent = await reqSignIn.PostUrlEncodedAsync(signInBody).ReceiveString();
            }
            _logger.Debug($"{GetLogPrefix()}：收到晚点签到响应：{signInContent}。");
            YibanNightAttendanceSignInApiRes sigInRes = ParseYibanNightAttendanceSignInApiRes(JsonNode.Parse(signInContent!)!);
            if (sigInRes.Code != 0)
            {
                _logger.Error($"{GetLogPrefix()}：签到失败，服务端返回消息：{sigInRes.Msg}。");
                return false;
            }
            return true;
        }
    }
}
