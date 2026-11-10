#!/usr/bin/env bash
set -euo pipefail
if [[ $# -lt 1 ]]; then
    printf 'Usage: bash scripts/run-lan-benchmarks.sh ws://GENERATOR-IP:5050 [profile options]\n' >&2
    exit 2
fi
generator_url=$1
shift
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec python3 "$script_dir/streaming_benchmarks.py" --url "$generator_url" "$@"
