# Hollis 的易班校本化晚点签到程序

&emsp;&emsp;本项目**仅供学习使用**，使用时**自负风险**。

&emsp;&emsp;适用于黑龙江科技大学，其他学校未测试。我的学院在范围内签到时不需提交照片，因此没写相关代码，如果你在黑龙江科技大学并且你的学院在签到时必须提交照片，请联系我适配。

&emsp;&emsp;感谢 [yiban](https://github.com/Sricor/yiban) 项目提供参考。

## 安装

&emsp;&emsp;本程序基于 .NET 8.0 框架，采用独立部署模式，因此无需安装框架即可运行。

&emsp;&emsp;对于 Windows，只提供 32 位版本；解压 `YBNAS.<版本号>.win-x86.zip`，`YBNAS.exe` 即是程序入口。

&emsp;&emsp;对于 GNU/Linux，只提供 64 位版本；解压 `YBNAS.<版本号>.linux-x64.zip`，`YBNAS` 即是程序入口。使用前需赋予执行权限。

&emsp;&emsp;对于其他平台，请自行编译。

&emsp;&emsp;在运行程序之前需**参照下文编写配置文件**。

## 配置

&emsp;&emsp;配置文件 `config.json` 位于程序目录，可配置多账号批量签到。

``` JSON
{
  "AutoSignin": true,
  "AutoExit": false,
  "Proxy": "",
  "Shuffle": true,
  "MaxRunningTasks": 4,
  "MaxRetries": 3,
  "RandomDelay": [ 1, 10 ],
  "SigninConfigs": [
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
      "Photo": "D:\\signin_photo\\photo.jpg",
      "Reason": "跟国际友人聚餐。",
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

&emsp;&emsp;`AutoSignin` 字段配置是否自动签到。如不自动签到，则等待用户按任意键签到。

&emsp;&emsp;`AutoExit` 字段配置已完成所有任务的执行或程序运行出错后，是否自动退出。如不自动退出，则等待用户按任意键退出。

&emsp;&emsp;`Proxy` 字段配置网络代理，须以 `http://`、`https://`、`socks4://`、`socks4a://` 或 `socks5://` 开头。若为空字符串，则不使用代理。

&emsp;&emsp;`Shuffle` 字段配置是否打乱签到顺序。若不打乱，则按照配置文件内的顺序签到。

&emsp;&emsp;`MaxRunningTasks` 字段配置初始同时运行任务数，可以简单理解为最多同时运行几个任务，内置值及配置默认值均为 `4`。

&emsp;&emsp;`MaxRetries` 字段配置每个任务最大的重试次数，若该任务重试次数已到达该值，就不再重试。内置值及配置默认值均为 `3`。在进行校本化认证的过程中，有时候会报 `易班接口请求出错~`，（报错就报错，还他妈发嗲，真是的……😓）目前原因不明，但在多次尝试后可能会成功。

&emsp;&emsp;`RandomDelay` 字段配置是否在获取签到信息和开始签到之间随机插入以秒为单位的延迟以使多账号批量签到的时间呈一定随机性，以及该随机延迟的范围。若两个值都是 `0`，则不延迟签到；若两个值相同且不是 `0`，则以该固定值作为延迟秒数，因此多账号批量签到的时间可能看起来很整齐；若两个值不同，则在该范围内取随机值作为延迟秒数。延迟最大可设 120s。

&emsp;&emsp;`SigninConfigs` 为签到任务配置，其中：

&emsp;&emsp;`Enable` 字段配置这条签到配置是否启用。未启用的签到配置在解析时跳过。

&emsp;&emsp;`Device` 字段（包含 `Code` 字段和 `PhoneModel` 字段）配置授权设备信息，可在易班校本化“设备识别”页面获取，留空则从接口获取。

&emsp;&emsp;`Position` 字段配置签到坐标，第一个值是经度，第二个值是纬度，默认坐标在第十八学生公寓。

&emsp;&emsp;`Address` 字段配置对应的签到地址。

&emsp;&emsp;`Position` 和 `Address` 所需值可以在[这个网页](https://lbs.amap.com/api/javascript-api/guide/services/geocoder)的“UI组件-拖拽选址”部分获取。

&emsp;&emsp;`Photo` 字段配置附件照片的路径。照片要求为 JPEG 格式，且具有扩展名 `.jpg` 或 `.jpeg`。在 Windows 系统下，路径中的反斜杠需要输两个。

&emsp;&emsp;`Reason` 字段配置签到原因。

&emsp;&emsp;对于**黑龙江科技大学**，**签到**坐标和地址**在校内时**，**不应填写 `Photo` 和 `Reason` 字段**，而是保留为空字符串。

&emsp;&emsp;`TimeSpan` 字段配置签到时间段，按顺序分别填开始小时、开始分钟、结束小时、结束分钟。程序运行时读取系统时间，若其不在此字段设定的时间段内，则跳过此用户的签到任务创建。另外，签到时也会动态获取当前时间，与校本化接口返回的签到时间段比对，若不在允许的时间段内，同样也会跳过签到。

## 使用的库

- [Flurl](https://github.com/tmenier/Flurl)
- [RSA-csharp](https://github.com/xiangyuecn/RSA-csharp)
- [NLog](https://github.com/NLog/NLog)
