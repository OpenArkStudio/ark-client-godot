# Local macOS development export

On macOS, use Godot **4.7.2 .NET**, matching **.NET export templates**, and SDK
**8.0.427**. Select external tools and put the SDK on `PATH` as in
[run instructions](run.md). Install templates through **Editor > Manage Export
Templates**. Network access or cached `osx-arm64` / `osx-x64` runtime packs are
required. For the rendered smoke, start a matching guest-room server with an
empty lobby and use a graphical desktop.

```sh
# Choose a new absolute destination; existing apps are preserved.
bash tools/export-macos.sh /absolute/output/ArkGuestRoom.app
EXPORTED_CLIENT="/absolute/output/ArkGuestRoom.app/Contents/MacOS/ARK Guest Room" \
  bash tools/smoke-two-clients.sh 127.0.0.1 19100 --render
```

| Setting | Behavior |
| --- | --- |
| `macOS development` | Official Universal 2 debug template; signing and notarization disabled |
| Bundle | C# assemblies and self-contained runtime for arm64 and x86_64; ETC2/ASTC imports enabled |
| Core locks | Separate macOS RID locks; original portable lock and locked restore retained |
| Export guard | Rejects engine error diagnostics and missing executable/architecture assemblies |
| `EXPORTED_CLIENT` | Absolute executable path; omits `--path` and uses bundled assets |
| Smoke output | Logs and captures in the printed temporary directory; omit `--render` for headless |

Local acceptance on **2026-11-03** used macOS 27.0.1, Apple M4 Pro arm64,
Godot `4.7.2.stable.mono.official.ed1daf0bf` and SDK `8.0.427`.

| Input / check | Result |
| --- | --- |
| Client source | `d8d61a33ad3aed30b376d514fd99ddb3d84a734f` |
| Client tree | `97b95d3efe9ae74ae27e45c83809138f15bb18ba` |
| ARK server source | `c9aa911a4893ffa2681925e8ebf13e703f8f4c16`; six reused exact-candidate binaries, isolated loopback port 19486 |
| Export / portable locked restore and build | Passed, exit 0; original portable lock unchanged |
| Two exported rendered clients | READY, join, movement, own/peer damage → skill → rest, fresh-identity reconnect passed; both clients and runner exited 0 |
| Captures | Both 1120×824; room, YOU/PLAYER markers and reset HP/MP visually inspected |
| Server shutdown | SIGINT exit 0 without force; listener released |

This is an **unsigned, unnotarized local development app**. Godot's inherited
template signature does not cover this bundle; strict signature verification
exited 1. Native arm64 execution passed without editor/SDK environment variables.
Intel, Gatekeeper/download acceptance, Windows/Linux, cross-host behavior and
production use remain untested. Raw provenance and logs remain in the local receipt.

Official references: [macOS export](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_for_macos.html),
[Godot .NET downloads](https://godotengine.org/download/macos/).
