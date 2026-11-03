# Bounded local multi-process soak

Start a dedicated, empty guest-room server, then run with .NET 8:

```sh
DOTNET=/path/to/dotnet bash tools/soak-three-clients.sh 127.0.0.1 19100 600
```

The duration defaults to 600 seconds and accepts 30–3600 seconds. The runner
builds the permanent C# test executable serially, then starts three independent
portable client processes. Alice and Bob retain their connections for the full
duration. Churn disconnects and rejoins about every five seconds, stopping five
seconds before the residents so both can confirm its final removal. A start
barrier requires all three clients to join before timed activity begins. The
server must be owned by the operator; the runner does not start or stop it.

For an already built, matching test DLL, append `--no-build`. The normal build
disables shared compilation and compiler parallelism as well as build servers.
Keep each worktree's `obj` and output directories independent when another local
build is active; `DOTNET_CLI_HOME` can select an isolated CLI state directory.

Each client requests one move, one action and one ping about every 250 ms. Every
iteration waits up to eight seconds for the matching movement sequence and
position, action sequence and HP/MP, and heartbeat nonce. Actions repeat damage,
skill, rest and capped rest. Reconnection checks fresh player and Entity IDs,
one full baseline and reset attributes. Residents also validate the protocol
replica throughout peer movement, actions, additions and removals. The final
cross-process check requires both residents to observe every churn connection
and removal. Any error fails the run; activity is not retried or replayed.

The printed temporary directory contains each process's log, JSON counts, exit
codes, PIDs and one-second `ps` samples of CPU percentage, RSS in KiB and
cumulative CPU time. CPU scope is one process; 100% represents one logical core.
These samples include process startup and do not measure whole-machine CPU or
precise allocation retention. Keep raw measurements outside version control.
Record server CPU/RSS separately, identify exact source and binary hashes, and
stop the owned server normally before confirming its listener has exited.

This is a bounded functional and resource-observation workload on three
portable C# clients. It does not execute Godot scenes or exported packages,
validate cross-host networking, explain the deferred TCP 5000 issue, or establish
production PCU capacity. See [validation](validation.md) for other checks.
