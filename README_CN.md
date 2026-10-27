# ark-client-godot

基于 Godot C# 的 OpenArkStudio [ark](https://github.com/OpenArkStudio/ark) 客户端。
加入共享二维访客房间后，可使用 WASD 或方向键移动，并在断线后以新访客身份重新加入。
玩家位置和房间成员列表以服务端快照为准。

[English](README.md)

## 运行

前置条件：Godot **4.7.2 .NET**、**.NET 8 SDK**，以及配套的 ark `guest_room` 示例服务端。
标准版 Godot 编辑器不能构建本 C# 项目。本示例使用桌面端原始 TCP，不面向 Web 导出。

```sh
bash tools/test-core.sh
bash tools/build.sh
godot --path . -- --host=127.0.0.1 --port=19100 --name=Alice --connect
```

命令中的 `godot` 应指向 Godot .NET 可执行文件。使用便携运行时执行 Shell 脚本时，
通过 `DOTNET` 和 `GODOT` 指定可执行文件路径。若引擎无法发现运行时，
将 `DOTNET_ROOT` 指向 SDK 与运行时所在目录。

再以 Bob 的名字启动一个客户端，即可观察两名服务端管理的玩家。
点击 **Disconnect** 离开房间，点击 **Connect / Rejoin** 创建新身份。
意外断线后最多自动重试五次，间隔依次为 1、2、4、8、8 秒；界面保留失败信息。
房间最多容纳 16 名访客，访客名字最多包含 64 个 UTF-8 编码字节。
头像标签限制为 160 像素，超出时显示省略号；发生重叠时在场地内错行显示。
房间状态中的完整名字保持不变。

操作与验证说明见 [运行文档](docs/run_CN.md)，协议见 [线协议约定](docs/protocol_CN.md)。
示例提供临时访客会话，不提供账号凭据、身份续接、持久化、加密或生产容量验证。
历史 Unity 登录与游戏消息使用不同协议。

## 目录

- `src/Ark.Client.Core`：协议帧、TCP 传输和房间状态，不依赖 Godot。
- `Scripts` 与 `Scenes`：Godot 输入、绘制、状态显示与重连生命周期。
- `tests`：编译执行的 C# 检查及共享协议向量。

项目采用 [Apache-2.0](LICENSE) 许可证。贡献约定见 [AGENTS.md](AGENTS.md)。
