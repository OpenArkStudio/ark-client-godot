# ark-client-godot

**基于 Godot 与 C# 的 [ark](https://github.com/OpenArkStudio/ark) 二维联机演示。**

[English](README.md) · [简体中文](README_CN.md) · [运行指南](docs/run_CN.md) · [数据同步](docs/data-sync_CN.md)

![两个访客在村庄中互动，展示玩家身份标记与同步的 HP/MP](docs/images/room.png)

用两个客户端共同探索村庄，观察输入如何成为服务端维护的状态。一起移动、改变 HP/MP、断线重连，也可以展开面板查看场景背后的网络消息。

| 演示内容 | 可以观察到什么 |
| --- | --- |
| 共享房间 | 访客加入与离开、成员列表同步、服务端确认的位置 |
| 玩家身份 | 自己使用 **YOU**、菱形与双地面环；其他玩家使用 **PLAYER** 与空心标记 |
| HP 与 MP | 服务端校验动作、数值条以及确认后的属性变化反馈 |
| 连接恢复 | 可见的连接状态、有次数限制的重试、重新连接后的新访客会话 |
| 网络诊断 | 应用 RTT、帧与字节统计、序号、提议位置与确认位置对照 |

## 本地体验

需要准备：

- **Godot 4.7.2 .NET**：标准版编辑器无法运行此 C# 项目。
- **.NET 8 SDK**。
- 配套的 ark **ARK2** [`guest_room` 服务端](https://github.com/OpenArkStudio/ark/tree/main/examples/guest_room)，监听 `127.0.0.1:19100`。请先按其说明构建并启动，无需数据库或 Registry 服务。

克隆并构建客户端：

```sh
git clone https://github.com/OpenArkStudio/ark-client-godot.git
cd ark-client-godot
bash tools/build.sh
```

将 `godot` 指向 Godot .NET 可执行文件，在此目录打开**两个终端**：

```sh
# 终端 1
godot --path . -- --host=127.0.0.1 --port=19100 --name=Alice --connect
```

```sh
# 终端 2
godot --path . -- --host=127.0.0.1 --port=19100 --name=Bob --connect
```

便携工具链、编辑器设置与故障排查见[运行指南](docs/run_CN.md)。

## 房间操作

| 操作 | 效果 |
| --- | --- |
| WASD / 方向键 | 移动自己的角色；另一个客户端显示服务端确认的位置 |
| **Take hit** | 自己的 HP 减少 15 |
| **Use skill** | MP 足够时消耗 20 |
| **Rest** | HP 与 MP 各恢复 20，不超过各自上限 |
| **Leave** → **Connect** | 离开后以新访客身份加入；**Rejoin** 同样创建新会话 |
| **NETWORK** | 查看传输和同步信息，切换原始服务端位置标记 |
| **Visual smoothing** | 对比平滑显示与原始服务端位置 |

初始 HP/MP 均为 100。动作只提交变更请求，由服务端确定结果；本演示中 HP 为零仍可移动。意外断线后最多重试五次，重连会重置访客状态，不恢复旧会话。

## 代码与文档

`src/Ark.Client.Core` 提供协议帧、TCP 传输和房间状态，不依赖 Godot；`Scripts/` 与 `Scenes/` 负责输入、画面和引擎生命周期。配套 ark 插件维护 Entity 字段并广播房间快照。

- [数据流程与指标定义](docs/data-sync_CN.md)
- [ARK2 线协议](docs/protocol_CN.md)
- [测试与验收范围](docs/validation_CN.md)：C# 编译检查、真实双客户端联调、服务端重启与本机人工体验
- [素材署名](docs/third-party-notices_CN.md)：Pixel-Boy 和 AAA 的 CC0 Ninja Adventure

执行 `bash tools/test-core.sh` 可运行独立于引擎的回归测试。贡献约定见 [AGENTS.md](AGENTS.md)。

本项目是桌面 TCP 演示，提供最多 16 名玩家的临时访客会话，不包含账号、持久化、加密与 Web 导出，也不代表生产容量测试结果。ARK2 需要配套的客户端与服务端，和历史 Unity 协议不同。

项目采用 [Apache-2.0](LICENSE) 许可证。
