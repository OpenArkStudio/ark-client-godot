# Validation scope

Local validation on 2026-10-26 and 2026-10-27 used an Apple M4 Pro with 14 CPU
cores and 48 GiB memory, macOS 27.0.1 arm64, Godot
`4.7.2.stable.mono.official.ed1daf0bf`, .NET SDK `10.0.403`, and .NET runtime
`8.0.33`. All projects target `net8.0`. A normal .NET 8 SDK provides the matching
runtime and reference packs; the local SDK 10 setup restored official .NET 8
reference packs and retained the matching .NET 8 runtime alongside runtime 10.

| Check | Result |
| --- | --- |
| Permanent compiled C# checks | 12 groups passed, covering shared vectors, split/multiple frames, lengths/headers, state/sequence separation, bounded labels, immutable counters, nonce-correlated RTT/age/reset, display smoothing/removal/fresh IDs, role identity and displacement-driven walking, real TCP and reconnect |
| Godot C# solution build | Passed, zero warnings and errors |
| Official .NET editor headless import | Passed |
| Offline engine scene execution | Passed with explicit C# scene readiness marker |
| Native-window visual inspection | Passed with two real guests, licensed village art, distinct YOU/PLAYER text and markers, keyboard movement and a closed network drawer |
| Two headless .NET engine clients against the real ark guest-room sample | Passed: both joined, saw authoritative moves, rejoined with fresh IDs, observed the peer's replacement ID, and recorded matching-pong RTT with actual frame/byte counters |
| Five-minute continuous run | Passed: two Godot processes remained connected for 300 seconds while ten additional two-client join/move/rejoin cycles ran at 30-second intervals; at most four simultaneous clients |
| Server restart | Passed: the same two Godot processes automatically joined the restarted server; a subsequent two-client movement/rejoin check passed, and both server instances stopped normally |
| Hosted client CI | Passed on Windows, macOS and Linux; Linux also verified the engine archive and built, imported and ran the offline Godot C# scene |

The two-peer check used the actual ark launcher and example plugin with Net,
Bus, Utility and Kernel, rather than a replacement TCP server. Local
smoke logs remain outside version control. The scripts in [run.md](run.md)
reproduce the client checks against a matching example server.

## Tested revisions

- Client source: `14c084b813053325aea5e0a3ce244213e2a92f7c`, accepted as
  `4163352875610d5712f3027afba909345859d0c0` in [client PR #1](https://github.com/OpenArkStudio/ark-client-godot/pull/1).
  Their source trees are identical. [Hosted run 40441829338](https://github.com/OpenArkStudio/ark-client-godot/actions/runs/40441829338)
  passed all three OS jobs using SDK `8.0.427`; hosted checks do not start an ark server.
- The continuous-run and restart campaign used server runtime source
  `33f906bb439cdca65635b4924b1d855b03f7ce15`. Its production sources, room resources,
  configuration and staging scripts match accepted server commit
  `1119f9db748854395224720fb69e438f513119b3` from [ark PR #173](https://github.com/OpenArkStudio/ark/pull/173).
- Combined framework commit `a11ae49c54c71a8ba7cd4a4b21227f788cf94097`, containing
  the room, custom uint32 fix and static service bootstrap, was independently
  rebuilt and passed native room regressions and another real two-Godot-client
  movement/rejoin check. This separate check was not a five-minute run.

The five-minute campaign is bounded functional evidence, not a performance,
memory-leak or production-capacity test. Server-process restart can reuse numeric
player IDs; guests create new sessions without account or state resumption.
Exported desktop packages, deployment, authentication, persistence and cross-host
networks remain outside these checks. Raw logs remain local.
