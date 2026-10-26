# shared-contracts Specification

## Purpose

Define shared data contracts used by generator, validators, and engine compatibility boundaries without adding runtime gameplay behavior.

## Requirements

### Requirement: Shared mechanic maturity contracts

`Game.Shared` SHALL expose data-only mechanic maturity contracts representing mechanic identity, status, tier, status transitions, and status registry records.

#### Scenario: Mechanic status serializes as a reviewable value

- GIVEN a mechanic status contract uses `APPROVED_FOR_RULESET`
- WHEN it is serialized as JSON
- THEN the status is emitted as a string value
- AND no runtime service is required.

### Requirement: Shared ruleset profile contracts

`Game.Shared` SHALL expose data-only ruleset profile contracts representing editable and resolved profile shapes, format mechanic state, limits, policy references, legacy policy, and metadata.

#### Scenario: Resolved profile pins hash and allowed mechanics

- GIVEN a resolved ruleset profile contract
- WHEN it is created by a caller
- THEN it can carry the source profile id, source version, resolved hash, enabled mechanics, disabled mechanics, allowed actions, allowed effects, limits, and policies
- AND the shared contract does not calculate the hash itself.

### Requirement: Shared governance contracts remain behavior-free

The governance contracts SHALL NOT implement profile resolution, mechanic status transition validation, catalog publication, generation context building, or gameplay execution behavior.

#### Scenario: Shared assembly remains a governance contract assembly

- GIVEN code references the governance contracts
- WHEN the shared tests inspect the contract assembly
- THEN the contracts are records/enums only
- AND they do not require infrastructure, API, generator, or domain runtime services.

### Requirement: Shared card definition contract

`Game.Shared` SHALL expose data-only card DSL contracts for card identity, static card data, entity definitions, effect definitions, metadata, timing, triggers, conditions, and targets.

#### Scenario: Card definition carries catalog and ruleset identity

- GIVEN a shared card definition
- WHEN it is created by a caller
- THEN it can carry schema version, engine version, primitive registry version, ruleset profile id, optional catalog snapshot id, static data, entity data, effects, text, balance metadata, asset metadata, and authoring metadata
- AND the shared contract does not validate or execute the card.

### Requirement: Action definitions are specialized union variants

`Game.Shared` SHALL expose action definitions as a tagged union with concrete action variant shapes, not a generic nullable payload object.

#### Scenario: Deal damage action serializes with an action discriminator

- GIVEN a shared effect containing a deal damage action
- WHEN it is serialized as JSON
- THEN the action emits `actionType` as `DEAL_DAMAGE`
- AND the action carries its specialized fields.

### Requirement: Shared DSL contracts remain behavior-free

The card DSL contracts SHALL NOT implement schema generation, semantic validation, action execution, target resolution, generation context building, or gameplay state mutation.

#### Scenario: Shared assembly remains a DSL contract assembly

- GIVEN code references the card DSL contracts
- WHEN shared contract tests inspect the exported DSL types
- THEN the contracts are records/enums only
- AND they do not require infrastructure, API, generator, or domain runtime services.

### Requirement: Shared engine capability registry contract

`Game.Shared` SHALL expose a data-only engine capability registry contract representing the capabilities a specific engine version declares it can support.

#### Scenario: Capability registry carries supported capability identifiers

- GIVEN a shared engine capability registry
- WHEN it is created by a caller
- THEN it can carry engine version, supported schema versions, supported primitive registry versions, supported ruleset profile versions, supported action types, supported effect kinds, supported mechanics, supported phases, supported source zones, supported resolution policies, supported fizzle policies, supported asset manifest versions, and metadata
- AND the shared contract does not validate compatibility or calculate hashes.

### Requirement: Shared capability contracts remain behavior-free

The engine capability contracts SHALL NOT implement engine compatibility validation, catalog publication checks, ruleset enforcement, canonical hashing, generation context building, or gameplay execution behavior.

#### Scenario: Shared assembly remains a capability contract assembly

- GIVEN code references the capability registry contract
- WHEN shared contract tests inspect the exported capability types
- THEN the contracts are records/enums only
- AND they do not require infrastructure, API, generator, or domain runtime services.

### Requirement: Shared proposed event contract

`Game.Shared` SHALL expose a data-only proposed event contract representing pre-mutation event intentions used by replacement and prevention windows.

#### Scenario: Proposed event serializes as a pre-mutation intention

- GIVEN a shared proposed event uses `DAMAGE_WOULD_BE_DEALT`
- WHEN it is serialized as JSON
- THEN the proposed event type is emitted as a string value
- AND the contract carries command sequence, phase, source refs, target refs, payload entries, replacement window, prevention allowance, and RNG roll refs
- AND no runtime service is required.

### Requirement: Shared committed event contract

`Game.Shared` SHALL expose a data-only committed event contract representing gameplay facts produced after state mutation.

#### Scenario: Committed event carries mutation and state hash refs

- GIVEN a shared committed event uses `ENTITY_DAMAGED`
- WHEN it is created by a caller
- THEN it can carry event sequence, command sequence, phase, source refs, target refs, payload entries, mutation batch refs, mutation refs, RNG roll refs, pre-state hash, and post-state hash
- AND the shared contract does not calculate or verify hashes.

### Requirement: Shared event contracts remain behavior-free

The shared event contracts SHALL NOT implement event queue ordering, event factory mapping, replacement/prevention resolution, trigger resolution, history storage, presentation cue resolution, replay comparison, or gameplay state mutation.

#### Scenario: Shared assembly remains an event contract assembly

- GIVEN code references the shared event contracts
- WHEN shared contract tests inspect the exported event types
- THEN the contracts are records/enums only
- AND they do not require infrastructure, API, generator, application, or domain runtime services.

### Requirement: Shared mutation batch contracts

`Game.Shared` SHALL expose data-only mutation contracts representing state mutation records, proposed mutation batches, applied mutation batches, mutation categories, and reviewable mutation type identifiers.

#### Scenario: Proposed mutation batch carries pre-state hash only

- GIVEN a shared proposed mutation batch contract
- WHEN it is created by a caller
- THEN it can carry batch id, command sequence, optional parent event sequence, phase, source ref, reason, mutations, pre-state hash, optional replacement policy ref, and optional fizzle policy ref
- AND it does not carry a post-state hash.

#### Scenario: Applied mutation batch carries post-state hash

- GIVEN a shared applied mutation batch contract
- WHEN it is created by a caller
- THEN it can carry applied mutations, skipped mutations, pre-state hash, post-state hash, derived event refs, and applied step.

### Requirement: Shared mutation contracts remain behavior-free

The shared mutation contracts SHALL NOT implement state mutation handlers, state mutators, action execution, event creation, canonical hashing, validation, replacement/prevention resolution, or presentation playback behavior.

#### Scenario: Shared mutation namespace remains data-only

- GIVEN code references shared mutation contracts
- WHEN shared contract tests inspect the exported mutation types
- THEN the contracts are records/enums only
- AND they do not require domain, application, API, generator, or infrastructure runtime services.

### Requirement: Shared presentation cue contracts remain data-only

`Game.Shared` SHALL expose presentation cue contracts as data-only records used by application presentation boundaries.

#### Scenario: Presentation cue carries source event and asset refs

- GIVEN a presentation cue is created by an application resolver
- WHEN the shared cue contract is inspected
- THEN it carries cue id, cue type, source event sequence, source card refs, asset manifest id, asset ref, playback policy, visibility policy, and fallback policy
- AND it does not execute playback, mutate gameplay state, or calculate gameplay hashes.

### Requirement: Shared presentation timeline contracts remain data-only

`Game.Shared` SHALL expose presentation timeline contracts as data-only records used by application presentation replay boundaries.

#### Scenario: Presentation timeline carries derived cue entries

- GIVEN application code builds a presentation timeline from committed gameplay events and asset manifests
- WHEN the shared timeline contract is inspected
- THEN it carries a timeline id, presentation catalog hash, source event range, ordered presentation entries, and entry payload data
- AND it does not execute playback, mutate gameplay state, calculate gameplay hashes, or call external services.

### Requirement: Shared contract schema manifest

`Game.Shared` SHALL expose a data-only contract schema manifest that identifies shared contract categories, contract names, namespaces, field names, and optional enum values for review tooling.

#### Scenario: Schema manifest serializes as reviewable data

- GIVEN a shared contract schema manifest with DSL, event, mutation, catalog, ruleset, engine capability, deck, asset, primitive, and schema entries
- WHEN the manifest is serialized
- THEN category values are emitted as strings
- AND field names and enum values are explicit caller-provided data.

#### Scenario: Schema manifest remains behavior-free

- GIVEN code references the schema manifest contracts
- WHEN shared contract tests inspect the schema namespace
- THEN the contracts are records/enums only
- AND they do not generate schemas, validate cards, calculate hashes, resolve rulesets, execute gameplay, or call external services.
