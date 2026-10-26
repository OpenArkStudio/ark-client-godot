# ark-client-godot

A Godot C# client for OpenArkStudio [ark](https://github.com/OpenArkStudio/ark).
Join a shared 2D guest room, move using WASD or arrow keys, and reconnect with a
fresh guest identity. Player positions and the roster come from server snapshots.

[中文说明](README_CN.md)

## Run

Prerequisites: Godot **4.7.2 .NET**, a **.NET 8 SDK**, and the companion ark
`guest_room` example server. The standard Godot editor cannot build this C#
project. This sample uses raw TCP on desktop; it does not target web exports.

```sh
bash tools/test-core.sh
bash tools/build.sh
godot --path . -- --host=127.0.0.1 --port=19100 --name=Alice --connect
```

Use the Godot .NET executable for `godot`. Set `DOTNET` and `GODOT` to executable
paths when using the shell scripts with portable runtimes. `DOTNET_ROOT` must
point to that SDK/runtime directory when the engine cannot discover it.

Open a second client as Bob to see both server-owned players. Use **Disconnect**
to leave and **Connect / Rejoin** to create a new identity. Unexpected disconnects
retry up to five times with 1, 2, 4, 8 and 8 second delays; errors remain visible.
The room has 16 guest slots. Guest names contain at most 64 UTF-8 bytes.

See [run and verification](docs/run.md) and the [wire contract](docs/protocol.md).
The sample provides temporary guest sessions. It does not provide account
credentials, identity resumption, persistence, encryption or production capacity
validation. Legacy Unity login/game messages use a different protocol.

## Structure

- `src/Ark.Client.Core`: portable framing, TCP transport and room state; no Godot dependency.
- `Scripts` and `Scenes`: Godot input, drawing, status and reconnect lifecycle.
- `tests`: compiled C# checks and shared wire vectors.

Licensed under [Apache-2.0](LICENSE). Contribution conventions: [AGENTS.md](AGENTS.md).
