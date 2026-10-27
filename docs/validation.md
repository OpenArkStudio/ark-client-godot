# Validation scope

The ARK2 room demo was checked on 2026-10-27 with client runtime source
`decae703e4b0f258cdc3fd6a80b713168c74e07f` and ark server source
`8221a6f1bad2802e58783fa365cdd634c07ee144`. They correspond to
[client PR #3](https://github.com/OpenArkStudio/ark-client-godot/pull/3) and
[ark PR #180](https://github.com/OpenArkStudio/ark/pull/180); upgrade both sides
of the ARK2 protocol together.

## Environment

Apple M4 Pro, 14 logical CPU cores, 48 GiB memory, macOS 27.0.1 arm64;
Godot `4.7.2.stable.mono.official.ed1daf0bf`, local .NET SDK `10.0.403` and
.NET runtime `8.0.33`. Projects target `net8.0`. The hosted client checks use
SDK `8.0.427`; a normal .NET 8 SDK supplies the required runtime and reference packs.

## Results

| Check | Result |
| --- | --- |
| Compiled C# regressions | 16 groups passed: framing, field/range validation, action correlation, sequence bounds, confirmed-only deltas, role markers, stat-panel bounds, deferred send preparation, own/peer action evidence and reconnect reset |
| Godot build, import and offline execution | Passed, zero build warnings/errors and explicit ARK2 C# readiness marker |
| Actual two-engine room | Both clients saw their own and the peer's damage 85/100, skill 85/80 and recovery 100/100 HP/MP at action sequences 1/2/3; authoritative movement and fresh rejoin passed |
| Native export → JSON → Kernel → Godot | The C++ tool exported the field table and profile; both server instances loaded that JSON during the continuity/restart campaign |
| Continuous connection and churn | Two Godot processes stayed connected for 300 seconds while ten additional two-client join/action/move/rejoin cycles ran at 30-second intervals; maximum four simultaneous clients |
| Server restart | The same two persistent Godot processes automatically rejoined; another action/movement/rejoin pair passed after restart |
| Cleanup | Both owned server instances exited normally; all 26 owned child processes exited and were reaped |
| Hosted client checks | [Run 40452878174](https://github.com/OpenArkStudio/ark-client-godot/actions/runs/40452878174) passed all three OS jobs at the runtime revision above; Linux also built/imported/ran the offline Godot scene |
| Visual/manual controls | Village art, YOU/PLAYER labels, distinct markers and keyboard movement were reviewed before the attribute change. Populated HP/MP controls still require a separate visual/manual review |

The server is the actual ark launcher with Net, Bus, Utility, Kernel and the
room plugin, rather than a replacement TCP server. Each smoke pair requires
both its own and the peer's intermediate attribute states before reconnect;
checking only a final 100/100 snapshot would not satisfy it. A fresh connection
loads 100 HP/MP, action sequence 0 and no pending or replayed action.

The server selects JSON Entity fields and business profiles. The existing
plugin-bootstrap configuration wrapper remains XML. See [data synchronization](data-sync.md)
and [run instructions](run.md) for the model and permanent compiled/engine checks.

## Limits

This is bounded local functional continuity and recovery evidence. It does not
establish performance, memory-leak absence, production capacity, cross-host
behavior or exported desktop-package acceptance. Server-process restart creates
new sessions and may reuse numeric IDs; it does not restore an account or prior
state. Authentication and persistence remain outside the demo. Hosted offline
checks do not start an ark server. Raw logs and measurement receipts remain local.
