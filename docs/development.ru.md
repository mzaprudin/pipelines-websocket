# Сборка и разработка

[English](development.md) · [Русский](development.ru.md)

Требуются .NET 10 SDK и Python 3 для скриптов. Все пакеты имеют фиксированные версии; packages.lock.json проверяются в locked restore.

```sh
dotnet restore Pipelines.WebSockets.sln --locked-mode
dotnet build Pipelines.WebSockets.sln -c Release --no-restore
dotnet test tests/Pipelines.WebSockets.Tests -c Release --no-build
dotnet run -c Release --no-build --project samples/Pipelines.WebSockets.Demo
```

Для чистой проверки кода создайте новый git clone и выполните те же команды. При изменении кода, зависимостей, сборки или CI запускается полная матрица Linux/Windows: тесты, пример, все benchmark-сценарии с `--job dry`, streaming и Windows-пакет. Dry проверяет работоспособность; для таблиц используются реальные длительные замеры.

Windows CI также собирает self-contained генератор и запускает распакованный exe без установленного отдельно .NET runtime. Локальная сборка пакета:

```sh
python3 scripts/build-windows-generator.py
```

[LAN generator instructions](lan-benchmark.md) · [TCP controls](lan-controls.md)

Общий streaming-раннер по умолчанию запускает локальный сервер в отдельном процессе. Параметр `--url ws://HOST:5050` выбирает внешний сервер с тем же профилем. CI также выполняет `python -m unittest discover -s scripts -p test_streaming_benchmarks.py -v`: жизненный цикл процессов и валидация отчётов. См. [методику](benchmark-methodology.ru.md).

Для документации, результатов измерений и скриптов генерации таблиц используется короткая проверка:

```sh
python3 scripts/verify-docs.py
```

Она проверяет локальные ссылки, формат Markdown, синтаксис Python, соответствие таблиц данным и целостность результатов. Матрица выполнения для таких правок пропускается. Выбор проверок учитывает весь push/PR, удаления и прежние пути переименованных файлов; неизвестные файлы и недоступная история включают полный CI. См. [AGENTS.md](../AGENTS.md).
