# Observe the network and data flow

The client proposes a target position and a strictly increasing move sequence.
The ark example validates the sequence and room bounds on its owner thread,
updates the Kernel-owned player entity, and broadcasts a full room snapshot.
Both clients update their authoritative `RoomSession` from those snapshots.
Joins, accepted moves and disconnects trigger snapshots; this is not a fixed-rate
snapshot stream.

```mermaid
flowchart LR
    Input[WASD / arrows] --> Proposal[Client proposal: seq + target]
    Proposal -->|60002 / ARK2 TCP| Net[ark Net callback / owner thread]
    Net --> Check[Validate finite bounds + increasing seq]
    Check -->|Accepted| Kernel[Kernel-owned GuestPlayer state]
    Check -->|60004 rejected| Error[Visible client error]
    Action[Take hit / Use skill / Rest] -->|60008: independent seq + kind| ActionCheck[Server validates action / fixed amounts]
    ActionCheck -->|Accepted| Kernel
    ActionCheck -->|60004 + action_seq| Rejection[Nonfatal matching-action feedback]
    Kernel --> Snapshot[Sample builds full room snapshot]
    Snapshot -->|60003 / TCP to joined peers| Room[Each client: RoomSession]
    Room --> Raw[Optional raw markers + roster + confirmed values]
    Room --> Stats[Confirmed HP/MP bars + numeric values]
    Stats --> Delta[Feedback only for received stat deltas]
    Room --> Display[Display-only easing / raw toggle]
    Display --> Avatar[Licensed animated sprite + role nameplate]
```

The compact connection card selects the host, port and guest name. YOU text,
a diamond and double ground ring identify the local ninja; PLAYER text and hollow
markers identify peers independently of color. The 960×540 village plaza is the
main view. NETWORK opens the full roster, server coordinates and diagnostics.
The logical canvas scales while retaining the full playfield. Names remain
complete in state even when visible labels use ellipsis. Art credit and licensing
are recorded in [third-party notices](third-party-notices.md); props add no collision rules.

The **Visual smoothing** toggle controls display-only exponential easing toward
the latest received coordinates. New IDs start at their received position;
removed IDs disappear; reconnect clears all display state. The outlined ring
is a display identity marker; separate raw-server markers are off by default and
can be enabled in NETWORK. Sprite facing and 6 fps walking advance only while
the displayed position actually moves. This is not client prediction, rollback,
extrapolation or interpolation through a time-buffered snapshot history.
`RoomSession` and the server-confirmed sequence/coordinates stay unchanged by
smoothing.

Expand **NETWORK** to compare actual observations:

| Field | Meaning |
| --- | --- |
| TCP peer | Resolved remote endpoint of the current connection; absent before connect |
| Entity ID | Opaque temporary ID returned by the server, replaced on rejoin |
| Move proposal | Latest generated move sequence and target, before server confirmation |
| Server-confirmed | This player's sequence and coordinates in the latest applied full snapshot |
| HP / MP and maxima | Required server-owned values; bars and numbers update only from a valid full snapshot |
| Action proposal / pending / action_seq | A separate sequence from movement. Only own matching/newer confirmed `action_seq` resolves acceptance; an exact correlated error resolves rejection without leaving |
| Last room update age | Monotonic elapsed time since this client applied the latest valid snapshot; not server generation age or one-way latency |
| App ping RTT | Monotonic time from beginning the ping send call to processing its matching pong on the client owner thread; includes local sends, network transit, server queues/polling and client receive queues/polling |
| Ping / pong nonce | Current pending nonce and last matched nonce; stale or duplicate pongs do not create a new RTT sample |
| Tx completed frames / bytes | Complete application-frame writes accepted by the local stream, including the 12-byte header; no peer receipt/ACK claim, and no estimate of partial failed writes |
| Rx decoded frames | Valid complete ARK2 frames parsed from the stream |
| Rx stream bytes | Actual stream-read bytes, including headers, incomplete data and invalid framing; excludes TCP/IP overhead |

A ping starts after the initial snapshot and repeats every five seconds.
Each connection permits only one in-flight local stream write. A busy connection
defers movement and heartbeat preparation before consuming a sequence, nonce or
deadline; an action click reports nonfatal busy feedback without creating a
pending action. There is no additional send queue and no peer-ACK inference.
Unavailable observations show an em dash rather than fabricated zero latency.
Transport counters belong to one `TcpConnection`; a new instance starts at zero.
Disconnect/rejoin clears proposals, age, pending nonce, RTT and display state.
It also clears pending business actions without retry. New identities begin at
the server's HP/MP values. Taking a hit, using a skill or resting does not alter
client attributes in anticipation. Floating HP/MP amounts compare confirmed
snapshots, so a capped no-op creates no fabricated increase/decrease. HP zero
does not disable demo movement. The request contains no amount or target.
Counter snapshots are immutable and protected by a short transport lock; room,
RTT and presentation state are mutated only on the client owner thread.

There is no latency/loss simulation. Application RTT, byte totals and visible
smoothing do not establish TCP throughput, packet loss, production capacity or
cross-host performance.

The room-update age naturally grows while everyone is idle because snapshots
are event-driven. It does not trigger a connection failure; the actual matching
ping/pong deadline checks health. The metrics text refreshes at about 5 Hz while
avatars update on every display frame.
