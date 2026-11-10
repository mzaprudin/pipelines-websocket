# Build and development

[English](development.md) · [Русский](development.ru.md)

Requires .NET 10 SDK and Python 3 for tooling. Package versions are pinned and packages.lock.json files are enforced by locked restore.

```sh
dotnet restore Pipelines.WebSockets.sln --locked-mode
dotnet build Pipelines.WebSockets.sln -c Release --no-restore
dotnet test tests/Pipelines.WebSockets.Tests -c Release --no-build
dotnet run -c Release --no-build --project samples/Pipelines.WebSockets.Demo
```

For clean runtime verification, make a fresh git clone and run the same commands. Changes to code, dependencies, build inputs or CI run the full Linux/Windows matrix: tests, sample, every benchmark with `--job dry`, streaming and the Windows package. Dry validates execution; published timing tables use full measured runs.

Windows CI also builds the self-contained generator and runs the unpacked executable. Build the portable package locally with:

```sh
python3 scripts/build-windows-generator.py
```

[LAN generator instructions](lan-benchmark.md) · [TCP controls](lan-controls.md)

The common streaming runner uses a separate local server by default. Add `--url ws://HOST:5050` to use an external server with the same profile. CI also runs `python -m unittest discover -s scripts -p test_streaming_benchmarks.py -v` for process lifecycle and report validation. See [methodology](benchmark-methodology.md).

Documentation, evidence and table-rendering edits use the lightweight check:

```sh
python3 scripts/verify-docs.py
```

It checks local links, Markdown formatting, Python syntax, generated tables and evidence integrity. The runtime matrix is skipped for these edits. Routing uses the full push/PR diff, includes deleted and renamed paths, and defaults to full CI for unknown inputs or unavailable history. See [AGENTS.md](../AGENTS.md).
