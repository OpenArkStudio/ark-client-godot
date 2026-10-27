<p align="right">English · <a href="README_CN.md">简体中文</a></p>

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/OpenArkStudio/.github/main/assets/ark-logo-dark.svg?v=20261022">
    <img src="https://raw.githubusercontent.com/OpenArkStudio/.github/main/assets/ark-logo-light.svg?v=20261022" alt="ARK" width="337">
  </picture>
</p>

<h3 align="center">A Godot and C# client demo for ark.</h3>

<p align="center">Share a room. Move together. See state synchronize.</p>

<p align="center">
  <a href="https://github.com/OpenArkStudio/ark-client-godot/actions/workflows/ci.yml"><img src="https://github.com/OpenArkStudio/ark-client-godot/actions/workflows/ci.yml/badge.svg?branch=main" alt="Client checks"></a>
  <a href="project.godot"><img src="https://img.shields.io/badge/Godot-4.7.2%20.NET-5267df" alt="Godot 4.7.2 .NET"></a>
  <a href="Ark.Client.Godot.csproj"><img src="https://img.shields.io/badge/.NET-8-5267df" alt=".NET 8"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache--2.0-5267df" alt="Apache-2.0"></a>
</p>

<p align="center"><a href="docs/run.md"><strong>Get started →</strong></a> &nbsp; · &nbsp; <a href="docs/data-sync.md">Data synchronization</a> &nbsp; · &nbsp; <a href="https://github.com/OpenArkStudio/ark/tree/main/examples/guest_room">Server example</a> &nbsp; · &nbsp; <a href="docs/validation.md">Validation</a></p>

---

## Features

- **Shared room:** join a pixel-art village, see other guests and move with WASD or arrow keys.
- **Entity attributes:** HP/MP actions and numeric bars show server-owned state and confirmed changes.
- **Player identity:** YOU/PLAYER nameplates and distinct ground markers identify yourself and other players.
- **Network visibility:** inspect RTT, message counts and proposed versus confirmed positions; compare visual smoothing with raw snapshots and try disconnecting/rejoining.

![Two guests in the village with synchronized positions and HP/MP](docs/images/room.png)

## Get started

You need:

- **Godot 4.7.2 .NET** — the standard editor cannot run this C# project.
- **.NET 8 SDK**.
- The companion ark [`guest_room` server](https://github.com/OpenArkStudio/ark/tree/main/examples/guest_room), built from the current matching example and running on `127.0.0.1:19100`. Follow its build/run instructions first; no database or Registry service is required.

Clone and build:

```sh
git clone https://github.com/OpenArkStudio/ark-client-godot.git
cd ark-client-godot
bash tools/build.sh
```

Use your Godot .NET executable for `godot`. In two terminals, run Alice and Bob:

```sh
# Terminal 1
godot --path . -- --host=127.0.0.1 --port=19100 --name=Alice --connect
```

```sh
# Terminal 2
godot --path . -- --host=127.0.0.1 --port=19100 --name=Bob --connect
```

Portable toolchain setup and troubleshooting are in the [run guide](docs/run.md).

## Explore the demo

| Control | Result |
| --- | --- |
| WASD / arrow keys | Move; both clients see the server-confirmed position |
| **Take hit** / **Use skill** / **Rest** | Reduce HP by 15 / spend 20 MP / restore 20 HP and MP, within server limits |
| **Leave** → **Connect**, or **Rejoin** | Join as a fresh guest with initial HP/MP of 100 |
| **NETWORK** / **Visual smoothing** | Inspect synchronization and compare raw positions with eased rendering |

The client proposes actions; ark decides the resulting positions and attributes. Unexpected disconnects retry up to five times. Reconnecting creates fresh guest state. The sample has 16 guest slots and does not provide accounts, persistence, encryption, web export or production capacity validation.

[Data flow](docs/data-sync.md) · [Protocol](docs/protocol.md) · [Tests](docs/validation.md) · [Contribute](AGENTS.md) · [Art credits](docs/third-party-notices.md)

---

[Apache-2.0](LICENSE) · [CC0 Ninja Adventure art](docs/third-party-notices.md) · [OpenArkStudio](https://github.com/OpenArkStudio)
