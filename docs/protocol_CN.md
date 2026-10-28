# 访客房间 Protobuf 协议

配套 ark 服务端与 Godot 客户端在现有 ARK2 TCP 消息帧内交换生成的 Protobuf 消息。
两端必须同时升级：历史 JSON 房间对端不兼容，即使消息帧标记仍为 ARK2。
旧 Login/Unity 协议及 ark 默认 CS/SS 消息头属于独立协议。

| 偏移量 | 字节数 | 内容 |
| --- | --- | --- |
| 0 | 4 | ASCII `ARK2` |
| 4 | 2 | 无符号大端消息 ID |
| 6 | 2 | 无符号大端标志，必须为零 |
| 8 | 4 | 无符号大端正文字节数，不含消息头，最多 8192 |

编解码器保留不完整帧、解析合并帧，并在重连时清空缓冲区。
最多保留一个不完整的 8204 字节帧。无效标记、标志或长度在房间消息分发前关闭连接。

[规范 schema](../protocol/guest_room.proto) 的 package 为 `ark.protobuf.guest_room`，
C# 命名空间为 `Ark.Client.Protocol`。入仓 C# 类型由官方 `protoc 33.4` 生成；
官方 Google.Protobuf 运行时通过 NuGet 内容哈希锁定为 `3.33.4`。
协议和状态代码为可移植 C#，不依赖 Godot。
[清单](../protocol/manifest.json) 固定 schema、生成代码和共享协议向量的 SHA-256。

协议文件名、package 分段、message/enum 名称、字段和枚举值统一采用小写 snake_case。
官方生成的 C# 属性与枚举成员保留 PascalCase，例如 `ProtocolVersion` 和 `ActionDamage`。
尚未发布的新动作枚举值为 `action_unspecified=0`、`action_damage=1`、`action_skill=2`、`action_rest=3`。
本次将此前 `ACTION_*` 源码拼写改为小写，枚举数值和字段 tag 保持不变。
因此二进制房间帧及共享向量不变，Protobuf JSON 枚举文本改用小写名称；不承诺兼容此前大写 JSON 拼写。
房间传输仍采用二进制 Protobuf。

| ID | 方向 | 生成消息 |
| --- | --- | --- |
| 60000 | 客户端 → 服务端 | `join_request`：name、room、protocol_version=1 |
| 60001 | 服务端 → 新连接 | `join_accepted`：逻辑 player_id、entity_id、name、lobby 尺寸、protocol_version=1 |
| 60002 | 客户端 → 服务端 | `move_request`：uint32 sequence、double x/y |
| 60003 | 服务端 → 新连接 | `room_baseline`：lobby 与完整授权成员基线 |
| 60004 | 服务端 → 客户端 | `error_response`：code、message、可选 action_sequence |
| 60005 / 60006 | 客户端 → 服务端 / 服务端 → 客户端 | `heartbeat`：uint32 nonce |
| 60008 | 客户端 → 服务端 | `action_request`：uint32 sequence、action_kind |
| 60009 | 服务端 → 房间成员 | `room_delta`：新增成员基线、标量增量和删除墓碑 |

60007 属于内部消息，客户端不支持。不要使用 ark Net 保留的 ID 1 和 65535。
未知消息 ID 与未知动作枚举值会导致失败。允许未知 Protobuf 字段；重复的已知单值字段、
重复 oneof 分支、已知字段的错误 wire type 以及无效编码会导致失败。
房间策略还要求已知整数字段采用规范 varint，布尔值为 0/1；这些属于通用 Protobuf 解码之上的房间校验规则。
未知字段仍由官方读取器处理。
解析和结构检查使用生成类型及官方 `CodedInputStream`/descriptor，没有手写二进制解析器。

固定 lobby 最多容纳 16 人。逻辑玩家 ID 为非零 uint64 的规范十进制字符串。
实体 ID 为实际 Kernel Entity GUID，通过 uint64 传输，取值为 `1..INT64_MAX`。
通用 `guid_value` 分支采用有符号 `sint64`，支持负 GUID，与覆盖完整无符号范围的 `uint64_value` 分开。
Accepted 将两种身份绑定到本地玩家。两种 ID 都不是凭据，请求不包含客户端选择的目标实体。
名字必须非空、为有效 UTF-8、不超过 64 个编码字节，且不含控制字符。
坐标为有限 double，分别位于 `0..960` 和 `0..540`。

`player_baseline` 将逻辑玩家 ID 映射到 `scalar_baseline`。
schema 为 `ark.guest-player.v1`，class 为 `GuestPlayer`，选中字段严格对应以下九个索引。
配置字段 99 和服务端私有字段不参与同步。`string_value` 为 Protobuf **bytes** 分支；
玩家投影另行验证名字的 UTF-8。通用 bytes 与 string 不混用。

| Entity 索引 | 玩家值 | 精确标量分支 |
| --- | --- | --- |
| 100 | name | string_value / bytes |
| 101 / 102 | x / y | double_value |
| 103 | 移动序列号 | uint32_value |
| 104 / 105 | HP / MP | uint32_value |
| 106 / 107 | HP / MP 上限 | uint32_value |
| 108 | 动作序列号 | uint32_value |

基线必须包含每个选中字段，且各出现一次。增量仅携带变化字段，索引不能重复。
标量代次必须为正数，且与已安装基线相同；基线修订及增量前置修订也必须为正数。新发布必须满足
`required_revision == current_revision` 和 `revision == required_revision + 1`。
结构有效的旧修订或重复修订被忽略，不回退状态。修订缺口、代次变化、序列号回退、修订溢出、
未知 schema/class/索引、错误值分支、无效 UTF-8 或 HP/MP 范围都会导致失败。
属性上限必须为正数，当前 HP/MP 不能超过各自上限。

删除增量不含字段，但仍推进修订。删除墓碑禁止同一 Entity GUID 复活。
新增基线必须使用未出现过的 Entity GUID，且逻辑 ID 不能与存活成员重复。
一个连接最多保留 4096 个已见身份，包含墓碑；达到上限后需要重连。

客户端先在候选副本中验证**整个房间消息**，再提交成员列表、自己的动作确认和显示输入。
无效的其他成员不能提前提交有效的自身变化。有效列表必须保留 Accepted 绑定的本地身份。
协议错误会使场景关闭连接并清空状态，再通过新连接的 Accepted 与完整基线恢复。
不提供同连接修复、ACK 或重放。

服务端在所属线程捕获 Kernel 标量状态，在房间 flush 时合并发布变化字段。
新连接先收到 Accepted，再收到一次完整房间基线；已有成员收到新增基线、增量和删除通知。
普通移动不广播完整房间基线。首次发布前创建又删除的实体可以省略。

移动与动作采用两套独立 uint32 序列号，从 1 开始。
动作枚举为 `action_damage=1`、`action_skill=2`、`action_rest=3`，
未指定值和未知值均失败。受击减少自己 15 HP；技能消耗自己 20 MP，不足时拒绝；
休息分别恢复 20 HP/MP，由服务端限制到上限。HP 为零仍允许示例移动。
接受裁剪后没有属性变化的动作仍推进 `action_sequence`。
仅已完整提交的自身 `action_sequence >= pending.Sequence` 确认唯一待处理请求。
精确关联的错误清除请求并保留连接；不匹配的错误不会清除其他请求。
重连清空提议、两套生成器、待处理动作、反馈和不完整帧，绝不重试旧动作。

客户端每秒最多发送 10 次移动请求。接收事件上限为 64，只允许一次待完成本地写入。
连接和发送截止时间为 5 秒，初始基线为 8 秒。初始基线后开始 ping，此后每 5 秒一次，
必须在 4 秒内收到匹配 pong。这些是示例限制，不代表生产容量。

复现生成代码与字节检查：

```sh
PROTOC=/path/to/protoc-33.4 bash tools/generate-protocol.sh --check
bash tools/check-protocol.sh /path/to/matching/ark
```

省略 `--check` 可重新生成入仓 C# 文件。主动更新契约时，必须同步更新两端仓库、共享向量和清单哈希。
可移植代码、引擎及集成检查分别见[数据流程](data-sync_CN.md)、[运行说明](run_CN.md)和[验证范围](validation_CN.md)。
