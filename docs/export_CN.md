# 本地 macOS 开发包导出

使用 Godot **4.7.2 .NET**、匹配的 **.NET 导出模板**及 SDK **8.0.427**。按[运行指南](run_CN.md)选择工具并将 SDK 加入 `PATH`。通过 **Editor > Manage Export Templates** 安装模板；导出需要网络访问，或已缓存的 `osx-arm64` / `osx-x64` 运行库。

```sh
# 选择新的绝对路径；保留已有应用。
bash tools/export-macos.sh /absolute/output/ArkGuestRoom.app

# 先启动匹配的服务端并确保大厅为空，再运行两个有界面的应用实例。
EXPORTED_CLIENT="/absolute/output/ArkGuestRoom.app/Contents/MacOS/ARK Guest Room" \
  bash tools/smoke-two-clients.sh 127.0.0.1 19100 --render
```

| 设置 | 行为 |
| --- | --- |
| `macOS development` | 官方 Universal 2 debug 模板；关闭签名和公证 |
| 应用包 | 当前嵌套 Entity 协议、C# 程序集，以及 arm64 和 x86_64 自足 .NET 运行库 |
| Core 锁文件 | 分别保留 macOS RID 锁文件及原便携锁文件，继续使用锁定还原 |
| 导出检查 | 拒绝引擎错误诊断，以及缺失可执行文件或任一架构程序集的产物 |
| `EXPORTED_CLIENT` | 使用绝对可执行路径，不传 `--path`，读取包内素材 |
| 有界面验收 | 需要图形桌面，打印本机日志/截图目录；省略 `--render` 可无界面运行 |

## 验证范围

**2026-11-10**，客户端 `3f2b1e2` 的导出包与 ark `7db3640b` 通过双客户端验收，覆盖移动、HP/MP、嵌套 Entity 变化、重连及正常退出。386 个应用文件运行前后摘要一致，运行时未依赖编辑器/SDK 环境变量。准确输入与结果见[验证](validation_CN.md)。

本次仅验证了本机 **arm64 开发应用**。包内包含并检查了 x86_64 内容，但未执行。应用未签名、未公证；模板继承的签名已无法覆盖整个包，因此严格签名校验失败。这不代表已通过 Gatekeeper、下载分发或生产发布验收。

官方参考：[macOS 导出](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_for_macos.html)、[Godot .NET 下载](https://godotengine.org/download/macos/)。
