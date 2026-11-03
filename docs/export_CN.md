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

## 本机联调记录

原导出输入与 `d8d61a33ad3aed30b376d514fd99ddb3d84a734f` 一致，tree 为
`97b95d3efe9ae74ae27e45c83809138f15bb18ba`。导出及 portable 锁定还原／构建曾使用
SDK `8.0.427` 通过；这些结果保留原归属，没有在以下网络检查中重跑。

**2026-11-03**，同一未签名应用在 macOS 27.0.1 arm64 上与新构建的 ARK 候选联调，
Godot 为 `4.7.2.stable.mono.official.ed1daf0bf`。应用的全部 386 个文件在执行前后均匹配
原哈希；没有重复导出包、准备模板、运行 Core 测试或 Editor import。

| 输入 / 检查 | 结果 |
| --- | --- |
| ARK 候选 | `8bdd734b9837015cab97f022c7258830ee889f47`；tree 为 `491c38bfd5eb6da86eeaf437f2fd1ccecbdbce14` |
| 新服务端构建 | Launcher、Bus、Kernel、Net、Utility 和 guest_room；独立 cache／runtime，回环端口 19492；自有进程实际加载该 app 和全部五个匹配 DSO |
| 可选插件开关 | `ARK_BUILD_PLUGIN_CONSUL`、`ARK_BUILD_PLUGIN_ETCD_CLIENT`、`ARK_BUILD_PLUGIN_KAFKA`、`ARK_BUILD_PLUGIN_OSSSDK` 均为 `OFF` |
| 协议输入 | 两端 schema 与 wire vectors 逐字节一致；Boost 1.92、已安装的 arknet 匹配 `a0dfe09cd543e75cfb5996e537ce2fa49f2a758f`，Protobuf 33.4 头／protoc／archive 匹配 |
| 两个可见导出客户端 | READY、入房、移动、自身及对方受伤 85/100 → 技能 85/80 → 休息 100/100 通过；Alice 2→3／Bob 1→4 重连后属性与序列重置；客户端与 runner 均退出 0 |
| 截图 | 两张均为 1120×824；已检查房间、YOU/PLAYER 标记及重置后的 HP/MP |
| 服务端关闭 | SIGINT 退出 0，没有强杀；监听端口已释放 |

本结果只证明这个**未签名、未公证的本机 arm64 开发应用**在回环环境中的运行。
原导出的严格签名检查因 Godot 上游模板签名不覆盖应用包而退出 1，本轮没有重跑。
执行时没有编辑器／SDK 环境变量。
Intel、Gatekeeper/下载分发、Windows/Linux、跨主机行为与生产用途均未验收。
详细来源记录和原始日志保存在本地 receipt。

官方参考：[macOS 导出](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_for_macos.html)、
[Godot .NET 下载](https://godotengine.org/download/macos/)。
