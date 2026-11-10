# Local macOS development export

Use Godot **4.7.2 .NET**, its matching **.NET export templates**, and SDK **8.0.427**. Select tools and put the SDK on `PATH` as in the [run guide](run.md). Install templates through **Editor > Manage Export Templates**. Export requires network access or cached `osx-arm64` / `osx-x64` runtime packs.

```sh
# Choose a new absolute destination; existing apps are preserved.
bash tools/export-macos.sh /absolute/output/ArkGuestRoom.app

# Start a matching server with an empty lobby, then run two visible app instances.
EXPORTED_CLIENT="/absolute/output/ArkGuestRoom.app/Contents/MacOS/ARK Guest Room" \
  bash tools/smoke-two-clients.sh 127.0.0.1 19100 --render
```

| Setting | Behavior |
| --- | --- |
| `macOS development` | Official Universal 2 debug template; signing and notarization disabled |
| Bundle | Current nested Entity protocol, C# assemblies and self-contained .NET runtimes for arm64 and x86_64 |
| Core locks | Separate macOS RID locks; original portable lock and locked restore retained |
| Export guard | Rejects engine error diagnostics and missing executable/architecture assemblies |
| `EXPORTED_CLIENT` | Absolute executable path; omits `--path` and uses bundled assets |
| Rendered smoke | Requires a graphical desktop; prints the local logs/capture directory. Omit `--render` for headless execution |

## Verified scope

The **2026-11-10** export from client `3f2b1e2` passed two-client integration with ark `7db3640b`, including movement, HP/MP, nested Entity changes, reconnect and normal exit. All 386 bundle files retained their hashes. The clients ran without editor/SDK environment variables. Exact inputs and results are in [Validation](validation.md).

This verifies a local **arm64 development application**. The x86_64 payload is included and inspected, but was not executed. The app is unsigned and unnotarized; strict signature verification fails because the inherited template signature no longer covers the bundle. This does not establish Gatekeeper/download acceptance or production distribution.

Official references: [macOS export](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_for_macos.html), [Godot .NET downloads](https://godotengine.org/download/macos/).
