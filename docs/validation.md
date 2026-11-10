# Validation

Two freshly exported Godot clients passed the room flow with a freshly built ark server on **2026-11-10**. This was one bounded loopback functional run, not a capacity or sustained-load test.

## Inputs

Machine: Apple M4 Pro Mac mini, 14 logical cores, 48 GiB memory, macOS 27.0.1 (26A434), arm64.

| Input | Tested value |
| --- | --- |
| ark | `7db3640b3cc3ef46e3ab3f1b356cd33aa6c1736f` |
| Godot client | `3f2b1e2e00033fdaf8450854627aa28b66bcfc02` |
| arknet pin | `d8ff34162cd1b490683f579ef23431068d8b7d8b` |
| Native build | Debug, Apple Clang 21.0.0; launcher and all five matching plugin images rebuilt; actual loaded paths and hashes checked |
| Native dependencies | Boost 1.92.0, OpenSSL 4.1.0; matched Protobuf 33.4 headers, compiler and archive |
| Client tools | Godot `4.7.2.stable.mono.official.ed1daf0bf`, SDK 8.0.427, Google.Protobuf 3.33.4 |
| Export | Universal 2 development app, self-contained .NET 8.0.33 runtimes; executed natively on arm64 |
| Wire inputs | Both canonical schemas and shared vectors match the server; pinned protoc generation check passed |

## Results

| Check | Result |
| --- | --- |
| Join and movement | Both players joined the same room and observed each other's authoritative positions |
| HP/MP | Both observed own and peer damage 85/100 → skill 85/80 → rest 100/100 |
| Nested Entity updates | Both observed row patch, deletion and reuse; equipment replacement, child patch and reappearance before reconnecting |
| Reconnect baseline | Alice 1→3 and Bob 2→4; fresh identities, inventory/equipment, HP/MP and action sequences validated |
| Presentation | Both 1120 × 884 captures inspected: YOU/PLAYER text, distinct markers, committed HP/MP, potion quantity and weapon power |
| Export identity | Both ran the exported executable without `--path` or editor/SDK environment variables; all 386 bundle files retained their hashes |
| Server diagnostics | Zero error/warning-level records and zero retired-Entity update errors; disconnect EOF and accept cancellation on stop were expected info records |
| Cleanup | Both clients and runner exited 0; server exited 0 on SIGINT without force, and port 19690 was released |

The server loaded the real launcher, Bus, Kernel, Net, Utility and guest-room images. Default logging was console-only; stdout/stderr and both client logs were reviewed. Raw logs, captures and full provenance remain local.

## Reproduce

Build and start the matching [ark room server](https://github.com/OpenArkStudio/ark/tree/main/examples/guest_room) with an empty lobby, then [export the client](export.md):

```sh
bash tools/export-macos.sh /absolute/output/ArkGuestRoom.app
EXPORTED_CLIENT="/absolute/output/ArkGuestRoom.app/Contents/MacOS/ARK Guest Room" \
  bash tools/smoke-two-clients.sh 127.0.0.1 19100 --render
```

Protocol and portable regression commands are in the [run guide](run.md); export prerequisites are in [macOS export](export.md).

Only the **unsigned, unnotarized local arm64 development app** was executed. The x86_64 payload was inspected but not run. Gatekeeper/download acceptance, Windows/Linux client execution and cross-host deployment remain unverified. Capacity and sustained-load acceptance await the deferred [5,000-connection investigation](https://github.com/OpenArkStudio/arknet/issues/61). Release scope and acceptance criteria remain undecided.
