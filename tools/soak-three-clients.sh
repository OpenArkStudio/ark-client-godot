#!/usr/bin/env bash
set -euo pipefail
repo=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
dotnet_cmd=${DOTNET:-dotnet}
host=${1:-127.0.0.1}
port=${2:-19100}
seconds=${3:-}
logs=${4:-}
mode=${5:-}
if (( $# < 4 || $# > 5 )) || [[ ! "$seconds" =~ ^[0-9]{1,4}$ ]] || [[ "$logs" != /* ]] || [[ -n "$mode" && "$mode" != --no-build ]]; then
  printf 'Usage: %s host port seconds:30..3600 absolute-local-output-directory [--no-build]\n' "$0" >&2
  exit 2
fi
seconds=$((10#$seconds))
if (( seconds < 30 || seconds > 3600 )); then printf 'Duration must be 30..3600 seconds.\n' >&2; exit 2; fi
parent=$logs
while [[ ! -d "$parent" && "$parent" != / ]]; do parent=$(dirname -- "$parent"); done
if git -C "$parent" rev-parse --show-toplevel >/dev/null 2>&1; then
  printf 'Choose a local output directory outside Git repositories.\n' >&2
  exit 2
fi
if [[ -e "$logs" ]]; then
  if [[ ! -d "$logs" ]] || ! entries=$(ls -A "$logs") || [[ -n "$entries" ]]; then
    printf 'Output directory must be absent or inspectably empty; existing evidence is never overwritten.\n' >&2
    exit 2
  fi
fi
mkdir -p -- "$logs"
printf 'Local soak logs: %s\n' "$logs"
git -C "$repo" rev-parse HEAD > "$logs/client-head.txt" 2>/dev/null || printf 'unavailable\n' > "$logs/client-head.txt"
git -C "$repo" status --porcelain > "$logs/client-status.txt" 2>/dev/null || printf 'unavailable\n' > "$logs/client-status.txt"
printf 'requested_seconds=%s\nprotocol_version=2\nworkload=nested\n' "$seconds" > "$logs/plan.txt"
dll="$repo/tests/Ark.Client.Core.Tests/bin/Debug/net8.0/Ark.Client.Core.Tests.dll"
if [[ "$mode" == --no-build ]]; then
  [[ -f "$dll" ]] || { printf 'Build the matching client test DLL first.\n' >&2; exit 1; }
  printf 'Using previously built client DLL: %s\n' "$dll"
else
  "$dotnet_cmd" build "$repo/tests/Ark.Client.Core.Tests/Ark.Client.Core.Tests.csproj" --disable-build-servers -m:1 -p:UseSharedCompilation=false -p:ConcurrentBuild=false
fi
pids=("" "" "")
sampler_pid=
started=no
active_jobs=
read_active_jobs() {
  active_jobs=" $(jobs -pr) $(jobs -ps) "
  active_jobs=${active_jobs//$'\n'/ }
}
retire_client() {
  local index=$1 pid=${pids[$1]} rc=0
  # Retire before wait so an interrupt cannot signal an already-reaped PID.
  pids[$index]=
  wait "$pid" || rc=$?
  touch "$logs/reaped-$index"
  printf 'client_%s_exit=%s\n' "$index" "$rc" >> "$logs/exit-codes.txt"
  return "$rc"
}
poll_clients() {
  local index pid rc
  read_active_jobs
  for index in 0 1 2; do
    pid=${pids[$index]}
    if [[ -z "$pid" || "$active_jobs" == *" $pid "* ]]; then continue; fi
    rc=0
    retire_client "$index" || rc=$?
    if [[ "$rc" != 0 || "$started" == no ]]; then
      printf 'Client %s exited before completion: %s\n' "$index" "$rc" >&2
      if [[ "$rc" == 0 ]]; then rc=1; fi
      return "$rc"
    fi
  done
}
cleanup() {
  local status=$? pid index attempt
  trap - EXIT
  trap '' INT TERM
  read_active_jobs
  if [[ -n "$sampler_pid" ]]; then
    pid=$sampler_pid
    sampler_pid=
    if [[ "$active_jobs" == *" $pid "* ]]; then kill "$pid" 2>/dev/null || true; fi
    wait "$pid" 2>/dev/null || true
  fi
  read_active_jobs
  for pid in "${pids[@]}"; do
    if [[ -n "$pid" && "$active_jobs" == *" $pid "* ]]; then kill "$pid" 2>/dev/null || true; fi
  done
  for attempt in {1..50}; do
    read_active_jobs
    for index in 0 1 2; do
      pid=${pids[$index]}
      if [[ -n "$pid" && "$active_jobs" != *" $pid "* ]]; then retire_client "$index" || true; fi
    done
    if [[ -z "${pids[0]}${pids[1]}${pids[2]}" ]]; then break; fi
    sleep .1
  done
  read_active_jobs
  for index in 0 1 2; do
    pid=${pids[$index]}
    if [[ -z "$pid" ]]; then continue; fi
    if [[ "$active_jobs" == *" $pid "* ]]; then kill -KILL "$pid" 2>/dev/null || true; fi
    retire_client "$index" || true
  done
  exit "$status"
}
trap cleanup EXIT
trap 'exit 130' INT TERM
index=0
for name in Alice Bob Churn; do
  role=resident
  if [[ "$name" == Churn ]]; then role=churn; fi
  "$dotnet_cmd" "$dll" --soak-client "$host" "$port" "$name" "$role" "$seconds" "$logs" > "$logs/$name.log" 2>&1 &
  pids[$index]=$!
  printf '%s %s\n' "$name" "$!" >> "$logs/processes.txt"
  index=$((index + 1))
done
max_wall_seconds=$((seconds + 60))
watchdog_deadline=$(( $(date +%s) + max_wall_seconds ))
(
  trap - EXIT INT TERM
  printf 'unix_seconds name pid cpu_percent rss_kib cpu_time\n'
  for ((sample=0; sample<=max_wall_seconds; sample++)); do
    active=no
    for index in 0 1 2; do
      if [[ -f "$logs/reaped-$index" ]]; then continue; fi
      pid=${pids[$index]}
      name=Alice
      if [[ "$index" == 1 ]]; then name=Bob; fi
      if [[ "$index" == 2 ]]; then name=Churn; fi
      if stats=$(ps -p "$pid" -o pid=,%cpu=,rss=,time=); then
        printf '%s %s %s\n' "$(date +%s)" "$name" "$stats"
        active=yes
      fi
    done
    if [[ "$active" == no ]]; then break; fi
    sleep 1
  done
) > "$logs/resources.txt" &
sampler_pid=$!
for attempt in {1..160}; do
  if [[ -f "$logs/ready-Alice" && -f "$logs/ready-Bob" && -f "$logs/ready-Churn" ]]; then break; fi
  poll_clients
  sleep .1
done
if [[ ! -f "$logs/ready-Alice" || ! -f "$logs/ready-Bob" || ! -f "$logs/ready-Churn" ]]; then
  printf 'All three clients must become ready within the finite startup window.\n' >&2
  exit 1
fi
touch "$logs/begin"
started=yes
result=0
for ((attempt=0; attempt<max_wall_seconds*10; attempt++)); do
  poll_clients
  if [[ -z "${pids[0]}${pids[1]}${pids[2]}" ]]; then break; fi
  if (( $(date +%s) >= watchdog_deadline )); then break; fi
  sleep .1
done
if [[ -n "${pids[0]}${pids[1]}${pids[2]}" ]]; then
  printf 'Finite workload watchdog expired after at most %s seconds.\n' "$max_wall_seconds" >&2
  exit 1
fi
for name in Alice Bob Churn; do
  tail -1 "$logs/$name.log"
  grep -q "^SOAK PASS $name " "$logs/$name.log" || result=1
done
if [[ "$result" == 0 ]]; then
  "$dotnet_cmd" "$dll" --soak-verify "$logs" | tee "$logs/verification.txt"
fi
exit "$result"
