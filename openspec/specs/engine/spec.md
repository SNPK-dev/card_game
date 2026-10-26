# engine Specification

## Purpose

Define the authoritative engine state requirements for deterministic gameplay state, zone invariants, and mutation-driven state movement.

## Requirements

### Requirement: Authoritative state carries players and zones

The engine SHALL represent each player with deterministic card zones and entity zones as part of authoritative gameplay state.

#### Scenario: Player zones are part of gameplay state hash

- GIVEN two states with the same revision and counters
- AND their card zones differ
- WHEN gameplay state hashes are computed
- THEN the hashes differ.

### Requirement: Card instances exist in exactly one card zone

Each card instance SHALL exist in at most one card zone for a player, and movement between card zones SHALL remove it from the previous zone before adding it to the destination zone.

#### Scenario: Moving a card preserves unique zone membership

- GIVEN a player has a card instance in deck
- WHEN a `MOVE_CARD_ZONE` mutation moves it to hand
- THEN the card instance is no longer in deck
- AND the card instance is in hand exactly once.

### Requirement: Card zone movement uses mutation pipeline

Card zone movement SHALL be applied by a state mutation handler through `StateMutator`.

#### Scenario: Move card zone mutation records post-state hash

- GIVEN a proposed mutation batch containing `MOVE_CARD_ZONE`
- WHEN `StateMutator` applies the batch
- THEN the resulting state has the card in the destination zone
- AND the applied batch includes the post-state hash.

### Requirement: Action executor registry controls action execution

The engine SHALL dispatch supported action execution through exactly one deterministic action executor per action type.

#### Scenario: Duplicate action executors are rejected

- GIVEN an action executor registry is created with duplicate action types
- WHEN registry construction runs
- THEN construction fails before any command can execute.

#### Scenario: Unsupported action type is rejected

- GIVEN an action executor registry does not contain a requested action type
- WHEN command execution resolves that action type
- THEN execution fails before any state mutation is applied.

### Requirement: Basic action executors produce mutation batches

The engine SHALL provide deterministic bootstrap action executors for draw card, summon entity, deal damage, and heal entity actions that return `ProposedMutationBatch` values without directly mutating gameplay state.

#### Scenario: Basic action executor does not mutate state

- GIVEN gameplay state with a player deck and board entity
- WHEN a basic action executor runs
- THEN the input state remains unchanged
- AND the executor returns a proposed mutation batch with the current pre-state hash.

### Requirement: Basic gameplay mutations apply through state mutator

The engine SHALL apply draw card, summon entity, deal damage, and heal entity state changes only through registered mutation handlers invoked by `StateMutator`.

#### Scenario: Draw card moves a card from deck to hand

- GIVEN a player has a card in deck
- WHEN a `DRAW_CARD` mutation is applied
- THEN the card is removed from deck
- AND the card is added to hand.

#### Scenario: Summon entity adds an entity to board

- GIVEN a player has no board entity with an entity id
- WHEN a `SUMMON_ENTITY` mutation is applied
- THEN the entity appears on that player's board exactly once.

#### Scenario: Damage and heal update entity damage

- GIVEN a player controls an entity
- WHEN an `APPLY_DAMAGE` mutation is applied and then a `HEAL_ENTITY` mutation is applied
- THEN the entity damage changes deterministically through mutation handlers.

### Requirement: Play card command creates mutation-backed minion play batch

The engine SHALL provide a bootstrap `PLAY_CARD` command path for minion cards that creates a proposed mutation batch containing card zone movement and entity summon mutations without directly mutating gameplay state.

#### Scenario: Play card command moves hand card and summons entity

- GIVEN a player has a minion card instance in hand
- WHEN the bootstrap `PLAY_CARD` command is executed
- THEN the card instance is removed from hand through a card-zone mutation
- AND a corresponding entity instance is summoned to that player's board through a summon mutation.

#### Scenario: Play card command rejects missing hand card before mutation

- GIVEN a player does not have the requested card instance in hand
- WHEN the bootstrap `PLAY_CARD` command is executed
- THEN execution fails before gameplay state changes.

### Requirement: Play card command can be replayed from canonical refs

The bootstrap `PLAY_CARD` command path SHALL support canonical replay materialization from deterministic command payload refs without reading derived events.

#### Scenario: Canonical play card replay uses mutation pipeline

- GIVEN an initial gameplay state with a minion card instance in hand
- AND a canonical `PLAY_CARD` payload references that card instance and the entity instance to summon
- WHEN the command log is replayed
- THEN the final state has the card moved out of hand
- AND the summoned entity is on the player's board
- AND the state change is applied by `StateMutator`.

### Requirement: Authoritative state carries bootstrap turn metadata

The engine SHALL represent bootstrap active player, turn number, and phase as authoritative gameplay state included in gameplay state hashing.

#### Scenario: Turn metadata changes gameplay state hash

- GIVEN two gameplay states with the same players, zones, counters, and revision
- AND their active player, turn number, or phase differs
- WHEN gameplay state hashes are computed
- THEN the hashes differ.

### Requirement: End turn command creates mutation-backed turn advancement

The engine SHALL provide a bootstrap `END_TURN` command path that creates a proposed mutation batch containing an `ADVANCE_TURN` mutation without directly mutating gameplay state.

#### Scenario: End turn command advances active player through mutation

- GIVEN gameplay state with two players and an active player
- WHEN the bootstrap `END_TURN` command is executed
- THEN the active player advances through an `ADVANCE_TURN` mutation
- AND the input state is unchanged before `StateMutator` applies the batch.

### Requirement: Bootstrap turn advancement is deterministic

Bootstrap turn advancement SHALL order players by ordinal player id and increment turn number only when advancement wraps from the last player id to the first.

#### Scenario: End turn wraps and increments turn number

- GIVEN active player is the last player id in deterministic order
- WHEN an `ADVANCE_TURN` mutation is applied
- THEN active player becomes the first player id
- AND turn number increments by one
- AND phase becomes `TURN_START`.

### Requirement: Target resolver validates deterministic target choices

The engine SHALL provide a domain target resolver that validates target requirements against authoritative gameplay state without mutating that state.

#### Scenario: Player-chosen board target is validated

- GIVEN a target requirement that allows minions on board
- AND a player-chosen target references a matching board entity
- WHEN target resolution runs
- THEN the target is accepted
- AND gameplay state is unchanged.

#### Scenario: Invalid chosen target is rejected before action execution

- GIVEN a target requirement that allows minions on board
- AND a player-chosen target references a missing entity
- WHEN target resolution runs
- THEN the target is rejected
- AND no mutation batch is produced by the resolver.

### Requirement: Target resolver orders candidates deterministically

The target resolver SHALL return matching target candidates in deterministic order before selecting auto-valid or all-matching targets.

#### Scenario: All matching targets are stable

- GIVEN multiple matching entities controlled by different players
- WHEN all-matching target resolution runs
- THEN the resolved targets are ordered deterministically by player id, target kind, and target id.

### Requirement: Random targeting waits for RNG roll logging

The bootstrap target resolver SHALL reject random target selection until deterministic RNG roll logging is available.

#### Scenario: Random target mode is unsupported

- GIVEN a target requirement uses random selection
- WHEN target resolution runs
- THEN the resolver returns an unsupported selection mode error.

### Requirement: Condition evaluator checks state-visible DSL conditions

The engine SHALL provide a domain condition evaluator that evaluates supported DSL conditions against authoritative gameplay state without mutating that state.

#### Scenario: Player health condition is evaluated

- GIVEN a condition checks that a player's hero health is less than or equal to a threshold
- WHEN condition evaluation runs
- THEN the result reports whether the condition is satisfied
- AND gameplay state is unchanged.

### Requirement: Condition evaluator reuses target resolution

The condition evaluator SHALL evaluate `HAS_VALID_TARGET` by delegating target legality to the target resolver.

#### Scenario: Valid target condition is satisfied

- GIVEN a `HAS_VALID_TARGET` condition and a target requirement with at least one legal candidate
- WHEN condition evaluation runs
- THEN the condition is satisfied.

### Requirement: Unsupported conditions are rejected explicitly

The bootstrap condition evaluator SHALL reject condition kinds that require unavailable runtime systems instead of silently treating them as true or false.

#### Scenario: Unsupported condition kind is rejected

- GIVEN a condition kind with no bootstrap evaluator
- WHEN condition evaluation runs
- THEN the result is rejected with an unsupported condition diagnostic.

### Requirement: Pool resolver resolves fixed pools deterministically

The engine SHALL provide a domain pool resolver that resolves explicit fixed pools into deterministic candidate lists without mutating gameplay state.

#### Scenario: Fixed pool candidates are ordered deterministically

- GIVEN a fixed pool contains candidates in non-sorted order
- WHEN pool resolution runs
- THEN resolved candidates are ordered by candidate kind and candidate id.

### Requirement: Pool resolver rejects unsupported pool behavior

The bootstrap pool resolver SHALL reject unsupported pool types and random ordering instead of silently choosing candidates.

#### Scenario: Random pool ordering is rejected

- GIVEN a fixed pool requests random ordering
- WHEN pool resolution runs
- THEN resolution is rejected with an unsupported ordering diagnostic.

### Requirement: Pool-not-empty condition uses pool resolver

The condition evaluator SHALL evaluate `POOL_NOT_EMPTY` through the domain pool resolver.

#### Scenario: Fixed pool not empty condition is satisfied

- GIVEN a `POOL_NOT_EMPTY` condition references a fixed pool with at least one candidate
- WHEN condition evaluation runs
- THEN the condition is satisfied.

### Requirement: Effect action compiler creates mutation batches

The engine SHALL provide a deterministic domain compiler that turns supported DSL effect actions into `ProposedMutationBatch` values without mutating authoritative gameplay state.

#### Scenario: Fixed damage action is compiled

- GIVEN an effect containing a fixed `DEAL_DAMAGE` action and resolved entity target
- WHEN effect action compilation runs
- THEN the compiler SHALL return an accepted proposed mutation batch containing an `APPLY_DAMAGE` mutation.
- AND the input gameplay state SHALL remain unchanged.

### Requirement: Effect conditions gate action compilation

The effect action compiler SHALL evaluate effect conditions before compiling actions.

#### Scenario: Unsatisfied condition fizzles actions

- GIVEN an effect with an unsatisfied supported condition
- WHEN effect action compilation runs
- THEN the compiler SHALL return an accepted empty mutation batch.

### Requirement: Unsupported effect actions are rejected explicitly

The effect action compiler SHALL reject unsupported DSL action definitions and unsupported value expressions instead of silently ignoring them.

#### Scenario: Unsupported action definition is rejected

- GIVEN an effect action whose runtime support is not available in the bootstrap compiler
- WHEN effect action compilation runs
- THEN the result SHALL be rejected with an unsupported-action diagnostic.

### Requirement: Draw action compilation is deterministic

The effect action compiler SHALL compile a fixed single-card `DRAW_CARD` action by selecting the first card in the selected player's authoritative deck order.

#### Scenario: Fixed draw action is compiled

- GIVEN an effect containing a fixed count-one `DRAW_CARD` action
- WHEN effect action compilation runs for a player with at least one card in deck
- THEN the compiler SHALL return a proposed mutation batch containing a `DRAW_CARD` mutation for the first deck card.

### Requirement: History and trigger bootstrap preserve mutation pipeline

The engine SHALL expose history, trigger, and replacement bootstrap boundaries that read proposed or committed events but do not mutate gameplay state or execute actions directly.

#### Scenario: Trigger matching does not bypass mutation pipeline

- GIVEN a committed event matches a registered trigger definition
- WHEN trigger resolution runs
- THEN the resolver returns a trigger match descriptor
- AND it does not create or apply state mutations, execute action definitions, or alter gameplay state.

#### Scenario: Replacement resolution does not bypass mutation pipeline

- GIVEN proposed event records and a proposed mutation batch
- WHEN bootstrap replacement resolution runs without registered replacement effects
- THEN the resolver returns the original proposed mutation batch as final
- AND it does not apply state mutations or alter gameplay state.
