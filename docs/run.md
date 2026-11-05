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
   manifest and an available listener port. Rebuild both peers with matching
   `entity_sync.proto` and `guest_room.proto`; the current contract requires
   handshake value 2 and the `ARK2` sample header. This is a breaking change with no compatibility fallback;
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
   → rest committed states at action sequences 1 → 2 → 3. Both views must then
   observe potion patch → row removal → row reuse → weapon switch → child power
   patch → original child return at sequences 4 through 9. Only then reconnect
   with fresh IDs. Both new identities must have 100 HP/MP, two potions, an initial
   Blade, action sequence zero and no replayed pending action. The logs must include
   `SMOKE PEER ATTRIBUTES`, `SMOKE NESTED`, `SMOKE ATTRIBUTES` and `SMOKE PASS`.
   The script retains logs in a
   local temporary directory and returns nonzero on failure.
6. Open `project.godot` in the .NET editor for visual inspection. Run two windows,
   join as Alice and Bob, move with WASD/arrows, disconnect, and rejoin. Confirm
   the visible roster, positions, potion quantities and equipped weapon/power
   follow committed server baselines and deltas.

The main scene is `Scenes/GuestRoom.tscn`; its C# node is `Scripts/GuestRoom.cs`.
`GuestRoomHud.cs` owns native containers and the foldable diagnostics/roster;
`GuestRoomWorld.cs` draws the licensed pixel-art plaza and characters.
`GuestItemIcon.cs` draws potion, Blade and Staff silhouettes. The fixed
960×540 arena is centered in a resizable 1120×884 logical canvas; diagnostics are closed by default.
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
The item row shows confirmed potion quantity and equipped weapon/power. **Drink**
consumes one potion and restores up to 10 HP, including consumption at full HP.
Drinking the last potion empties the table; **+ Potion** adds one and reuses row 1
with a newer incarnation when empty. The stack limit is 99. **Switch weapon**
swaps Blade and Staff; **+ Power** strengthens the current child, capped at 100.
Switching back preserves that weapon's identity and prior power. New guests have
two potions and Blade at power 10; Staff starts at power 20.
There is one pending action at a time. Movement remains available, including at
zero HP. Matching action rejection clears the request and shows feedback without
leaving; an unrelated movement delta does not confirm it. Rejoin clears it
without retry. Buttons never change local item or attribute values before a valid
server update. Empty-inventory and full-stack errors are nonfatal correlated
rejections. Item tooltips show row/child identities and lifetimes; NETWORK shows
both players' item values, maxima, confirmed `action_seq` and pending/proposed actions.
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
Recorded development-package inputs are in [local macOS export](export.md).
Use an export built from the matching current client for this contract.

Run `bash tools/check-format.sh` after restoring/building the projects to verify
C# whitespace and explicit control-flow braces. The `Client checks` workflow
selects SDK `8.0.427` and runs portable checks and formatting on Linux, macOS and
Windows. Its Linux job also verifies the pinned SHA-512 of the official Godot
4.7.2 .NET archive, builds the scene, imports it and requires the C# readiness
marker from an offline engine run. Hosted checks do not start an ark server and
therefore do not establish two-peer framework integration.

Only the current generic Entity protocol is supported. The canonical ark sources
are `src/proto/proto/entity_sync.proto` and `src/proto/proto/guest_room.proto`;
the [client mirrors](protocol.md) must match both. Historical screenshots, exports,
soak runs and hosted results establish only their recorded inputs, not acceptance
of this contract; see the separate scopes in [validation](validation.md).

Run `bash tools/check-protocol.sh /path/to/matching/ark` for pinned SHA and
byte-for-byte schema/fixture parity. With protoc 33.4 installed, run
`PROTOC=/path/to/protoc-33.4 bash tools/generate-protocol.sh --check` to reproduce
the committed official generated source. Neither normal C# builds nor hosted
portable checks require a protoc installation.

Before engine integration, `bash tools/test-live.sh 127.0.0.1 19100` runs two
headless portable C# clients against an initially empty real room. It checks late
join, movement and HP/MP deltas, accepted capped rest, potion row removal/reuse,
equipment replacement/strengthening and each weapon's retained power, peer removal,
fresh reconnect and ping. It requires exactly one full baseline per connection. Malformed payloads,
revision gaps and atomic rejection remain separate permanent fixture/loopback tests.
The live runner does not establish same-flush server coalescing by itself.

For two resident portable C# clients and a third that repeatedly rejoins, run
`bash tools/soak-three-clients.sh 127.0.0.1 19100 600 /absolute/local/soak-run`
against a dedicated empty room. The explicit output directory must be absent or
empty and outside Git repositories. The bounded nested workload covers movement,
HP/MP, potion row removal/reuse, equipment replacement/power/identity, both
resident views, ping and periodic fresh reconnects. Its permanent preparation
test is `--soak-workload`; duration, cleanup and the actual 30-second feature
result with its separate server-error observation are in
[bounded local multi-process soak](soak.md). Sustained acceptance remains
**PENDING** until the 5,000-connection issue is fixed and passes.
