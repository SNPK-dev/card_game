# state_mutation_spec.md
## State Mutation Specification
### Draft 2 — Audio / Presentation Boundary

---

## 0. Назначение

Документ описывает модель изменения `GameState`.

Главное правило:

```text
ActionExecutor не мутирует GameState напрямую.
Resolver не мутирует GameState напрямую.
StateMutator — единственная точка применения mutations.
```

Цель:

```text
- контролируемые изменения state;
- deterministic replay;
- простые unit tests;
- защита от god-executor;
- traceable state transitions;
- совместимость с event system.
```

---

## 1. Основной pipeline

```text
Command / Event / Action
↓
ActionExecutor or Resolver
↓
ProposedMutationBatch
↓
PreMutationEvents, если нужны
↓
Replacement / Prevention Resolver
↓
Final ProposedMutationBatch
↓
StateMutator.Apply()
↓
AppliedMutationBatch
↓
EventFactory
↓
CommittedEvents
↓
StateHash
```

---

## 2. Executor responsibility

Executor отвечает за расчет намерения.

```text
Executor:
  - validates local action preconditions;
  - resolves action-specific data;
  - creates ProposedMutationBatch;
  - does not mutate GameState;
  - does not calculate postStateHash;
  - does not write event log;
  - does not call external systems.
```

Пример:

```text
DealDamageExecutor
  input: DealDamageAction, target
  output: ProposedMutationBatch containing ApplyDamageMutation
```

---

## 3. ProposedMutationBatch

`ProposedMutationBatch` — набор изменений, которые движок собирается применить, но еще не применил.

```text
ProposedMutationBatch:
  batchId
  commandSeq
  parentEventSeq | null
  phase
  sourceRef
  reason
  mutations[]
  preStateHash
  replacementPolicyRef | null
  fizzlePolicyRef | null
```

Важно:

```text
ProposedMutationBatch не содержит postStateHash.
ProposedMutationBatch может быть изменен replacement/prevention resolver.
ProposedMutationBatch может быть отменен.
ProposedMutationBatch не является фактом изменения state.
```

---

## 4. AppliedMutationBatch

`AppliedMutationBatch` — факт применения mutations.

```text
AppliedMutationBatch:
  batchId
  commandSeq
  parentEventSeq | null
  phase
  sourceRef
  reason
  appliedMutations[]
  skippedMutations[]
  preStateHash
  postStateHash
  derivedEventRefs[]
  appliedAtStep
```

Правила:

```text
AppliedMutationBatch создается только StateMutator.
postStateHash рассчитывается после применения mutations.
AppliedMutationBatch immutable.
AppliedMutationBatch участвует в replay comparison.
```

---

## 5. StateMutation

Базовая модель:

```text
StateMutation:
  mutationId
  mutationType
  sourceRef
  targetRef | null
  payload
  phase
  causedByCommandSeq
  causedByEventSeq | null
  orderingKey
```

`orderingKey` нужен для deterministic sorting внутри batch, если mutation order имеет значение.

---

## 6. Mutation categories

Минимальные категории:

```text
ZoneMutation
  MOVE_CARD_ZONE
  MOVE_ENTITY_ZONE
  REMOVE_FROM_ZONE
  ADD_TO_ZONE

StatMutation
  SET_HEALTH
  MODIFY_HEALTH
  SET_ATTACK
  MODIFY_ATTACK
  APPLY_DAMAGE_STATE
  CLEAR_DAMAGE_STATE

ResourceMutation
  MODIFY_MANA
  SET_MANA
  MODIFY_ARMOR

CardMutation
  DRAW_CARD
  DISCARD_CARD
  CREATE_CARD_INSTANCE
  DESTROY_CARD_INSTANCE

EntityMutation
  CREATE_ENTITY
  DESTROY_ENTITY
  SUMMON_ENTITY
  TRANSFORM_ENTITY

ModifierMutation
  APPLY_MODIFIER
  EXPIRE_MODIFIER
  REMOVE_MODIFIER

AuraMutation
  APPLY_AURA
  REMOVE_AURA
  REFRESH_AURA

HistoryMutation
  APPEND_HISTORY_RECORD

ChoiceMutation
  CREATE_CHOICE
  RESOLVE_CHOICE
  CLEAR_CHOICE

GameMutation
  SET_PHASE
  SET_ACTIVE_PLAYER
  ADVANCE_TURN
  SET_TERMINAL_STATE
```

---

## 6.1 Presentation and audio are not mutations

Audio, visual effects, animations and client playback state are not gameplay state mutations.

Запрещенные mutation types:

```text
PLAY_AUDIO
STOP_AUDIO
LOAD_AUDIO_ASSET
SET_CLIENT_VOLUME
SET_CARD_HOVER_STATE
SET_ANIMATION_FRAME
SET_GIF_PLAYBACK_STATE
```

Если gameplay event должен вызвать звук, это делается не через `StateMutation`, а через presentation pipeline:

```text
AppliedMutationBatch
↓
CommittedEvent
↓
PresentationCueResolver
↓
AudioCueEvent
↓
Client playback
```

Правило:

```text
StateMutation changes authoritative gameplay state only.
PresentationCue changes no gameplay state.
```

Asset manifests могут быть связаны с `CardVersion`/`CatalogSnapshot`, но не мутируются во время матча.

---

## 7. StateMutationHandlerRegistry

`StateMutator` не должен быть god-class.

Он делегирует применение mutation handlers.

```text
StateMutationHandlerRegistry:
  mutationType → IStateMutationHandler<TMutation>
```

Handler contract:

```text
IStateMutationHandler<TMutation>:
  CanApply(GameState state, TMutation mutation) -> bool
  Apply(GameState state, TMutation mutation) -> GameState
```

Правила:

```text
- every mutation type has exactly one handler;
- duplicate handlers rejected at startup;
- missing handler blocks engine startup/profile activation;
- handler applies one mutation type;
- handler is deterministic;
- handler does not call external services;
- handler does not emit events directly.
```

---

## 8. StateMutator

`StateMutator` orchestrates handlers.

```text
StateMutator.Apply(GameState state, ProposedMutationBatch batch) -> AppliedMutationBatch
```

Responsibilities:

```text
- verify preStateHash;
- canonicalize mutation order;
- validate mutation applicability;
- call mutation handlers;
- calculate postStateHash;
- create AppliedMutationBatch;
- return new GameState and applied batch.
```

StateMutator не должен:

```text
- resolve targets;
- choose random;
- evaluate conditions;
- execute card effects;
- call database;
- call LLM;
- emit gameplay events directly.
```

---

## 9. Mutation ordering

Mutation order must be deterministic.

Canonical ordering:

```text
1. explicit orderingKey;
2. mutation category priority;
3. sourceRef canonical order;
4. targetRef canonical order;
5. mutationId.
```

Некоторые batches должны сохранять declared order:

```text
- sequential actions;
- ordered zone movement;
- explicit action result refs.
```

Некоторые batches должны применяться simultaneously:

```text
- batch damage;
- batch destroy;
- simultaneous deaths;
- aura refresh.
```

`ResolutionPolicy` определяет режим:

```text
SEQUENTIAL
SIMULTANEOUS
BATCH
```

---

## 10. Validation before apply

Перед применением batch:

```text
- preStateHash must match current state hash;
- all mutation types must be supported;
- all target refs must exist unless mutation creates them;
- zone transitions must be legal;
- RulesetProfile must allow required mechanic;
- mutation must not violate hard invariants;
- batch must not exceed event/mutation limits.
```

Если mutation invalid:

```text
Live match:
  ENGINE_FAULT if invalid state after command accepted.

Simulation:
  scenario failed.

Replay:
  mismatch at commandSeq/eventSeq.
```

---

## 11. Zone mutation rules

Zone mutation must enforce:

```text
- card exists in exactly one card zone;
- entity exists in exactly one entity zone;
- moving card removes from previous zone;
- moving entity removes from previous zone;
- zone capacity checked before apply;
- hidden info policy updated after move;
- history updated via committed event/history mutation.
```

Examples:

```text
HAND → BOARD:
  CardInstance removed from hand.
  EntityInstance created on board.

BOARD → GRAVEYARD:
  EntityInstance removed from board.
  death/graveyard record created.

DECK → HAND:
  CardInstance removed from deck.
  CardInstance added to hand.
```

---

## 12. Damage mutation rules

Damage should be represented as mutation only after replacement/prevention.

Pipeline:

```text
DealDamageAction
↓
ProposedMutationBatch with ApplyDamageMutation
↓
DAMAGE_WOULD_BE_DEALT ProposedEvent
↓
ReplacementResolver
↓
final ApplyDamageMutation
↓
StateMutator applies damage
↓
ENTITY_DAMAGED CommittedEvent
↓
Death check
```

Damage mutation payload:

```text
ApplyDamageMutation:
  targetEntityRef
  sourceRef
  amount
  damageSchool | null
  preventable
  lifestealSourceRef | null
  damageInstanceId
```

---

## 13. Death mutation rules

Death is not direct damage.

Death processing creates mutations after death check:

```text
ENTITY_WOULD_DIE ProposedEvent
↓
ReplacementResolver
↓
DestroyEntityMutation / MoveEntityToGraveyardMutation
↓
ENTITY_DIED CommittedEvent
↓
Deathrattle phase
```

Simultaneous deaths must be batch-processed.

---

## 14. Choice mutations

Choice state is part of authoritative state.

```text
CreateChoiceMutation:
  choiceId
  playerId
  choiceType
  options
  visibilityPolicy
  sourceRef
```

```text
ResolveChoiceMutation:
  choiceId
  selectedOptionId
```

Rules:

```text
- pending choice blocks unrelated commands if policy says so;
- choice options must be deterministic;
- hidden options must not leak through view projection;
- choice resolution must be replayable.
```

---

## 15. History mutations

History is updated through deterministic records.

```text
AppendHistoryRecordMutation:
  historyRecordId
  recordType
  sourceEventRef
  payload
```

History mutations should be derived from committed events, not proposed events.

---

## 16. Mutation and events

StateMutator does not create events directly.

EventFactory maps applied mutations to committed events.

```text
AppliedMutationBatch
↓
EventFactory
↓
CommittedEvents
```

Some events are not mutation-derived:

```text
RNG_ROLLED
TRIGGER_MATCHED
ENGINE_FAULTED
CHOICE_CREATED
AUDIO_CUE_CREATED
VISUAL_CUE_CREATED
```

These are created by resolver/diagnostic/presentation subsystems.

`AUDIO_CUE_CREATED` and `VISUAL_CUE_CREATED` are presentation events and must not cause additional StateMutation.

---

## 17. Mutation and state hash

Hash rules:

```text
- ProposedMutationBatch has preStateHash only.
- AppliedMutationBatch has preStateHash and postStateHash.
- postStateHash is computed from canonical serialized GameState.
- envelope metadata excluded.
- logs excluded unless hash policy says otherwise.
```

---

## 18. TDD requirements

### Unit tests

```text
[ ] executor returns ProposedMutationBatch.
[ ] executor does not mutate state.
[ ] StateMutator applies mutation.
[ ] AppliedMutationBatch gets postStateHash.
[ ] ProposedMutationBatch has no postStateHash.
[ ] invalid mutation rejected.
```

### Integration tests

```text
[ ] PlayCard command creates zone mutations.
[ ] DealDamage creates proposed damage mutation.
[ ] Replacement modifies proposed mutation.
[ ] StateMutator applies final mutation.
[ ] EventFactory creates committed event.
[ ] audio cue creation does not create StateMutation.
[ ] missing audio asset does not alter GameState.
[ ] presentation event is not consumed by StateMutator.
```

### Property tests

```text
[ ] every CardInstance exists in exactly one zone.
[ ] every EntityInstance exists in exactly one zone.
[ ] applying same batch to same state gives same hash.
[ ] mutation order canonicalization stable.
[ ] mutation batch applied exactly once.
```

### Golden tests

```text
[ ] summon minion.
[ ] draw card.
[ ] deal damage.
[ ] prevent damage.
[ ] batch destroy.
[ ] return entity to hand.
```

---

## 19. MVP scope

MVP mutation system must support:

```text
- move card between zones;
- create entity;
- summon entity;
- apply damage;
- heal entity/hero;
- draw card;
- destroy entity;
- apply basic modifier;
- append history record;
- create/resolve choice;
- set phase;
- set terminal state.
```

Post-MVP:

```text
- persistent enchantment carry;
- transformation identity policies;
- magnetic attach/component mutations;
- questline stage mutations;
- extra turn scheduler mutations;
- advanced replacement loops.
```

---

## 20. Definition of Done

State mutation system is ready when:

```text
[ ] ProposedMutationBatch DTO exists.
[ ] AppliedMutationBatch DTO exists.
[ ] StateMutation DTO exists.
[ ] StateMutationHandlerRegistry exists.
[ ] StateMutator applies mutations only through handlers.
[ ] Executors do not mutate state.
[ ] EventFactory maps applied mutations to events.
[ ] Presentation/audio events are explicitly excluded from StateMutation model.
[ ] StateMutationHandlerRegistry rejects presentation-only mutation types.
[ ] State hash integration works.
[ ] Replay verifies mutation results.
[ ] Property invariants pass.
[ ] Golden scenarios pass.
```
