# generator Specification

## Purpose

Define deterministic generator-side boundaries for constrained card DSL candidates before publication or gameplay use.

## Requirements

### Requirement: Generation context excludes unavailable mechanics

`Game.Generator` SHALL build a generation context from a resolved ruleset profile and mechanic status records that includes only mechanics enabled by the profile, not disabled by the profile, not disabled by format state, and approved for ruleset use.

#### Scenario: Disabled mechanic is hidden from context

- GIVEN a resolved profile enables `BATTLECRY` and disables `SMART_OFFER`
- AND both mechanic records exist
- WHEN a generation context is built
- THEN `BATTLECRY` is included
- AND `SMART_OFFER` is excluded.

### Requirement: Candidate validation rejects mechanics outside context

`Game.Generator` SHALL reject a card candidate that declares an effect mechanic tag not present in the generation context.

#### Scenario: Immature mechanic is rejected

- GIVEN a generation context does not include `SMART_OFFER` because its status is below `APPROVED_FOR_RULESET`
- WHEN a card candidate has an effect tagged `SMART_OFFER`
- THEN validation fails with a mechanic maturity diagnostic.

### Requirement: Candidate validation enforces profile allowlists

`Game.Generator` SHALL reject card candidates using card types, effect kinds, source zones, phases, or action types outside the resolved profile allowlists.

#### Scenario: Disallowed effect kind is rejected

- GIVEN a resolved profile allows `SIMPLE_PLAY_EFFECT`
- WHEN a card candidate uses `BATCH_EFFECT`
- THEN validation fails with an effect-kind diagnostic.

### Requirement: Candidate validation enforces profile limits

`Game.Generator` SHALL reject card candidates exceeding resolved profile complexity, randomness, effects-per-card, or actions-per-effect limits.

#### Scenario: Too many actions are rejected

- GIVEN a resolved profile allows one action per effect
- WHEN a card candidate has two actions in one effect
- THEN validation fails with a limit diagnostic.

### Requirement: Generator does not depend on gameplay runtime

`Game.Generator` SHALL depend on `Game.Shared` contracts only and SHALL NOT reference `Game.Domain`.

#### Scenario: Generator project avoids domain dependency

- GIVEN the generator project file is inspected
- WHEN project references are listed
- THEN only `Game.Shared` is referenced.

### Requirement: Validation pipeline reports ordered stage results

`Game.Generator` SHALL expose a generated card validation pipeline that runs schema, semantic, rule, and static-analysis stages in deterministic order and returns a report for each stage.

#### Scenario: Invalid candidate records every stage

- GIVEN a generated card candidate has a schema error and a rule error
- WHEN the validation pipeline runs
- THEN the report contains schema, semantic, rule, and static-analysis stage reports in that order
- AND the final decision is rejected.

### Requirement: Validation report is generation-log ready

`Game.Generator` SHALL include generation id, pipeline version, ruleset profile id, ruleset profile hash, final decision, and ordered stage reports in the validation pipeline report.

#### Scenario: Report pins profile identity

- GIVEN a generation request uses profile `ruleset_mvp_core_v1` with hash `resolved-hash`
- WHEN validation completes
- THEN the report carries both the profile id and resolved hash.

### Requirement: Schema stage rejects structurally incomplete candidates

The schema validation stage SHALL reject candidates missing required identifiers and version fields needed to review and replay generated cards.

#### Scenario: Missing schema version is rejected

- GIVEN a candidate has an empty schema version
- WHEN schema validation runs
- THEN the schema stage emits an error diagnostic.

### Requirement: Semantic stage rejects internally inconsistent candidates

The semantic validation stage SHALL reject candidates with duplicate effect ids, duplicate action ids within an effect, or value expressions that are structurally incomplete.

#### Scenario: Duplicate action ids are rejected

- GIVEN a candidate has two actions with the same action id in one effect
- WHEN semantic validation runs
- THEN the semantic stage emits an error diagnostic.

### Requirement: Static-analysis stage flags review risks

The static-analysis stage SHALL emit review diagnostics for candidates that exceed warning thresholds without automatically rejecting valid candidates.

#### Scenario: High complexity emits warning

- GIVEN a candidate is within the hard profile limit but near the maximum complexity score
- WHEN static analysis runs
- THEN the static-analysis stage emits a warning diagnostic
- AND the pipeline can still accept the candidate if no error diagnostics exist.

### Requirement: Generation audit entry preserves validation report identity

`Game.Generator` SHALL expose an immutable generation audit log entry that carries generation id, pipeline version, context version, ruleset profile id, ruleset profile hash, validation final decision, and validation stage summaries.

#### Scenario: Validation report is converted to audit entry

- GIVEN a validation report for generation `generation-001`
- WHEN an audit log entry is built
- THEN the entry carries the same generation id, pipeline version, profile id/hash, final decision, and stage diagnostics.

### Requirement: Generation audit entry is deterministic

`Game.Generator` SHALL NOT call system time, external services, or random sources while building a generation audit entry.

#### Scenario: Caller supplies creation timestamp

- GIVEN a caller supplies `createdAt`
- WHEN an audit log entry is built
- THEN the entry uses that timestamp exactly.

### Requirement: Generation audit store is append-only

`Game.Generator` SHALL expose a bootstrap append-only generation audit log store that rejects duplicate generation ids and never mutates existing entries.

#### Scenario: Duplicate generation id is rejected

- GIVEN a store already contains `generation-001`
- WHEN another entry with `generation-001` is appended
- THEN the append result is rejected
- AND the original entry remains unchanged.

### Requirement: Generation context applies intent narrowing

`Game.Generator` SHALL allow a generation intent to narrow resolved profile card types, rarities, classes, tribes, keywords, mechanics, actions, effect kinds, source zones, phases, and pools without widening profile permissions.

#### Scenario: Intent cannot allow profile-disabled primitive

- GIVEN a resolved profile allows `MINION_CARD`
- AND an intent requests `SPELL_CARD`
- WHEN the generation context is built
- THEN `SPELL_CARD` is not included in allowed card types.

### Requirement: Generation context exposes whitelist manifests

`Game.Generator` SHALL expose deterministic whitelist manifests for the allowed profile and intent dimensions used by generation.

#### Scenario: Whitelist values are sorted

- GIVEN allowed mechanics are provided in non-sorted order
- WHEN the generation context is built
- THEN the mechanic whitelist manifest values are sorted ordinally.

### Requirement: Candidate validation enforces whitelist dimensions

`Game.Generator` SHALL reject generated candidates using rarity, class tag, tribe, or keyword values outside the generation context.

#### Scenario: Disallowed class tag is rejected

- GIVEN the generation context allows class tag `mage`
- WHEN a candidate uses class tag `rogue`
- THEN validation fails with a class whitelist diagnostic.

### Requirement: Skeleton generation is constrained by context whitelists

`Game.Generator` SHALL reject skeleton requests that use card type, rarity, class tag, tribe, or keyword values outside the `GenerationContext`.

#### Scenario: Disallowed rarity is rejected

- GIVEN a generation context allows rarity `RARE`
- WHEN a skeleton request uses rarity `LEGENDARY`
- THEN skeleton generation fails with a whitelist diagnostic.

### Requirement: Skeleton generation produces behavior-free candidates

`Game.Generator` SHALL compile accepted skeletons into `CardDefinition` candidates with static data, entity data, metadata, and empty effects.

#### Scenario: Minion skeleton compiles to minion candidate

- GIVEN a valid minion skeleton request
- WHEN the skeleton is compiled
- THEN the candidate contains minion entity data
- AND the candidate effects list is empty.

### Requirement: Skeleton generation is deterministic

`Game.Generator` SHALL NOT call system time, RNG, external catalogs, model APIs, or gameplay runtime services while building skeletons.

#### Scenario: Caller supplies authoring timestamp

- GIVEN a skeleton request includes `createdAt`
- WHEN the skeleton is compiled
- THEN the candidate authoring metadata uses that timestamp exactly.

### Requirement: Generator expands constrained on-play effects

`Game.Generator` SHALL expose a deterministic effect expansion step that adds allowed DSL `EffectDefinition` data to a generated candidate without calling gameplay runtime services, model APIs, RNG, or external catalogs.

#### Scenario: Complex on-play effect is expanded and validated

- GIVEN a behavior-free skeleton candidate and a generation context allowing `SIMPLE_PLAY_EFFECT`, `ON_PLAY`, `HAND`, `DEAL_DAMAGE`, and `DRAW_CARD`
- WHEN the generator expands a complex on-play effect containing fixed damage and fixed draw actions
- THEN the resulting candidate contains the generated effect
- AND the candidate is validated against the same generation context limits and allowlists.

### Requirement: Validated generated cards publish as immutable card versions

The generator publication bootstrap SHALL publish only candidates whose validation pipeline report is accepted.

#### Scenario: Accepted candidate is published

- GIVEN a generated card validation request and accepted validation report with matching generation id
- WHEN publication is requested with a card version id and catalog snapshot id
- THEN the publisher SHALL create a `CardVersion` pinned to the request candidate, pipeline version, ruleset profile id, and ruleset profile hash.

#### Scenario: Rejected candidate is not published

- GIVEN a generated card validation report with a rejected final decision
- WHEN publication is requested
- THEN the publisher SHALL reject publication and SHALL NOT produce a card version or catalog snapshot.

### Requirement: Catalog snapshots pin ruleset profile identity

A catalog snapshot produced by generator publication SHALL store the ruleset profile id and resolved profile hash from the validation report.

#### Scenario: Snapshot is created

- GIVEN a successful publication
- WHEN the catalog snapshot is inspected
- THEN it SHALL contain the same ruleset profile id and hash as the validation report.

### Requirement: Catalog snapshots are queryable through application boundary

The application layer SHALL expose a bootstrap catalog snapshot store and query service that can retrieve snapshots by id and retrieve the latest stored snapshot.

#### Scenario: Latest snapshot is retrieved deterministically

- GIVEN multiple catalog snapshots stored in application order
- WHEN the latest snapshot is requested
- THEN the query service SHALL return the most recently stored snapshot.

#### Scenario: Duplicate snapshot id is rejected

- GIVEN a snapshot id already exists in the store
- WHEN another snapshot with the same id is stored
- THEN the store SHALL reject the duplicate and preserve the existing snapshot.

### Requirement: Catalog API is read-only during bootstrap

The API SHALL expose read-only catalog snapshot endpoints for bootstrap discovery and SHALL NOT expose a public publish endpoint in this change.

#### Scenario: Current catalog snapshot is requested

- GIVEN a catalog snapshot exists in the application store
- WHEN `/catalog/snapshots/current` is requested
- THEN the API SHALL return the latest snapshot.

### Requirement: Catalog snapshots are checked against engine capabilities

The application layer SHALL validate every card version in a catalog snapshot against the current engine capability registry before the snapshot is considered compatible.

#### Scenario: Unsupported card version is rejected

- GIVEN a catalog snapshot containing a card version whose card definition uses an unsupported primitive
- WHEN catalog compatibility validation runs
- THEN the validator SHALL reject the snapshot and identify the unsupported card version.

### Requirement: Published generated cards compose with match and replay bootstrap

The repository SHALL include a deterministic integration smoke path proving an accepted generated card can be published into a catalog snapshot, stored through the application catalog boundary, used by valid decks, accepted by match readiness/start, replayed through the canonical command log path, and referenced by derived presentation timeline output.

#### Scenario: Generated card reaches deterministic match, replay, and presentation flow

- GIVEN a generated card candidate accepted by the validation pipeline
- WHEN the card is published, stored in a catalog snapshot, used by two decks, and a match is started
- THEN match readiness and match start succeed using the published catalog snapshot
- AND a canonical `PLAY_CARD` command log verifies against matching initial and final state hashes
- AND presentation timeline generation resolves a cue for a committed event referencing the published card version without changing the gameplay hash.
