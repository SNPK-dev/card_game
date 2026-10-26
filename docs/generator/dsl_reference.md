# DSL Reference

## Controlled event-driven CCG engine

**Version:** v0.2-draft  
**Status:** Draft  
**Owner:** DSL Owner  
**Depends on:** `architecture_contract.md`, `ruleset_profile_spec.md`, `mechanic_status_spec.md`, `effect_kind_registry.md`, `action_registry.md`, `phase_timing_registry.md`, `source_zone_registry.md`

---

## 0. Changelog v0.2

Changes from v0.1:

- Replaced hybrid `actionType + payload` wording with explicit discriminated union variants.
- Added missing `RANDOM_SELECT` and `RETURN_TO_HAND` actions used by examples.
- Added `ADD_CARD_TO_HAND` and `MOVE_TO_ZONE` as core movement actions.
- Split `CardType` from runtime `EntityType`.
- Added `CardTextGeneration` rules.
- Added `GeneratedCardSnapshot`.
- Added initial `ValueExpression` model.
- Added initial `EventType` taxonomy.
- Added `StateHash / CanonicalSerialization` placeholders.
- Tightened semantic validation checklist.

---

## 1. Purpose

This document defines the first practical DSL reference for the controlled event-driven CCG engine.

The DSL converts card design intent into a strictly typed, deterministic, validated and replayable object that the game engine can execute.

The DSL is not:

- arbitrary JSON;
- a scripting language;
- trusted LLM output;
- a place for hidden engine exceptions.

The DSL is a constrained AST:

```text
CardDefinition
  StaticCardData
  EntityDefinition
  Effects[]
    EffectDefinition
      sourceZones
      trigger/timing
      conditions
      targetRequirement
      actions[]
      resolutionPolicy
      fizzlePolicy
      replayPolicy
```

---

## 2. Design principles

1. Every executable behavior is represented as typed DSL.
2. Every effect declares `sourceZones`.
3. Every effect declares timing / phase semantics.
4. Every action is a discriminated union variant.
5. Randomness goes through deterministic RNG.
6. Pools resolve against `catalogSnapshotId`.
7. Fizzle behavior is explicit.
8. Generated cards have snapshots.
9. Player-facing card text is generated from DSL or validated against DSL.
10. Published cards are replayable from `seed + command log + catalog snapshot`.

---

## 3. Versioning

```yaml
schemaVersion: string
engineVersion: string
primitiveRegistryVersion: string
rulesetProfileId: string
catalogSnapshotId: string | null
```

Published cards are immutable. Any behavior change creates a new `cardVersion`.

---

## 4. Root model: CardDefinition

```yaml
CardDefinition:
  cardId: string | null
  cardVersion: string | null
  schemaVersion: string
  engineVersion: string
  primitiveRegistryVersion: string
  rulesetProfileId: string
  catalogSnapshotId: string | null

  staticData: StaticCardData
  entity: EntityDefinition
  effects: EffectDefinition[]

  cardText: CardTextGeneration
  balanceMetadata: BalanceMetadata
  assetMetadata: AssetMetadata
  authoringMetadata: AuthoringMetadata
```

---

## 5. StaticCardData

```yaml
StaticCardData:
  name: string
  cardType: CardType
  classTags: string[]
  rarity: Rarity
  manaCost: integer
  tribes: Tribe[]
  keywords: Keyword[]
  collectible: boolean
  formatTags: string[]
```

### 5.1 CardType

`CardType` describes what kind of card exists in hand/deck/catalog.

```text
MINION_CARD
SPELL_CARD
WEAPON_CARD
LOCATION_CARD
HERO_CARD
HERO_POWER_CARD
TOKEN_CARD
```

MVP card types:

```text
MINION_CARD
SPELL_CARD
TOKEN_CARD
```

---

## 6. Runtime EntityType

`EntityType` describes runtime objects in zones.

```text
MINION
HERO
WEAPON
LOCATION
SECRET
QUEST
QUESTLINE
CARD_IN_HAND
CARD_IN_DECK
CARD_IN_GRAVEYARD
GLOBAL_RULE
```

Important distinction:

```text
HERO_CARD is a CardType.
HERO is a runtime EntityType.
```

A hero card may create or replace a runtime `HERO` entity.

---

## 7. EntityDefinition

Exactly one main block is active for a playable card. Inactive blocks are `null`.

```yaml
EntityDefinition:
  minion: MinionDefinition | null
  spell: SpellDefinition | null
  weapon: WeaponDefinition | null
  location: LocationDefinition | null
  heroCard: HeroCardDefinition | null
  heroPower: HeroPowerDefinition | null
```

### 7.1 MinionDefinition

```yaml
MinionDefinition:
  attack: integer
  health: integer
  tribes: Tribe[]
  baseKeywords: Keyword[]
  canAttack: boolean
```

### 7.2 SpellDefinition

```yaml
SpellDefinition:
  spellSchool: SpellSchool | null
  requiresTarget: boolean
```

### 7.3 WeaponDefinition

```yaml
WeaponDefinition:
  attack: integer
  durability: integer
  baseKeywords: Keyword[]
```

### 7.4 LocationDefinition

```yaml
LocationDefinition:
  durability: integer
  cooldownPolicy: CooldownPolicy
  activationLimit: ActivationLimit
```

### 7.5 HeroCardDefinition

```yaml
HeroCardDefinition:
  armorGain: integer
  heroPowerReplacement: HeroPowerDefinition | null
  portraitAssetRef: string | null
```

### 7.6 HeroPowerDefinition

```yaml
HeroPowerDefinition:
  cost: integer
  activationTiming: HeroPowerActivationTiming
  targetRequirement: TargetSpec | null
  effects: EffectDefinition[]
```

---

## 8. EffectDefinition

```yaml
EffectDefinition:
  effectId: string
  effectKind: EffectKind
  sourceZones: SourceZone[]
  trigger: TriggerDefinition | null
  timingPolicy: TimingPolicy
  conditions: Condition[]
  targetRequirement: TargetSpec | null
  actions: ActionDefinition[]
  resolutionPolicy: ResolutionPolicy
  fizzlePolicy: FizzlePolicy
  replayPolicy: ReplayPolicy
  tags: string[]
```

### 8.1 EffectKind

Architecture-supported initial set:

```text
SIMPLE_PLAY_EFFECT
TRIGGERED_BOARD_EFFECT
TRIGGERED_HAND_EFFECT
TRIGGERED_DECK_EFFECT
DEATHRATTLE_EFFECT
SECRET_EFFECT
QUEST_EFFECT
QUESTLINE_EFFECT
AURA_EFFECT
MODIFIER_EFFECT
HISTORY_BASED_EFFECT
REEXECUTABLE_HISTORY_EFFECT
POOL_BASED_EFFECT
BATCH_EFFECT
COMPOSITE_EFFECT
REPLACEMENT_EFFECT
PREVENTION_EFFECT
ALTERNATIVE_PLAY_EFFECT
CARD_CRAFTING_EFFECT
SMART_OFFER_EFFECT
HERO_CARD_EFFECT
HERO_POWER_EFFECT
LOCATION_ABILITY_EFFECT
WEAPON_EFFECT
START_OF_GAME_EFFECT
WIN_CONDITION_EFFECT
REST_OF_GAME_EFFECT
```

`RulesetProfile` decides which effect kinds are enabled.

---

## 9. Source zones

```text
BOARD
HAND
DECK
SECRET_ZONE
QUEST_ZONE
WEAPON_ZONE
LOCATION_ZONE
HERO_ZONE
GRAVEYARD
SET_ASIDE
GLOBAL
```

Validation rules:

- `SECRET_EFFECT` requires `SECRET_ZONE`.
- `QUEST_EFFECT` requires `QUEST_ZONE`.
- `QUESTLINE_EFFECT` requires `QUEST_ZONE`.
- `TRIGGERED_DECK_EFFECT` requires `DECK`.
- `START_OF_GAME_EFFECT` usually uses `DECK` or `GLOBAL`.
- `HERO_POWER_EFFECT` requires `HERO_ZONE`.

---

## 10. Timing and phases

```text
DECK_SUBMITTED
MATCH_INITIALIZED
START_OF_GAME_PRE_MULLIGAN
START_OF_GAME_EFFECTS
MULLIGAN
TURN_0_SETUP

BEFORE_PLAY
PAY_COST
CARD_LEAVES_HAND
ON_PLAY
BATTLECRY_PHASE
ON_SUMMON
AFTER_SUMMON
SECRET_WINDOW
AFTER_PLAY

PRE_DAMAGE
DAMAGE_APPLIED
AFTER_DAMAGE
FATAL_DAMAGE_CHECK

DEATH_CHECK
DEATH_PROCESSING
DEATHRATTLE_PHASE
AFTER_DEATH

TURN_START
TURN_END
EXTRA_TURN_SCHEDULING
AURA_REFRESH
```

### 10.1 TimingPolicy

```yaml
TimingPolicy:
  phase: Phase
  window: TimingWindow | null
  priority: integer
  orderingPolicy: OrderingPolicy
```

---

## 11. EventType taxonomy v0.2

Initial event taxonomy:

```text
MATCH_INITIALIZED
CARD_PLAY_VALIDATION_STARTED
CARD_PLAYED
CARD_LEFT_HAND
CARD_SUMMONED
MINION_SUMMONED
SPELL_CAST
WEAPON_EQUIPPED
LOCATION_ACTIVATED
HERO_POWER_USED

TURN_STARTED
TURN_ENDED

DAMAGE_WOULD_BE_DEALT
DAMAGE_DEALT
FATAL_DAMAGE_DETECTED
HEAL_DONE

DEATH_CHECK_REQUESTED
ENTITY_DIED
DEATHRATTLE_TRIGGERED

CARD_DRAWN
CARD_DISCARDED
CARD_SHUFFLED_INTO_DECK
CARD_RETURNED_TO_HAND
CARD_ADDED_TO_HAND
CARD_GENERATED

QUEST_PROGRESS_CHANGED
QUEST_COMPLETED

SECRET_REVEALED
AURA_REFRESH_REQUESTED
```

The final taxonomy belongs in `event_taxonomy_spec.md`.

---

## 12. TriggerDefinition

```yaml
TriggerDefinition:
  eventType: EventType
  eventFilter: EventFilter | null
  controllerScope: ControllerScope
  oncePerEvent: boolean
```

Example:

```yaml
trigger:
  eventType: TURN_ENDED
  eventFilter:
    activePlayer: SELF
  controllerScope: SOURCE_CONTROLLER
  oncePerEvent: true
```

---

## 13. Conditions

```yaml
Condition:
  kind: ConditionKind
  parameters: object
```

Initial condition kinds:

```text
HAS_VALID_TARGET
DECK_HAS_NO_DUPLICATES
CARD_ORIGIN_MATCHES
SOURCE_IS_IN_ZONE
PLAYER_HEALTH_LESS_OR_EQUAL
BOARD_HAS_SPACE
HAND_HAS_SPACE
POOL_NOT_EMPTY
HISTORY_QUERY_NOT_EMPTY
PLAYED_CARD_HAS_TAG
IS_OWNER_TURN
```

---

## 14. TargetSpec

```yaml
TargetSpec:
  required: boolean
  selectionMode: TargetSelectionMode
  candidates: EntityQuery
  count: CountSpec
  retargetPolicy: RetargetPolicy
```

### 14.1 TargetSelectionMode

```text
PLAYER_CHOSEN
RANDOM
AUTO_VALID
EVENT_SOURCE
EVENT_TARGET
PREVIOUS_ACTION_RESULT
ALL_MATCHING
NONE
```

### 14.2 EntityQuery

```yaml
EntityQuery:
  controller: SELF | OPPONENT | ANY
  zones: SourceZone[]
  entityTypes: EntityType[]
  requiredTags: string[]
  excludedTags: string[]
  aliveOnly: boolean
```

Example:

```yaml
targetRequirement:
  required: false
  selectionMode: RANDOM
  candidates:
    controller: OPPONENT
    zones: [BOARD, HERO_ZONE]
    entityTypes: [MINION, HERO]
    requiredTags: []
    excludedTags: []
    aliveOnly: true
  count:
    mode: EXACT
    value: 1
  retargetPolicy: RETRY_RANDOM_TARGET
```

---

## 15. ValueExpression

`ValueExpression` represents deterministic numeric values.

```yaml
ValueExpression:
  kind: ValueExpressionKind
  value: integer | null
  ref: string | null
  operation: ValueOperation | null
  operands: ValueExpression[]
```

Initial kinds:

```text
CONSTANT
X_VALUE
HISTORY_COUNT
CARD_COST
SOURCE_ATTACK
SOURCE_HEALTH
TARGET_ATTACK
TARGET_HEALTH
PLAYER_ARMOR
PLAYER_HEALTH
INFUSE_COUNT
```

Examples:

```yaml
amount:
  kind: CONSTANT
  value: 3
```

```yaml
totalAmount:
  kind: HISTORY_COUNT
  ref: spellsCastThisGame
```

---

## 16. ActionDefinition as discriminated union

`ActionDefinition` is a discriminated union.

Do not model actions as:

```yaml
ActionDefinition:
  actionType: DEAL_DAMAGE
  payload: ...
```

Instead, every action variant is its own object shape.

### 16.1 Union

```text
ActionDefinition =
  DealDamageAction
  | DamageDistributionAction
  | HealAction
  | FullyHealAction
  | DrawCardAction
  | DiscardCardAction
  | SummonAction
  | MoveSelfToBoardAction
  | DestroyAction
  | BatchDestroyAction
  | RandomSelectAction
  | SilenceAction
  | FreezeAction
  | TransformAction
  | DiscoverAction
  | ApplyModifierAction
  | ApplyAuraAction
  | ApplyRestOfGameRuleAction
  | PreventDamageAction
  | RedirectDamageAction
  | ReplaceEventAction
  | ResurrectAction
  | ReturnToHandAction
  | AddCardToHandAction
  | MoveToZoneAction
  | ReplayCardPlayAction
  | ReplayEffectAction
  | CraftCustomCardAction
  | OfferSmartChoicesAction
  | TakeExtraTurnAction
  | AttachToEntityAction
  | ShuffleIntoDeckAction
  | WinGameAction
  | ReplaceHeroAction
  | ReplaceHeroPowerAction
```

### 16.2 ActionType set

```text
DEAL_DAMAGE
DAMAGE_DISTRIBUTION
HEAL
FULLY_HEAL
DRAW_CARD
DISCARD_CARD
SUMMON
MOVE_SELF_TO_BOARD
DESTROY
BATCH_DESTROY
RANDOM_SELECT
SILENCE
FREEZE
TRANSFORM
DISCOVER
APPLY_MODIFIER
APPLY_AURA
APPLY_REST_OF_GAME_RULE
PREVENT_DAMAGE
REDIRECT_DAMAGE
REPLACE_EVENT
RESURRECT
RETURN_TO_HAND
ADD_CARD_TO_HAND
MOVE_TO_ZONE
REPLAY_CARD_PLAY
REPLAY_EFFECT
CRAFT_CUSTOM_CARD
OFFER_SMART_CHOICES
TAKE_EXTRA_TURN
ATTACH_TO_ENTITY
SHUFFLE_INTO_DECK
WIN_GAME
REPLACE_HERO
REPLACE_HERO_POWER
```

### 16.3 DealDamageAction

```yaml
DealDamageAction:
  actionId: string
  actionType: DEAL_DAMAGE
  amount: ValueExpression
  target: TargetRef
  damageSchool: DamageSchool | null
  lifesteal: boolean
  preventable: boolean
```

### 16.3.1 DamageDistributionAction runtime support

Current deterministic runtime support is limited to fixed positive `totalAmount`, entity candidates from board/global target queries, `SOURCE_ORDER` or `RANDOM_SPLIT`, and `SEPARATE_DAMAGE_INSTANCES`.

Supported lifecycle policies:

- `lifestealAggregationPolicy`: `NO_LIFESTEAL`, `AGGREGATE_LIFESTEAL`;
- `overkillPolicy`: `ALLOW_OVERKILL`, `CAP_AT_REMAINING_HEALTH`, `SKIP_DEAD_TARGETS`;
- `RANDOM_SPLIT` requires `rngPolicy: RANDOM_TARGET`.

Damage-distribution mutations carry explicit missing-target skip metadata for mid-resolution target disappearance. Divine-shield damage prevention is handled by the replacement window before `APPLY_DAMAGE`: the engine consumes the shield through a replayable `REMOVE_KEYWORD` mutation and leaves later prevention modifiers available for subsequent damage.

### 16.4 RandomSelectAction

```yaml
RandomSelectAction:
  actionId: string
  actionType: RANDOM_SELECT
  from: EntityQuery | CardQuery | PoolRef
  count: CountSpec
  resultRef: string
  rngPolicy: RngPolicy
  emptyPolicy: FizzleBehavior
```

### 16.5 BatchDestroyAction

```yaml
BatchDestroyAction:
  actionId: string
  actionType: BATCH_DESTROY
  targets: TargetRef
  deathProcessingPolicy: SIMULTANEOUS
  invalidTargetPolicy: SKIP_INVALID_TARGETS
```

Runtime support for `SKIP_INVALID_TARGETS` is partial-resolution semantics: missing,
dead, or otherwise invalid board-entity targets are skipped while valid targets in
the same deterministic target list still produce `DESTROY_ENTITY` mutations.

### 16.6 ReturnToHandAction

```yaml
ReturnToHandAction:
  actionId: string
  actionType: RETURN_TO_HAND
  target: TargetRef
  owner: ORIGINAL_OWNER | CONTROLLER
  handFullPolicy: DESTROY_INSTEAD | FIZZLE_WHOLE_EFFECT | SKIP_ACTION
  resultRef: string | null
```

### 16.7 AddCardToHandAction

```yaml
AddCardToHandAction:
  actionId: string
  actionType: ADD_CARD_TO_HAND
  cardSource: CardSource
  player: SELF | OPPONENT
  handFullPolicy: BURN | SKIP_ACTION | FIZZLE_WHOLE_EFFECT
  resultRef: string | null
```

### 16.8 MoveToZoneAction

```yaml
MoveToZoneAction:
  actionId: string
  actionType: MOVE_TO_ZONE
  target: TargetRef
  destinationZone: SourceZone
  ownershipPolicy: KEEP_OWNER | SET_TO_CONTROLLER
  visibilityPolicy: VisibilityPolicy
  resultRef: string | null
```

### 16.9 SummonAction

```yaml
SummonAction:
  actionId: string
  actionType: SUMMON
  summonSource: SummonSource
  controller: SELF | OPPONENT | SOURCE_CONTROLLER
  placementPolicy: PlacementPolicy
  count: ValueExpression
  boardFullPolicy: SKIP_EXCESS | FIZZLE_WHOLE_EFFECT
  resultRef: string | null
```

### 16.10 DrawCardAction

```yaml
DrawCardAction:
  actionId: string
  actionType: DRAW_CARD
  player: SELF | OPPONENT
  count: ValueExpression
  deckEmptyPolicy: FATIGUE | SKIP_ACTION | FIZZLE_WHOLE_EFFECT
  resultRef: string | null
```

### 16.11 DiscoverAction

```yaml
DiscoverAction:
  actionId: string
  actionType: DISCOVER
  player: SELF
  poolRef: PoolRef
  offerCount: integer
  choicePolicy: PLAYER_CHOICE
  addToZone: HAND | DECK | SET_ASIDE
  poolEmptyPolicy: SKIP_ACTION | FAIL_VALIDATION
  resultRef: string | null
```

### 16.12 TakeExtraTurnAction

```yaml
TakeExtraTurnAction:
  actionId: string
  actionType: TAKE_EXTRA_TURN
  player: SELF | OPPONENT
  oncePerGame: boolean
  currentTurnEndsNormally: boolean
  extraTurnStartPolicy: AFTER_CURRENT_TURN
  turnSchedulerPolicy: TurnSchedulerPolicy
  commandButtonReplacement: string | null
  modifierDurationInteractionPolicy: DurationInteractionPolicy
```

Current deterministic runtime support is limited to:

- `currentTurnEndsNormally: true`
- `extraTurnStartPolicy: AFTER_CURRENT_TURN`
- `turnSchedulerPolicy: QUEUE_AFTER_CURRENT_TURN`
- `commandButtonReplacement: null`
- `modifierDurationInteractionPolicy: EXPIRE_CURRENT_TURN_ONLY`

Other extra-turn policy variants must be rejected by generator semantic validation before publication.

### 16.13 ApplyAuraAction runtime support

Current deterministic runtime support is limited to board-source aura families that lower to the canonical `REFRESH_AURAS` mutation lifecycle. Supported aura lifecycles are:

- `PERMANENT`;
- `WHILE_SOURCE_ACTIVE`.

Supported aura modifier families are:

- `BOARD_ATTACK_MODIFIER` / `PERMANENT` or `WHILE_SOURCE_ACTIVE`;
- `BOARD_HEALTH_MODIFIER` / `PERMANENT` or `WHILE_SOURCE_ACTIVE`;
- `BOARD_KEYWORD_MODIFIER` / `PERMANENT` or `WHILE_SOURCE_ACTIVE`.

For `WHILE_SOURCE_ACTIVE`, the source must be represented by a board entity. Aura refresh recomputes active board sources and clears the aura when the source is no longer on board. Cross-zone aura retention and non-board source lifecycles must be rejected by generator semantic validation until executable support exists.

### 16.14 ReplaceEventAction runtime support

Current deterministic runtime support is limited to:

- `eventType: DAMAGE_WOULD_BE_DEALT`;
- `replacementPolicy: PREVENT_DAMAGE`;
- `duration: NEXT_DAMAGE`.

The action lowers to the same mutation-backed damage-prevention modifier consumed by the replacement window before `APPLY_DAMAGE`. Other event replacement families must be rejected by generator semantic validation until executable support exists.

---

## 17. Action result references

```text
LAST_RETURNED_CARD
LAST_ADDED_CARD
LAST_SUMMONED_ENTITY
LAST_DESTROYED_ENTITY
LAST_DAMAGED_ENTITY
EVENT_SOURCE
EVENT_TARGET
PREVIOUS_TARGET
ACTION_RESULT:<actionId>
```

Example:

```yaml
actions:
  - actionId: a1
    actionType: RETURN_TO_HAND
    target: PLAYER_CHOSEN_TARGET
    owner: ORIGINAL_OWNER
    handFullPolicy: SKIP_ACTION
    resultRef: returnedCard

  - actionId: a2
    actionType: APPLY_MODIFIER
    target: ACTION_RESULT:a1
    modifier:
      modifierType: COST_MODIFIER
      value:
        kind: CONSTANT
        value: -2
      duration:
        kind: WHILE_IN_HAND
```

---

## 18. ResolutionPolicy

```yaml
ResolutionPolicy:
  mode: ResolutionMode
  maxIterations: integer | null
  maxEventCount: integer | null
  deathProcessingPolicy: DeathProcessingPolicy
  auraRefreshPolicy: AuraRefreshPolicy
```

Resolution modes:

```text
SEQUENTIAL_RESOLUTION
SIMULTANEOUS_RESOLUTION
BATCH_RESOLUTION
RANDOM_SELECTION
REPEAT_UNTIL
FOR_EACH
CONDITIONAL_BRANCH
PARTIAL_RESOLUTION
EVENT_CHAIN_LIMITED_RESOLUTION
```

---

## 19. FizzlePolicy

```yaml
FizzlePolicy:
  targetInvalid: FizzleBehavior
  poolEmpty: FizzleBehavior
  boardFull: FizzleBehavior
  handFull: FizzleBehavior
  sourceMissing: FizzleBehavior
  historyEmpty: FizzleBehavior
```

Fizzle behaviors:

```text
SKIP_ACTION
SKIP_INVALID_TARGETS
RESOLVE_PARTIAL
FIZZLE_WHOLE_EFFECT
RETRY_RANDOM_TARGET
FAIL_VALIDATION
```

---

## 20. ModifierDefinition

```yaml
ModifierDefinition:
  modifierId: string | null
  modifierType: ModifierType
  source: SourceRef
  owner: PlayerRef
  scope: ModifierScope
  filter: EntityQuery | CardQuery | null
  value: ValueExpression | null
  duration: DurationPolicy
  priority: integer
  stackingPolicy: StackingPolicy
  expirationPolicy: ExpirationPolicy
  crossZonePolicy: CrossZonePolicy
  replayRepresentation: ReplayModifierSnapshot
```

Modifier types:

```text
STAT_MODIFIER
STAT_OVERRIDE_MODIFIER
SET_BASE_STATS_MODIFIER
COST_MODIFIER
KEYWORD_MODIFIER
TRIGGER_MODIFIER
RULE_MODIFIER
ZONE_MODIFIER
TARGETING_MODIFIER
DAMAGE_MODIFIER
CARD_ORIGIN_MODIFIER
```

---

## 21. PoolDefinition

```yaml
PoolDefinition:
  poolId: string
  poolType: PoolType
  query: PoolQuery
  formatRestriction: string | null
  maxCandidateCount: integer | null
  emptyPoolPolicy: FizzleBehavior
```

Pool types:

```text
FIXED_POOL
CATALOG_QUERY_POOL
FORMAT_POOL
CLASS_POOL
TRIBE_POOL
SPELL_SCHOOL_POOL
HISTORY_POOL
TOKEN_POOL
GENERATED_POOL
CHOICE_TREE_POOL
ADVISOR_CANDIDATE_POOL
```

---

## 22. HistoryQuery

```yaml
HistoryQuery:
  eventTypes: EventType[]
  owner: SELF | OPPONENT | ANY
  controllerAtEventTime: SELF | OPPONENT | ANY
  cardTypes: CardType[]
  entityTypes: EntityType[]
  tags: string[]
  origins: CardOrigin[]
  timeScope: THIS_GAME | THIS_TURN | LAST_N_TURNS
  maxResults: integer | null
  ordering: ORIGINAL_ORDER | REVERSE_ORDER | RANDOM_ORDER
```

---

## 23. GeneratedCardSnapshot

Generated cards must be represented as immutable snapshots.

```yaml
GeneratedCardSnapshot:
  generatedCardId: string
  sourceCardId: string
  sourceEffectId: string
  recipeId: string | null
  choices: ChoiceRecord[]
  compiledCardDefinition: CardDefinition
  generatedText: CardTextGeneration
  catalogSnapshotId: string
  rngRollRefs: string[]
```

Generated cards cannot call LLM during match.

---

## 24. CardTextGeneration

Player-facing text must be derived from DSL or validated against DSL.

```yaml
CardTextGeneration:
  text: string
  generationMode: DERIVED_FROM_DSL | LLM_DRAFT_VALIDATED | MANUAL
  textTemplateId: string | null
  referencedEffectIds: string[]
  validationStatus: TEXT_MATCHES_DSL | TEXT_NEEDS_REVIEW | TEXT_INVALID
```

Validation rules:

- Text cannot describe mechanics absent from DSL.
- Text cannot omit mandatory target/random/fizzle semantics when player-facing clarity requires them.
- LLM-written text is flavor-level until validated against DSL.
- Engine executes DSL, not text.

---

## 25. StateHash / CanonicalSerialization

Replay validation requires canonical serialization.

```yaml
StateHashPolicy:
  algorithm: string
  canonicalSerializationVersion: string
  includeHiddenZones: boolean
  includeRngState: boolean
  includeAuraState: boolean
  includeModifierState: boolean
  includeHistoryState: boolean
```

Initial requirement:

```text
Every replay validation step must be able to compare canonical state hash.
```

Final details belong in `replay_spec.md`.

---

## 26. Minimal examples

### 26.1 Vanilla minion

```yaml
staticData:
  name: River Crocolisk-like
  cardType: MINION_CARD
  manaCost: 2
entity:
  minion:
    attack: 2
    health: 3
    tribes: [BEAST]
    baseKeywords: []
    canAttack: true
effects: []
```

### 26.2 Battlecry damage

```yaml
effects:
  - effectId: e1
    effectKind: SIMPLE_PLAY_EFFECT
    sourceZones: [HAND]
    trigger:
      eventType: CARD_PLAYED
      eventFilter:
        card: SELF
    timingPolicy:
      phase: BATTLECRY_PHASE
      priority: 0
      orderingPolicy: NORMAL
    targetRequirement:
      required: true
      selectionMode: PLAYER_CHOSEN
      candidates:
        controller: ANY
        zones: [BOARD, HERO_ZONE]
        entityTypes: [MINION, HERO]
        aliveOnly: true
    actions:
      - actionId: a1
        actionType: DEAL_DAMAGE
        amount:
          kind: CONSTANT
          value: 3
        target: PLAYER_CHOSEN_TARGET
        damageSchool: null
        lifesteal: false
        preventable: true
    resolutionPolicy:
      mode: SEQUENTIAL_RESOLUTION
    fizzlePolicy:
      targetInvalid: FIZZLE_WHOLE_EFFECT
```

### 26.3 Brawl-like batch destroy

```yaml
effects:
  - effectId: e1
    effectKind: BATCH_EFFECT
    sourceZones: [HAND]
    timingPolicy:
      phase: ON_PLAY
    actions:
      - actionId: a1
        actionType: RANDOM_SELECT
        from:
          controller: ANY
          zones: [BOARD]
          entityTypes: [MINION]
          aliveOnly: true
        count:
          mode: EXACT
          value: 1
        resultRef: survivor
        rngPolicy:
          stream: CARD_EFFECT
        emptyPolicy: SKIP_ACTION

      - actionId: a2
        actionType: BATCH_DESTROY
        targets: ALL_MINIONS_EXCEPT(ACTION_RESULT:a1)
        deathProcessingPolicy: SIMULTANEOUS
        invalidTargetPolicy: SKIP_INVALID_TARGETS
    resolutionPolicy:
      mode: BATCH_RESOLUTION
      deathProcessingPolicy: SIMULTANEOUS
```

---

## 27. Semantic validation checklist

A generated card is invalid if:

- action shape does not match its discriminator;
- effectKind is disabled by `RulesetProfile`;
- actionType is disabled by `RulesetProfile`;
- sourceZones are incompatible with effectKind;
- timing phase is incompatible with trigger;
- target uses `CardType` where `EntityType` is required;
- secret lacks consumePolicy or revealCondition;
- quest lacks progress tracker;
- modifier lacks duration;
- random effect lacks rngPolicy;
- pool can resolve to empty and emptyPoolPolicy is missing;
- repeated effect lacks max cap;
- replayable effect lacks replayPolicy;
- generated card lacks `GeneratedCardSnapshot`;
- player-facing card text contradicts DSL;
- deprecated mechanic is used for a new card;
- card exceeds `RulesetProfile` limits.

---

## 28. Open questions

1. Final JSON Schema discriminator style.
2. Exact event taxonomy.
3. Exact state hash algorithm.
4. Final card text derivation model.
5. MVP choice: weapons or locations.
6. Required subset of `ValueExpression` for MVP.
