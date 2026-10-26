# event_system_spec.md
## Event System Specification
### Draft 2 — Audio / Presentation Events

---

## 0. Назначение

Документ описывает event system игрового движка.

Главное решение:

```text
Command replay primary.
EventLog derived.
```

То есть event system нужна для runtime resolution, trigger processing, diagnostics, UI timeline, replay comparison и analytics, но не является source of truth для восстановления матча.

---

## 1. Основные определения

### 1.1 EventQueue

Runtime-очередь событий текущего исполнения.

```text
EventQueue:
  - используется во время command/effect resolution;
  - хранит события, которые еще должны быть обработаны;
  - deterministic FIFO с canonical ordering;
  - не является replay source;
  - не равна persisted DerivedEventLog.
```

### 1.2 DerivedEventLog

Persisted deterministic output исполнения.

```text
DerivedEventLog:
  - создается во время исполнения canonical command log;
  - используется для UI timeline, debug, analytics, replay comparison;
  - не является source of truth;
  - может быть заново сгенерирован из command replay.
```

### 1.3 ProposedEvent / PreMutationEvent

Событие-намерение до применения state mutation.

Нужно для replacement/prevention effects.

Примеры:

```text
DAMAGE_WOULD_BE_DEALT
CARD_WOULD_BE_DRAWN
ENTITY_WOULD_DIE
CARD_WOULD_MOVE_ZONE
ENTITY_WOULD_BE_DESTROYED
HEAL_WOULD_BE_APPLIED
```

### 1.4 CommittedEvent / DerivedGameEvent

Событие-факт после применения state mutation.

Примеры:

```text
ENTITY_DAMAGED
CARD_DRAWN
ENTITY_DIED
CARD_MOVED_ZONE
ENTITY_DESTROYED
ENTITY_HEALED
```

---

## 2. Event taxonomy

Event system делится на категории:

```text
ProposedEvent
  До mutation.
  Может быть заменено, отменено или модифицировано.

CommittedEvent
  После mutation.
  Используется after-triggers.

ResolverEvent
  Событие resolution pipeline.
  Например TRIGGER_MATCHED, TARGET_RESOLVED.

DiagnosticEvent
  Трассировка и debug.
  Не всегда gameplay-significant.

PresentationEvent
  UI/audio/visual cue, derived from committed gameplay events.
  Не участвует в gameplay resolution.

SystemEvent
  GAME_STARTED, TURN_STARTED, GAME_ENDED, ENGINE_FAULTED.
```

---

## 2.1 PresentationEvent / AudioCueEvent

`PresentationEvent` — событие для клиента, UI, визуалов и аудио. Оно может быть создано на основании `CommittedEvent`, `SystemEvent` или view projection policy.

Ключевое правило:

```text
PresentationEvent не является gameplay event.
PresentationEvent не может запускать TriggerResolver.
PresentationEvent не попадает в HistoryStore как gameplay fact.
PresentationEvent не создает StateMutation.
PresentationEvent не влияет на state hash.
```

Audio playback должен быть presentation-only.

```text
CommittedEvent
↓
PresentationCueResolver
↓
AudioCueEvent / VisualCueEvent
↓
Client playback/rendering
```

### 2.1.1 AudioCueEvent model

```text
AudioCueEvent:
  presentationEventId
  sourceEventSeq
  cueType
  sourceCardId | null
  sourceCardVersion | null
  sourceEntityId | null
  assetManifestId
  audioManifestId
  audioAssetRef
  playbackPolicy
  visibilityPolicy
  fallbackPolicy
  localizationKey | null
```

### 2.1.2 Audio cue types

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

`ON_HOVER` и `ON_INSPECT` являются pure client presentation cues и не должны попадать в gameplay event queue.

### 2.1.3 Audio cue determinism

Если нужно выбрать один вариант из нескольких audio assets, выбор должен быть presentation-deterministic, но не gameplay-deterministic.

Допустимые стратегии:

```text
FIXED_FIRST
HASH_BY_EVENT_SEQ
HASH_BY_CARD_INSTANCE
CLIENT_RANDOM_NON_REPLAY_CRITICAL
```

Запрещено использовать gameplay RNG stream для выбора audio-варианта.

---

## 3. ProposedEvent

### 3.1 Назначение

`ProposedEvent` описывает то, что движок собирается сделать, но еще не сделал.

Он нужен для эффектов вида:

```text
- prevent fatal damage;
- redirect damage;
- replace draw;
- prevent death;
- modify damage amount;
- replace zone movement;
```

### 3.2 ProposedEvent model

```text
ProposedEvent:
  eventId
  eventSeqCandidate
  commandSeq
  parentEventSeq | null
  phase
  proposedEventType
  sourceRef
  targetRefs
  proposedOperationRef
  payload
  replacementWindow
  preventionAllowed
  rngRollRefs
```

Важно:

```text
ProposedEvent не является committed fact.
ProposedEvent не обязан попасть в final DerivedEventLog как gameplay fact.
Но для debug trace он может логироваться отдельно.
```

### 3.3 Required ProposedEvent types

```text
DAMAGE_WOULD_BE_DEALT
CARD_WOULD_BE_DRAWN
ENTITY_WOULD_DIE
ENTITY_WOULD_BE_DESTROYED
CARD_WOULD_MOVE_ZONE
HEAL_WOULD_BE_APPLIED
MODIFIER_WOULD_BE_APPLIED
```

---

## 4. Replacement / Prevention processing

Общий pipeline:

```text
ActionExecutor creates ProposedMutationBatch
↓
ProposedEvents are emitted for replaceable/preventable operations
↓
ReplacementResolver evaluates replacement/prevention effects
↓
ProposedMutationBatch is modified, cancelled, or confirmed
↓
StateMutator applies final ProposedMutationBatch
↓
AppliedMutationBatch is produced
↓
CommittedEvents are created
```

### 4.1 Replacement result

```text
ReplacementResult:
  UNCHANGED
  MODIFIED
  CANCELLED
  REDIRECTED
  REPLACED_WITH_OTHER_OPERATION
```

### 4.2 Prevention result

```text
PreventionResult:
  NOT_PREVENTED
  PREVENTED
  PARTIALLY_PREVENTED
```

### 4.3 Loop protection

Replacement loops must be bounded.

```text
ReplacementResolutionPolicy:
  maxReplacementDepth
  maxReplacementsPerEvent
  deterministicPriorityOrder
  loopBehavior: FAIL_EFFECT | ENGINE_FAULT | SKIP_REPLACEMENT
```

Для MVP:

```text
maxReplacementDepth: 8
loopBehavior: ENGINE_FAULT in live match
```

---

## 5. CommittedEvent

### 5.1 Назначение

`CommittedEvent` описывает факт, который уже произошел после применения mutation.

Используется для:

```text
- after-triggers;
- death processing;
- quest progress;
- history store;
- UI timeline;
- derived event log;
- replay comparison;
- analytics.
```

### 5.2 CommittedEvent model

```text
CommittedEvent:
  eventSeq
  parentEventSeq | null
  commandSeq | null
  eventType
  phase
  sourceRef
  targetRefs
  payload
  mutationBatchRef
  mutationRefs
  rngRollRefs
  preStateHash
  postStateHash
```

### 5.3 Required CommittedEvent types

```text
GAME_STARTED
TURN_STARTED
TURN_ENDED
CARD_PLAYED
CARD_MOVED_ZONE
CARD_DRAWN
CARD_DISCARDED
MINION_SUMMONED
ENTITY_DAMAGED
ENTITY_HEALED
ENTITY_DESTROYED
ENTITY_DIED
DEATHRATTLE_TRIGGERED
SECRET_REVEALED
QUEST_PROGRESS_CHANGED
MODIFIER_APPLIED
MODIFIER_EXPIRED
AURA_REFRESHED
WEAPON_EQUIPPED
HERO_POWER_USED
CHOICE_CREATED
CHOICE_RESOLVED
RNG_ROLLED
GAME_ENDED
ENGINE_FAULTED
```

---

## 6. ResolverEvent

Resolver events are execution trace, not gameplay facts.

Примеры:

```text
TRIGGER_MATCHED
CONDITION_EVALUATED
TARGET_RESOLVED
ACTION_EXECUTION_STARTED
ACTION_EXECUTION_COMPLETED
POOL_RESOLVED
FIZZLE_APPLIED
```

Использование:

```text
- debug trace;
- diagnostics;
- tests;
- replay mismatch investigation.
```

Не использовать как gameplay trigger source, если это явно не разрешено spec.

---

## 7. DiagnosticEvent

Diagnostic events are observability artifacts.

Примеры:

```text
STATE_HASH_CALCULATED
COMMAND_VALIDATED
COMMAND_REJECTED
CAPABILITY_CHECK_PASSED
CAPABILITY_CHECK_FAILED
VIEW_PROJECTED
```

DiagnosticEvent не должен влиять на GameState.

---

## 8. Event ordering

### 8.1 eventSeq

```text
eventSeq strictly increases within a match.
eventSeq is assigned only when event is committed to runtime processing/log.
```

### 8.2 parentEventSeq

`parentEventSeq` связывает цепочку событий:

```text
CARD_PLAYED
  → BATTLECRY_TRIGGERED
    → DAMAGE_WOULD_BE_DEALT
    → REPLACEMENT_RESOLVED
    → ENTITY_DAMAGED
      → ENTITY_WOULD_DIE
      → ENTITY_DIED
        → DEATHRATTLE_TRIGGERED
```

Current production damage-prevention slice emits `DAMAGE_WOULD_BE_DEALT` as a derived `PROPOSED`
event when the target has an active prevention/replacement candidate, emits `REPLACEMENT_RESOLVED`
as a derived `RESOLVER` event, then emits committed mutation events. Command replay regenerates
that sequence from the canonical command log and gameplay state; the event log is not a replay
source.

### 8.3 Canonical trigger ordering

При нескольких eligible triggers порядок определяется:

```text
1. phase priority;
2. controller priority;
3. source zone priority;
4. board position / zone position;
5. entityInstanceId / cardInstanceId;
6. effectId;
7. trigger registration order only if canonicalized.
```

Нельзя полагаться на unordered collection order.

---

## 9. EventQueue rules

```text
- EventQueue is runtime-only.
- EventQueue is drained deterministically.
- EventQueue uses stable FIFO.
- Child events are enqueued according to canonical priority.
- EventQueue has max depth and max event count.
- EventQueue overflow creates deterministic ENGINE_FAULT or simulation failure.
```

MVP limits:

```text
maxEventDepth: 64
maxEventsPerCommand: 512
maxEventsPerTurn: 2048
```

Финальные значения выносятся в `ruleset_profile_spec.md` / `balance_model_spec.md`.

---

## 10. DerivedEventLog rules

```text
- stores committed gameplay events;
- may store selected resolver/diagnostic events for debug mode;
- stores preStateHash/postStateHash;
- stores rngRollRefs;
- stores mutationBatchRef;
- deterministic under command replay;
- can be regenerated from command replay.
```

Важно:

```text
DerivedEventLog mismatch is replay failure,
but DerivedEventLog is not replay source.
```

---

## 11. Event visibility

События имеют visibility policy.

```text
EventVisibility:
  PUBLIC
  OWNER_ONLY
  CONTROLLER_ONLY
  HIDDEN_TO_OPPONENT
  DEBUG_ONLY
```

Примеры:

```text
CARD_DRAWN from own deck:
  owner sees card identity;
  opponent may see only "card drawn".

SECRET_REVEALED:
  public after reveal.

RNG_ROLLED:
  debug/replay only, unless player-facing randomness requires reveal.

AudioCueEvent:
  visible only to clients according to ViewProjectionService; never gameplay-significant.
```

ViewProjectionService обязан учитывать event visibility.

---

## 12. Event and history

HistoryStore consumes committed events.

Правило:

```text
HistoryStore must not consume ProposedEvent as fact.
```

Исключение:

```text
Diagnostic history may store ProposedEvents for debug trace.
```

Примеры:

```text
ENTITY_DIED → minionsDiedThisGame
CARD_PLAYED → cardsPlayedThisGame
SPELL_CAST → spellsCastThisGame
CARD_GENERATED → cardsGeneratedThisGame
```

---

## 13. TDD requirements

### 13.1 Unit tests

```text
[ ] ProposedEvent created before damage mutation.
[ ] Prevention cancels damage before mutation.
[ ] CommittedEvent created after mutation.
[ ] History consumes committed event only.
[ ] EventQueue drains in stable order.
[ ] parentEventSeq assigned correctly.
```

### 13.2 Replay tests

```text
[ ] same command log produces same event log.
[ ] DerivedEventLog mismatch detected.
[ ] ProposedEvent debug trace deterministic.
[ ] EventQueue not used as replay source.
```

### 13.3 Property tests

```text
[ ] eventSeq strictly increasing.
[ ] no committed event without valid mutation or system cause.
[ ] committed event pre/post hashes match applied state.
[ ] proposed events cannot directly mutate state.
```

### 13.4 Golden tests

```text
[ ] damage prevented by secret.
[ ] damage committed without prevention.
[ ] death event after damage.
[ ] deathrattle after death committed event.
[ ] quest progress after committed card-play event.
```

---

## 14. MVP scope

MVP event system must support:

```text
- ProposedEvent for damage;
- CommittedEvent for damage/draw/summon/destroy/move;
- EventQueue;
- DerivedEventLog;
- parentEventSeq;
- eventSeq;
- pre/post state hash;
- RNG event refs;
- basic trigger processing from committed events;
- basic PresentationCueResolver for audio/visual cues;
- AudioCueEvent as presentation-only event.
```

Post-MVP:

```text
- ProposedEvent for draw replacement;
- ProposedEvent for death replacement;
- advanced replacement loops;
- event visibility policies;
- detailed resolver/diagnostic event streams.
```

---

## 15. Definition of Done

Event system spec is implementation-ready when:

```text
[ ] ProposedEvent and CommittedEvent DTOs defined.
[ ] EventQueue implementation has deterministic ordering.
[ ] DerivedEventLog implementation exists.
[ ] ReplacementResolver consumes ProposedEvents.
[ ] TriggerResolver consumes CommittedEvents.
[ ] HistoryStore consumes CommittedEvents.
[ ] Replay compares DerivedEventLog as output.
[ ] Event visibility policy exists.
[ ] PresentationEvent cannot trigger gameplay.
[ ] AudioCueEvent is excluded from gameplay history/state hash.
[ ] Missing audio asset uses fallback without affecting gameplay.
[ ] Golden tests pass.
[ ] Property tests pass.
```
