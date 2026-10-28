# 验证记录

## 测试环境

2026-10-28 的本地检查使用 Apple M4 Pro、14 个逻辑 CPU 核、48 GiB 内存、macOS 27.0.1 arm64、Godot `4.7.2.stable.mono.official.ed1daf0bf`、.NET SDK `10.0.403` 与运行时 `8.0.33`。项目目标为 `net8.0`；客户端托管检查选用 SDK `8.0.427`。

客户端使用官方 protoc 33.4 生成的 C# 源码与锁定的 Google.Protobuf 3.33.4。协议清单记录定义、生成源码与共享样本的 SHA-256。服务端为真实 ark 启动器及 Net、Bus、Utility、Kernel 和访客房间插件。

## 验证结果

| 检查 | 本地结果 |
| --- | --- |
| C# 永久测试 | 20 组通过，包含拆包、精确标量位宽、零修订号拒绝、房间原子提交、身份、动作、非法消息与真实 TCP 重连 |
| 跨语言契约 | 协议定义、10 个房间帧与 13 个标量边界样本与服务端逐字节一致，官方解析器保持样本编码 |
| 生成源码 | protoc 33.4 可逐字节重新生成仓库中的 C# 消息源码 |
| 原生服务端检查 | codec 的 8 个用例、471 个断言通过；真实启动器验证合并、晚加入、HP／MP、只推进动作序号、删除、重连、队列超限、非法帧、配置上限与停服 |
| 便携客户端连接 ark | 两个 C# 客户端完成晚加入、移动、HP／MP、封顶恢复、删除、新 Entity GUID 重连与心跳，三条连接均只接收一次完整房间基线 |
| Godot 构建与离线场景 | 通过，构建零警告、零错误，并输出明确的 C# 场景就绪标记 |
| 两个真实 Godot 客户端 | Alice 与 Bob 均先观察到自己及对方的受伤 85/100、技能 85/80、恢复 100/100 HP／MP，再重连；随后确认新身份、重置属性与权威移动 |

初始基线之后，移动与属性更新通过 Protobuf 标量增量传输。Entity 字段定义与业务配置继续使用 JSON。界面区分权威状态与视觉平滑，并展示基线／增量计数。

## 复现方式

先构建并启动配套的 [ark 房间服务端](https://github.com/OpenArkStudio/ark/tree/main/examples/guest_room)，再执行：

```sh
bash tools/test-core.sh
bash tools/check-protocol.sh /path/to/ark
PROTOC=/path/to/protoc-33.4 bash tools/generate-protocol.sh --check
bash tools/build.sh
bash tools/check-engine.sh
bash tools/test-live.sh 127.0.0.1 19100
bash tools/smoke-two-clients.sh 127.0.0.1 19100
```

`DOTNET` 与 `GODOT` 可指定仓库外的可执行文件，见[运行说明](run_CN.md)。联调要求空大厅与本机 socket 权限，验证功能行为，不测量延迟或吞吐。托管检查以对应 PR 的结果单独记录，原始日志仅保存在本机。

这些有界检查不代表生产容量、跨主机网络、任意插件热更新或桌面导出包兼容性。重连会创建新的访客身份与状态；认证和持久化不在本演示范围内。
