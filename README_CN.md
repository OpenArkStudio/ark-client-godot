# ark-client-godot

基于 Godot C# 的 OpenArkStudio [ark](https://github.com/OpenArkStudio/ark) 客户端。
加入共享二维访客房间后，可使用 WASD 或方向键移动，并在断线后以新访客身份重新加入。
玩家位置和房间成员列表以服务端快照为准。
HP/MP 条以及 **Take hit**、**Use skill**、**Rest** 按钮演示服务端拥有的业务属性。
点击按钮仅提议动作；只有确认快照能改变数值或产生浮动增减反馈。动作拒绝不会断开房间。

[English](README.md)

## 运行

前置条件：Godot **4.7.2 .NET**、**.NET 8 SDK**，以及配套的 **ARK2** ark `guest_room` 示例服务端。
必须同时升级两端；旧 ARK1 客户端或服务端会被拒绝。
标准版 Godot 编辑器不能构建本 C# 项目。本示例使用桌面端原始 TCP，不面向 Web 导出。

```sh
bash tools/test-core.sh
bash tools/build.sh
godot --path . -- --host=127.0.0.1 --port=19100 --name=Alice --connect
```

命令中的 `godot` 应指向 Godot .NET 可执行文件。使用便携运行时执行 Shell 脚本时，
通过 `DOTNET` 和 `GODOT` 指定可执行文件路径。若引擎无法发现运行时，
将 `DOTNET_ROOT` 指向 SDK 与运行时所在目录。

再以 Bob 的名字启动一个客户端，即可共同探索带插画场景的村庄广场。
点击 **Leave** 离开房间，点击 **Rejoin** 创建新身份。
意外断线后最多自动重试五次，间隔依次为 1、2、4、8、8 秒；界面保留失败信息。
房间最多容纳 16 名访客，访客名字最多包含 64 个 UTF-8 编码字节。
头像标签限制为 160 像素，超出时显示省略号；发生重叠时在场地内错行显示。
房间状态中的完整名字保持不变。
地形、村庄装饰与动画角色来自 Pixel-Boy 和 AAA 的 CC0 Ninja Adventure 素材。
本地忍者使用 **YOU** 标签、菱形及双地面环；其他成员使用 **PLAYER** 标签与空心标记。
行走和朝向依据实际显示位移更新。见 [素材署名](docs/third-party-notices_CN.md)。
紧凑 HUD 展示连接、人数和真实应用 RTT。展开 **NETWORK** 可查看实际远端、序列号、目标、确认坐标、快照年龄、
应用 ping RTT 及消息帧与字节累计值。青色标识自己，琥珀色标识其他成员。
**Visual smoothing** 仅平滑角色绘制位置。原始服务端标记默认关闭，可在 NETWORK 中开启；
地面身份环跟随显示位置。场景装饰不增加碰撞规则。
可编辑的 [数据流程图](docs/data-sync_CN.md) 说明完整路径。

操作与验证说明见 [运行文档](docs/run_CN.md)，协议见 [线协议约定](docs/protocol_CN.md)。
示例提供临时访客会话，不提供账号凭据、身份续接、持久化、加密或生产容量验证。
历史 Unity 登录与游戏消息使用不同协议。

## 目录

- `src/Ark.Client.Core`：协议帧、TCP 传输和房间状态，不依赖 Godot。
- `Scripts` 与 `Scenes`：Godot 输入、绘制、状态显示与重连生命周期。
- `tests`：编译执行的 C# 检查及共享协议向量。

项目采用 [Apache-2.0](LICENSE) 许可证。贡献约定见 [AGENTS.md](AGENTS.md)。
