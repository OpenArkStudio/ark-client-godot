# Validation

## Environment

Local client checks on 2026-11-02 used an Apple M4 Pro with 14 logical CPU cores and 48 GiB memory, macOS 27.0.1 arm64, Godot `4.7.2.stable.mono.official.ed1daf0bf`, .NET SDK `8.0.427` and runtime `8.0.33`. Projects target `net8.0`.

Client inputs match `6009266`. The server runtime inputs match accepted ark `931b68f3`; the existing native binaries were reused from the byte-equivalent `bc90b681` input scope, rather than rebuilt under the later commit label.

The client uses official protoc 33.4 generated C# and pinned Google.Protobuf 3.33.4. The protocol manifest records schema, generated-source and shared-fixture SHA-256 values. The server is the real ark launcher with Net, Bus, Utility, Kernel and the guest-room plugin.

## Results

| Check | Earlier result |
| --- | --- |
| Permanent C# tests | 21 groups passed: the original 20 plus schema snake_case, enum numbers, unchanged binary action bytes and lowercase Protobuf JSON enum text |
| Cross-language contract | Schema and 10 room-frame / 13 scalar-boundary vectors match the server byte-for-byte; official parsers preserve the encoded samples |
| Generated source | protoc 33.4 regenerates the committed C# source byte-for-byte |
| Protected server checks | ark [#247](https://github.com/OpenArkStudio/ark/pull/247) passed the required Linux/macOS/Windows candidate checks before merge; detailed native validation belongs to the server reports |
| Portable clients against ark | Two C# clients passed late join, movement, HP/MP, capped rest, removal, fresh Entity GUID reconnect and ping. Each of three connections received exactly one full room baseline |
| Godot build and offline scene | Passed with zero build warnings/errors and an explicit C# scene readiness marker |
| Two actual Godot clients | Alice and Bob each observed their own and the peer's damage 85/100, skill 85/80 and recovery 100/100 HP/MP before reconnecting. Both then observed fresh identities, reset attributes and authoritative movement |
| Rendered clients | The same two-client assertions passed with the macOS display driver; both 1120 × 824 viewport captures show the YOU/PLAYER labels, distinct markers, HP/MP bars and room UI |

Movement and attribute updates use Protobuf scalar deltas after the initial baseline. Entity field definitions and business profiles remain JSON. The UI distinguishes authoritative state from visual smoothing and exposes baseline/delta counters.

The headless and rendered checks both exited successfully, followed by a successful SIGINT shutdown of the owned server. Captures and raw logs remain local.

## Exported network integration on 2026-11-03

A fresh six-component server build from ARK candidate
`8bdd734b9837015cab97f022c7258830ee889f47` used Launcher, Bus, Kernel, Net,
Utility and guest_room on isolated loopback port 19492. The CONSUL, ETCD_CLIENT,
KAFKA and OSSSDK runtime plugin flags were explicitly OFF. The original exported
app's 386 files were reused unchanged. [Export inputs and scope](export.md)
record its provenance and matching protocol/dependency inputs.

| Check | Result |
| --- | --- |
| Two real exported clients | Join, own/peer movement and HP/MP actions passed; Alice 2→3 and Bob 1→4 reconnected with reset attributes/sequences |
| Rendered execution | Both clients and runner exited 0; both 1120×824 captures were visually inspected |
| Server provenance and stop | The owned process loaded the new app and five matching DSOs, then stopped on SIGINT/exit 0 without force; listener released |

This run did not repeat the 21 client Core test groups, portable client tests, client build/Editor import,
package export or hosted checks. The earlier results above and the original export
retain their own inputs.

## Bounded sustained run on 2026-11-03

These later portable C# checks are separate from the engine results above.

| Scope | Local result |
| --- | --- |
| 600-second activity | All three clients exited 0; 7096 moves, actions and pings each were acknowledged, and cross-process verification passed. The original wrapper exited 1 afterward because of Bash PID-array cleanup |
| 30-second wrapper regression | After the cleanup fix, the wrapper and all clients exited 0 |

Both owned servers stopped with SIGINT/exit 0 and their listeners exited. Exact
source revisions, binary hashes, source/blob equivalence and resource observations
are in the [local soak report](soak.md). These checks do not run Godot scenes or
exported packages.

## Reproduce

Build and stage the matching [ark room server](https://github.com/OpenArkStudio/ark/tree/main/examples/guest_room), then run:

```sh
bash tools/test-core.sh
bash tools/check-protocol.sh /path/to/ark
PROTOC=/path/to/protoc-33.4 bash tools/generate-protocol.sh --check
bash tools/build.sh
bash tools/check-engine.sh
bash tools/test-live.sh 127.0.0.1 19100
bash tools/smoke-two-clients.sh 127.0.0.1 19100
# Optional: run with two visible windows and capture each client's own viewport.
bash tools/smoke-two-clients.sh 127.0.0.1 19100 --render
```

`DOTNET` and `GODOT` select external executables as described in [run instructions](run.md). The live tests require an empty lobby and local socket access; they verify functional behavior, not latency or throughput. Hosted checks are reported separately on the matching PR. Raw logs remain local.

`--render` requires a graphical desktop. It runs the same movement, HP/MP and reconnect assertions, saves `alice.png` and `bob.png` in the printed log directory, and exits. Captures contain only the Godot viewport; no desktop screen-recording permission is needed. Image creation does not replace visual inspection or desktop export testing. The default remains headless.

The exported run covers only an unsigned, unnotarized macOS arm64 development app on loopback. These bounded checks do not establish production capacity, cross-host behavior, arbitrary plugin hot updates, signed/download distribution or exported-app compatibility on other platforms/architectures. Reconnect creates a new guest identity and state; authentication and persistence are outside this demo.
