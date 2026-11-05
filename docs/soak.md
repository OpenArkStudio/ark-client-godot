# Bounded local multi-process soak

> Sustained acceptance is paused until the [5,000-connection investigation](https://github.com/OpenArkStudio/arknet/issues/61) is resolved and passes. The dated results below used earlier protocol inputs; no sustained result is claimed for the current nested contract.

Use .NET 8 and a dedicated, empty guest-room server. Select external tools as in
the [run guide](run.md); the operator starts and stops the server.

```sh
DOTNET=/path/to/dotnet bash tools/soak-three-clients.sh 127.0.0.1 19100 600
```

Duration defaults to 600 seconds and accepts 30–3600. The runner builds serially;
append `--no-build` to use an already built, matching test DLL.

| Mode / output | Behavior |
| --- | --- |
| Residents | Alice and Bob retain one connection each; timing starts after all three clients join |
| Churn | Rejoins about every five seconds; finishes five seconds early so residents confirm removal |
| Activity | One move, action and ping about every 250 ms; actions cycle damage, skill, rest and capped rest |
| Assertions | Matching position/sequences, HP/MP and nonce within eight seconds; fresh reconnect identity/baseline; every churn addition/removal observed; no retry or replay |
| Supervision | First nonzero client exit stops/reaps the others; reaped PIDs are retired before further signals; remaining children are killed after a five-second termination grace |
| Evidence | Temporary logs, counts, exits, PIDs and approximately one-second CPU/RSS samples; `bash tools/test-soak-runner.sh` checks child failures without networking |

Local acceptance on **2026-11-03** used Apple M4 Pro, 14 cores, 48 GB reported
memory, macOS 27.0.1 arm64, SDK 8.0.427/runtime 8.0.33 and loopback port 19531.
The server used Debug/Apple clang 21.0.0; the client used Google.Protobuf 3.33.4.

| 600-second input | Identity |
| --- | --- |
| ARK server | `c9aa911a4893ffa2681925e8ebf13e703f8f4c16`; six candidate images byte-matched and loaded images checked |
| Client inputs | Build `9f4a8f91da260a562e34f5ff02e1a4139a7440d7`; execution `1e6181e160b663cffec3e032f75fc1dc778d4326`; C# inputs byte-identical |
| Test DLL SHA-256 | `18b273b56634903c52aca19b7ca3d7ff5baa4a65919a15998b8f2251f196fb5c`; `--no-build`, before/after hashes matched |

| Process | Active seconds | Connections | Fresh reconnects | Confirmed iterations |
| --- | ---: | ---: | ---: | ---: |
| Alice | 600.141 | 1 | 0 | 2374 |
| Bob | 600.137 | 1 | 0 | 2374 |
| Churn | 595.105 | 112 | 111 | 2348 |

Maximum joined clients and sampled established connections were both three.
All 7096 iterations confirmed a move, action and ping, with zero business/protocol
failures. Both residents observed all 112 churn additions/removals.

| Check | Result |
| --- | --- |
| 600-second activity | All three clients exited 0; cross-process verification passed; original wrapper exited 1 afterward on Bash PID-array cleanup |
| 30-second supervised runner | Port 19533: 337 confirmed iterations; wrapper, clients and count verification exited 0; this does not relabel the 600-second receipt |
| Server shutdowns | SIGINT exit 0 without force; listeners and owned processes exited |

| Process | Average CPU % | Peak sampled CPU % | RSS at 30–60 s, MiB | Final 30 s RSS, MiB | Peak sampled RSS, MiB |
| --- | ---: | ---: | ---: | ---: | ---: |
| Server | 10.13 | 15.6 | 42.03 | 42.23 | 42.23 |
| Alice | 11.87 | 25.8 | 59.59 | 62.22 | 62.45 |
| Bob | 12.01 | 25.8 | 59.48 | 62.13 | 62.38 |
| Churn | 11.40 | 25.3 | 60.59 | 64.17 | 64.20 |

Average CPU uses cumulative process CPU time / observed wall time; 100% is one
logical core. RSS columns are window means, with 1024 KiB per MiB. Other local
validation shared the host; this is not an isolated throughput/latency benchmark.

These portable C# checks do not validate Godot scenes, exported packages,
cross-host behavior, production PCU or absence of leaks. Complete manifests and raw receipts stay local; [other checks](validation.md).
