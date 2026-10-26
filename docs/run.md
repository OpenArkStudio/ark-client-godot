# Run and verify

Use Godot 4.7.2 .NET and a .NET 8 SDK. Keep the engine and SDK outside this
repository. A portable SDK can be selected without changing global tools:

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
   manifest and an available listener port. It must use the `ARK1` sample header;
   the existing Login listener does not implement this guest protocol.
2. Run portable checks with `bash tools/test-core.sh`. This compiles permanent
   C# checks and exercises real loopback TCP, including fragmented receives,
   reconnect buffer reset and visible connection refusal.
3. Build the Godot C# project with `bash tools/build.sh`.
4. Parse the scene and execute a short offline headless run with
   `bash tools/check-engine.sh`.
5. Exercise two real engine clients against the running server with
   `bash tools/smoke-two-clients.sh 127.0.0.1 19100`. Each client must see both
   players and their authoritative moves, reconnect with a fresh ID, see the
   peer's replacement ID, and print `SMOKE PASS`. The script retains logs in a
   local temporary directory and returns nonzero on failure.
6. Open `project.godot` in the .NET editor for visual inspection. Run two windows,
   join as Alice and Bob, move with WASD/arrows, disconnect, and rejoin. Confirm
   the visible roster and positions follow server snapshots.

The main scene is `Scenes/GuestRoom.tscn`; its C# node is `Scripts/GuestRoom.cs`.
Client options follow Godot's `--` separator:

```sh
"$GODOT" --path . -- --host=127.0.0.1 --port=19100 --name=Alice --connect
```

An unavailable server produces a visible error and bounded retries. Invalid
room messages close the client session with a protocol error. Rejected business
requests remain visible and require manual rejoin. Server errors and logs should
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
