# 验证记录

## 测试环境

2026-11-02 的本地客户端检查使用 Apple M4 Pro、14 个逻辑 CPU 核、48 GiB 内存、macOS 27.0.1 arm64、Godot `4.7.2.stable.mono.official.ed1daf0bf`、.NET SDK `8.0.427` 与运行时 `8.0.33`。项目目标为 `net8.0`。

客户端输入与 `6009266` 一致。服务端运行代码与已合入的 ark `931b68f3` 一致；复用了运行代码逐字节相同的 `bc90b681` 范围内的原生构建产物，没有将其标记为后续提交的新构建。

客户端使用官方 protoc 33.4 生成的 C# 源码与锁定的 Google.Protobuf 3.33.4。协议清单记录定义、生成源码与共享样本的 SHA-256。服务端为真实 ark 启动器及 Net、Bus、Utility、Kernel 和访客房间插件。

## 验证结果

| 检查 | 历史结果 |
| --- | --- |
| C# 永久测试 | 21 组通过：保留原有 20 组，新增 schema snake_case、枚举数值、动作二进制字节不变和小写 Protobuf JSON 枚举文本检查 |
| 跨语言契约 | 协议定义、10 个房间帧与 13 个标量边界样本与服务端逐字节一致，官方解析器保持样本编码 |
| 生成源码 | protoc 33.4 可逐字节重新生成仓库中的 C# 消息源码 |
| 受保护的服务端检查 | ark [#247](https://github.com/OpenArkStudio/ark/pull/247) 合入前通过必需的 Linux／macOS／Windows 候选检查；详细原生验收见服务端报告 |
| 便携客户端连接 ark | 两个 C# 客户端完成晚加入、移动、HP／MP、封顶恢复、删除、新 Entity GUID 重连与心跳，三条连接均只接收一次完整房间基线 |
| Godot 构建与离线场景 | 通过，构建零警告、零错误，并输出明确的 C# 场景就绪标记 |
| 两个真实 Godot 客户端 | Alice 与 Bob 均先观察到自己及对方的受伤 85/100、技能 85/80、恢复 100/100 HP／MP，再重连；随后确认新身份、重置属性与权威移动 |
| 图形客户端 | macOS 显示驱动下通过相同的双客户端断言；两张 1120 × 824 画面均展示 YOU／PLAYER 标签、不同标记、HP／MP 条及房间界面 |

初始基线之后，移动与属性更新通过 Protobuf 标量增量传输。Entity 字段定义与业务配置继续使用 JSON。界面区分权威状态与视觉平滑，并展示基线／增量计数。

无界面与图形检查均正常退出，自有服务端随后收到 SIGINT 并正常结束。截图与原始日志仅保存在本机。

## 2026-11-03 导出包网络联调

从 ARK 候选 `8bdd734b9837015cab97f022c7258830ee889f47` 新构建 Launcher、Bus、Kernel、Net、
Utility 和 guest_room 六个组件，使用独立回环端口 19492。CONSUL、ETCD_CLIENT、
KAFKA、OSSSDK 运行时插件开关均显式 OFF；复用原导出应用的 386 个未变文件。
来源及协议／依赖匹配范围见[导出记录](export_CN.md)。

| 检查 | 结果 |
| --- | --- |
| 两个真实导出客户端 | 入房、自身／对方移动及 HP／MP 动作通过；Alice 2→3、Bob 1→4 重连后属性与序列重置 |
| 图形执行 | 两端及 runner 均退出 0；两张 1120×824 截图均已目检 |
| 服务端来源与停止 | 自有进程实际加载新 app 和五个匹配 DSO，随后通过 SIGINT 退出 0，没有强杀，监听端口释放 |

本轮没有重复客户端 21 组 Core 测试、便携客户端测试、客户端构建／Editor import、导出包或托管检查；
上面的历史结果及原导出分别保留自己的输入归属。

## 2026-11-03 有界持续运行

以下便携 C# 检查与上面的引擎结果分别记录。

| 范围 | 本地结果 |
| --- | --- |
| 600 秒活动 | 三个客户端均退出 0；各 7096 次移动、动作、心跳得到确认，跨进程检查通过。原包装器随后因 Bash PID 数组清理退出 1 |
| 30 秒包装器回归 | 修复清理逻辑后，包装器及所有客户端均退出 0 |

两次自有服务端均通过 SIGINT 退出 0，监听端口均退出。精确源码版本、二进制哈希、
源码／blob 等价范围及资源观察见[本机持续验收记录](soak_CN.md)。这些检查不运行
Godot 场景或导出包。

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
# 可选：显示两个窗口，并分别保存客户端自身的画面。
bash tools/smoke-two-clients.sh 127.0.0.1 19100 --render
```

`DOTNET` 与 `GODOT` 可指定仓库外的可执行文件，见[运行说明](run_CN.md)。联调要求空大厅与本机 socket 权限，验证功能行为，不测量延迟或吞吐。托管检查以对应 PR 的结果单独记录，原始日志仅保存在本机。

`--render` 需要图形桌面，执行相同的移动、HP／MP 和重连断言，将 `alice.png`、`bob.png` 保存到输出的日志目录后退出。截图仅包含 Godot 自身画面，无需桌面录屏权限；生成图片不代替人工视觉检查或桌面导出测试。默认仍为无界面模式。

导出运行仅覆盖未签名、未公证的 macOS arm64 开发应用及本机回环。这些有界检查不代表生产容量、跨主机网络、任意插件热更新、签名／下载分发或导出包在其他平台／架构上的兼容性。重连会创建新的访客身份与状态；认证和持久化不在本演示范围内。
