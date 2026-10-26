# Детерминированная CCG-платформа

Репозиторий содержит платформу коллекционной карточной игры с детерминированным движком, Unity-клиентом и генерацией карт и ресурсов с помощью локальных провайдеров или OpenAI API.

ИИ помогает создавать контент, но не является источником истины для правил матча. Игровое состояние изменяется только движком, а replay воспроизводится из канонического журнала команд.

## Архитектура

- `Game.Domain` — детерминированные игровые правила и мутации.
- `Game.Application` — прикладные сценарии и orchestration.
- `Game.Infrastructure` — постоянные хранилища и внешние адаптеры.
- `Game.Api` — локальный авторитетный HTTP API.
- `clients/unity/CardGame.Unity` — Unity-клиент.
- `docs/engine/` и `docs/generator/` — архитектурные контракты.
- `openspec/specs/` — требования к поведению продукта.

`Game.Domain` не зависит от сети, базы данных, LLM, генерации ресурсов, системных часов или внешней случайности.

## Сборка

Требуется .NET SDK 10, версия закреплена в `global.json`.

```powershell
dotnet restore
dotnet build --configuration Release
```

## Запуск локального сервера

Unity-клиент ожидает API по адресу `http://127.0.0.1:5000`. Из корня репозитория запустите сервер с постоянным локальным хранилищем:

```powershell
dotnet run --project src/Game.Api --configuration Release --no-launch-profile --urls http://127.0.0.1:5000 --Storage:RootPath=artifacts/local-player-data
```

Генерация карт имеет два режима. **Локальный** режим использует Ollama и Stable Diffusion через локальные шлюзы; сначала установите модели по [инструкции](docs/user/local-generation.md). **OpenAI Connector** читает `OPENAI_API_KEY` из ближайшего `.env` (из корня репозитория при разработке или рядом с `PLAY.bat` в релизе) и использует Responses API и Images API для схем карт и иллюстраций.

Проверка в другом терминале:

```powershell
Invoke-RestMethod http://127.0.0.1:5000/health
```

Для готового Windows-релиза запустите `PLAY.bat` либо выполните из корня распакованного релиза:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
./server/Game.Api.exe --urls http://127.0.0.1:5000 --Storage:RootPath=./data
```

После этого запустите `./client/windows/CardGame.exe` во втором терминале. Подробности приведены в [русском Quick Start](docs/user/quick-start.ru.md).

## Тестирование

```powershell
dotnet test --configuration Release
bash scripts/ai/forbidden-api-scan.sh
```

Тесты проверяют детерминизм replay, каноническую сериализацию, hash игрового состояния, разделение RNG-потоков и обязательное использование mutation pipeline.

## Основные ограничения детерминизма

- Источник replay — канонический журнал команд.
- Event log является производным результатом.
- Игровые изменения проходят через `IStateMutator`.
- Игровой и presentation RNG разделены.
- Визуальные и звуковые данные не влияют на gameplay hash.
- Во время матча запрещены вызовы LLM и внешних генераторов.

## Документация

- [Quick Start на русском](docs/user/quick-start.ru.md)
- [Quick Start на английском](docs/user/quick-start.md)
- [Ручной предрелизный прогон](docs/user/manual-release-test.ru.md)
- [Основной README на английском](README.md)

## Лицензия

Проект распространяется под лицензией [Apache-2.0](LICENSE).
