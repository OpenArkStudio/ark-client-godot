# 运行与验证

使用 Godot 4.7.2 .NET 和 .NET 8 SDK。引擎与 SDK 放在仓库外。首次还原固定的官方 Google.Protobuf 3.33.4 NuGet 包
需要网络或已填充的仓库外包缓存。
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
   服务端必须采用匹配的 Protobuf v1 schema 与 `ARK2` 示例消息头；已有 Login 监听器不实现本访客协议。
2. 执行 `bash tools/test-core.sh`。脚本编译永久 C# 检查，并通过真实回环 TCP
   验证分段接收、重连缓冲区清理和可见的连接拒绝结果。
3. 执行 `bash tools/build.sh`，构建 Godot C# 项目。
4. 执行 `bash tools/check-engine.sh`，解析场景并进行短时离线无界面运行。
5. 执行 `bash tools/smoke-two-clients.sh 127.0.0.1 19100`，
   将两个真实引擎客户端连接至运行中的服务端。
   每个客户端必须观察两名玩家及服务端确认的移动，确认双方受伤 → 技能 → 休息的已提交状态，
   对应动作序列号 1 → 2 → 3，然后以新 ID 重新加入。
   双方新身份的 HP/MP 必须为 100、动作序列号为零，且没有重放的待确认动作。
   日志必须包含 `SMOKE PEER ATTRIBUTES`、`SMOKE ATTRIBUTES` 与 `SMOKE PASS`。
   脚本将日志保留在本地临时目录；检查失败时返回非零退出码。
6. 在 .NET 编辑器中打开 `project.godot`，检查可见界面。
   启动两个窗口，分别以 Alice 和 Bob 加入，使用 WASD 或方向键移动，再断开并重新加入。
   确认成员列表和位置随已提交的服务端基线与增量更新。

主场景为 `Scenes/GuestRoom.tscn`，C# 节点为 `Scripts/GuestRoom.cs`。
`GuestRoomHud.cs` 管理原生容器、可折叠诊断与成员列表；`GuestRoomWorld.cs` 绘制授权像素素材组成的广场与角色。
固定 960×540 场地居中放置在可缩放的 1120×824 逻辑画布内，诊断默认折叠。
切换 **Visual smoothing** 可比较平滑显示与原始确认状态，展开 **NETWORK** 可比较最近的提议序列号、目标和确认值。
RTT 通过应用 nonce 匹配测量，不属于 TCP ACK 或单向延迟。定义见 [数据流程与观测值](data-sync_CN.md)。
在 NETWORK 中开启 **Raw server markers** 可观察权威位置；默认地面环用于身份识别。
本地玩家有 YOU 文字、菱形与双环；其他成员有 PLAYER 文字与空心标记。
精灵行走和朝向依据实际显示位移更新。装饰仅用于显示，不增加碰撞。
玩家头上的 HP/MP 条显示最近已提交服务端状态中的数值。
底部紧凑面板提供 **Take hit**（自己的 HP 减少 15）、**Use skill**（自己的 MP 减少 20，不足时拒绝）
和 **Rest**（两项分别增加 20，由服务端限制到上限）。同一时间只保留一个待确认动作。
移动仍然可用，HP 为零也不禁用。匹配的动作拒绝清除对应请求并显示反馈，不离开房间；
仅移动更新的增量不能确认动作。重新加入会清除动作且不重试。
NETWORK 展示属性上限、确认的 `action_seq` 以及待确认和已提议动作。
客户端参数位于 Godot 的 `--` 分隔符之后：

```sh
"$GODOT" --path . -- --host=127.0.0.1 --port=19100 --name=Alice --connect
```

服务端不可达时，界面显示错误并进行次数有限的重试。
无效房间消息会导致客户端关闭连接并显示协议错误。
加入被拒绝时，界面保留错误信息，需手动重新加入；关联的动作拒绝不关闭连接。
集成检查失败后，应先检查服务端错误及日志，再决定是否重试。

可移植代码检查、Godot 编译、引擎无界面执行、真实服务端集成和可见界面检查分别证明不同事实。
构建通过本身不能证明玩法或服务端兼容性。
这些检查不代表账号登录、生产部署、导出安装包、跨主机网络或容量验证。

本地结果及证明边界见 [验证范围](validation_CN.md)。

还原并构建项目后，执行 `bash tools/check-format.sh`，验证 C# 空白格式及控制流的显式花括号。
`Client checks` 工作流选择 SDK `8.0.427`，在 Linux、macOS 与 Windows 上运行可移植代码检查及格式检查。
Linux 任务还验证官方 Godot 4.7.2 .NET 压缩包的固定 SHA-512，构建并导入场景，
且要求离线引擎执行输出 C# 就绪标记。托管检查不启动 ark 服务端，因此不证明双客户端框架集成。

Protobuf 协议要求同时更新服务端与客户端。历史 JSON 房间运行及托管结果不能验证本协议；
证明范围分列于[验证文档](validation_CN.md)。

执行 `bash tools/check-protocol.sh /path/to/matching/ark`，检查固定 SHA 及两端 schema/向量逐字节一致。
安装 protoc 33.4 后，执行
`PROTOC=/path/to/protoc-33.4 bash tools/generate-protocol.sh --check`，复现入仓的官方生成代码。
普通 C# 构建与托管可移植检查不需要安装 protoc。

引擎联调前，可执行 `bash tools/test-live.sh 127.0.0.1 19100`，将两个无界面可移植 C# 客户端
连接到初始为空的真实房间。检查后加入成员、移动与 HP/MP 增量、上限处休息动作、成员移除、
新身份重连与 ping，要求每个连接仅收到一次完整基线。无效消息、修订缺口和原子拒绝
由独立永久向量及回环测试验证。此 runner 本身不能证明服务端同次 flush 合并。
