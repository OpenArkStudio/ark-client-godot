# Bounded local multi-process soak

> **PENDING:** sustained acceptance of the current nested Entity protocol must wait until the [5,000-connection investigation](https://github.com/OpenArkStudio/arknet/issues/61) is fixed and passes. The tool preparation below is not a sustained-run receipt.

Use .NET 8 and a dedicated empty guest-room server built with the matching current
[protocol](protocol.md). The operator starts and stops that server. After the
connection issue is resolved and the run is explicitly scheduled, select external
tools as in the [run guide](run.md):

```sh
DOTNET=/path/to/dotnet bash tools/soak-three-clients.sh \
  127.0.0.1 19100 600 /absolute/local/soak-run
```

Duration is required and accepts 30–3600 seconds. The output directory is also
required: an absolute, absent or empty local directory outside Git repositories.
Existing evidence is never overwritten, and raw logs/metrics are not publication
assets. The runner builds serially; append `--no-build` only for an already built,
matching test DLL. Files record source HEAD/status, both client assembly SHA-256
values, workload counts, process exits/PIDs and one-second CPU/RSS samples. Record
server revision, binaries, settings and shutdown evidence separately.

| Mode / bound | Behavior |
| --- | --- |
| Residents | Alice and Bob keep one connection each; timing starts after all three clients join |
| Complete cycle | 12 moves/actions/pings targeting 250 ms per step: damage, skill, rest, capped rest, Drink twice, pickup twice, switch, strengthen, switch back, strengthen |
| Both views | Every committed envelope is observed; each resident waits for the other resident's validated move/action phase before advancing |
| Nested assertions | Quantity patches, last-row removal, row reuse with a greater incarnation; weapon replacement, child power changes with stable GUID/incarnation, returning weapon's retained power |
| Churn | Reconnect after two complete cycles, about six seconds plus reconnect time; reserve pause and settling time, verify fresh root/equipment identities and initial state; no retry after failure |
| Duration plan | Residents round up to a complete three-second cycle, at most two planned seconds beyond the request; churn reserves five seconds and its 250 ms reconnect pauses |
| Failure bounds | Eight-second activity/coordination deadline, 15-second workload grace after planned duration, finite wrapper watchdog at requested seconds + 60 |
| Supervision | First nonzero client exit stops/reaps the others; retired PIDs are not signalled; five-second termination grace before killing only remaining owned children |

Each full cycle confirms a move, action and matching pong at every step.
Cross-process verification checks completed cycles, exact action/row/container
operation totals, child patch counts (allowing capped no-ops), both-view
progress and every churn addition/removal. The server and other processes are
never stopped by this wrapper. No workload runs indefinitely or retries until green.

Prepare without contacting a server:

```sh
/path/to/dotnet build tests/Ark.Client.Core.Tests/Ark.Client.Core.Tests.csproj --disable-build-servers -m:1
/path/to/dotnet tests/Ark.Client.Core.Tests/bin/Debug/net8.0/Ark.Client.Core.Tests.dll --soak-workload
TMPDIR=/absolute/local/test-temp bash tools/test-soak-runner.sh
```

The permanent C# check covers finite scheduling and valid/invalid nested state
transitions, including capped power and lifetime regressions. The shell fixture
checks failed, interrupted and stubborn children without networking. These checks
compile/verify the tool; they do not validate a sustained server run, Godot scene
or exported package.

## Earlier protocol results

The dated measurements below used earlier protocol inputs and are historical.
They do not establish sustained acceptance of the current nested contract.

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
