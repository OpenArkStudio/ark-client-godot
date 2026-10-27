# 运行与验证

使用 Godot 4.7.2 .NET 和 .NET 8 SDK。引擎与 SDK 放在仓库外。
可通过环境变量选择便携 SDK，无需修改全局工具：

```sh
export DOTNET_ROOT=/absolute/path/to/dotnet-sdk
export DOTNET="$DOTNET_ROOT/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export GODOT=/absolute/path/to/Godot_mono.app/Contents/MacOS/Godot
```

Godot 安装包包含本地 Godot NuGet 包。无法在线还原时，可在仓库外配置 NuGet 包源，
指向安装包中的 `Contents/Resources/GodotSharp/Tools/nupkgs` 目录。
不要提交个人运行时路径，也不要替换全局 NuGet 配置。

1. 启动配套的 ark `guest_room` 示例，指定应用清单和可用监听端口。
   服务端必须使用 `ARK1` 示例消息头；已有 Login 监听器不实现本访客协议。
2. 执行 `bash tools/test-core.sh`。脚本编译永久 C# 检查，并通过真实回环 TCP
   验证分段接收、重连缓冲区清理和可见的连接拒绝结果。
3. 执行 `bash tools/build.sh`，构建 Godot C# 项目。
4. 执行 `bash tools/check-engine.sh`，解析场景并进行短时离线无界面运行。
5. 执行 `bash tools/smoke-two-clients.sh 127.0.0.1 19100`，
   将两个真实引擎客户端连接至运行中的服务端。
   每个客户端必须观察两名玩家及服务端确认的移动，以新 ID 重新加入，
   再观察另一客户端的新 ID，并输出 `SMOKE PASS`。
   脚本将日志保留在本地临时目录；检查失败时返回非零退出码。
6. 在 .NET 编辑器中打开 `project.godot`，检查可见界面。
   启动两个窗口，分别以 Alice 和 Bob 加入，使用 WASD 或方向键移动，再断开并重新加入。
   确认成员列表和位置随服务端快照更新。

主场景为 `Scenes/GuestRoom.tscn`，C# 节点为 `Scripts/GuestRoom.cs`。
`GuestRoomHud.cs` 管理原生容器、可折叠诊断与成员列表；`GuestRoomWorld.cs` 绘制程序角色及原始服务端圆环。
固定 960×540 场地与侧栏共用可调整窗口大小的 1384×800 逻辑画布。
切换 **Visual smoothing** 可比较平滑显示与原始快照，展开 **Show network details** 可比较最近的提议序列号、目标和确认值。
RTT 通过应用 nonce 匹配测量，不属于 TCP ACK 或单向延迟。定义见 [数据流程与观测值](data-sync_CN.md)。
客户端参数位于 Godot 的 `--` 分隔符之后：

```sh
"$GODOT" --path . -- --host=127.0.0.1 --port=19100 --name=Alice --connect
```

服务端不可达时，界面显示错误并进行次数有限的重试。
无效房间消息会导致客户端关闭连接并显示协议错误。
业务请求被拒绝时，界面保留错误信息，需手动重新加入。
集成检查失败后，应先检查服务端错误及日志，再决定是否重试。

可移植代码检查、Godot 编译、引擎无界面执行、真实服务端集成和可见界面检查分别证明不同事实。
构建通过本身不能证明玩法或服务端兼容性。
这些检查不代表账号登录、生产部署、导出安装包、跨主机网络或容量验证。

本地结果及证明边界见 [验证范围](validation_CN.md)。

还原并构建项目后，执行 `bash tools/check-format.sh`，验证 C# 空白格式及控制流的显式花括号。
`Client checks` 工作流选择 SDK `8.0.427`，在 Linux、macOS 与 Windows 上运行可移植代码检查及格式检查。
Linux 任务还验证官方 Godot 4.7.2 .NET 压缩包的固定 SHA-512，构建并导入场景，
且要求离线引擎执行输出 C# 就绪标记。托管检查不启动 ark 服务端，因此不证明双客户端框架集成。
