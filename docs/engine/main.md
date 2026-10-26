# Deterministic Event-Driven CCG Engine
## main.md — Engine Architecture Contract
### Draft 4 — Audio Asset Support

---

## 0. Статус документа

`main.md` — главный architecture contract игрового движка.

Документ фиксирует:

- продуктовую позицию движка;
- runtime-модель матча;
- совместимость с генератором карт;
- TDD-first подход;
- SOLID/OOP/Clean Architecture требования;
- детерминизм и replay;
- границы MVP;
- обязательные дочерние спецификации.

Документ не является полным описанием всех enum, DTO, validators, action executors и card mechanics.

Детальная runtime-семантика вынесена в дочерние файлы:

```text
event_system_spec.md
state_mutation_spec.md
canonical_serialization_spec.md
phase_timing_spec.md
action_executor_spec.md
replay_spec.md
state_hash_spec.md
engine_capability_registry_spec.md
view_projection_spec.md
asset_generation_spec.md
presentation_cue_spec.md
```

---

## 1. Product Position

Мы строим не клон Hearthstone.

Мы строим собственный deterministic event-driven CCG engine:

```text
Hearthstone-class by architecture,
controlled by product ruleset,
strict by DSL,
deterministic by execution,
test-first by development.
```

Движок должен уметь архитектурно выражать сложные CCG-механики, но production ruleset обязан разрешать только проверенную, стабильную и балансируемую часть механик.

---

## 2. Главная цепочка исполнения

Базовая runtime-цепочка:

```text
Published CardVersion
↓
CatalogSnapshot
↓
RulesetProfile
↓
EngineCapabilityRegistry
↓
DeckValidation
↓
MatchStart
↓
CanonicalCommandPayload
↓
CommandValidation
↓
Action / Resolver Execution
↓
ProposedEvent / PreMutationEvent, если нужно
↓
Replacement / Prevention Resolver
↓
ProposedMutationBatch
↓
StateMutator
↓
AppliedMutationBatch
↓
CommittedEvent / DerivedGameEvent
↓
EventQueue
↓
Trigger / Condition / Target / Action
↓
History
↓
StateHash
↓
Command Replay
```

Ключевой принцип:

```text
Игрок не меняет GameState напрямую.
UI не меняет GameState напрямую.
Генератор не меняет GameState напрямую.
ActionExecutor не мутирует GameState напрямую.

Все изменения проходят через ProposedMutationBatch → StateMutator → AppliedMutationBatch.
```

---

## 3. Command Replay Primary

Проект использует `command replay primary`.

Это обязательное архитектурное решение.

```text
CanonicalCommandLog — источник истины для replay.
DerivedEventLog — deterministic output исполнения.
DerivedEventLog не является source of truth.
State восстанавливается повторным исполнением canonical commands.
```

Replay-инвариант:

```text
Same EngineVersion
+ Same EngineCapabilityRegistry
+ Same PrimitiveRegistryVersion
+ Same RulesetProfile / resolvedRulesetProfileHash
+ Same CatalogSnapshot
+ Same Seed
+ Same CanonicalCommandLog
= Same DerivedEventLog
= Same FinalStateHash
```

Запрещено строить hybrid event sourcing, где одновременно и command log, и event log являются primary replay source.

---

## 4. Event model: Proposed vs Committed

В движке есть две категории gameplay-событий:

```text
ProposedEvent / PreMutationEvent
  Событие-намерение до применения mutation.
  Нужно для replacement/prevention effects.

CommittedEvent / DerivedGameEvent
  Событие-факт после применения mutation.
  Нужно для after-triggers, replay timeline, analytics, UI.
```

Примеры:

```text
DAMAGE_WOULD_BE_DEALT     → ProposedEvent
ENTITY_DAMAGED            → CommittedEvent

CARD_WOULD_BE_DRAWN       → ProposedEvent
CARD_DRAWN                → CommittedEvent

ENTITY_WOULD_DIE          → ProposedEvent
ENTITY_DIED               → CommittedEvent

CARD_WOULD_MOVE_ZONE      → ProposedEvent
CARD_MOVED_ZONE           → CommittedEvent
```

Подробно: `event_system_spec.md`.

---

## 5. StateMutation model

Executor не применяет изменения напрямую.

Правильная модель:

```text
ActionExecutor / Resolver
↓
ProposedMutationBatch
↓
Replacement / Prevention processing, если применимо
↓
StateMutator.Apply()
↓
AppliedMutationBatch
↓
EventFactory
↓
CommittedEvents
```

Важно:

```text
ProposedMutationBatch не содержит postStateHash.
AppliedMutationBatch содержит postStateHash.
```

`postStateHash` может быть рассчитан только после применения batch.

Подробно: `state_mutation_spec.md`.

---

## 6. Single-threaded match execution

Один матч исполняется в одном deterministic single-threaded loop.

```text
One match = one deterministic execution loop.
No parallel event resolution inside one match.
No parallel trigger resolution inside one match.
No parallel action execution inside one match.
No parallel death processing inside one match.
No parallel aura refresh inside one match.
```

Разрешено параллелить:

```text
- разные матчи;
- разные replay jobs;
- разные simulation jobs;
- разные batch balance simulations.
```

Запрещено параллелить внутри одного матча всё, что влияет на ordering или RNG.

---

## 7. Core Engine Stability Boundary

Следующие контракты являются core stability boundary и требуют major version review при изменении:

```text
- Entity identity model
- Zone model
- CardDefinition / CardInstance / EntityInstance separation
- Command payload canonical format
- Command sequencing rules
- ProposedEvent / CommittedEvent semantics
- ProposedMutationBatch / AppliedMutationBatch semantics
- RNG stream policy
- State hash algorithm
- Canonical serialization format
- CatalogSnapshot semantics
- RulesetProfile pinning
- ActionExecutor interface
- StateMutationHandler interface
- Phase ordering contract
- Trigger ordering contract
- Death processing ordering
- Aura refresh ordering
```

Изменение этих контрактов может сломать:

```text
- old replay;
- old CatalogSnapshot;
- old CardVersion;
- balance reports;
- simulation baselines;
- generated card compatibility.
```

---

## 8. Technology Stack

Рекомендуемый MVP stack:

```text
Language: C#
Runtime: .NET
Architecture: Clean Architecture + DDD + Hexagonal Architecture
Tests: xUnit / NUnit
Property tests: FsCheck or equivalent
Serialization: System.Text.Json source generation
Schema: JSON Schema generated from shared contracts
Storage: PostgreSQL
Cache / Queue: Redis
Observability: OpenTelemetry
API: ASP.NET Core
CI/CD: GitHub Actions
```

Почему C#:

```text
- строгая статическая типизация;
- хорошая ООП-модель;
- records/immutability patterns;
- зрелая testing ecosystem;
- удобная сериализация;
- достаточная производительность для card engine;
- высокая скорость разработки;
- хорошая поддержка Clean Architecture.
```

---

## 9. Architecture Layers

```text
Game.Api
  REST/gRPC endpoints
  Auth
  Request validation
  DTO mapping
  View projections

Game.Application
  Use cases
  Match orchestration
  Command submission
  Replay orchestration
  Simulation orchestration
  Catalog loading
  Capability checks

Game.Domain
  GameState
  Commands
  Events
  StateMutations
  Rules
  Resolvers
  Executors
  Phase transitions
  Deterministic RNG
  State hashing contracts

Game.Infrastructure
  Database
  Catalog storage
  Log storage
  OpenTelemetry
  File/object storage
  Message queue
```

Dependency direction:

```text
Infrastructure → Application → Domain
Api            → Application → Domain
Generator      → Shared Contracts
Engine         → Shared Contracts
```

Domain не зависит от:

```text
- API;
- database;
- file system;
- OpenAI;
- HTTP;
- UI;
- background jobs;
- logging frameworks напрямую.
```

---

## 10. Project Structure

```text
/src
  /Game.Shared
    /Primitives
    /Dsl
    /Ruleset
    /Mechanics
    /Schema
    /Serialization

  /Game.Domain
    /State
    /Commands
    /Events
    /Mutations
    /Phases
    /Resolvers
    /Executors
    /Modifiers
    /History
    /Replay
    /Hashing

  /Game.Application
    /UseCases
    /Ports
    /Services
    /Simulation
    /Replay
    /Capabilities
    /ViewProjections

  /Game.Infrastructure
    /Persistence
    /Catalog
    /Logging
    /Telemetry
    /Serialization

  /Game.Api
    /Controllers
    /Contracts
    /Mapping

/tests
  /Game.Shared.Tests
  /Game.Domain.Tests
  /Game.Application.Tests
  /Game.Integration.Tests
  /Game.Property.Tests
  /Game.Golden.Tests
  /Game.Replay.Tests
  /Game.Simulation.Tests
```

---

## 11. Shared Kernel / Shared Primitives

`Game.Shared` — общий пакет между генератором, валидаторами, симулятором и движком.

Shared содержит только контракты:

```text
CardType
EntityType
Zone
SourceZone
Phase
EventType
ActionType
EffectKind
TriggerType
ConditionKind
TargetSelectionMode
ResolutionMode
FizzleBehavior
Keyword
MechanicId
MechanicStatus
RulesetProfile
CardDefinition DTO
EffectDefinition DTO
ActionDefinition DTO
ModifierDefinition DTO
PoolDefinition DTO
ValidationResult DTO
BalanceReport DTO
CatalogSnapshotRef
EngineCapabilityRef
AssetManifestRef
VisualAssetManifest DTO
AudioAssetManifest DTO
PresentationCue DTO
```

Shared не содержит:

```text
- OpenAI gateway;
- prompt templates;
- database entities;
- EF Core models;
- API controllers;
- UI DTOs;
- engine runtime services;
- infrastructure logging;
- production secrets;
- behavior/services.
```

Правило:

```text
Shared contains contracts only.
No behavior.
No infrastructure.
No runtime services.
```

---

## 12. EngineCapabilityRegistry

`EngineCapabilityRegistry` описывает, что конкретная версия движка умеет исполнять.

```text
EngineCapabilityRegistry:
  engineVersion
  supportedSchemaVersions
  supportedPrimitiveRegistryVersions
  supportedRulesetProfileVersions
  supportedActionTypes
  supportedEffectKinds
  supportedMechanics
  supportedPhases
  supportedSourceZones
  supportedResolutionPolicies
  supportedFizzlePolicies
supportedAssetManifestVersions
supportedPresentationCueTypes
```

Используется:

```text
- перед публикацией карты;
- перед стартом матча;
- перед replay;
- при activation RulesetProfile;
- при загрузке CatalogSnapshot.
```

Если карта требует unsupported action/effect/mechanic, матч не стартует, а публикация блокируется.

---

## 13. Engine ↔ Generator Compatibility

Генератор производит:

```text
GeneratedCardCandidate:
  CardDefinition
  schemaVersion
  primitiveRegistryVersion
  rulesetProfileId
  generationPipelineVersion
  balanceHints
  visualAssetRequest
  audioAssetRequest
  assetRequest
```

Движок принимает только:

```text
Published CardVersion:
  validated
  catalog-published
  bound to CatalogSnapshot
  compatible with EngineVersion
  compatible with RulesetProfile
  compatible with EngineCapabilityRegistry
  linked to AssetManifestVersion
```

Движок не принимает:

```text
- raw LLM response;
- unvalidated card candidate;
- card with unknown primitive;
- card using disabled mechanic;
- card outside active CatalogSnapshot;
- card with unsupported schemaVersion;
- card requiring unsupported engine feature.
```

Compatibility gates перед матчем:

```text
DeckValidation
↓
CatalogSnapshotValidation
↓
RulesetProfileValidation
↓
ResolvedRulesetProfileHashCheck
↓
EngineVersionCompatibilityCheck
↓
EngineCapabilityRegistryCheck
↓
PrimitiveRegistryCompatibilityCheck
↓
CardDefinitionDeserialization
↓
ReadyToStartMatch
```

---

## 14. Core Domain Model

### 14.1 GameState

```text
GameState:
  gameId
  engineVersion
  engineCapabilityRegistryVersion
  rulesetProfileId
  resolvedRulesetProfileHash
  catalogSnapshotId
  primitiveRegistryVersion
  seed
  rngState
  rngStreams
  activePlayerId
  turnNumber
  phase
  players
  eventQueue
  derivedEventLog
  canonicalCommandLog
  historyStore
  modifierState
  auraState
  pendingChoices
  terminalState
  stateHash
```

### 14.2 PlayerState

```text
PlayerState:
  playerId
  hero
  deck
  hand
  board
  graveyard
  secretZone
  questZone
  weaponZone
  locationZone
  setAside
  removed
  mana
  armor
  fatigueCounter
  heroPower
  playerModifiers
```

### 14.3 CardDefinition / CardInstance / EntityInstance

```text
CardDefinition = immutable published definition.
CardInstance   = карта в deck/hand/graveyard.
EntityInstance = runtime object на board/hero/weapon/location.
```

`CardType` и `EntityType` нельзя смешивать.

---

## 15. MatchTerminalState

`GameState.terminalState` должен иметь строгую модель.

```text
MatchTerminalState:
  NONE
  PLAYER_WON
  PLAYER_CONCEDED
  DRAW
  TURN_LIMIT_REACHED
  ENGINE_FAULT
  REPLAY_MISMATCH
  SIMULATION_ABORTED
```

Дополнительно:

```text
GameEndInfo:
  terminalState
  winnerPlayerId | null
  loserPlayerId | null
  gameEndReason
  faultRef | null
  finalStateHash
```

Правила:

```text
- terminalState = NONE означает матч активен;
- terminalState != NONE означает матч завершен;
- после terminal state gameplay commands не принимаются;
- replay mismatch не является обычным gameplay outcome;
- ENGINE_FAULT всегда сохраняет debug trace.
```

---

## 16. Identity / Lifecycle Rules

Каждый runtime object имеет стабильный ID.

Правила:

```text
- summon creates new EntityInstance;
- transform may preserve or replace entity identity depending on policy;
- copy creates new identity;
- return to hand converts EntityInstance back to CardInstance;
- resurrect creates new EntityInstance from history snapshot;
- attach/magnetic may create component/enchantment identity depending on mechanic spec.
```

Инварианты:

```text
CardInstance exists in exactly one card zone.
EntityInstance exists in exactly one entity zone.
```

---

## 17. Command Model

Команды делятся на:

```text
PlayerCommand
SystemCommand
ChoiceCommand
DebugCommand
```

### PlayerCommand

```text
PLAY_CARD
ATTACK
USE_HERO_POWER
ACTIVATE_LOCATION
CHOOSE_TARGET
CHOOSE_OPTION
CHOOSE_DISCOVER_OPTION
CHOOSE_CRAFTING_OPTION
END_TURN
CONCEDE
```

### SystemCommand

```text
START_GAME
START_TURN
RESOLVE_PENDING_EFFECTS
FORCE_TIMEOUT_END_TURN
APPLY_START_OF_GAME_EFFECTS
COMPLETE_MULLIGAN
```

`START_TURN` не является публичной командой игрока.

### DebugCommand

```text
SET_STATE
ADD_CARD_TO_HAND
FORCE_DRAW
FORCE_RNG_STATE
SKIP_TO_PHASE
```

DebugCommand запрещены в production match.

DebugCommand может попасть только в debug replay с явным `debugReplayFlag`.

---

## 18. CommandEnvelope vs DeterministicCommandPayload

`CommandEnvelope` не входит в deterministic replay payload.

```text
GameCommandEnvelope:
  commandId
  receivedAt
  clientTimestamp
  correlationId
  authSessionId
  transportMetadata
  rawRequestRef
```

Replay использует только:

```text
DeterministicCommandPayload:
  commandSeq
  playerId
  commandType
  payload
  refs
  expectedPreStateHash | null
  commandCategory
  debugReplayFlag
```

Правило:

```text
Replay uses only canonical DeterministicCommandPayload.
Envelope metadata never affects state hash.
Envelope metadata never affects canonical command hash.
Gameplay refs are part of the canonical command payload and are ordered by key.
```

---

## 19. RNG Stream Policy

Должна быть явная политика RNG-потоков.

```text
RngStream:
  GAME_SETUP
  SHUFFLE
  CARD_DRAW_RANDOM
  CARD_EFFECT
  RANDOM_TARGET
  DISCOVER_OFFER
  BOT_DECISION
  SIMULATION_ONLY
```

Правила:

```text
Gameplay RNG != Bot RNG != Simulation scheduling RNG.
Card effect RNG must not depend on bot decision RNG.
Changing bot implementation must not change gameplay effect RNG.
Every RNG roll is logged.
Every RNG stream has deterministic seed derivation.
```

RNG log:

```text
RngRollLog:
  rngRollId
  stream
  preRngState
  postRngState
  candidateCount
  selectedIndex
  reason
  commandSeq
  eventSeq | null
```

---

## 20. EventQueue vs DerivedEventLog

Нужно разделять:

```text
EventQueue
  runtime queue used during current command/effect resolution.

DerivedEventLog
  persisted deterministic output of command execution.
```

Правила:

```text
EventQueue is not replay source.
DerivedEventLog is not replay source.
CanonicalCommandLog is replay source.
DerivedEventLog is used for comparison, debug, UI timeline, analytics.
```

---

## 21. StateMutationHandlerRegistry

`StateMutator` не должен стать god-class.

Он должен быть orchestrator над handler registry.

```text
StateMutationHandlerRegistry:
  mutationType → IStateMutationHandler<TMutation>
```

Правила:

```text
- every mutation type has exactly one handler;
- duplicate handlers rejected at startup;
- missing handler blocks engine startup/profile activation;
- handler applies one mutation type;
- handler is deterministic;
- handler does not call external services.
```

---

## 22. View Projection / Hidden Information

Authoritative state never returned directly.

```text
AuthoritativeGameState
  full internal state

PlayerView
  visible to specific player

OpponentView
  what player can know about opponent

SpectatorView
  depends on visibility policy

ReplayView
  event timeline / optionally full info after game

DebugView
  full internal state, test/admin only
```

API must not leak:

```text
- opponent hand;
- opponent deck order;
- hidden secrets;
- unrevealed random pool outcomes;
- unrevealed generated choices;
- internal RNG state;
- hidden quest/secret metadata if ruleset says hidden.
```

---

## 23. Presentation Assets: visual, animation and audio

Карты могут иметь не только картинку или `.gif`/idle-анимацию, но и audio assets.

Важно: audio/visual assets являются presentation layer, а не gameplay layer.

```text
Audio не влияет на GameState.
Audio не влияет на legality.
Audio не влияет на target selection.
Audio не влияет на trigger resolution.
Audio не влияет на state hash.
Audio не может запускать gameplay effect.
```

### 23.1 AssetManifest

```text
AssetManifest:
  assetManifestId
  assetManifestVersion
  cardId
  cardVersion
  visualAssets
  animationAssets
  audioAssets
  localizationRefs
  moderationStatus
  contentHashes
  fallbackPolicy
```

### 23.2 AudioAssetManifest

```text
AudioAssetManifest:
  audioManifestId
  audioManifestVersion
  cardId
  cardVersion
  audioMode
  cues[]
  fallbackCueRefs
  localizationPolicy
  volumeGroup
  contentModerationStatus
  contentHash
```

Минимальные audio modes:

```text
NONE
SFX_ONLY
VOICE_LINE_ONLY
SFX_AND_VOICE
FULL_AUDIO_SET
```

Минимальные cue types:

```text
ON_PLAY
ON_SUMMON
ON_ATTACK
ON_DEATH
ON_TRIGGER
ON_SECRET_REVEAL
ON_QUEST_COMPLETE
ON_HOVER
ON_INSPECT
ON_CRAFTED
```

### 23.3 PresentationCue

Игровые события могут порождать presentation cues.

```text
CommittedEvent
↓
PresentationCueResolver
↓
PresentationEvent / AudioCueEvent
↓
Client playback
```

`PresentationCue` не является gameplay event. Он не должен попадать в TriggerResolver, ConditionEvaluator, HistoryStore или StateMutator.

```text
PresentationCue:
  cueId
  cueType
  sourceEventSeq
  sourceCardId
  sourceCardVersion
  assetManifestId
  assetRef
  playbackPolicy
  visibilityPolicy
  fallbackPolicy
```

### 23.4 Audio generation policy

LLM/asset generator может создавать audio prompt и audio asset только вне матча.

Во время матча запрещено:

```text
- вызывать LLM для генерации audio;
- синтезировать новый audio asset;
- менять AssetManifest;
- зависеть от результата audio playback;
- использовать audio как gameplay signal.
```

Если asset отсутствует или не загрузился:

```text
- gameplay продолжается;
- client использует fallback или silence;
- replay gameplay не ломается;
- presentation replay может отметить missing asset.
```

### 23.5 Hashing rule

Gameplay state hash не включает:

```text
- audio playback state;
- loaded audio buffers;
- client volume;
- CDN URL;
- playback timestamp;
- hover/inspect cues.
```

Для presentation replay допускается отдельный `PresentationCatalogHash`, который включает asset refs и content hashes.

Подробно: `asset_generation_spec.md`, `event_system_spec.md`, `canonical_serialization_spec.md`.

---

## 24. Illegal State Policy

### Live match

If command precondition is invalid:

```text
Reject command.
State unchanged.
Return COMMAND_REJECTED.
```

If illegal state occurs after command accepted:

```text
Match enters terminal ENGINE_FAULT state.
Engine emits ENGINE_FAULTED event.
Replay trace preserved.
No silent recovery.
No nondeterministic rollback.
```

### Simulation

```text
Scenario fails.
SimulationResult contains deterministic trace.
Generated card candidate is rejected or sent to repair.
```

### Replay

```text
Replay fails at exact commandSeq/eventSeq.
State diff and hashes are reported.
RNG trace is reported.
```

---

## 25. Error Categories

```text
COMMAND_REJECTED
INVALID_TARGET
INVALID_PHASE
INVALID_CARD_STATE
RULESET_VIOLATION
CATALOG_MISMATCH
UNKNOWN_PRIMITIVE
UNSUPPORTED_MECHANIC
DETERMINISM_VIOLATION
REPLAY_MISMATCH
EVENT_CHAIN_LIMIT_EXCEEDED
RNG_MISMATCH
ILLEGAL_STATE
ENGINE_FAULT
```

DomainError:

```text
DomainError:
  code
  category
  severity
  message
  technicalDetails
  gameId
  commandSeq
  eventSeq
  preStateHash
  postStateHash
  correlationId
```

---

## 26. Simulation Harness

Simulation is used by generator validation and balance checks.

Components:

```text
SimulationRunner
ScenarioRunner
RandomLegalBot
ScriptedBot
BaselineDeckProvider
GeneratedDeckBuilder
SimulationReportBuilder
LegalityOracle
```

Rule:

```text
RandomLegalBot must use the same CommandValidator/LegalityOracle as real gameplay.
It must not generate commands through duplicated rules.
```

---

## 27. Test Strategy

Test pyramid:

```text
Unit Tests
  pure domain logic

Contract Tests
  shared contracts, serialization, schemas

Property-Based Tests
  invariants

Integration Tests
  command → mutation → event → state flow

Golden Tests
  fixed scenario, fixed event log/hash

Replay Tests
  command log replay

Simulation Tests
  generated decks / bots

Mutation Tests
  validators and rules
```

Coverage targets:

```text
Domain unit coverage: >= 85%
Critical systems coverage: >= 95%
Replay/golden scenarios: mandatory for every mechanic
```

Critical systems:

```text
PhaseManager
EventQueue
TriggerResolver
ActionExecutorRegistry
StateMutator
StateMutationHandlerRegistry
DamageSystem
DeathProcessing
ModifierSystem
ReplayRunner
StateHasher
CanonicalSerializer
```

---

## 28. Property-Based Invariants

```text
- card instance exists in exactly one zone;
- entity instance exists in exactly one zone;
- mana never below 0;
- hand size <= hand limit;
- board size <= board limit;
- dead minion not on board;
- eventSeq strictly increasing;
- commandSeq strictly increasing;
- RNG calls deterministic;
- same command log produces same final hash;
- no disabled mechanic executes;
- no unknown primitive executes;
- state hash stable under canonical serialization;
- envelope metadata never affects replay;
- no unsupported action executes;
- every mutation batch is applied once;
- proposed batch has no postStateHash;
- applied batch has postStateHash;
- EventQueue and DerivedEventLog are not treated as replay source.
```

---

## 29. Golden Test Scenarios

MVP golden scenarios:

```text
1. Vanilla minion play.
2. Battlecry deal damage.
3. Deathrattle summon token.
4. End-turn triggered damage.
5. Basic secret trigger.
6. Basic quest progress/reward.
7. Basic aura apply/remove.
8. Batch destroy with simultaneous deaths.
9. Random target with fixed seed.
10. Basic resurrect from history.
11. Full match with generated decks.
12. Replay full match.
```

Post-MVP:

```text
13. Start-of-game effect.
14. Take extra turn.
15. Infuse in hand.
16. Magnetic attach.
17. Persistent enchantment retention.
18. ReExecutableCardPlayHistory.
19. Questline staged reward.
20. Damage distribution.
```

---

## 30. Development Roadmap

### Milestone -1 — Playable Vertical Slice

```text
[ ] Minimal GameState.
[ ] 2 players.
[ ] deck/hand/board/hero.
[ ] PLAY_CARD.
[ ] END_TURN.
[ ] DealDamageAction.
[ ] SummonAction.
[ ] DrawCardAction.
[ ] Deterministic RNG.
[ ] StateHash.
[ ] Command replay.
[ ] 3 static CardDefinitions.
[ ] 1 simple generated CardDefinition from generator pipeline.
[ ] Full mini-match completes.
[ ] Replay mini-match matches final hash.
```

### Milestone 0 — Foundations

```text
[ ] repository structure;
[ ] shared primitives project;
[ ] test projects;
[ ] CI;
[ ] code style;
[ ] deterministic RNG abstraction;
[ ] canonical serializer prototype;
[ ] state hash prototype;
[ ] command replay decision documented;
[ ] state mutation model documented;
[ ] event system split documented.
```

### Milestone 1 — Shared Contracts

```text
[ ] CardDefinition shared DTO.
[ ] EffectDefinition shared DTO.
[ ] ActionDefinition union prototype.
[ ] RulesetProfile DTO.
[ ] MechanicStatus DTO.
[ ] EngineCapabilityRegistry DTO.
[ ] ProposedEvent / CommittedEvent DTO.
[ ] ProposedMutationBatch / AppliedMutationBatch DTO.
[ ] schema snapshot tests.
[ ] serialization tests.
```

### Milestone 2 — Core State

```text
[ ] GameState.
[ ] PlayerState.
[ ] CardInstance.
[ ] EntityInstance.
[ ] zones.
[ ] mana.
[ ] hero.
[ ] basic state movement.
[ ] identity/lifecycle tests.
```

### Milestone 3 — Commands, Mutations and Events

```text
[ ] CommandEnvelope.
[ ] DeterministicCommandPayload.
[ ] PlayerCommand/SystemCommand split.
[ ] CommandValidator.
[ ] ProposedMutationBatch.
[ ] AppliedMutationBatch.
[ ] StateMutationHandlerRegistry.
[ ] StateMutator.
[ ] EventFactory.
[ ] EventQueue.
[ ] DerivedEventLog.
```

### Milestone 4 — Basic Action Execution

```text
[ ] ActionExecutorRegistry.
[ ] Startup registry validation.
[ ] DealDamageAction.
[ ] HealAction.
[ ] DrawCardAction.
[ ] SummonAction.
[ ] DestroyAction.
[ ] BatchDestroyAction.
```

### Milestone 5 — Triggers and Phases

```text
[ ] PhaseManager.
[ ] TriggerResolver.
[ ] ProposedEvent windows.
[ ] CommittedEvent triggers.
[ ] on-play effects.
[ ] deathrattle effects.
[ ] turn-start/turn-end effects.
[ ] canonical trigger ordering.
```

### Milestone 6 — Targeting and Conditions

```text
[ ] TargetResolver.
[ ] ConditionEvaluator.
[ ] PoolResolver basic.
[ ] deterministic random targeting.
[ ] target snapshot policy.
```

### Milestone 7 — Modifiers, Auras, Secrets, Quests

```text
[ ] ModifierSystem basic.
[ ] AuraSystem basic.
[ ] SecretSystem basic.
[ ] QuestSystem basic.
```

### Milestone 8 — Replay and Simulation

```text
[ ] ReplayRunner.
[ ] StateHasher.
[ ] Golden tests.
[ ] SimulationRunner.
[ ] RandomLegalBot.
[ ] generated deck full match.
```

### Milestone 9 — Generator Integration Gate

```text
[ ] Engine accepts published CardVersion.
[ ] Engine rejects raw candidate.
[ ] Engine validates RulesetProfile.
[ ] Engine validates CatalogSnapshot.
[ ] Engine validates EngineCapabilityRegistry.
[ ] full generator → catalog → deck → match → replay flow.
```

---

## 31. MVP Definition of Done

Engine MVP готов, если:

```text
[ ] Domain Layer не зависит от infrastructure.
[ ] Все изменения GameState проходят через mutations.
[ ] ActionExecutor не мутирует GameState напрямую.
[ ] Command replay primary зафиксирован.
[ ] EventLog является derived output.
[ ] ProposedEvent / CommittedEvent semantics описаны.
[ ] ProposedMutationBatch / AppliedMutationBatch semantics реализованы.
[ ] Engine исполняет published CardDefinition DSL.
[ ] Engine не знает ничего про OpenAI.
[ ] Shared primitives используются и генератором, и движком.
[ ] EngineCapabilityRegistry работает.
[ ] RulesetProfile enforced.
[ ] CatalogSnapshot pinned.
[ ] Deterministic RNG используется во всех random choices.
[ ] RNG stream policy реализована.
[ ] StateHash работает.
[ ] Canonical serialization работает.
[ ] Command replay работает.
[ ] Event log полон.
[ ] Basic actions работают.
[ ] Basic triggers работают.
[ ] Basic secrets работают.
[ ] Basic quests работают.
[ ] Basic aura/modifier работают.
[ ] Batch destroy работает.
[ ] History-based resurrect работает.
[ ] ViewProjection не раскрывает hidden info.
[ ] Audio/visual assets не влияют на gameplay state hash.
[ ] PresentationCue не попадает в TriggerResolver/HistoryStore.
[ ] Missing audio asset не ломает match/replay.
[ ] SimulationRunner работает.
[ ] Full match generated deck vs generated deck завершается.
[ ] Replay full match совпадает.
[ ] Critical domain coverage >= 85%.
[ ] Golden tests проходят.
```

---

## 32. Финальная формула

Движок должен быть:

```text
strict enough to be deterministic,
flexible enough to support new mechanics,
narrow enough to remain balanceable,
typed enough to be safe,
tested enough to be trusted.
```

Главное:

```text
Генератор создает кандидатов.
Каталог публикует версии.
Движок исполняет только опубликованные DSL-карты.
RulesetProfile ограничивает разрешенную механику.
EngineCapabilityRegistry подтверждает исполнимость.
StateMutation model контролирует изменения состояния.
Command replay доказывает детерминизм.
TDD доказывает корректность каждого слоя.
```
