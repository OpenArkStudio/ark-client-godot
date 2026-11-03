# macOS 本地开发包导出

在 macOS 上使用 Godot **4.7.2 .NET** 编辑器、版本匹配的 **.NET 导出模板**和
.NET SDK **8.0.427**。按[运行说明](run_CN.md)选择仓库外的引擎与 SDK，并将 SDK
目录加入 `PATH`。通过 **Editor > Manage Export Templates** 安装匹配的模板，
或将模板解压到引擎的外部模板目录。Godot 独立模式可以将模板与设置保存在常规用户
目录之外，参见[官方数据路径说明](https://docs.godotengine.org/en/4.7/tutorials/io/data_paths.html#self-contained-mode)。
首次导出还需要联网获取 `osx-arm64` 和 `osx-x64` 的 .NET 运行时包，或使用已填充的
外部 NuGet 缓存。工具、包缓存和应用产物均不得提交到版本控制。

```sh
# 指定一个新的绝对路径；脚本不会覆盖已有应用。
bash tools/export-macos.sh /absolute/output/ArkGuestRoom.app
```

仓库中的 `macOS development` preset 使用官方 **Universal 2** 模板导出调试应用，
关闭代码签名与公证。引擎会导入资源，并将每种架构的 C# 程序集与运行时发布到包内。
Core 依赖继续锁定还原：macOS 发布选择独立的 RID 锁文件，普通可移植构建仍使用
原锁文件。项目启用了 Godot 通用架构导出要求的 ETC2/ASTC 导入。脚本还会检查引擎
错误诊断，避免 .NET 发布失败时引擎仍退出 0、留下不完整应用的情况。

该包仅用于本地开发，没有执行应用签名或公证。上游模板可执行文件可能保留 Godot
自身的签名，该签名不代表此应用包已签名。该包没有分发验收；从网络下载的
未签名应用可能被 Gatekeeper 拦截。脚本不会修改系统设置或移除隔离属性。

直接启动包内的可执行文件，并在客户端参数前保留 `--` 分隔符：

```sh
"/absolute/output/ArkGuestRoom.app/Contents/MacOS/ARK Guest Room" \
  -- --host=127.0.0.1 --port=19100 --name=Alice --connect
```

自动验收时，先启动协议匹配且大厅为空的 guest-room 服务端，然后用已有双客户端
smoke 入口启动导出包：

```sh
EXPORTED_CLIENT="/absolute/output/ArkGuestRoom.app/Contents/MacOS/ARK Guest Room" \
  bash tools/smoke-two-clients.sh 127.0.0.1 19100 --render
```

`EXPORTED_CLIENT` 必须是可执行文件的绝对路径。此模式不传入 `--path`，因此两个
进程均使用包内资源与程序集。测试复用场景中的入房、移动、自身和对方 HP/MP、
断线重连及新身份断言，并在输出的临时目录保留日志与视口截图。`--render` 需要
图形桌面；省略该参数即可无头运行。未设置 `EXPORTED_CLIENT` 时仍通过 `GODOT`
启动源项目。导出成功、包内程序运行、本地服务端集成与视觉检查分别证明不同事实。
通用架构应用本身不证明 Intel Mac 可执行。此流程不验证 Windows、Linux、跨主机
行为、发布包或生产用途。

官方参考：[macOS 导出](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_for_macos.html)
与 [Godot .NET 下载](https://godotengine.org/download/macos/)。
