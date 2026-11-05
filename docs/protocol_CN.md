# 访客房间 Protobuf 协议

客户端与配套 ark 服务端只使用当前通用 Entity 契约。本次为破坏性变更，必须同时重新构建两端。
握手要求 `protocol_version=2`，不提供兼容回退；文件、消息和 API 不加版本后缀。
Entity 定义与业务 profile 继续使用 JSON。

## Schema 与消息帧

| ark 规范源 | 客户端镜像 | C# 命名空间 |
| --- | --- | --- |
| `src/proto/proto/entity_sync.proto` | [entity_sync.proto](../protocol/entity_sync.proto) | `Ark.Client.Protocol.EntitySync` |
| `src/proto/proto/guest_room.proto` | [guest_room.proto](../protocol/guest_room.proto) | `Ark.Client.Protocol` |

通用 schema 定义拥有数据的标量、表行与子 Entity 快照及补丁；房间 schema 引用它并增加入房、
玩家实例绑定与业务动作。两个 C# 文件均由官方 `protoc 33.4` 生成，官方 Google.Protobuf
运行时固定为 `3.33.4`。[清单](../protocol/manifest.json) 固定两份 schema、生成文件及共享向量。
协议文件、package、消息、字段与枚举值使用小写 snake_case；生成 C# 属性与枚举成员
保留官方 PascalCase API。

| 帧偏移量 | 字节数 | 内容 |
| --- | --- | --- |
| 0 | 4 | ASCII `ARK2` |
| 4 | 2 | 无符号大端消息 ID |
| 6 | 2 | 无符号大端标志，必须为零 |
| 8 | 4 | 无符号大端正文字节数，不含消息头，最多 8192 |

编解码器处理分段与合并帧，最多保留一个不完整的 8204 字节帧，重连时清空缓冲。
无效消息帧关闭连接。访客监听器采用此示例消息头；ark 默认 CS/SS 消息头及 Login/Unity 契约独立。

| ID | 方向 | 消息 |
| --- | --- | --- |
| 60000 | 客户端 → 服务端 | `join_request`：name、lobby、protocol_version=2 |
| 60001 | 服务端 → 新连接 | `join_accepted`：player_id、entity_id、name、lobby 尺寸、protocol_version=2 |
| 60002 | 客户端 → 服务端 | `move_request`：uint32 sequence、double x/y |
| 60003 | 服务端 → 新连接 | `room_baseline`：完整授权玩家基线 |
| 60004 | 服务端 → 客户端 | `error_response`：code、message、可选 action_sequence |
| 60005 / 60006 | 客户端 → 服务端 / 服务端 → 客户端 | `heartbeat`：uint32 nonce |
| 60008 | 客户端 → 服务端 | `action_request`：uint32 sequence、action_kind |
| 60009 | 服务端 → 房间成员 | `room_delta`：新增玩家基线、通用嵌套增量与根墓碑 |

60007 为内部消息，客户端不支持；不使用 ark Net 保留 ID 1 和 65535。
未知消息 ID 与动作会失败。生成解析器及官方读取器检查二进制结构：重复已知单值字段、
重复 oneof 分支、错误 wire type、已知整数的非规范 varint、溢出及非 0/1 布尔值均失败。
允许未知 Protobuf 字段。索引集合使用 repeated 记录，解码器拒绝重复字段／表／容器索引、行号和槽位。

## 可信投影与拥有型状态

[`NestedReplica`](../src/Ark.Client.Core/NestedReplica.cs) 在接收基线前取得可信
`NestedStreamBinding`；远端状态不能自行决定授权成员与标量类型。
[`NestedProtobuf`](../src/Ark.Client.Core/NestedProtobuf.cs) 将生成消息复制为拥有数据的不可变模型，
包括字节数组。九种标量分别保留 boolean、int32、uint32、int64、uint64、float、double、bytes
与有符号 GUID。浮点值必须有限；`string_value` 承载 bytes，仅玩家名字投影另行要求 UTF-8。

每个 `player_baseline` 包含逻辑 `player_id`、`nested_baseline` 与实例绑定：根 Entity ID／incarnation、
generation 和两个固定装备 GUID。[`RoomSession.PlayerBinding`](../src/Ark.Client.Core/RoomSession.cs)
提供下表的字段与类型布局，不从收到的值推导类型。装备 GUID 必须为正、互异且不同于根；
不同玩家的绑定不得交叠。ID 不属于凭据，请求不选择目标 Entity。

| 选中成员 | Class／值 | 精确类型 |
| --- | --- | --- |
| 根 | `GuestPlayer`，schema `ark.guest-player` | Entity |
| 字段 100；101 / 102 | name；x / y | bytes；double |
| 字段 103；104 / 105；106 / 107；108 | 移动序列；HP / MP；上限；动作序列 | uint32 |
| 表 200 | `InventoryItem`；列 1=item_id、2=quantity | uint32 列 |
| 容器 300 | 槽位 1，授权的 `GuestEquipment` 子实体；字段 1=item_id、2=power | uint32 字段 |

完整快照必须恰好包含全部选中字段、表与容器，包括空集合。子实体按 GUID 单独授权，
并具有自身布局。同一根快照内 Entity GUID 不得重复；同表当前行的 incarnation 必须为正且互异。
通用 Entity ID 与 GUID 标量均为有符号 64 位；本房间将 Entity ID 限制在 `1..INT64_MAX`，
包括 join／binding 中以 uint64 承载的值。逻辑 ID 为非零 uint64 的规范十进制字符串。
名字非空、为有效 UTF-8、不超过 64 字节且不含控制字符；有限坐标位于 `0..960` 和 `0..540`。
配置与私有字段不参与同步。

房间投影只允许空背包或行 1（item 1、quantity 1..99）。装备必须只有槽位 1，item 为 1001 或 1002，
匹配各自声明的 GUID，power 不超过 100。HP/MP 上限为正，当前值不得超过上限。

## 基线与补丁的原子规则

`nested_baseline` 包含正 generation／revision 和完整 `nested_snapshot`。
generation、schema、根 ID 与根 incarnation 必须匹配绑定。每根只有一条修订流：新增量要求
`required_revision == current_revision` 且 `revision == required_revision + 1`。
前置修订为零或溢出会失败。先检查 schema、类型与操作结构，再忽略有效的旧／重复增量。
较旧的有效基线视为重复；同修订基线必须与已安装状态一致。较新替换基线被拒绝，恢复需要新流。

| 行操作 | required_incarnation | incarnation | 字段 |
| --- | --- | --- | --- |
| 新增 | 0 | 新的正生命周期 | 完整列 |
| 补丁 | 当前生命周期 | 相同生命周期 | 非空变化列 |
| 替换 | 当前生命周期 | 更大生命周期 | 完整列 |
| 删除 | 当前生命周期 | 0 | 空 |

新增／替换行必须超过该表历史 incarnation 水位，删除行不能重置水位。
接收器保留 Entity incarnation／class 历史与表水位，避免离槽子实体或复用行号回退生命周期。
授权子实体可用相同 GUID 与 incarnation 重新入槽；一旦观察到较新 incarnation，旧值不得再出现。

容器 `replacement` 提供完整最终授权成员集合；容器 `patch` 在现有槽位提交非空子变更，
保留子 GUID／incarnation。标量、行、容器替换与递归子补丁统一暂存，完整重建快照和生命周期历史
全部验证后才提交。拒绝后，`NestedReplica` 进入终态无效并清空状态；此前保留的不可变快照不变。
根墓碑携带空根补丁并推进修订，留下有效但无状态的流；后续新数据不能使其复活。

`RoomSession` fork 受影响接收器并暂存整个房间消息，包括玩家投影与自己的动作确认。
无效的其他成员不能部分改变本地玩家的背包、装备或待确认动作。提交成员列表必须保留 Accepted
绑定的本地身份。根墓碑移除其他成员；每连接最多保留 4096 个已见根（含墓碑）。
协议失败关闭连接，通过新连接的 Accepted 与新基线恢复，不提供同连接修复、重放或送达 ACK。

| 房间预算 | 上限 |
| --- | --- |
| 完整根 | 深度 1、3 个 Entity、8 行、64 个选中值／成员、512 字节元数据／字节值 |
| 补丁 | 128 次操作、64 个值、512 字节元数据／字节值 |
| 生命周期历史 | 每根 64 个 Entity／表／最近可见行记录 |
| 传输 | 8192 字节正文、16 名访客、64 个接收事件、一次待完成本地写入 |

这些房间预算收窄了可配置的递归接收器，不代表生产容量。服务端在所属线程捕获状态，
在 flush 时合并变更，每个新连接只发送一次完整房间基线；普通移动与动作发送增量。
首次发布前创建又删除的实体可以省略。

## 动作与恢复

| action_kind | 界面 | 服务端维护的结果 |
| --- | --- | --- |
| `action_damage=1` | Take hit | HP 减少 15，最低为零 |
| `action_skill=2` | Use skill | MP 减少 20，不足时拒绝 |
| `action_rest=3` | Rest | HP/MP 各恢复 20，不超过上限 |
| `action_pickup=4` | + Potion | 数量加一，上限 99；空表重新分配行 1 及新的 incarnation |
| `action_use_potion=5` | Drink | 数量减一，HP 最多恢复 10；最后一瓶删除行 1，空背包时拒绝 |
| `action_equip=6` | Switch weapon | 在槽位 1 切换两个固定子 Entity，各自保留 power |
| `action_upgrade=7` | + Power | 当前子装备 power 加一，上限 100 |

新访客获得 100 HP/MP、两瓶药水及 Blade（item 1001、power 10）。
Staff（item 1002、power 20）离槽时仍由服务端保留。满血喝药仍消耗一瓶，HP 为零仍可移动。
请求仅包含序列号／kind，不携带客户端提供的数值、物品值或目标 ID。

移动与动作分别采用从 1 开始递增的 uint32 序列。接受的上限裁剪空操作仍推进 `action_sequence`。
仅已提交的自身 `action_sequence >= pending.Sequence` 确认唯一待处理动作；精确关联错误非致命地拒绝它。
界面值不能先于服务端确认变化。重连清空提议、两套生成器、待确认动作、反馈、显示状态及不完整帧，
再创建新的访客状态。意外断线最多重试五次，不重试旧动作。

移动最多每秒 10 次。连接／发送截止时间为 5 秒，初始基线为 8 秒；基线后开始 ping，
每 5 秒一次，要求 4 秒内收到匹配 pong。

```sh
PROTOC=/path/to/protoc-33.4 bash tools/generate-protocol.sh --check
bash tools/check-protocol.sh /path/to/matching/ark
```

省略 `--check` 可重新生成两个 C# 文件。契约变更须同步更新两份规范 schema 及镜像、生成代码、
共享向量与清单哈希。[数据流程](data-sync_CN.md)、[运行说明](run_CN.md)和
[验证范围](validation_CN.md)分别说明可移植、引擎与真实服务端证据。
