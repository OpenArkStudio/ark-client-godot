# Guest-room Protobuf protocol

The client and matching ark server use one current, generic Entity contract.
This is a breaking change: rebuild both peers together. The handshake requires
`protocol_version=2`; there is no compatibility fallback. Files, messages and
APIs have no version suffix. Entity definitions and business profiles remain JSON.

## Schemas and framing

| Canonical ark source | Client mirror | C# namespace |
| --- | --- | --- |
| `src/proto/proto/entity_sync.proto` | [entity_sync.proto](../protocol/entity_sync.proto) | `Ark.Client.Protocol.EntitySync` |
| `src/proto/proto/guest_room.proto` | [guest_room.proto](../protocol/guest_room.proto) | `Ark.Client.Protocol` |

The generic schema defines owning snapshots and patches for scalars, table rows
and child Entities. The room schema imports it and adds joins, player bindings
and business actions. Official `protoc 33.4` generates both C# files; the official
Google.Protobuf runtime is pinned to `3.33.4`. The [manifest](../protocol/manifest.json)
pins both schemas, generated files and shared golden vectors. Protocol filenames,
packages, messages, fields and enum values use lower snake_case; generated C#
properties and enum members retain the official PascalCase API.

| Frame offset | Bytes | Value |
| --- | --- | --- |
| 0 | 4 | ASCII `ARK2` |
| 4 | 2 | Unsigned big-endian message ID |
| 6 | 2 | Unsigned big-endian flags, zero |
| 8 | 4 | Unsigned big-endian body length, excluding header, at most 8192 |

The codec handles split/coalesced frames, retains at most one incomplete
8204-byte frame and resets it on reconnect. Invalid framing closes the session.
The guest listener uses this sample header; ark's default CS/SS headers and
Login/Unity contracts are separate.

| ID | Direction | Message |
| --- | --- | --- |
| 60000 | Client → server | `join_request`: name, lobby, protocol_version=2 |
| 60001 | Server → joining client | `join_accepted`: player_id, entity_id, name, lobby dimensions, protocol_version=2 |
| 60002 | Client → server | `move_request`: uint32 sequence, double x/y |
| 60003 | Server → new connection | `room_baseline`: complete authorized player baselines |
| 60004 | Server → client | `error_response`: code, message, optional action_sequence |
| 60005 / 60006 | Client → server / server → client | `heartbeat`: uint32 nonce |
| 60008 | Client → server | `action_request`: uint32 sequence, action_kind |
| 60009 | Server → joined peers | `room_delta`: added player baselines, generic nested deltas and root tombstones |

60007 is internal and unsupported. Reserved ark Net IDs 1 and 65535 are unused.
Unknown IDs/actions fail. Generated parsers and official readers validate binary
shape: duplicate known singular fields, repeated oneof alternatives, wrong wire
types, noncanonical known integer varints, overflow and non-0/1 booleans fail.
Unknown Protobuf fields are allowed. Indexed collections use repeated records;
the decoder rejects duplicate field/table/container indices, row numbers and slots.

## Trusted projection and owning state

[`NestedReplica`](../src/Ark.Client.Core/NestedReplica.cs) receives a trusted
`NestedStreamBinding` before any baseline. Remote state cannot define its own
allowed members or scalar types. [`NestedProtobuf`](../src/Ark.Client.Core/NestedProtobuf.cs)
copies generated messages into immutable owning models, including byte arrays.
The nine scalar alternatives preserve boolean, int32, uint32, int64, uint64,
float, double, bytes and signed GUID values. Floating values must be finite;
`string_value` carries bytes, and only the player-name projection requires UTF-8.

Each `player_baseline` carries a logical `player_id`, a `nested_baseline` and
instance bindings: root Entity ID/incarnation, generation and two fixed equipment
GUIDs. [`RoomSession.PlayerBinding`](../src/Ark.Client.Core/RoomSession.cs) supplies
the field/type layout below; it does not infer types from received values.
Equipment GUIDs are positive, distinct and different from the root. No player
bindings may overlap. IDs are not credentials; requests choose no target Entity.

| Selected member | Class / value | Exact type |
| --- | --- | --- |
| Root | `GuestPlayer`, schema `ark.guest-player` | Entity |
| Fields 100; 101 / 102 | name; x / y | bytes; double |
| Fields 103; 104 / 105; 106 / 107; 108 | move sequence; HP / MP; maxima; action sequence | uint32 |
| Table 200 | `InventoryItem`; columns 1=item_id, 2=quantity | uint32 columns |
| Container 300 | slot 1, authorized `GuestEquipment` child; fields 1=item_id, 2=power | uint32 fields |

A complete snapshot contains every selected field, table and container exactly
once, including empty collections. Child authorization is per GUID and includes
its own layout. One Entity GUID appears at most once in a root snapshot; row
incarnations are positive and unique within their table. Generic Entity IDs and
GUID scalar values are signed 64-bit; this room restricts Entity IDs to
`1..INT64_MAX`, including those carried as uint64 in join/binding messages.
Logical IDs are canonical nonzero decimal uint64 strings. Names are nonempty,
valid UTF-8, at most 64 bytes and free of control characters; finite positions
are bounded by `0..960` and `0..540`. Configuration/private fields are excluded.

The room projection allows an empty inventory or row 1 with item 1 and quantity
1..99. Equipment has exactly slot 1 with item 1001 or 1002, bound to its declared
GUID, and power at most 100. HP/MP maxima are positive and current values cannot
exceed them.

## Atomic baseline and patch rules

A `nested_baseline` contains positive generation/revision and a complete
`nested_snapshot`. Generation, schema, root ID and root incarnation must match
the binding. Each root has one revision stream: a new delta requires
`required_revision == current_revision` and `revision == required_revision + 1`.
Zero required revisions and overflow fail. Schema, types and operation shape are
validated before well-formed stale/duplicate deltas are ignored. An older valid
baseline is a duplicate; an equal-revision baseline must match installed state.
A newer replacement baseline is rejected; recovery requires a new stream.

| Row operation | required_incarnation | incarnation | Fields |
| --- | --- | --- | --- |
| Add | 0 | New positive lifetime | Complete columns |
| Patch | Current lifetime | Same lifetime | Nonempty changed columns |
| Replace | Current lifetime | Greater lifetime | Complete columns |
| Remove | Current lifetime | 0 | Empty |

New/replaced rows must exceed the table's historical incarnation watermark.
Deleting a row does not reset it. The receiver retains Entity incarnation/class
history and table watermarks so detached children or reused row numbers cannot
roll back lifetimes. An authorized detached child may return with the same GUID
and incarnation; a newer observed incarnation prevents an older one returning.

A container `replacement` supplies its complete final authorized membership.
A container `patch` supplies nonempty child changes at existing slots, preserving
child GUID/incarnation. Scalars, rows, replacements and recursive child patches
are staged together, then the complete reconstructed snapshot and lifetime
history are validated before committing. On rejection, `NestedReplica` becomes
terminally invalid and clears its state; previously retained immutable snapshots
remain unchanged. A root tombstone has an empty root patch, advances revision
and leaves a valid stream with no state; subsequent new data cannot revive it.

`RoomSession` forks affected receivers and stages the entire room envelope,
including player projections and own action confirmation. A bad peer cannot
partially change the local player's inventory, equipment or pending action.
The committed roster must retain the accepted local identity. Root tombstones
remove peers; at most 4096 seen roots, including tombstones, remain per connection.
Protocol failure closes the session; a new connection installs Accepted and a
fresh baseline. There is no same-connection repair, replay or delivery ACK.

| Room budget | Bound |
| --- | --- |
| Complete root | Depth 1, 3 Entities, 8 rows, 64 selected values/members, 512 metadata/byte-value bytes |
| Patch | 128 operations, 64 values, 512 metadata/byte-value bytes |
| Lifetime history | 64 Entity/table/last-visible-row entries per root |
| Transport | 8192-byte body, 16 guests, 64 queued receive events, one pending local write |

These room budgets narrow the configurable recursive receiver; they are not
production capacity claims. The server captures on its owner thread, coalesces
changes on flush and sends one room baseline per new connection. Routine moves
and actions send deltas. Create/remove before first publication may be omitted.

## Actions and recovery

| action_kind | UI | Server-owned result |
| --- | --- | --- |
| `action_damage=1` | Take hit | HP −15, floored at zero |
| `action_skill=2` | Use skill | MP −20; insufficient MP rejects |
| `action_rest=3` | Rest | HP/MP +20, capped at maxima |
| `action_pickup=4` | + Potion | Quantity +1, maximum 99; an empty table receives row 1 with a new incarnation |
| `action_use_potion=5` | Drink | Quantity −1, HP +10 capped; the last potion deletes row 1; an empty inventory rejects |
| `action_equip=6` | Switch weapon | Swap the two fixed child Entities in slot 1; each retains its own power |
| `action_upgrade=7` | + Power | Equipped child's power +1, capped at 100 |

New guests start at 100 HP/MP, two potions and Blade (item 1001, power 10).
Staff (item 1002, power 20) stays server-owned while unequipped. Drinking at full
HP still consumes a potion. HP zero still permits movement. Requests contain
only sequence/kind, never client-supplied amounts, item values or target IDs.

Movement and actions have independent increasing uint32 sequences starting at 1.
Accepted capped no-ops still advance `action_sequence`. Only a committed own
`action_sequence >= pending.Sequence` confirms the single pending action; an
exact correlated error rejects it nonfatally. UI values never change ahead of
server confirmation. Reconnect clears proposals, both generators, pending actions,
feedback, display state and partial frames, then creates fresh guest state.
Unexpected disconnects retry at most five times and never retry an old action.

Movement is limited to 10 requests/second. Connect/send deadlines are five seconds,
the initial baseline eight seconds. Ping starts after that baseline, repeats
every five seconds and requires a matching pong within four seconds.

```sh
PROTOC=/path/to/protoc-33.4 bash tools/generate-protocol.sh --check
bash tools/check-protocol.sh /path/to/matching/ark
```

Omit `--check` to regenerate both C# files. Contract changes update both canonical
schemas and their mirrors, generated sources, shared vectors and manifest hashes
together. [Data flow](data-sync.md), [run instructions](run.md) and
[validation scope](validation.md) separate portable, engine and real-server evidence.
