# ark-client-godot

**A playable 2D networking demo for [ark](https://github.com/OpenArkStudio/ark), built with Godot and C#.**

[English](README.md) · [简体中文](README_CN.md) · [Run guide](docs/run.md) · [Data synchronization](docs/data-sync.md)

![Two guests in the village, with distinct player markers and synchronized HP/MP](docs/images/room.png)

Explore a shared village with two clients and see how input becomes server-owned state. Move together, change HP/MP, and reconnect while inspecting the messages behind the scene.

| Explore | What it demonstrates |
| --- | --- |
| Shared room | Guest join/leave, a synchronized roster and server-confirmed positions |
| Player identity | **YOU** with a diamond and double ground ring; **PLAYER** with a hollow marker |
| HP and MP | Server-validated actions, numeric bars and confirmed attribute-change feedback |
| Connection recovery | Visible connection state, bounded retries and a fresh guest session after reconnecting |
| Network diagnostics | Application RTT, frame/byte counts, sequences and proposed versus confirmed positions |

## Try it locally

You need:

- **Godot 4.7.2 .NET** — the standard editor cannot run this C# project.
- **.NET 8 SDK**.
- The matching ark **ARK2** [`guest_room` server](https://github.com/OpenArkStudio/ark/tree/main/examples/guest_room), running on `127.0.0.1:19100`. Follow its build/run instructions first; no database or Registry service is needed.

Clone and build the client:

```sh
git clone https://github.com/OpenArkStudio/ark-client-godot.git
cd ark-client-godot
bash tools/build.sh
```

Use your Godot .NET executable for `godot`. Open **two terminals** in this directory:

```sh
# Terminal 1
godot --path . -- --host=127.0.0.1 --port=19100 --name=Alice --connect
```

```sh
# Terminal 2
godot --path . -- --host=127.0.0.1 --port=19100 --name=Bob --connect
```

For portable toolchains, editor setup and troubleshooting, see the [run guide](docs/run.md).

## Explore the room

| Control | Result |
| --- | --- |
| WASD / arrow keys | Move your character; the other client sees the server-confirmed position |
| **Take hit** | Reduce your HP by 15 |
| **Use skill** | Spend 20 MP, if enough remains |
| **Rest** | Restore 20 HP and MP, capped at their maxima |
| **Leave** → **Connect** | Leave, then join as a fresh guest; **Rejoin** also starts a fresh session |
| **NETWORK** | Inspect transport and synchronization details; toggle raw server markers |
| **Visual smoothing** | Compare eased rendering with raw server positions |

HP/MP start at 100. Actions request a change; the server decides the result. Zero HP does not prevent movement in this demo. Unexpected disconnects retry up to five times; reconnecting resets guest state rather than restoring it.

## How it fits together

`src/Ark.Client.Core` contains framing, TCP transport and room state without a Godot dependency. `Scripts/` and `Scenes/` handle input, presentation and engine lifecycle. The companion ark plugin owns Entity fields and broadcasts room snapshots.

- [Data flow and metric definitions](docs/data-sync.md)
- [ARK2 wire protocol](docs/protocol.md)
- [Tests and validation scope](docs/validation.md) — compiled C# checks, real two-client integration, server restart and local manual review
- [Art credits](docs/third-party-notices.md) — CC0 Ninja Adventure by Pixel-Boy and AAA

Run the portable regressions with `bash tools/test-core.sh`. See [AGENTS.md](AGENTS.md) for contribution conventions.

This desktop TCP demo provides temporary guest sessions for up to 16 players. Accounts, persistence, encryption and web export are outside its scope; it is not a production capacity benchmark. ARK2 requires matching client/server versions and differs from the legacy Unity protocol.

Licensed under [Apache-2.0](LICENSE).
