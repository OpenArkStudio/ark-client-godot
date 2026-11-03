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

## Recorded local integration

The original export inputs match `d8d61a33ad3aed30b376d514fd99ddb3d84a734f`,
tree `97b95d3efe9ae74ae27e45c83809138f15bb18ba`. Export and portable locked
restore/build passed with SDK `8.0.427`; those results retain their original
attribution and were not rerun for the network check below.

On **2026-11-03**, the same unsigned app was paired with a newly built ARK
candidate on macOS 27.0.1 arm64, using Godot
`4.7.2.stable.mono.official.ed1daf0bf`. All 386 app files matched the original
hashes before and after execution; no package export, template setup, Core tests
or Editor import was repeated.

| Input / check | Result |
| --- | --- |
| ARK candidate | `8bdd734b9837015cab97f022c7258830ee889f47`; tree `491c38bfd5eb6da86eeaf437f2fd1ccecbdbce14` |
| Fresh server build | Launcher, Bus, Kernel, Net, Utility and guest_room; isolated cache/runtime, loopback port 19492; the owned process loaded this app and all five matching DSOs |
| Optional plugin flags | `ARK_BUILD_PLUGIN_CONSUL`, `ARK_BUILD_PLUGIN_ETCD_CLIENT`, `ARK_BUILD_PLUGIN_KAFKA`, `ARK_BUILD_PLUGIN_OSSSDK`: all `OFF` |
| Protocol inputs | Server/client schema and wire vectors matched byte-for-byte; Boost 1.92, installed arknet matching `a0dfe09cd543e75cfb5996e537ce2fa49f2a758f`, matched Protobuf 33.4 headers/protoc/archive |
| Two exported rendered clients | READY, join, movement and own/peer damage 85/100 → skill 85/80 → rest 100/100 passed; reconnect Alice 2→3 / Bob 1→4 reset attributes and sequences; both clients and runner exited 0 |
| Captures | Both 1120×824; room, YOU/PLAYER markers and reset HP/MP visually inspected |
| Server shutdown | SIGINT exit 0 without force; listener released |

This proves only this **unsigned, unnotarized local arm64 development app** on
loopback. The original export's strict signature check exited 1 because Godot's
inherited template signature does not cover the bundle; it was not repeated here.
Execution passed without editor/SDK environment variables.
Intel, Gatekeeper/download acceptance, Windows/Linux, cross-host behavior and
production use remain untested. Raw provenance and logs remain in the local receipt.

Official references: [macOS export](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_for_macos.html),
[Godot .NET downloads](https://godotengine.org/download/macos/).
