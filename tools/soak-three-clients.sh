#!/usr/bin/env bash
set -euo pipefail
repo=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
dotnet_cmd=${DOTNET:-dotnet}
host=${1:-127.0.0.1}
port=${2:-19100}
seconds=${3:-600}
if [[ ! "$seconds" =~ ^[0-9]+$ ]] || (( seconds < 30 || seconds > 3600 )); then
  printf 'Usage: %s [host] [port] [seconds: 30..3600]\n' "$0" >&2
  exit 2
fi
logs=$(mktemp -d "${TMPDIR:-/tmp}/ark-godot-soak.XXXXXX")
printf 'Local soak logs: %s\n' "$logs"
"$dotnet_cmd" build "$repo/tests/Ark.Client.Core.Tests/Ark.Client.Core.Tests.csproj" --disable-build-servers -m:1
dll="$repo/tests/Ark.Client.Core.Tests/bin/Debug/net8.0/Ark.Client.Core.Tests.dll"
pids=()
sampler_pid=
cleanup() {
  if [[ -n "$sampler_pid" ]]; then kill "$sampler_pid" 2>/dev/null || true; wait "$sampler_pid" 2>/dev/null || true; fi
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; done
  for pid in "${pids[@]}"; do wait "$pid" 2>/dev/null || true; done
}
trap cleanup EXIT
trap 'exit 130' INT TERM
for name in Alice Bob Churn; do
  role=resident
  if [[ "$name" == Churn ]]; then role=churn; fi
  "$dotnet_cmd" "$dll" --soak-client "$host" "$port" "$name" "$role" "$seconds" "$logs" > "$logs/$name.log" 2>&1 &
  pids+=("$!")
  printf '%s %s\n' "$name" "$!" >> "$logs/processes.txt"
done
(
  printf 'unix_seconds name pid cpu_percent rss_kib cpu_time\n'
  while true; do
    active=no
    for index in 0 1 2; do
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
  for pid in "${pids[@]}"; do kill -0 "$pid" 2>/dev/null || { cat "$logs/"*.log; exit 1; }; done
  sleep .1
done
[[ -f "$logs/ready-Alice" && -f "$logs/ready-Bob" && -f "$logs/ready-Churn" ]]
touch "$logs/begin"
result=0
for index in 0 1 2; do
  rc=0
  wait "${pids[$index]}" || rc=$?
  printf 'client_%s_exit=%s\n' "$index" "$rc" >> "$logs/exit-codes.txt"
  if [[ "$rc" != 0 ]]; then result=1; fi
done
pids=()
for name in Alice Bob Churn; do
  tail -1 "$logs/$name.log"
  grep -q "^SOAK PASS $name " "$logs/$name.log" || result=1
done
if [[ "$result" == 0 ]]; then
  "$dotnet_cmd" "$dll" --soak-verify "$logs" | tee "$logs/verification.txt"
fi
exit "$result"
