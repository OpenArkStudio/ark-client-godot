# Run and verify

Use Godot 4.7.2 .NET and a .NET 8 SDK. Keep the engine and SDK outside this
repository. Restore the pinned official Google.Protobuf 3.33.4 NuGet package
(network access or a populated external package cache is required). A portable SDK can be selected without changing global tools:

```sh
export DOTNET_ROOT=/absolute/path/to/dotnet-sdk
export DOTNET="$DOTNET_ROOT/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export GODOT=/absolute/path/to/Godot_mono.app/Contents/MacOS/Godot
```

The Godot package includes local Godot NuGet packages. If online restore is
unavailable, configure an external NuGet package source pointing to its
`Contents/Resources/GodotSharp/Tools/nupkgs` directory. Do not commit a personal
runtime path or replace global NuGet configuration.

1. Start the companion ark `guest_room` example, selecting its application
   manifest and an available listener port. It must use the matching Protobuf v1 schema and `ARK2` sample header;
   the existing Login listener does not implement this guest protocol.
2. Run portable checks with `bash tools/test-core.sh`. This compiles permanent
   C# checks and exercises real loopback TCP, including fragmented receives,
   reconnect buffer reset and visible connection refusal.
3. Build the Godot C# project with `bash tools/build.sh`.
4. Parse the scene and execute a short offline headless run with
   `bash tools/check-engine.sh`.
5. Exercise two real engine clients against the running server with
   `bash tools/smoke-two-clients.sh 127.0.0.1 19100`. Each client must see both
   players and their authoritative moves, confirm both players' damage → skill
   → rest committed states at action sequences 1 → 2 → 3, and reconnect with fresh IDs.
   Both new identities must have 100 HP/MP, action sequence zero and no replayed
   pending action. The logs must include `SMOKE PEER ATTRIBUTES`,
   `SMOKE ATTRIBUTES` and `SMOKE PASS`. The script retains logs in a
   local temporary directory and returns nonzero on failure.
6. Open `project.godot` in the .NET editor for visual inspection. Run two windows,
   join as Alice and Bob, move with WASD/arrows, disconnect, and rejoin. Confirm
   the visible roster and positions follow committed server baselines and deltas.

The main scene is `Scenes/GuestRoom.tscn`; its C# node is `Scripts/GuestRoom.cs`.
`GuestRoomHud.cs` owns native containers and the foldable diagnostics/roster;
`GuestRoomWorld.cs` draws the licensed pixel-art plaza and characters. The fixed
960×540 arena is centered in a resizable 1120×824 logical canvas; diagnostics are closed by default.
Toggle **Visual smoothing** to compare eased display with raw committed state, and
expand **NETWORK** to compare the latest proposed sequence/target
with confirmed values. RTT is a correlated application measurement, not a TCP
ACK or one-way latency. See [data flow and metric definitions](data-sync.md).
Enable **Raw server markers** there to show authoritative positions; the default
ground rings are identity markers. The local player has YOU text, a diamond and
a double ring; peers have PLAYER text and hollow markers. Sprite walking/facing
uses actual displayed displacement. Scenery is visual only; no collisions were added.
Above-player HP/MP bars include numeric values from the latest committed server state.
The compact bottom panel offers **Take hit** (own HP −15), **Use skill** (own MP −20,
rejected if insufficient) and **Rest** (+20 to each, capped by the server).
There is one pending action at a time. Movement remains available, including at
zero HP. Matching action rejection clears the request and shows feedback without
leaving; an unrelated movement delta does not confirm it. Rejoin clears it
without retry. NETWORK shows maxima, confirmed `action_seq` and pending/proposed actions.
Client options follow Godot's `--` separator:

```sh
"$GODOT" --path . -- --host=127.0.0.1 --port=19100 --name=Alice --connect
```

An unavailable server produces a visible error and bounded retries. Invalid
room messages close the client session with a protocol error. Join errors remain
visible and require manual rejoin; correlated action errors are nonfatal. Server errors and logs should
be inspected before retrying a failing integration check.

Portable checks, Godot compilation, headless engine execution, real-server
integration, and visual inspection establish separate facts. A passing build
alone does not establish gameplay or server compatibility. No account login,
production deployment, exported package, cross-host network or capacity
validation is implied by these checks.

Recorded local results and proof boundaries: [validation scope](validation.md).

Run `bash tools/check-format.sh` after restoring/building the projects to verify
C# whitespace and explicit control-flow braces. The `Client checks` workflow
selects SDK `8.0.427` and runs portable checks and formatting on Linux, macOS and
Windows. Its Linux job also verifies the pinned SHA-512 of the official Godot
4.7.2 .NET archive, builds the scene, imports it and requires the C# readiness
marker from an offline engine run. Hosted checks do not start an ark server and
therefore do not establish two-peer framework integration.

The Protobuf protocol requires the matching updated server and client. Historical
JSON room runs and hosted results do not validate this protocol; see the separate
scopes in [validation](validation.md).

Run `bash tools/check-protocol.sh /path/to/matching/ark` for pinned SHA and
byte-for-byte schema/fixture parity. With protoc 33.4 installed, run
`PROTOC=/path/to/protoc-33.4 bash tools/generate-protocol.sh --check` to reproduce
the committed official generated source. Neither normal C# builds nor hosted
portable checks require a protoc installation.

Before engine integration, `bash tools/test-live.sh 127.0.0.1 19100` runs two
headless portable C# clients against an initially empty real room. It checks late
join, movement and HP/MP deltas, accepted capped rest, peer removal, fresh reconnect
and ping. It requires exactly one full baseline per connection. Malformed payloads,
revision gaps and atomic rejection remain separate permanent fixture/loopback tests.
The live runner does not establish same-flush server coalescing by itself.
