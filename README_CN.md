# ark-client-godot

基于 Godot C# 的 OpenArkStudio [ark](https://github.com/OpenArkStudio/ark) 客户端。
首个示例面向二维访客房间，范围包括移动和断线后重新加入。

[English](README.md)

当前提交建立仓库基础。客户端实现与验证将在独立变更中交付。
访客身份为临时身份；本示例不提供账号认证或玩家数据持久化。

## 开发要求

使用 Godot .NET 编辑器和匹配的 .NET SDK。协议与传输代码独立于 Godot 场景节点。
可执行测试使用 C# 或 Godot，允许使用 Shell 脚本编排。

贡献约定见 [AGENTS.md](AGENTS.md)。项目采用 [Apache-2.0](LICENSE) 许可证。
