# Validation

## Local environment

The 2026-11-05 functional checks used an Apple M4 Pro Mac mini, 14 logical cores,
48 GiB memory and macOS 27.0.1 arm64. Tools: Godot
`4.7.2.stable.mono.official.ed1daf0bf`, .NET SDK `8.0.427`, protoc `33.4` and
Google.Protobuf `3.33.4`. Projects target `net8.0`.

The implementation is recorded in client `1fce4f3` and ark `7b219028`, with arknet
pinned to accepted source `d8ff34162cd1b490683f579ef23431068d8b7d8b`. Local execution
used the feature worktrees and their built binaries before publication; hosted
checks and merge acceptance are separate and belong to the corresponding PRs.
No production or accepted-main execution is implied by these local results.

## Results

| Check | Result |
| --- | --- |
| Portable C# | All 22 permanent check groups passed, including real loopback transport and connection failure |
| Shared protocol | Both canonical schemas and 15 room / 13 scalar-boundary vectors match C++ byte-for-byte; protoc regenerates C# exactly |
| Native codec | Generic codec `f950e246`: GUID64 13 cases / 122 assertions; GUID32 13 / 123, including both Windows-macro include orders. Room codec: 8 cases / 518 assertions |
| Native room | Real launcher/plugins and two TCP peers passed row deletion/reuse, equipment replacement/patches, movement, errors, reconnect and normal shutdown |
| Two C# clients | Movement, HP/MP, potion row removal/reuse, both weapon swaps, independent retained power, removal and fresh-identity reconnect passed; each connection installed one room baseline |
| Godot build/import | Build passed without warnings/errors; resource import completed |
| Two visible Godot clients | Both observed every own/peer row patch, removal, reuse, container replacement, child patch and reappearance before reconnecting |
| Presentation | Both macOS viewports captured at 1120 × 884; inventory/equipment controls, YOU/PLAYER labels and committed HP/MP are visible. The README uses the inspected Alice capture |
| Cleanup | Clients/runner exited 0; server exited 0 on SIGINT without force, and its listener closed |

The server used the real Launcher, Bus, Kernel, Net, Utility and guest-room DSOs
on isolated loopback TCP port 19645. Equipment checks compare GUID **and**
incarnation; reused row numbers must have a newer incarnation. Values shown by the
UI come from committed state, not local button effects. Raw logs remain local.

## Reproduce

Start a matching, empty [ark room server](https://github.com/OpenArkStudio/ark/tree/main/examples/guest_room), then run:

```sh
bash tools/test-core.sh
bash tools/check-protocol.sh /path/to/ark
PROTOC=/path/to/protoc-33.4 bash tools/generate-protocol.sh --check
bash tools/build.sh
bash tools/check-engine.sh
bash tools/test-live.sh 127.0.0.1 19100
bash tools/smoke-two-clients.sh 127.0.0.1 19100 --render
```

Select `DOTNET` and `GODOT` as in the [run guide](run.md). Rendered checks require a
graphical desktop; omit `--render` for headless execution. This run used editor
project binaries, not a fresh exported application. [Export instructions](export.md)
retain that distinction. Cross-host deployment is deferred while only this Mac is
available; capacity/sustained acceptance awaits the paused [5,000-connection investigation](https://github.com/OpenArkStudio/arknet/issues/61).
Release scope and acceptance criteria remain undecided.
