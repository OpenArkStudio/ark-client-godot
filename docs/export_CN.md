# macOS 本地开发包导出

在 macOS 上使用 Godot **4.7.2 .NET**、匹配的 **.NET 导出模板**和 SDK
**8.0.427**。按[运行说明](run_CN.md)选择仓库外的工具，并将 SDK 加入 `PATH`。
通过 **Editor > Manage Export Templates** 安装模板。需要联网获取或已缓存的
`osx-arm64` / `osx-x64` 运行时包。可见 smoke 还要求图形桌面，以及协议匹配、
大厅为空的 guest-room 服务端。

```sh
# 指定新的绝对路径；已有应用不会被覆盖。
bash tools/export-macos.sh /absolute/output/ArkGuestRoom.app
EXPORTED_CLIENT="/absolute/output/ArkGuestRoom.app/Contents/MacOS/ARK Guest Room" \
  bash tools/smoke-two-clients.sh 127.0.0.1 19100 --render
```

| 设置 | 行为 |
| --- | --- |
| `macOS development` | 官方 Universal 2 调试模板；关闭签名与公证 |
| 应用包 | 包含 arm64 / x86_64 的 C# 程序集及独立运行时；启用 ETC2/ASTC 导入 |
| Core 锁文件 | macOS 使用独立 RID 锁；保留原 portable 锁及锁定还原 |
| 导出检查 | 拒绝引擎错误诊断、缺失的可执行文件或架构程序集 |
| `EXPORTED_CLIENT` | 可执行文件绝对路径；不传入 `--path`，使用包内资源 |
| Smoke 产物 | 日志与截图保存在输出的临时目录；省略 `--render` 即无头运行 |

**2026-11-03** 本地验收环境：macOS 27.0.1、Apple M4 Pro arm64、
Godot `4.7.2.stable.mono.official.ed1daf0bf`、SDK `8.0.427`。

| 输入 / 检查 | 结果 |
| --- | --- |
| 客户端源码 | `d8d61a33ad3aed30b376d514fd99ddb3d84a734f` |
| 客户端 tree | `97b95d3efe9ae74ae27e45c83809138f15bb18ba` |
| ARK 服务端源码 | `c9aa911a4893ffa2681925e8ebf13e703f8f4c16`；复用该候选的六个二进制，独立回环端口 19486 |
| 导出 / portable 锁定还原与构建 | 通过，退出 0；原 portable 锁不变 |
| 两个可见导出客户端 | READY、入房、移动、自身及对方受伤 → 技能 → 休息、新身份重连通过；客户端与 runner 均退出 0 |
| 截图 | 两张均为 1120×824；已检查房间、YOU/PLAYER 标记及重置后的 HP/MP |
| 服务端关闭 | SIGINT 退出 0，没有强杀；监听端口已释放 |

这是**未签名、未公证的本地开发应用**。Godot 上游模板保留的签名不覆盖此应用包，
严格签名校验退出 1。本机 arm64 已在没有编辑器/SDK 环境变量的条件下执行通过。
Intel、Gatekeeper/下载分发、Windows/Linux、跨主机行为与生产用途均未验收。
详细来源记录和原始日志保存在本地 receipt。

官方参考：[macOS 导出](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_for_macos.html)、
[Godot .NET 下载](https://godotengine.org/download/macos/)。
