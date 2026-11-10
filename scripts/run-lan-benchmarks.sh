#!/usr/bin/env bash
set -euo pipefail

if [[ $# != 1 ]]; then
    printf 'Usage: bash scripts/run-lan-benchmarks.sh ws://GENERATOR-IP:5050\n' >&2
    exit 2
fi
generator_url=$1
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$script_dir/.."
output_dir="results/lan-1kib/$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$output_dir"
dotnet build benchmarks/WebSocketLoadTest/WebSocketLoadTest.csproj -c Release

for run in 1 2 3; do
    if (( run % 2 == 1 )); then client_order=(standard pipeline); else client_order=(pipeline standard); fi
    for client in "${client_order[@]}"; do
        client_options=()
        if [[ $client == standard ]]; then client_options=(--std); fi
        printf 'LAN: %s, run %s/3, 1024-byte messages.\n' "$client" "$run"
        dotnet run -c Release --no-build --project benchmarks/WebSocketLoadTest -- \
            --mode client --url "$generator_url" --payload-bytes 1024 --batch 1 \
            --count 10 --duration 60 --warmup 10 "${client_options[@]}" \
            --json "$output_dir/$client-$run.json" > "$output_dir/$client-$run.log" 2>&1
    done
done
printf 'LAN reports saved to %s\n' "$output_dir"
