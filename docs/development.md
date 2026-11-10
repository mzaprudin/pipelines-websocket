# Build and development

[English](development.md) · [Русский](development.ru.md)

Requires .NET 10 SDK and Python 3 for tooling. Package versions are pinned and packages.lock.json files are enforced by locked restore.

```sh
dotnet restore Pipelines.WebSockets.sln --locked-mode
dotnet build Pipelines.WebSockets.sln -c Release --no-restore
dotnet test tests/Pipelines.WebSockets.Tests -c Release --no-build
dotnet run -c Release --no-build --project samples/Pipelines.WebSockets.Demo
```

For clean verification, make a fresh git clone and run the same commands. CI runs all tests, the sample and every benchmark scenario with `--job dry` on Linux and Windows. Dry validates execution; published timing tables use full measured runs.

Windows CI also builds the self-contained generator and runs the unpacked executable. Build the portable package locally with:

```sh
python3 scripts/build-windows-generator.py
```

[LAN generator instructions](lan-benchmark.md) · [TCP controls](lan-controls.md)
