# Guest-room Protobuf protocol

The matching ark server and Godot client exchange generated Protobuf messages
inside the existing ARK2 TCP frame. Upgrade both peers together: earlier JSON
room peers are incompatible even though the frame marker remains ARK2. The
legacy Login/Unity contract and default ark CS/SS headers are separate protocols.

| Offset | Bytes | Value |
| --- | --- | --- |
| 0 | 4 | ASCII `ARK2` |
| 4 | 2 | Unsigned big-endian message ID |
| 6 | 2 | Unsigned big-endian flags, zero |
| 8 | 4 | Unsigned big-endian body length, excluding header, at most 8192 |

The codec retains partial frames, parses coalesced frames and discards buffered
data on reconnect. It retains at most one incomplete 8204-byte frame. Invalid
markers, flags or lengths close the session before room dispatch.

The canonical [schema](../protocol/guest_room.proto) has package
`ark.protobuf.guest_room` and C# namespace `Ark.Client.Protocol`. Committed C#
classes are produced by official `protoc 33.4`; the official Google.Protobuf
runtime is locked to `3.33.4` with a NuGet content hash. Protocol/state code is
portable C# and has no Godot dependency. The [manifest](../protocol/manifest.json)
pins schema, generated source and shared golden fixture SHA-256 digests.

Protocol filenames, package components, message/enum names, fields and enum
values use lower snake_case. Official generated C# properties and enum members
retain PascalCase, such as `ProtocolVersion` and `ActionDamage`. The new,
unreleased action enum values are `action_unspecified=0`, `action_damage=1`,
`action_skill=2` and `action_rest=3`. Their earlier `ACTION_*` source spelling
was renamed without changing numbers or field tags. Consequently binary room
frames and shared golden vectors are unchanged, while Protobuf JSON enum text
now uses the lowercase names; earlier uppercase JSON spellings are not a
compatibility promise. Room transport continues to use binary Protobuf.

| ID | Direction | Generated message |
| --- | --- | --- |
| 60000 | Client → server | `join_request`: name, room, protocol_version=1 |
| 60001 | Server → joining client | `join_accepted`: logical player_id, entity_id, name, lobby dimensions, protocol_version=1 |
| 60002 | Client → server | `move_request`: uint32 sequence, double x/y |
| 60003 | Server → new connection | `room_baseline`: lobby and complete authorized player baselines |
| 60004 | Server → client | `error_response`: code, message, optional action_sequence |
| 60005 / 60006 | Client → server / server → client | `heartbeat`: uint32 nonce |
| 60008 | Client → server | `action_request`: uint32 sequence, action_kind |
| 60009 | Server → joined peers | `room_delta`: added player baselines, changed scalar deltas and tombstones |

60007 is internal and unsupported by this client. Reserved ark Net IDs 1 and
65535 are not used. Unknown message IDs and unknown action enum values fail.
Unknown Protobuf fields are allowed; duplicate known singular fields, repeated
oneof alternatives, wrong known wire types and malformed encodings fail.
The room policy also requires canonical varints for known integer fields and
boolean values 0/1; these are room validation rules beyond generic Protobuf
decoding. Unknown fields continue through the official reader.
There is no handwritten binary parser: generated message classes and the
official `CodedInputStream`/descriptors perform parsing and shape checks.

The fixed lobby has at most 16 players. Logical player IDs are canonical nonzero
decimal uint64 strings. Entity IDs are actual nonzero Kernel Entity GUID values,
carried as uint64 in the positive signed-64 range `1..INT64_MAX`. The generic
`guid_value` alternative is signed `sint64`, including negative GUID values;
it is distinct from the full-range unsigned `uint64_value`. Accepted binds both
identities to the local player. Neither
ID is a credential, and requests contain no client-selected target entity.
Names are nonempty, valid UTF-8, at most 64 encoded bytes and contain no control
characters. Coordinates are finite doubles in `0..960` and `0..540`.

Each `player_baseline` maps a logical player ID to a `scalar_baseline`. Its
schema is `ark.guest-player.v1`, class is `GuestPlayer`, and selected fields are
exactly the following nine indices. Configuration field 99 and server-only
fields are excluded. `string_value` is a Protobuf **bytes** alternative; the
player projection separately validates its UTF-8 name. Generic bytes and strings
are not interchangeable.

| Entity index | Player value | Exact scalar alternative |
| --- | --- | --- |
| 100 | name | string_value / bytes |
| 101 / 102 | x / y | double_value |
| 103 | move sequence | uint32_value |
| 104 / 105 | HP / MP | uint32_value |
| 106 / 107 | maximum HP / MP | uint32_value |
| 108 | action sequence | uint32_value |

A baseline must contain every selected field exactly once. A delta contains only
changed selected fields, without duplicate indices. Scalar generation must be
positive and match the installed baseline; baseline revision and required delta
revision must also be positive. A new publication requires
`required_revision == current_revision` and `revision == required_revision + 1`.
Shape-valid stale/duplicate revisions are ignored without regressing state.
Gaps, generation changes, backwards sequences, revision overflow, unknown
schema/class/index, wrong value alternatives, invalid UTF-8 and invalid HP/MP
ranges fail. Maxima must be positive and current HP/MP cannot exceed them.

A removed delta has no fields and advances revision. Its tombstone prevents
resurrection under the same Entity GUID. An added baseline must use an unseen
Entity GUID and a unique live logical ID. A connection retains at most 4096
seen identities, including tombstones; exhaustion requires reconnect.

The client validates the **whole room envelope** against a candidate replica
before committing any roster, own action confirmation or presentation input.
An invalid peer cannot partially commit a valid own-player change. A valid
roster must retain the accepted local identity. The scene closes a connection
on protocol failure, clears state and uses a new connection's Accepted plus
full baseline to recover. There is no same-connection repair, ACK or replay.

The server captures Kernel scalar state on its owner thread and publishes
coalesced changed fields on room flush. New connections receive Accepted then
one full room baseline. Existing peers receive additions, deltas and removals;
ordinary movement does not broadcast full room baselines. Creation/removal
before first publication may be omitted.

Movement and action sequences are independent uint32 counters, starting at 1.
The action enum is `action_damage=1`, `action_skill=2`, `action_rest=3`;
unspecified/unknown values fail.
Damage subtracts 15 own HP, skill subtracts 20 own MP or rejects insufficient
resources, and rest restores 20 to each value capped at its server-owned maximum.
HP zero still permits demo movement. Accepted clamped actions advance
`action_sequence` even when HP/MP are unchanged. Only a valid committed own
`action_sequence >= pending.Sequence` confirms the one pending request. An
exact correlated error clears it nonfatally; an unrelated error does not clear
another request. Reconnect clears proposals, both generators, pending actions,
feedback and partial frames, and never retries an old action.

Client movement is limited to 10 requests/second. Receive events are bounded to
64 and local sends to one pending write. Connect/send deadlines are five
seconds, initial baseline eight seconds. Ping starts after the initial baseline,
repeats every five seconds and requires a matching pong within four seconds.
These are sample bounds, not a production capacity claim.

Reproduce the schema and byte checks with:

```sh
PROTOC=/path/to/protoc-33.4 bash tools/generate-protocol.sh --check
bash tools/check-protocol.sh /path/to/matching/ark
```

Omit `--check` to regenerate committed C# source. Intentional contract updates
must update both repositories, golden vectors and manifest digests together.
See [data flow](data-sync.md), [run instructions](run.md) and
[validation scope](validation.md) for separate portable/engine/integration checks.
