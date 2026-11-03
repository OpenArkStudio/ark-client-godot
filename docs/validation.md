# Validation

## Environment

Local client checks on 2026-11-02 used an Apple M4 Pro with 14 logical CPU cores and 48 GiB memory, macOS 27.0.1 arm64, Godot `4.7.2.stable.mono.official.ed1daf0bf`, .NET SDK `8.0.427` and runtime `8.0.33`. Projects target `net8.0`.

Client inputs match `6009266`. The server runtime inputs match accepted ark `931b68f3`; the existing native binaries were reused from the byte-equivalent `bc90b681` input scope, rather than rebuilt under the later commit label.

The client uses official protoc 33.4 generated C# and pinned Google.Protobuf 3.33.4. The protocol manifest records schema, generated-source and shared-fixture SHA-256 values. The server is the real ark launcher with Net, Bus, Utility, Kernel and the guest-room plugin.

## Results

| Check | Local result |
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

## Reproduce

For a longer run with two resident processes and a third process repeatedly
joining and leaving, see the [bounded local multi-process soak](soak.md).

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

These bounded checks do not establish production capacity, cross-host behavior, arbitrary plugin hot updates or exported desktop-package compatibility. Reconnect creates a new guest identity and state; authentication and persistence are outside this demo.
