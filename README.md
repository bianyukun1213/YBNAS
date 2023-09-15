# Hollis 的易班校本化晚点签到程序

&emsp;&emsp;适用于黑龙江科技大学，其他学校未测试。

&emsp;&emsp;感谢 [yiban](https://github.com/Sricor/yiban) 项目提供参考。

## 安装

&emsp;&emsp;本程序基于 .NET 6.0 框架，采用独立部署模式，因此无需安装框架即可运行。

&emsp;&emsp;对于 Windows，解压 `YBNAS.<版本号>.x86.zip`，`YBNAS.exe` 即是程序入口。其他平台未提供可执行文件。

&emsp;&emsp;在运行程序之前需**参照下文编写配置文件**。

## 配置

&emsp;&emsp;配置文件 `config.json` 位于程序目录，可配置多账号批量签到。

``` JSON
{
  "MaxRunningTasks": 4,
  "RandomDelay": true,
  "SigninConfigs": [
    {
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
      "TimeSpan": [
        21,
        50,
        22,
        20
      ]
    },
    {
      "Account": "账号 2",
      "Password": "密码 2",
      "Device": {},
      "Position": [
        126.65892872841522,
        45.820275900282255
      ],
      "Address": "黑龙江省哈尔滨市松北区浦源路2298号靠近黑龙江科技大学",
      "TimeSpan": [
        21,
        50,
        22,
        20
      ]
    }
  ]
}
```

&emsp;&emsp;`MaxRunningTasks` 字段配置初始同时运行任务数，可以简单理解为最多同时运行几个任务，内置值及配置默认值均为 `4`。

&emsp;&emsp;`RandomDelay` 字段配置是否在获取签到信息和开始签到之间随机插入 `1s-10s` 延迟，使多账号批量签到的时间呈一定随机性。

&emsp;&emsp;`SigninConfigs` 为签到任务配置，其中：

&emsp;&emsp;`Device` 字段（包含 `Code` 字段和 `PhoneModel` 字段）配置授权设备信息，可在易班校本化“设备识别”页面获取，留空则从接口获取。

&emsp;&emsp;`Position` 字段配置签到坐标，第一个值是经度，第二个值是纬度，默认坐标在第十八学生公寓。

&emsp;&emsp;`Address` 字段配置签到地址。

&emsp;&emsp;`Position` 和 `Address` 所需值可以在[这个网页](https://lbs.amap.com/api/javascript-api/guide/services/geocoder)的“UI组件-拖拽选址”部分获取。

&emsp;&emsp;`TimeSpan` 字段配置签到时间段，按顺序分别填开始小时、开始分钟、结束小时、结束分钟。程序运行时读取系统时间，若其不在此字段设定的时间段内，则跳过此用户的签到任务创建。另外，签到时也会动态获取当前时间，与校本化接口返回的签到时间段比对，若不在允许的时间段内，同样也会跳过签到。
