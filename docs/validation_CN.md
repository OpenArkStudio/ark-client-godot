# 验证

## 本机环境

2026-11-05 的功能检查使用 Apple M4 Pro Mac mini、14 个逻辑核心、48 GiB 内存及
macOS 27.0.1 arm64。工具为 Godot `4.7.2.stable.mono.official.ed1daf0bf`、
.NET SDK `8.0.427`、protoc `33.4`、Google.Protobuf `3.33.4`，项目目标为 `net8.0`。

实现记录于客户端 `1fce4f3` 和 ark `7b219028`，arknet 固定到已验收的
`d8ff34162cd1b490683f579ef23431068d8b7d8b`。本机执行使用功能工作区及其构建产物，
发生于发布前；托管检查与合入验收由对应 PR 单独记录，不将本机结果标为生产或 main 执行。

## 结果

| 检查 | 结果 |
| --- | --- |
| 可移植 C# | 22 组永久检查全部通过，包含真实回环传输与连接失败 |
| 共享协议 | 两份规范 Schema、15 个房间及 13 个标量边界向量与 C++ 逐字节一致；protoc 可精确重建 C# |
| 原生编解码 | 通用编解码 `f950e246`：GUID64 为 13 个用例／122 项断言，GUID32 为 13／123，包含两种 Windows 宏包含顺序；房间编解码为 8／518 |
| 原生房间 | 真实启动器／插件与两个 TCP 对端通过行删除／复用、装备替换／补丁、移动、错误、重连和正常退出 |
| 双 C# 客户端 | 移动、HP/MP、药水行删除／复用、双向换装、独立强化值保留、移除及新身份重连通过；每次连接安装一次房间基线 |
| Godot 构建／导入 | 构建无警告或错误，资源导入完成 |
| 两个可见 Godot 客户端 | 双方在重连前观察到自身与对方的行补丁、删除、复用、容器替换、子字段补丁及原有子身份重现 |
| 界面 | 两个 macOS 视口均截取为 1120 × 884；背包／装备控件、YOU／PLAYER 标记与已确认 HP/MP 显示完整；README 使用已检查的 Alice 截图 |
| 清理 | 客户端／启动脚本退出 0；服务端 SIGINT 退出 0，未强制终止，监听端口释放 |

服务端在独立的本机回环 TCP 端口 19645 加载实际 Launcher、Bus、Kernel、Net、Utility 和
访客房间动态库。装备断言同时核对 GUID 与存续期，复用的行号必须使用新存续期。
UI 值来自已提交状态，不由按钮提前修改。原始日志只保留在本地。

## 复现

启动匹配且为空的 [ark 房间服务端](https://github.com/OpenArkStudio/ark/tree/main/examples/guest_room)，再运行：

```sh
bash tools/test-core.sh
bash tools/check-protocol.sh /path/to/ark
PROTOC=/path/to/protoc-33.4 bash tools/generate-protocol.sh --check
bash tools/build.sh
bash tools/check-engine.sh
bash tools/test-live.sh 127.0.0.1 19100
bash tools/smoke-two-clients.sh 127.0.0.1 19100 --render
```

按[运行指南](run_CN.md)选择 `DOTNET` 与 `GODOT`。可见测试需要图形桌面，省略 `--render`
则以无界面方式执行。本次运行编辑器项目产物，未重新导出应用，见[导出说明](export_CN.md)。
目前只有这台 Mac，跨机器部署验收暂缓；规模／持续验收以暂停中的
[5,000 连接调查](https://github.com/OpenArkStudio/arknet/issues/61)解决并通过为前提。
发布范围与验收标准仍待定。
