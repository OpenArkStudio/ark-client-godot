# Guest-room protocol v1

This sample uses its own explicitly selected ark message header. Existing ark
native CS and SS headers are unchanged. Transport is raw TCP, with no Godot
multiplayer or ENet framing. The client and companion server share
[`tests/fixtures/wire-vectors.json`](../tests/fixtures/wire-vectors.json).

| Offset | Bytes | Value |
| --- | --- | --- |
| 0 | 4 | ASCII `ARK1`, including protocol version |
| 4 | 2 | Message ID, unsigned big-endian |
| 6 | 2 | Flags, unsigned big-endian, must be zero |
| 8 | 4 | Body byte length, unsigned big-endian, excluding header, at most 4096 |

The body is a UTF-8 JSON object. Invalid markers, flags and oversized lengths
close the session before business dispatch. A framing-only empty-body vector
exercises the codec; an empty body is not a valid room JSON message. Retain
partial frames, parse coalesced frames, and discard buffered data on reconnect.

| ID | Direction | Body |
| --- | --- | --- |
| 60000 | Client → server | `{"name":"Alice","room":"lobby"}` |
| 60001 | Server → joining client | `{"player_id":"1","name":"Alice","room":"lobby","width":960,"height":540}` |
| 60002 | Client → server | `{"seq":1,"x":120,"y":100}` |
| 60003 | Server → joined peers | `{"room":"lobby","players":[{"id":"1","name":"Alice","x":120,"y":100,"seq":1}]}` |
| 60004 | Server → client | `{"code":"invalid_message","message":"reason"}` |
| 60005 | Client → server | `{"nonce":1}` |
| 60006 | Server → client | `{"nonce":1}` |

One fixed `lobby` contains at most 16 players. Names are nonempty, at most 64
UTF-8 bytes, and contain no control characters. Sequence and nonce are unsigned
32-bit integers. Moves start at sequence 1 and strictly increase. Coordinates
must be finite and within `0..960` and `0..540`. Initial positions belong to the
server. Successful joins send acceptance before the full snapshot; accepted
moves and disconnects broadcast updated snapshots.

IDs are opaque process-local strings. Rejoining creates a fresh ID and full
snapshot; the client clears the old roster, sequence and partial frame. Errors
include `room_full`, `already_joined`, `not_joined`, `invalid_message` and
`stale_sequence`. Invalid business requests do not change room state. The UI
shows server errors and requires manual retry for rejected joins.

The client sends movement at most 10 times per second. Transport queues at most
64 receive events, retains at most one incomplete 4108-byte frame, and allows
one pending send. Connect and send operations have five-second deadlines; a
join requires a full snapshot within eight seconds. Joined clients ping every
five seconds and require the corresponding pong within four seconds. These
bounds describe the demonstration, not comprehensive hostile-client protection.
