# Guest-room protocol v2

Use the **ARK2** client and matching ark `guest_room` server together. ARK1 peers
are deliberately rejected; update both sides. Existing ark default CS/SS headers
are unchanged. This sample explicitly selects its own header over raw TCP.
The [shared golden vectors](../tests/fixtures/wire-vectors.json) include actions,
a complete attribute snapshot and a correlated business rejection.

| Offset | Bytes | Value |
| --- | --- | --- |
| 0 | 4 | ASCII `ARK2`, protocol/version marker |
| 4 | 2 | Message ID, unsigned big-endian |
| 6 | 2 | Flags, unsigned big-endian, must be zero |
| 8 | 4 | Body byte length, unsigned big-endian, excluding header, at most 8192 |

The body is a UTF-8 JSON object. Invalid markers, flags or excessive lengths
close the session before business dispatch. Retain partial frames, parse
coalesced frames, and discard buffered data on reconnect. The receive codec
retains at most one incomplete 8204-byte frame. The larger body accommodates the
16-player roster with escaped names and attributes; the ARK1 limit was 4096.
An empty-body vector tests framing only, not valid room JSON.

| ID | Direction | Body |
| --- | --- | --- |
| 60000 | Client → server | `{"name":"Alice","room":"lobby"}` |
| 60001 | Server → joining client | `{"player_id":"1","name":"Alice","room":"lobby","width":960,"height":540}` |
| 60002 | Client → server | `{"seq":1,"x":120,"y":100}` |
| 60003 | Server → joined peers | `{"room":"lobby","players":[{"id":"1","name":"Alice","x":120,"y":100,"seq":1,"hp":85,"mp":100,"hp_max":100,"mp_max":100,"action_seq":1}]}` |
| 60004 | Server → client | `{"code":"invalid_message","message":"reason"}`; action rejections additionally contain `"action_seq":1` |
| 60005 | Client → server | `{"nonce":1}` |
| 60006 | Server → client | `{"nonce":1}` |
| 60008 | Client → server | `{"seq":1,"kind":"damage"}`; kind is `damage`, `skill` or `rest` |

ID 60007 is not an action request. Do not use ark Net's reserved IDs 1 or 65535.
One fixed lobby contains at most 16 players. Names are nonempty, at most 64 UTF-8
bytes and contain no control characters. IDs are temporary opaque strings, not
credentials. Coordinates are finite within `0..960` and `0..540`. Initial
positions belong to the server. Movement `seq` and action `seq` are independent
uint32 counters, each starting at 1 and strictly increasing. `nonce` is uint32.

Every snapshot player requires uint32 `hp`, `mp`, `hp_max`, `mp_max` and
`action_seq`. Maxima must be positive; current values cannot exceed them. Both
confirmed move `seq` and `action_seq` must not regress for an existing player.
New players start at HP/MP/maxima 100 and action sequence 0.

Actions target only the requesting player's entity. The request contains exactly
`seq` and `kind`; no client amount, target or stat value is accepted.

| Action | Server-owned result |
| --- | --- |
| `damage` | Own HP −15, clamped to zero |
| `skill` | Own MP −20, rejected if insufficient |
| `rest` | Own HP and MP +20, each clamped to its maximum |

Each accepted action advances `action_seq` and broadcasts a full snapshot even
when clamping produces no stat change. HP zero does not disable movement in
this demonstration. There is no PvP, inventory or client-owned attribute write.

The client allows one pending action. Only its own valid snapshot with
`action_seq >= pending.seq` confirms it; a movement-only snapshot cannot do so.
Action errors echo the requested `action_seq`. Sequence 0 is an uncorrelated
sentinel for missing/invalid request sequences. An exact rejection clears the
matching pending request, shows nonfatal feedback and keeps the room connected.
Unmatched errors do not clear another request. Stats remain unchanged on
rejection. Other fatal join/protocol error behavior is retained.

Rejoin clears proposals, both sequence generators, pending actions, feedback and
partial frames. It never retries an old action. New session identity/state is
not resumed. Successful joins send acceptance before a full snapshot; joins,
accepted moves/actions and disconnects trigger room snapshots.

Movement is limited to 10 client requests per second. Transport queues at most
64 receive events and allows one pending local send. Connect/send deadlines are
five seconds; initial snapshot deadline is eight seconds. Ping starts after the
initial snapshot and repeats every five seconds, with a four-second matching
pong deadline. These are sample bounds, not comprehensive hostile-client protection.

See [data flow](data-sync.md) for display-only smoothing, confirmed HP/MP bars,
visual feedback and the application RTT/counter definitions.
