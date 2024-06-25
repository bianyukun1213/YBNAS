# Hollis 的易班校本化晚点签到程序

&emsp;&emsp;本项目**仅供学习使用**，使用时**自负风险**。

&emsp;&emsp;针对**黑龙江科技大学**开发，其他学校请自行测试。

&emsp;&emsp;感谢 [yiban](https://github.com/Sricor/yiban) 项目提供参考。

## 安装

&emsp;&emsp;本程序基于 .NET 8 框架，采用独立部署模式，因此无需安装框架即可运行。

&emsp;&emsp;对于 Windows，只提供 x86 版本；解压 `YBNAS.<版本号>.win-x86.zip`，`YBNAS.exe` 即是程序入口。

&emsp;&emsp;对于 GNU/Linux，只提供 x64 版本；解压 `YBNAS.<版本号>.linux-x64.zip`，`YBNAS` 即是程序入口。使用前需赋予执行权限。

&emsp;&emsp;对于其他平台与架构，请自行编译。

&emsp;&emsp;在运行程序之前需**参照下文编写配置文件**。

## 配置

&emsp;&emsp;默认配置文件 `config.json` 位于程序目录。

&emsp;&emsp;可使用命令行参数 `--config-path <PATH>` 或 `-c <PATH>` 指定替代的配置文件读取路径。还可使用 `--cache-path <PATH>` 或 `-k <PATH>` 指定替代的缓存文件读取路径；使用 `--log-path <PATH>` 或 `-l <PATH>` 指定替代的日志写入路径。

&emsp;&emsp;可配置多账号批量签到。

``` JSON
{
  "AutoSignIn": true,
  "AutoExit": false,
  "Execute": "",
  "Proxy": "",
  "Shuffle": true,
  "MaxRunningTasks": 4,
  "MaxRetries": 3,
  "RandomDelay": [ 1, 10 ],
  "ExpireIn": 0,
  "SignInConfigs": [
    {
      "Enable": true,
      "Name": "张三",
      "Account": "账号（手机号码）",
      "Password": "密码",
      "Device": {
        "Code": "校本化“设备识别”页面的“唯一授权码”，选填，留空则从接口获取",
        "PhoneModel": "校本化“设备识别”页面的“手机型号”，选填，留空则从接口获取"
      },
      "Position": [
        126.65892872841522,
        45.820275900282255
      ],
      "Address": "黑龙江省哈尔滨市松北区浦源路2298号靠近黑龙江科技大学",
      "Photo": "",
      "Reason": "",
      "Outside": false,
      "TimeSpan": [
        21,
        50,
        22,
        40
      ]
    },
    {
      "Enable": true,
      "Name": "李四",
      "Account": "账号 2",
      "Password": "密码 2",
      "Device": {},
      "Position": [
        126.642464,
        45.756982
      ],
      "Address": "黑龙江省哈尔滨市南岗区花园街道西大直街国际饭店",
      "Photo": "D:\\sign_in_photo\\photo.jpg",
      "Reason": "跟国际友人聚餐。",
      "Outside": true,
      "TimeSpan": [
        21,
        50,
        22,
        40
      ]
    }
  ]
}
```

&emsp;&emsp;`AutoSignIn` 字段配置是否自动签到。如不自动签到，则等待用户按任意键签到。

&emsp;&emsp;`AutoExit` 字段配置完成所有任务的执行或程序运行出错后，是否自动退出。如不自动退出，则等待用户按任意键退出。

&emsp;&emsp;`Execute` 字段配置完成所有任务的执行后运行的命令，如 `code {%RESULT_TEMP%}`，它会调用 Visual Studio Code 打开结果文件。可使用的变量有结果文件 `{%RESULT_TEMP%}`、总签到配置条数 `{%SIGN_IN_CONFIG_COUNT%}`、已解析的签到配置条数 `{%TASK_COUNT%}`、运行中的任务数量 `{%TASKS_RUNNING%}`、已完成的任务数量 `{%TASKS_COMPLETE%}`、已跳过的任务数量 `{%TASKS_SKIPPED%}`、等待中的任务数量 `{%TASKS_WAITING%}`、已终止的任务数量 `{%TASKS_ABORTED%}`。“运行中的任务数量”和“等待中的任务数量”基本上没什么用。

&emsp;&emsp;`Proxy` 字段配置网络代理，须以 `http://`、`https://`、`socks4://`、`socks4a://` 或 `socks5://` 开头。若为空字符串，则不使用代理。

&emsp;&emsp;`Shuffle` 字段配置是否打乱签到顺序。若不打乱，则按照配置文件内的顺序签到。

&emsp;&emsp;`MaxRunningTasks` 字段配置初始同时运行任务数，可以简单理解为最多同时运行几个任务，内置值及配置默认值均为 `4`。

&emsp;&emsp;`MaxRetries` 字段配置每个任务最大的重试次数，若该任务重试次数已到达该值，就不再重试。内置值及配置默认值均为 `3`。在进行校本化认证的过程中，有时候会报 `易班接口请求出错~`，（报错就报错，还他妈发嗲，真是的……😓）目前原因不明，但在多次尝试后可能会成功。

&emsp;&emsp;`RandomDelay` 字段配置是否在获取签到信息和开始签到之间随机插入以秒为单位的延迟以使多账号批量签到的时间呈一定随机性，以及该随机延迟的范围。若两个值都是 `0`，则不延迟签到；若两个值相同且不是 `0`，则以该固定值作为延迟秒数，因此多账号批量签到的时间可能看起来很整齐；若两个值不同，则在该范围内取随机值作为延迟秒数。延迟最大可设 120s。

&emsp;&emsp;`ExpireIn` 字段配置以秒为单位的缓存有效期。以 `300` 为例：在程序重复运行时，对于每个签到账号，若上次签到成功的时刻距离此刻不超过 300s，则跳过此账号的签到。跨日期的情况不计算在内。内置值及默认值均为 `0`，即在程序重复运行时，对于每个签到账号，不论上次签到成功是何时，都重复签到。设为 `-1` 则总是跳过已经签到成功的账号。可删除 `cache` 文件重置缓存。

&emsp;&emsp;`SignInConfigs` 为签到任务配置，其中：

&emsp;&emsp;`Enable` 字段配置这条签到配置是否启用。未启用的签到配置在解析时跳过。

&emsp;&emsp;`Device` 字段（包含 `Code` 字段和 `PhoneModel` 字段）配置授权设备信息，可在易班校本化“设备识别”页面获取，留空则从接口获取。

&emsp;&emsp;`Position` 字段配置签到坐标，第一个值是经度，第二个值是纬度，默认坐标在第十八学生公寓。

&emsp;&emsp;`Address` 字段配置对应的签到地址。

&emsp;&emsp;`Position` 和 `Address` 字段所需值可以在[这个网页](https://lbs.amap.com/api/javascript-api/guide/services/geocoder)的“UI组件-拖拽选址”部分获取。

&emsp;&emsp;`Outside` 字段配置给定位置是否在常规的签到范围外，影响某些信息提交的逻辑，见下文。

&emsp;&emsp;`Photo` 字段配置附件照片的路径。照片要求为 JPEG 格式，且具有扩展名 `.jpg` 或 `.jpeg`。大小我没有主动设限，但服务端可能有限制，建议不要太大。在 Windows 系统下，路径中的反斜杠需要输两个。

&emsp;&emsp;程序运行时会根据 `Outside` 字段、`Photo` 字段和接口返回的信息自动判断是否提交照片。只有在正确填写了 `Photo` 字段后，学校要求必须提交照片，或学校要求范围外签到提交照片并且已将 `Outside` 设为 `true` 时才会真正提交。

&emsp;&emsp;`Reason` 字段配置范围外签到原因。

&emsp;&emsp;程序运行时会根据 `Outside` 字段自动判断是否提交范围外签到原因。只有将 `Outiside` 设为 `true` 时才会提交范围外签到原因。

&emsp;&emsp;对于**黑龙江科技大学**，签到坐标和地址在校外时，应填写 `Photo` 和 `Reason` 字段——尽管服务端可能并不验证照片和原因，但在 App 内这两项为必填信息。

&emsp;&emsp;`TimeSpan` 字段配置签到时间段，按顺序分别填开始小时、开始分钟、结束小时、结束分钟。程序运行时读取系统时间，若其不在此字段设定的时间段内，则跳过此用户的签到任务创建。另外，签到时也会动态获取当前时间，与校本化接口返回的签到时间段比对，若不在允许的时间段内，同样也会跳过签到。

## 使用的库

- [CommandLineParser](https://github.com/commandlineparser/commandline)
- [Flurl](https://github.com/tmenier/Flurl)
- [RSA-csharp](https://github.com/xiangyuecn/RSA-csharp)
- [NLog](https://github.com/NLog/NLog)
