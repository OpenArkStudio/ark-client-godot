#!/usr/bin/env bash
set -euo pipefail
repo=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
dotnet_cmd=${DOTNET:-dotnet}
host=${1:-127.0.0.1}
port=${2:-19100}
seconds=${3:-600}
mode=${4:-}
if [[ ! "$seconds" =~ ^[0-9]+$ ]] || (( seconds < 30 || seconds > 3600 )) || [[ -n "$mode" && "$mode" != --no-build ]]; then
  printf 'Usage: %s [host] [port] [seconds: 30..3600] [--no-build]\n' "$0" >&2
  exit 2
fi
logs=$(mktemp -d "${TMPDIR:-/tmp}/ark-godot-soak.XXXXXX")
printf 'Local soak logs: %s\n' "$logs"
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
(
  trap - EXIT INT TERM
  printf 'unix_seconds name pid cpu_percent rss_kib cpu_time\n'
  while true; do
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
[[ -f "$logs/ready-Alice" && -f "$logs/ready-Bob" && -f "$logs/ready-Churn" ]]
touch "$logs/begin"
started=yes
result=0
while [[ -n "${pids[0]}${pids[1]}${pids[2]}" ]]; do
  poll_clients
  sleep .1
done
for name in Alice Bob Churn; do
  tail -1 "$logs/$name.log"
  grep -q "^SOAK PASS $name " "$logs/$name.log" || result=1
done
if [[ "$result" == 0 ]]; then
  "$dotnet_cmd" "$dll" --soak-verify "$logs" | tee "$logs/verification.txt"
fi
exit "$result"
