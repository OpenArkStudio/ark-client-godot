# 验证

**2026-11-10**，使用重新构建的 ark 服务端和两个重新导出的 Godot 客户端，完成房间功能闭环。这是一次有明确结束条件的本机功能验收，不是容量或持续负载测试。

## 测试输入

机器：Apple M4 Pro Mac mini，14 个逻辑核心，48 GiB 内存，macOS 27.0.1（26A434），arm64。

| 输入 | 实际测试值 |
| --- | --- |
| ark | `7db3640b3cc3ef46e3ab3f1b356cd33aa6c1736f` |
| Godot 客户端 | `3f2b1e2e00033fdaf8450854627aa28b66bcfc02` |
| arknet 固定版本 | `d8ff34162cd1b490683f579ef23431068d8b7d8b` |
| 原生构建 | Debug，Apple Clang 21.0.0；启动器及五个匹配插件全部重建，并核对实际加载路径和摘要 |
| 原生依赖 | Boost 1.92.0、OpenSSL 4.1.0；匹配的 Protobuf 33.4 头文件、编译器和静态库 |
| 客户端工具 | Godot `4.7.2.stable.mono.official.ed1daf0bf`、SDK 8.0.427、Google.Protobuf 3.33.4 |
| 导出包 | Universal 2 开发应用，包含自足的 .NET 8.0.33 运行库；实际以 arm64 运行 |
| 协议输入 | 两份规范 schema 和共享向量均与服务端一致；固定版本 protoc 生成校验通过 |

## 结果

| 检查 | 结果 |
| --- | --- |
| 入房与移动 | 两位玩家进入同一房间，互相观察到服务端确认的位置 |
| HP/MP | 双方均观察到自己和对方受伤 85/100 → 技能 85/80 → 休息 100/100 |
| 嵌套 Entity 增量 | 重连前，双方均观察到表行修改、删除、复用，以及装备替换、子实体修改和重新出现 |
| 重连基线 | Alice 1→3、Bob 2→4；新身份、背包装备、HP/MP 和动作序号均通过校验 |
| 界面 | 两张 1120 × 884 截图已检查：YOU/PLAYER 文字、不同标记，以及服务端确认的 HP/MP、药水数量和武器强度 |
| 导出包身份 | 两个进程均使用导出可执行文件，不传 `--path`，不依赖编辑器/SDK 环境变量；386 个应用文件运行前后摘要一致 |
| 服务端诊断 | error/warning 级别记录和已销毁 Entity 的更新错误均为 0；断开时的 EOF、停止监听时的取消均为预期 info 记录 |
| 退出 | 双客户端和测试入口均退出 0；服务端收到 SIGINT 后退出 0，未强制终止，19690 端口已释放 |

服务端实际加载了启动器、Bus、Kernel、Net、Utility 和房间插件。默认日志仅输出到控制台，已检查服务端 stdout/stderr 和双方客户端日志。原始日志、截图和完整构建凭据仅留在本机。

## 复现

构建并启动匹配的 [ark 房间服务端](https://github.com/OpenArkStudio/ark/tree/main/examples/guest_room)，确保大厅为空，再[导出客户端](export_CN.md)：

```sh
bash tools/export-macos.sh /absolute/output/ArkGuestRoom.app
EXPORTED_CLIENT="/absolute/output/ArkGuestRoom.app/Contents/MacOS/ARK Guest Room" \
  bash tools/smoke-two-clients.sh 127.0.0.1 19100 --render
```

协议与便携回归测试的永久入口见[运行指南](run_CN.md)，导出设置和前提见 [macOS 导出](export_CN.md)。

本次仅执行了**未签名、未公证的本机 arm64 开发应用**。x86_64 内容已检查，未运行。Gatekeeper/下载分发、Windows/Linux 客户端和跨机器部署尚未验收。容量及持续负载验收仍等待已暂缓的 [5,000 连接调查](https://github.com/OpenArkStudio/arknet/issues/61)。发布范围与验收标准尚未确定。
