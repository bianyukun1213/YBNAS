//using NLog;
//using RestSharp;
//using System;
//using System.Collections.Generic;
//using System.ComponentModel;
//using System.Linq;
//using System.Security.Cryptography;
//using System.Text;
//using System.Text.Json;
//using System.Text.Json.Nodes;
//using System.Threading.Tasks;

//namespace YBNAS
//{
//    // 此登录接口实际上用不了……因为不知道密码咋加密。我也不会 Android 逆向。



//    /* 实际登录数据格式，包含很多不需要的信息。
//    {
//        "response": 100,
//        "message": "请求成功",
//        "is_mock": false,
//        "data": {
//            "user": {
//                "sex": "0",
//                "name": "姓名",
//                "nick": "易友xxx",
//                "pic": {
//                    "s": "https://img02.fs.yiban.cn/10086/avatar/user/68",
//                    "m": "https://img02.fs.yiban.cn/10086/avatar/user/88",
//                    "b": "https://img02.fs.yiban.cn/10086/avatar/user/160",
//                    "o": "https://img02.fs.yiban.cn/10086/avatar/user/"
//                },
//                "user_id": 10086,
//                "phone": "13008888888",
//                "authority": "1",
//                "isSchoolVerify": true,
//                "school": {
//                    "isVerified": true,
//                    "schoolName": "哈尔滨佛学院",
//                    "schoolId": 10086,
//                    "schoolOrgId": 10010,
//                    "collegeName": "土木工程学院",
//                    "collegeId": 114,
//                    "className": "打胶20-1班",
//                    "classNameSerial": "打胶20-1班",
//                    "classId": 514,
//                    "joinSchoolYear": "2020",
//                    "type": 1
//                }
//            },
//            "access_token": "32位字符",
//            "urlBlackListLastUpdateTime": 1693820059
//        }
//    }
//    */

//    struct LoginData
//    {
//        public string? name;
//        public string nick;
//        public int userId;
//        public string phone;
//        public string? schoolName;
//        public int? schoolId;
//        public int? schoolOrgId;
//        public string? collegeName;
//        public int? collegeId;
//        public string? className;
//        public string? classNameSerial;
//        public int? classId;
//        public string accessToken;

//        public override readonly string ToString()
//        {
//            return $"name: '{name}', nick: '{nick}', userid: {userId}, phone: '{phone}', schoolName: '{schoolName}', schoolId: {schoolId}, schoolOrgId: {schoolOrgId}, collegeName: '{collegeName}', collegeId: {collegeId}, className: '{className}', classNameSerial: '{classNameSerial}', accessToken: '{accessToken}'";
//        }
//    }

//    internal class YibanMobileClient
//    {
//        readonly Logger logger = LogManager.GetCurrentClassLogger();
//        readonly RestClient client;
//        const string baseUrl = "https://m.yiban.cn/api/v4/";
//        const string appVersion = "5.0.18";

//        public YibanMobileClient()
//        {
//            logger.Debug("构造 YibanMobileClient。");
//            client = new RestClient(baseUrl);
//            client.AddDefaultHeader("AppVersion", appVersion);
//        }

//        public bool Login(string mobile, string password, out LoginData loginData/*, string loginToken = ""*/)
//        {
//            logger.Info($"开始登录，账号：{mobile}，密码：{password}。");
//            var request = new RestRequest("passport/login", Method.Post);
//            // 手动登录的必填参数，如果是自动登录（passport/autologin），则只需头部包含 AppVersion 和 loginToken（即 access_token），未实现自动登录，因为不需要。
//            var loginParams = new
//            {
//                ct = 2, // client type?
//                mobile,
//                password,
//                identify = Guid.NewGuid().ToString("N")[16..], // 16 位设备识别码，登录时随机生成。其实应该是 identity 吧……identify 是动词。
//                device = "Xiaomi:Redmi K30 Pro"
//            };
//            request.AddObject(loginParams);
//            logger.Debug($"向 {baseUrl + request.Resource} 发送请求，ct：{loginParams.ct}，mobile：{loginParams.mobile}，password: {loginParams.password}，identify：{loginParams.identify}，device：{loginParams.device}。");
//            var res = client.Execute(request);
//            logger.Debug($"收到响应：{res.Content}。");
//            JsonNode resNode = JsonNode.Parse(res.Content!)!; // 返回的 JSON。
//            int resCode = (int)resNode["response"]!;
//            string resMsg = (string)resNode["message"]!;
//            if (resCode != 100) // 登录失败。
//            {
//                logger.Error($"{mobile} 登录失败，服务端返回消息：{resMsg}。");
//                loginData = new();
//                return false;
//            }
//            // 不使用反序列化，手动一一赋值，因为易班接口返回的数据格式可能改变，另外很多数据不需要。
//            JsonNode resData = resNode["data"]!;
//            loginData = new()
//            {
//                name = (string?)resData["user"]!["name"],
//                nick = (string)resData["user"]!["nick"]!,
//                userId = (int)resData["user"]!["user_id"]!,
//                phone = (string)resData["user"]!["phone"]!,
//                schoolName = (string?)resData["user"]!["school"]?["schoolName"],
//                schoolId = (int?)resData["user"]!["school"]?["schoolId"],
//                schoolOrgId = (int?)resData["user"]!["school"]?["schoolOrgId"],
//                collegeName = (string?)resData["user"]!["school"]?["collegeName"],
//                collegeId = (int?)resData["user"]!["school"]?["collegeId"],
//                className = (string?)resData["user"]!["school"]?["className"],
//                classNameSerial = (string?)resData["user"]!["school"]?["classNameSerial"],
//                classId = (int?)resData["user"]!["school"]?["classId"],
//                accessToken = (string)resData["access_token"]!
//            };
//            logger.Debug($"登录成功，构造 loginData：{loginData}。");
//            return true;
//        }
//    }
//}
