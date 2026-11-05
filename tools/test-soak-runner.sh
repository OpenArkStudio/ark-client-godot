#!/usr/bin/env bash
set -euo pipefail
repo=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
base=$(mktemp -d "${TMPDIR:-/tmp}/ark-soak-runner-test.XXXXXX")
fixture="$base/repo"
mkdir -p "$fixture/tools" "$fixture/tests/Ark.Client.Core.Tests/bin/Debug/net8.0"
cp "$repo/tools/soak-three-clients.sh" "$fixture/tools/"
touch "$fixture/tests/Ark.Client.Core.Tests/bin/Debug/net8.0/Ark.Client.Core.Tests.dll"
runner_pid=
cleanup() {
  local status=$? active
  trap - EXIT
  if [[ -n "$runner_pid" ]]; then
    active=" $(jobs -pr) $(jobs -ps) "
    active=${active//$'\n'/ }
    if [[ "$active" == *" $runner_pid "* ]]; then kill -TERM "$runner_pid" 2>/dev/null || true; fi
    wait "$runner_pid" 2>/dev/null || true
    runner_pid=
  fi
  printf 'Local wrapper fault logs: %s\n' "$base"
  exit "$status"
}
trap cleanup EXIT
trap 'exit 130' INT TERM
cat > "$base/trace-env.sh" <<'ENV'
kill() {
  printf '%s %s\n' "$$" "$*" >> "$SOAK_FAULT_ROOT/kill-trace.txt"
  builtin kill "$@"
}
ENV
cat > "$base/fake-dotnet" <<'CLIENT'
#!/usr/bin/env bash
set -euo pipefail
[[ "${2:-}" == --soak-client ]] || exit 99
name=$5
logs=$8
touch "$logs/ready-$name"
until [[ -f "$logs/begin" ]]; do sleep .05; done
case "$name" in
  Alice) printf 'SOAK PASS Alice fixture\n'; exit 0 ;;
  Churn)
    sleep 1
    if [[ "$SOAK_FAULT_CASE" != interrupted ]]; then exit 23; fi
    printf 'SOAK PASS Churn fixture\n'
    exit 0 ;;
  Bob)
    if [[ "$SOAK_FAULT_CASE" == stubborn ]]; then trap '' TERM; else trap 'exit 143' TERM; fi
    mkfifo "$logs/bob-block"
    exec 3<> "$logs/bob-block"
    read -r -t 30 <&3 || true
    exit 0 ;;
esac
CLIENT
chmod +x "$base/fake-dotnet"
expect_argument_rejection() {
  local rc=0
  DOTNET="$base/fake-dotnet" /bin/bash "$fixture/tools/soak-three-clients.sh" "$@" > "$base/argument-rejection.log" 2>&1 || rc=$?
  [[ "$rc" == 2 ]]
}
expect_argument_rejection 127.0.0.1 19100 30
expect_argument_rejection 127.0.0.1 19100 29 "$base/invalid-duration"
mkdir "$base/preserved"
printf 'retained\n' > "$base/preserved/canary"
expect_argument_rejection 127.0.0.1 19100 30 "$base/preserved"
[[ "$(cat "$base/preserved/canary")" == retained ]]
git init -q "$base/guard-repo"
expect_argument_rejection 127.0.0.1 19100 30 "$base/guard-repo/output"
[[ ! -e "$base/guard-repo/output" && ! -e "$base/invalid-duration" ]]
printf 'PASS explicit_output_required=yes existing_evidence_preserved=yes git_output_rejected=yes\n'
for scenario in failure interrupted stubborn; do
  case_dir="$base/$scenario"
  mkdir "$case_dir"
  started=$(date +%s)
  SOAK_FAULT_CASE="$scenario" SOAK_FAULT_ROOT="$case_dir" BASH_ENV="$base/trace-env.sh" \
    TMPDIR="$case_dir" DOTNET="$base/fake-dotnet" /bin/bash "$fixture/tools/soak-three-clients.sh" \
    127.0.0.1 19100 30 "$case_dir/output" --no-build > "$case_dir/runner.log" 2>&1 &
  runner_pid=$!
  if [[ "$scenario" == interrupted ]]; then
    retired=no
    for attempt in {1..60}; do
      for logs in "$case_dir/output"; do
        if [[ -f "$logs/exit-codes.txt" ]] && grep -q '^client_0_exit=0$' "$logs/exit-codes.txt" && grep -q '^client_2_exit=0$' "$logs/exit-codes.txt"; then retired=yes; break; fi
      done
      if [[ "$retired" == yes ]]; then break; fi
      sleep .05
    done
    [[ "$retired" == yes ]]
    kill -TERM "$runner_pid"
  fi
  rc=0
  wait "$runner_pid" || rc=$?
  runner_pid=
  elapsed=$(( $(date +%s) - started ))
  expected=23
  if [[ "$scenario" == interrupted ]]; then expected=130; fi
  limit=5
  bob_exit=143
  if [[ "$scenario" == stubborn ]]; then limit=10; bob_exit=137; fi
  [[ "$rc" == "$expected" && "$elapsed" -lt "$limit" ]]
  for logs in "$case_dir/output"; do
    [[ $(wc -l < "$logs/exit-codes.txt") -eq 3 ]]
    grep -q "^client_1_exit=$bob_exit$" "$logs/exit-codes.txt"
    [[ -s "$case_dir/kill-trace.txt" ]]
    while read -r name pid; do
      if kill -0 "$pid" 2>/dev/null; then printf 'Surviving fixture child: %s %s\n' "$name" "$pid" >&2; exit 1; fi
      if [[ "$name" != Bob ]] && grep -Eq "(^| )$pid( |$)" "$case_dir/kill-trace.txt"; then printf 'Signalled retired child: %s %s\n' "$name" "$pid" >&2; exit 1; fi
    done < "$logs/processes.txt"
  done
  printf 'PASS %s exit=%s elapsed_seconds=%s retired_children_not_signalled=yes all_children_reaped=yes\n' "$scenario" "$rc" "$elapsed"
done
