# Потоковый тест локально и через Ethernet

[English](lan-benchmark.md) · [Русский](lan-benchmark.ru.md)

Один приёмный исполняемый файл сравнивает два клиента с генератором в другом процессе. Без адреса раннер сам запускает сервер на localhost; с `--url` подключается к другой машине. Общий профиль: JSON 128 байт, 20 фреймов на запись, 10 соединений, 5 с прогрева, 30 с измерения, по три свежих процесса на клиент.

```sh
python3 scripts/streaming_benchmarks.py
python3 scripts/streaming_benchmarks.py --url ws://GENERATOR-IP:5050
```

CPU, GC и аллокации относятся только к приёмнику, включая периодический сбор метрик. У локального сервера другой PID; он делит ресурсы машины с приёмником. См. [методику и ограничения](benchmark-methodology.ru.md).

## Внешний генератор

На серверной машине, из текущих исходников:

```sh
dotnet run -c Release --project benchmarks/WebSocketLoadTest -- \
  --mode server --listen 0.0.0.0 --port 5050 --payload-bytes 128 --batch 20
```

Для Windows соберите переносимый x64-пакет командой `python scripts/build-windows-generator.py`, скопируйте `artifacts/lan-generator-win-x64/pipelines-websocket-generator-win-x64.zip` на серверную машину, распакуйте и запустите `START-GENERATOR.cmd`. Пакет содержит .NET runtime; launcher выбирает общий компактный профиль. Остановите внешний сервер через Ctrl+C после всех запусков. При запросе брандмауэра разрешите доступ в частной Ethernet-сети. В `--url` укажите Ethernet-адрес сервера вместо `0.0.0.0`.

ZIP содержит хеши исходников и бинарника, точную команду publish, профиль и SHA-256 исполняемого файла. Publish работает в отдельной копии исходников без lock-файлов с `RestorePackagesWithLockFile=false`; обычные lock-файлы разработки не меняются. Общий раннер проверяется в Linux и Windows CI; Windows также запускает распакованный переносимый файл.

## Контрольные профили

Размер сообщения и batch на внешнем сервере и приёмнике должны совпадать. Маршрут профиля проверяется при HTTP upgrade. Для исторического профиля 1 KiB:

```sh
# Серверная машина:
dotnet run -c Release --project benchmarks/WebSocketLoadTest -- \
  --mode server --listen 0.0.0.0 --port 5050 --payload-bytes 1024 --batch 1
# Приёмная машина:
python3 scripts/streaming_benchmarks.py --url ws://GENERATOR-IP:5050 \
  --payload-bytes 1024 --batch 1 --count 10 --warmup 10 --duration 60
```

`bash scripts/run-lan-benchmarks.sh ws://GENERATOR-IP:5050` вызывает тот же раннер и принимает его параметры профиля. Оба клиента обходят HTTP-прокси из окружения. Размер сообщения: 87 байт для исходной trade-фикстуры или от 128 байт до 1 MiB для depth JSON. `--batch` задаёт число полных фреймов на запись, а не границы TCP-пакетов.

[Переносимый релиз от 8 ноября](https://github.com/mzaprudin/zero-alloc-json-ws/releases/tag/lan-benchmark-1kib) относится к архиву: его launcher выбирает 1024 байта и один фрейм. В ранней версии баннер может показывать loopback при привязке ко всем IPv4-интерфейсам. Для воспроизведения архивных данных сохраняйте идентичность пакета; для общего компактного профиля соберите текущую версию.

[Контрольные сетевые эксперименты](lan-controls.md) описывают историческую матрицу фиксированного и автоматического TCP-буфера. Текущий транспорт по-прежнему запрашивает 64 KiB.
