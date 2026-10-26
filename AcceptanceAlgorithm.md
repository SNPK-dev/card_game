# Полный алгоритм приемки проекта CardGame перед этапом разработки Unity-клиента

## 1. Цель документа

Настоящий документ определяет порядок приемки текущей итерации проекта CardGame.

Цель текущей итерации — подготовить полностью рабочую backend/platform-основу для последующей разработки production-клиента на Unity.

В рамках этой приемки проект должен доказать, что:

* игровой движок полностью исполняет заявленные игровые механики;
* генератор создает реальные карты через реальные локальные модели;
* сгенерированные карты можно валидировать, публиковать, добавлять в колоды и использовать в матчах;
* матчи полностью играются через API до терминального состояния;
* replay, audit, evidence и storage работают надежно;
* backend предоставляет стабильные контракты для будущего Unity-клиента;
* весь серверный контур готов к нагрузке, безопасности, восстановлению и production-like эксплуатации.

Важно: **разработка полноценного Unity-клиента, визуального интерфейса, клиентских анимаций и клиентского media playback не входит в текущую приемку**.

При этом текущая итерация обязана полностью подготовить backend, контракты, документацию и тестовые сценарии так, чтобы разработку Unity-клиента можно было начать без перепроектирования движка, API, генерации, матчей и replay.

---

## 2. Граница текущей приемки

## 2.1. Входит в приемку

В текущую приемку входят:

* backend API;
* игровой движок;
* runtime матчей;
* legal action system;
* command submission;
* match state snapshots;
* event stream / event log;
* replay;
* deterministic RNG trace;
* генерация карт;
* генерация ассетов;
* provider evidence;
* catalog;
* decks;
* matchmaking/session;
* bot runtime;
* persistent storage;
* security;
* observability;
* deployment;
* нагрузочные проверки;
* документация контрактов для будущего Unity-клиента.

## 2.2. Не входит в приемку

В текущую приемку не входят:

* production Unity-клиент;
* финальный игровой UI;
* drag-and-drop карт в клиенте;
* клиентские анимации;
* клиентский audio playback;
* mobile/desktop/WebGL билды;
* визуальная полировка;
* UX-флоу магазина, профиля, меню и коллекции;
* клиентский replay viewer;
* клиентский spectator UI.

Эти работы должны быть вынесены в отдельный **Client Production Track**.

## 2.3. Обязательное условие

Хотя Unity-клиент не входит в текущую приемку, backend обязан предоставить полный integration-ready контракт:

* API specification;
* схемы DTO;
* команды матча;
* legal actions;
* state snapshot;
* event stream;
* asset manifests;
* presentation timeline;
* playback plan;
* replay contract;
* reconnect protocol;
* auth/session protocol;
* error codes;
* sample API сценарии.

Если backend не позволяет корректно начать разработку Unity-клиента без архитектурных переделок, текущая итерация не принимается.

---

## 3. Базовый принцип приемки

Основной критерий готовности:

**Через API, без визуального клиента, должен проходить полный игровой цикл:**

1. Развернуть production-like окружение через контейнеры.
2. Поднять реальные локальные provider-сервисы:

   * structured card generation;
   * image generation;
   * animation generation;
   * audio generation;
   * moderation.
3. Через API сгенерировать карты со сложными эффектами.
4. Проверить карты валидаторами.
5. Опубликовать карты в catalog.
6. Сформировать валидные колоды.
7. Запустить матч.
8. Сыграть матч до terminal state через API.
9. Получить результат матча.
10. Получить command log, event log, replay, RNG trace, debug trace, audit/evidence.
11. Повторить replay и получить тот же final state hash.
12. Проверить три режима:

    * API-controlled human vs bot;
    * bot vs bot;
    * API-controlled human vs human.

Под `API-controlled human` понимается не реальный пользовательский интерфейс, а тестовый API-клиент, который имитирует действия человека через production API.

---

## 4. Термины

## 4.1. Backend/platform production-ready

Backend/platform production-ready означает, что серверная часть:

* воспроизводимо разворачивается;
* использует production-конфигурацию;
* не зависит от mock/fake/stub компонентов;
* имеет persistent storage;
* поддерживает реальные gameplay-сценарии;
* выдерживает заявленную нагрузку;
* корректно восстанавливается после сбоев;
* имеет auth, authorization, monitoring, logs, metrics и alerts;
* предоставляет стабильные контракты для будущего клиента.

## 4.2. Unity-ready backend

Unity-ready backend означает, что будущий Unity-клиент может:

* авторизоваться;
* получить список доступных матчей/колод/карт;
* создать матч;
* получить состояние матча;
* получить legal actions;
* отправить команду;
* получить события матча;
* восстановиться после reconnect;
* получить asset manifests;
* получить presentation timeline;
* получить replay data;
* корректно обработать ошибки.

## 4.3. Stub/mock/fake

Запрещенные элементы для production flow:

* mock provider;
* fake provider;
* deterministic test provider вместо реальной модели;
* in-memory storage для production data;
* hardcoded card/user/match values;
* default success response;
* ручная подмена результата;
* debug-only command path;
* test-only endpoint;
* undocumented fallback;
* отключенная валидация;
* отключенная модерация;
* незаписанный audit/evidence artifact.

---

# 5. Уровни приемки

| Уровень | Название                      | Цель                                |
| ------- | ----------------------------- | ----------------------------------- |
| L0      | Repository Gate               | Ветка, commit, структура проекта    |
| L1      | Build Gate                    | Сборка, тесты, CI                   |
| L2      | Infrastructure Gate           | Контейнеры, конфиги, storage        |
| L3      | Provider Gate                 | Реальные локальные модели           |
| L4      | Engine Gate                   | Полная готовность движка            |
| L5      | Generation Gate               | Полная генерация карт и ассетов     |
| L6      | Match Runtime Gate            | Полноценные матчи через API         |
| L7      | Replay & Evidence Gate        | Replay, audit, RNG trace, artifacts |
| L8      | Client Integration Gate       | Подготовка к Unity-клиенту          |
| L9      | Security & Observability Gate | Безопасность, логи, метрики         |
| L10     | Load & Recovery Gate          | Нагрузка, сбои, восстановление      |
| L11     | Final Acceptance Gate         | Финальное решение                   |

Нельзя перейти к следующему уровню, если предыдущий не пройден.

---

# 6. L0 — Repository Gate

## 6.1. Фиксация версии

Перед приемкой фиксируется:

* repository URL;
* branch name;
* commit SHA;
* дата проверки;
* список workflow runs;
* список build artifacts;
* список известных ограничений.

Критерий приемки:

* приемка проводится только по конкретному commit SHA;
* все изменения после начала приемки требуют повторной проверки;
* release candidate не содержит незакоммиченных изменений.

## 6.2. Проверка структуры

Обязательно проверить наличие модулей:

* Game.Api;
* Game.Application;
* Game.Domain;
* Game.Infrastructure;
* Game.Generator;
* Game.Shared;
* tests;
* scripts;
* docs;
* OpenAPI/API contracts;
* deployment configs;
* observability configs;
* security configs.

Критерий отказа:

* модуль заявлен в документации, но отсутствует в коде;
* модуль существует только как interface без implementation;
* production path использует test helper;
* production path использует in-memory fallback без явной dev-конфигурации.

## 6.3. Запрещенные элементы

Выполнить scan по паттернам:

* TODO;
* FIXME;
* HACK;
* STUB;
* MOCK;
* FAKE;
* DEMO;
* TEMP;
* BOOTSTRAP;
* NOT_IMPLEMENTED;
* Unsupported;
* NotImplementedException;
* return default;
* hardcoded secret;
* debug endpoint;
* test-only provider;
* in-memory production fallback.

Критерий отказа:

* найденный stub/mock/fake участвует в production flow;
* найден reachable NotImplementedException;
* production profile допускает silent fallback;
* unsupported mechanic может быть опубликована как playable card.

---

# 7. L1 — Build Gate

## 7.1. Clean build

Команды:

```bash
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
```

Критерий приемки:

* build successful;
* 0 release-blocking warnings;
* все тесты проходят;
* skipped tests отсутствуют или имеют approved waiver;
* flaky tests отсутствуют.

## 7.2. CI evidence

Должны быть подтверждены:

* CI run по release commit;
* restore passed;
* build passed;
* tests passed;
* forbidden API scan passed;
* doctor script passed;
* progress script passed;
* artifacts сохранены.

Критерий отказа:

* нет CI по release commit;
* локально проходит, но CI падает;
* тесты зависят от локального окружения разработчика.

## 7.3. Test classification

Тесты должны покрывать:

* unit tests;
* domain tests;
* application tests;
* integration tests;
* API contract tests;
* provider integration tests;
* E2E tests;
* replay determinism tests;
* storage restart tests;
* security tests;
* load tests;
* recovery tests.

Критерий отказа:

* нет E2E через API;
* provider tests используют только fake providers;
* нет replay drift tests;
* нет tests для command admission/legal actions;
* нет restart/recovery tests.

---

# 8. L2 — Infrastructure Gate

## 8.1. Контейнеризация

Должны быть production-like контейнеры для:

* API service;
* game runtime service, если выделен отдельно;
* generation orchestrator, если выделен отдельно;
* structured model provider/gateway;
* image model provider/gateway;
* animation model provider/gateway;
* audio model provider/gateway;
* moderation service;
* database;
* object storage;
* cache;
* message broker/event stream, если используется;
* observability stack или exporters.

Критерий приемки:

* весь стек поднимается одной командой;
* все containers healthy;
* readiness endpoints работают;
* secrets не лежат в образах;
* resource limits заданы;
* restart policies заданы;
* volumes явно описаны;
* production profile не включает mock/fake/in-memory flow.

## 8.2. Persistent storage

Проверить хранение:

* users;
* sessions;
* cards;
* card versions;
* catalog snapshots;
* decks;
* matches;
* game states;
* command logs;
* event logs;
* replay artifacts;
* RNG traces;
* debug traces;
* generated assets;
* generation audit logs;
* provider evidence;
* moderation results;
* admin actions.

Критерий приемки:

* данные переживают restart сервисов;
* данные переживают restart database;
* есть migrations;
* есть backup/restore;
* есть transaction/concurrency strategy;
* есть idempotency для команд;
* есть audit trail.

Критерий отказа:

* live match state хранится только in-memory;
* command log теряется после restart;
* event log теряется после restart;
* replay невозможен после restart;
* JSON file storage используется как основной production storage без concurrency guarantees.

---

# 9. L3 — Provider Gate

## 9.1. Реальные локальные модели

Для приемки должны быть подняты реальные provider-сервисы:

* structured card generation;
* image generation;
* animation generation;
* audio generation;
* moderation.

Запрещено принимать flow, если:

* provider заменен fake/deterministic/test provider;
* модель возвращает static fixture;
* image/audio/animation заменены placeholder;
* moderation всегда возвращает PASSED;
* pipeline работает только при отключенном audio.

## 9.2. Provider evidence

Для каждого вызова сохраняется:

* provider name;
* model id;
* model version;
* endpoint class;
* request id;
* sanitized prompt;
* style guide ref;
* safety policy ref;
* raw output ref;
* asset ref;
* content hash;
* moderation status;
* timeout;
* retry count;
* diagnostics;
* redacted config.

Критерий приемки:

* evidence создается для каждого вызова;
* hash проверяем;
* secrets скрыты;
* failed generation сохраняет diagnostics;
* audit chain связывает prompt → output → validation → publication.

## 9.3. Failure behavior

Проверить:

* timeout;
* malformed JSON;
* invalid card structure;
* invalid DSL;
* image generation failure;
* animation failure;
* audio failure;
* moderation reject;
* provider rate limit;
* corrupted artifact;
* storage unavailable.

Критерий приемки:

* невалидная карта не публикуется;
* partial publication невозможна;
* ошибка возвращается явно;
* audit сохраняется;
* повторный запуск безопасен.

---

# 10. L4 — Engine Gate

## 10.1. Базовые действия

Движок должен реально поддерживать:

* end turn;
* concede;
* draw card;
* play minion;
* play spell;
* play weapon;
* play location;
* play hero card;
* play hero power card;
* attack with minion;
* attack with weapon;
* use hero power;
* activate location;
* resolve choice.

## 10.2. Боевые правила

Проверить:

* summon sickness;
* attack once per turn;
* weapon durability;
* weapon destruction;
* minion damage;
* hero damage;
* lethal damage;
* simultaneous damage;
* taunt;
* stealth;
* board capacity;
* death cleanup;
* fatigue;
* terminal state.

## 10.3. Эффекты

Движок должен реально исполнять:

* battlecry/on play;
* targeted effects;
* random target effects;
* draw effects;
* heal;
* full heal;
* deal damage;
* destroy;
* batch destroy;
* discard;
* move to zone;
* return to hand;
* shuffle into deck;
* discover;
* resurrect;
* cost modifier;
* damage prevention;
* damage distribution;
* apply aura;
* replace event;
* take extra turn;
* board attack modifier;
* board health modifier;
* board keyword modifier;
* secret reveal;
* quest progress;
* quest reward;
* deathrattle;
* post-trigger target choice;
* multi-target choice.

Критерий отказа:

* эффект заявлен в DSL, но не исполняется;
* effect compiler отклоняет опубликованную карту;
* card text обещает поведение, которого нет в runtime;
* unsupported effect может попасть в playable catalog.

## 10.4. Жизненный цикл эффектов

Проверить source-lifecycle:

* источник появился;
* источник умер;
* источник ушел в руку;
* источник ушел в колоду;
* источник уничтожен;
* источник сменил контроллера;
* источник исчез из зоны действия;
* ход закончился;
* фаза закончилась;
* матч завершился.

Критерий приемки:

* ауры включаются и выключаются корректно;
* временные модификаторы истекают корректно;
* replacement effects не остаются висеть после исчезновения источника;
* replay воспроизводит тот же lifecycle.

---

# 11. L5 — Generation Gate

## 11.1. Типы карт

Через реальные модели сгенерировать минимум:

* minion;
* spell;
* weapon;
* location;
* hero card;
* hero power card;
* token;
* secret;
* quest;
* aura card;
* deathrattle card;
* discover card;
* resurrect card;
* random target card;
* damage prevention card;
* damage distribution card;
* replace event card;
* extra turn card.

## 11.2. Проверки каждой карты

Для каждой карты проверить:

* structured output valid;
* DSL valid;
* capability registry compatible;
* ruleset compatible;
* balance report generated;
* simulation report generated;
* replay report generated;
* assets generated;
* moderation passed;
* audit completed;
* publication completed;
* catalog snapshot updated;
* card can be added to deck;
* card can be used in match;
* behavior matches card text.

Критерий отказа:

* карта публикуется без validation;
* карта требует unsupported capability;
* карта не проходит simulation/replay;
* asset отсутствует без явной policy;
* audit/evidence incomplete.

## 11.3. Связь генератора с движком

Генератор не имеет права публиковать карту, если:

* DSL token неизвестен движку;
* action не поддержан compiler;
* effect kind не поддержан runtime;
* trigger/filter не поддержан;
* lifecycle правила не определены;
* replay не может воспроизвести эффект.

Критерий приемки:

* generator capability gates синхронизированы с engine capability registry;
* unsupported mechanics rejected before publication;
* publication невозможна без replay evidence.

---

# 12. L6 — Match Runtime Gate

## 12.1. API-controlled Human vs Bot

Сценарий:

1. API-клиент авторизуется.
2. Создает/выбирает deck.
3. Запускает матч против бота.
4. Получает legal actions.
5. Отправляет команды через API.
6. Bot отвечает legal commands.
7. Сложные generated cards реально играются.
8. Матч доходит до terminal state.
9. Result persisted.
10. Replay verified.

Критерий отказа:

* bot отправляет illegal command;
* API-клиент может отправить illegal command;
* матч зависает;
* replay не сходится.

## 12.2. Bot vs Bot

Запустить минимум 100 bot-vs-bot матчей.

Критерий приемки:

* 100/100 матчей terminal;
* 100/100 replay verified;
* 0 illegal commands;
* 0 stuck matches;
* все artifacts persisted.

## 12.3. API-controlled Human vs Human

Сценарий:

1. Игрок A создает матч.
2. Игрок B присоединяется.
3. Оба выбирают валидные колоды.
4. Сервер назначает player slots.
5. Игроки получают только разрешенные данные.
6. Игроки отправляют команды через API.
7. Сервер валидирует turn ownership.
8. Duplicate command не применяется дважды.
9. Reconnect восстанавливает state.
10. Матч завершается.
11. Replay verified.

Критерий отказа:

* игрок может ходить не в свой ход;
* игрок может действовать за другого;
* spectator может отправлять gameplay command;
* hidden information раскрывается не тому игроку;
* duplicate command applied twice.

---

# 13. L7 — Replay & Evidence Gate

## 13.1. Replay

Проверить:

* command log persistence;
* event log persistence;
* derived events regeneration;
* pre-state hash validation;
* final state hash validation;
* RNG trace validation;
* debug trace validation;
* replay after restart;
* replay after deployment;
* replay after catalog update.

Критерий отказа:

* replay дает другой final hash;
* replay проходит при tampered command log;
* replay проходит при tampered RNG trace;
* replay зависит от mutable catalog state.

## 13.2. Evidence artifacts

Сохранять:

* generation evidence;
* provider evidence;
* moderation evidence;
* publication evidence;
* match evidence;
* replay evidence;
* RNG trace;
* debug trace;
* asset evidence;
* acceptance report.

Критерий приемки:

* artifact имеет stable id;
* artifact имеет content hash;
* artifact можно получить через API;
* artifact переживает restart;
* artifact не содержит secrets.

---

# 14. L8 — Client Integration Gate

Этот уровень заменяет требование готового Unity-клиента.

## 14.1. API contract для Unity

Должны быть подготовлены:

* OpenAPI specification;
* DTO schemas;
* enum/token reference;
* command payload schemas;
* legal action response schema;
* match state snapshot schema;
* event stream schema;
* replay schema;
* asset manifest schema;
* presentation timeline schema;
* playback plan schema;
* error code catalog.

Критерий отказа:

* Unity-разработчик должен читать backend-код, чтобы понять API;
* схемы не соответствуют реальным ответам API;
* есть undocumented response fields;
* есть unstable magic strings без enum/reference.

## 14.2. Match integration protocol

Документация должна описывать:

* как авторизоваться;
* как создать матч;
* как подключиться к матчу;
* как получить initial state;
* как получить legal actions;
* как отправить command;
* как получить events;
* как обработать choice/discover;
* как обработать target selection;
* как обработать reconnect;
* как обработать terminal state;
* как получить replay.

Критерий отказа:

* нет пошагового протокола для клиента;
* reconnect не описан;
* command idempotency не описана;
* event ordering не описан;
* hidden information policy не описана.

## 14.3. Presentation preparation

Backend должен подготовить, но не реализовывать на клиенте:

* asset manifests;
* visual asset refs;
* audio asset refs;
* animation asset refs;
* presentation timeline;
* playback plan;
* event-to-cue mapping;
* fallback policy для missing assets;
* content hash;
* moderation status;
* asset permissions.

Критерий приемки:

* будущий Unity-клиент может понять, какие ассеты загрузить;
* future client может понять порядок событий;
* future client может понять, какой звук/анимацию связать с событием;
* playback plan соответствует event log.

Критерий отказа:

* backend не отдает достаточно данных для визуализации матча;
* asset refs не стабильны;
* нет связи между gameplay event и presentation cue;
* playback plan не воспроизводим.

## 14.4. Reference headless client

Должен быть реализован небольшой reference client без UI:

* CLI или integration-test client;
* умеет login;
* start match;
* get legal actions;
* submit command;
* listen/poll events;
* reconnect;
* finish match;
* verify replay.

Назначение: доказать, что будущий Unity-клиент сможет использовать API без скрытых договоренностей.

Критерий отказа:

* сценарий возможен только через внутренние сервисы, а не через публичный API;
* reference client требует ручных обходов;
* gameplay невозможен без прямого доступа к backend internals.

---

# 15. L9 — Security & Observability Gate

## 15.1. Security

Проверить:

* auth required для non-public endpoints;
* ownership checks;
* role checks;
* admin permissions;
* spectator permissions;
* bot/system permissions;
* token expiry;
* refresh/logout;
* command authorization;
* hidden information protection;
* rate limiting;
* request size limits;
* malformed JSON handling;
* replay tampering protection;
* artifact path traversal protection;
* provider endpoint SSRF protection;
* secrets redaction.

Критерий отказа:

* пользователь может управлять чужим матчем;
* spectator может submit command;
* hidden information leaked;
* API key попадает в logs/evidence;
* replay accepts tampered data.

## 15.2. Observability

Обязательные logs:

* request received;
* auth result;
* command accepted/rejected;
* mutation batch applied;
* engine fault;
* match started/completed;
* provider request started/completed;
* provider failure;
* artifact stored;
* replay verified/rejected;
* security violation.

Обязательные metrics:

* active matches;
* commands per second;
* command latency;
* legal action latency;
* event stream lag;
* replay success/failure;
* provider latency;
* provider failure rate;
* generation success/failure;
* DB latency;
* queue depth;
* error rate;
* p95/p99 latency;
* memory/CPU/disk/network.

Обязательные alerts:

* API error spike;
* command rejection spike;
* replay mismatch;
* engine fault spike;
* provider outage;
* generation failure spike;
* database latency high;
* queue backlog;
* stuck matches;
* storage write failure;
* suspicious command tampering.

---

# 16. L10 — Load & Recovery Gate

## 16.1. Load profile

Минимальный профиль для backend/platform:

* 100 000 registered users;
* 25 000 daily active users;
* 10 000 concurrent connected API clients;
* 5 000 concurrent active matches;
* 1 000 concurrent spectators/API observers;
* 500 card generation jobs/hour;
* 50 concurrent generation jobs;
* 2 000 gameplay commands/second peak;
* 5 000 event messages/second peak.

Это проверяет серверную готовность. Производительность будущего Unity-клиента проверяется отдельно в Client Production Track.

## 16.2. SLA

| Операция             |       p95 |       p99 |
| -------------------- | --------: | --------: |
| Get legal actions    |  ≤ 150 ms |  ≤ 300 ms |
| Submit command       |  ≤ 250 ms |  ≤ 500 ms |
| Match state snapshot |  ≤ 150 ms |  ≤ 300 ms |
| Event delivery       |  ≤ 300 ms |  ≤ 750 ms |
| Start match          | ≤ 1000 ms | ≤ 2000 ms |
| Save deck            |  ≤ 300 ms |  ≤ 700 ms |
| Replay medium match  | ≤ 3000 ms | ≤ 7000 ms |

## 16.3. Recovery tests

Проверить:

* API restart;
* runtime restart;
* database restart;
* cache loss;
* object storage temporary failure;
* provider timeout;
* provider malformed response;
* network partition;
* disk full simulation.

Критерий приемки:

* committed commands не теряются;
* duplicate commands не применяются дважды;
* active match восстанавливается;
* replay verified после восстановления;
* alerts triggered.

---

# 17. L11 — Final Acceptance Gate

## 17.1. Финальные артефакты

Перед приемкой должны быть переданы:

* commit SHA;
* build artifacts;
* container image digests;
* CI run links;
* test report;
* E2E report;
* load test report;
* security scan report;
* migration report;
* backup/restore report;
* provider evidence report;
* generated cards evidence;
* match evidence artifacts;
* replay verification artifacts;
* API contract;
* DTO schemas;
* event stream contract;
* replay contract;
* asset manifest contract;
* presentation timeline contract;
* playback plan contract;
* Unity integration guide;
* architecture diagram;
* deployment guide;
* operations guide;
* rollback plan;
* monitoring dashboards;
* alert rules;
* known limitations list.

## 17.2. Автоматический отказ

Текущая итерация не принимается, если:

* backend не позволяет сыграть матч через API;
* нет Human vs Bot через API;
* нет Bot vs Bot;
* нет Human vs Human через API;
* generated cards нельзя использовать в матче;
* сложные эффекты не исполняются;
* generator публикует unsupported cards;
* provider flow использует mock/fake/deterministic provider;
* live match state теряется после restart;
* command log/event log не сохраняются;
* replay не deterministic;
* production storage in-memory;
* нет auth/authorization;
* пользователь может управлять чужим матчем;
* spectator может отправлять команды;
* нет provider evidence;
* нет generation/publication audit;
* нет load test;
* нет security gate;
* нет observability;
* нет API/DTO/event/replay документации для Unity;
* Unity-разработка требует перепроектирования backend API.

---

# 18. Минимальный E2E-набор

## E2E-001: Production-like deployment

Ожидание:

* весь backend/model/storage stack поднимается из чистого окружения;
* все services healthy;
* migrations applied;
* providers ready;
* no mock/fake/in-memory production path.

## E2E-002: Generate complex cards

Ожидание:

* сгенерированы карты всех основных типов;
* сложные эффекты валидны;
* assets созданы;
* moderation passed;
* cards published.

## E2E-003: Build decks from generated cards

Ожидание:

* decks valid;
* deck readiness passed;
* generated cards included;
* persistence verified.

## E2E-004: API Human vs Bot match

Ожидание:

* матч завершен;
* generated cards сыграны;
* bot legal;
* replay verified.

## E2E-005: Bot vs Bot 100 matches

Ожидание:

* 100/100 terminal;
* 100/100 replay verified;
* 0 illegal commands;
* 0 stuck matches.

## E2E-006: API Human vs Human match

Ожидание:

* permissions correct;
* hidden info isolated;
* reconnect works;
* replay verified.

## E2E-007: Restart during match

Ожидание:

* active match restored;
* no command loss;
* no duplicate mutations;
* replay verified.

## E2E-008: Provider failure

Ожидание:

* generation blocked/failed safely;
* no partial publication;
* diagnostics saved;
* retry/idempotency correct.

## E2E-009: Security abuse

Ожидание:

* чужие команды rejected;
* spectator command rejected;
* tampered replay rejected;
* oversized/malformed payload rejected.

## E2E-010: Unity integration contract dry-run

Ожидание:

* reference headless client проходит полный match flow;
* все API схемы совпадают с реальными ответами;
* event stream ordering documented and verified;
* reconnect documented and verified;
* asset/presentation contracts available.

---

# 19. Финальная формула приемки

Текущая итерация принимается только если выполняется формула:

**Backend Platform Acceptance = Full Engine + Real Generation + API Gameplay + Durable Persistence + Secure Commands + Deterministic Replay + Complete Evidence + Unity-ready Contracts**

Unity-клиент, визуальный интерфейс и клиентское media playback принимаются отдельно в следующей итерации.

---

# 20. Executive checklist

Перед закрытием текущей итерации ответственный должен ответить “да” на все вопросы:

1. Можно ли поднять весь backend/model stack одной командой?
2. Используются ли реальные модели, а не fake providers?
3. Можно ли сгенерировать сложные карты через API?
4. Проходят ли карты validation/simulation/replay?
5. Можно ли опубликовать карты в catalog?
6. Можно ли собрать валидную колоду?
7. Можно ли сыграть Human vs Bot через API?
8. Можно ли сыграть Bot vs Bot?
9. Можно ли сыграть Human vs Human через API?
10. Проверяются ли legal actions server-side?
11. Защищены ли команды от чужого пользователя?
12. Есть ли persistent match state?
13. Есть ли command log и event log?
14. Есть ли deterministic replay?
15. Есть ли RNG trace и debug trace?
16. Есть ли provider evidence?
17. Есть ли generation/publication audit?
18. Есть ли persistent storage?
19. Есть ли auth/authorization?
20. Есть ли monitoring/logs/alerts?
21. Пройден ли load test?
22. Пройден ли recovery test?
23. Пройден ли security gate?
24. Есть ли OpenAPI/DTO/event/replay contracts?
25. Есть ли Unity integration guide?
26. Есть ли reference headless client?
27. Можно ли начать Unity-клиент без перепроектирования backend?

Если хотя бы один ответ “нет” — текущая итерация не принимается.
