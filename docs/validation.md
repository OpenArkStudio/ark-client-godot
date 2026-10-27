# Validation scope

Local validation on 2026-10-26 and 2026-10-27 used macOS arm64, Godot
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
| Bounded native-window scene/join execution | Passed with the Compatibility renderer and a real guest join; this does not replace screenshot inspection |
| Two headless .NET engine clients against the real ark guest-room sample | Passed: both joined, saw authoritative moves, rejoined with fresh IDs, observed the peer's replacement ID, and recorded matching-pong RTT with actual frame/byte counters |

The two-peer check used the actual ark launcher and example plugin with Net,
Bus, Utility and Kernel, rather than a replacement TCP fixture server. Local
smoke logs remain outside version control. The scripts in [run.md](run.md)
reproduce the client checks against a matching example server.

This record does not establish visual inspection, hosted CI, exported desktop
packages, deployment, account authentication, durable identity, cross-host
networks or production capacity. Those require their own evidence.

The repository includes cross-platform portable checks and a Linux offline
Godot .NET check in GitHub Actions. This local record does not claim that those
hosted jobs have run or passed.
