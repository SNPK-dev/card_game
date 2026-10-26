# application-api-infrastructure Specification

## Purpose

Define application and API orchestration requirements that sit outside deterministic gameplay execution while preparing catalog, replay, and match-start workflows.

## Requirements

### Requirement: Runtime engine capability discovery

`Game.Application` SHALL expose an application-owned provider for the current bootstrap `EngineCapabilityRegistry`.

#### Scenario: Application returns deterministic capability snapshot

- GIVEN the application capability provider is constructed
- WHEN the current engine capability registry is requested
- THEN it returns fixed engine, schema, primitive, ruleset, action, effect, mechanic, phase, zone, policy, asset manifest, and metadata identifiers
- AND the returned identifiers are deterministically ordered
- AND no runtime timestamp, random value, external service call, or gameplay state read is required.

### Requirement: API exposes runtime capability discovery

`Game.Api` SHALL expose a read-only endpoint for the current engine capability registry.

#### Scenario: Capability endpoint returns application registry

- GIVEN the API is running
- WHEN a caller requests the engine capability endpoint
- THEN the response contains the registry returned by the application capability provider
- AND the endpoint does not start a match, execute gameplay commands, mutate gameplay state, or calculate replay hashes.

### Requirement: Engine capability compatibility preflight

`Game.Application` SHALL expose a deterministic compatibility validator that compares an engine capability registry with resolved ruleset profile and card definition requirements before future catalog, deck, match-start, or replay gates consume them.

#### Scenario: Unsupported capability is rejected before gameplay execution

- GIVEN a resolved ruleset profile or card definition requires an unsupported action, effect kind, mechanic, phase, source zone, schema version, primitive registry version, ruleset profile version, resolution policy, or fizzle policy
- WHEN the application compatibility validator runs
- THEN it returns a rejected result with deterministic diagnostic codes and paths
- AND it does not execute actions, mutate gameplay state, read external services, or calculate gameplay state hashes.

### Requirement: Presentation cues resolve from committed events outside gameplay

The application layer SHALL resolve presentation cues from committed gameplay event records and asset manifests without mutating gameplay state, executing gameplay commands, or producing replay source data.

#### Scenario: Card played event resolves an on-play cue

- GIVEN a committed `CARD_PLAYED` event references a card with an asset manifest containing an `ON_PLAY` audio cue
- WHEN presentation cue resolution runs
- THEN the application returns a `PresentationCue` for that asset cue
- AND the cue references the source event sequence and asset manifest id.

#### Scenario: Missing presentation assets do not fail gameplay

- GIVEN a committed gameplay event has no matching asset manifest or no matching cue type
- WHEN presentation cue resolution runs
- THEN the application returns no cue for that event
- AND it does not reject, mutate, or replay gameplay.

### Requirement: Presentation cue ordering is deterministic

The application layer SHALL produce presentation cues in deterministic order independent of input asset manifest ordering.

#### Scenario: Input manifests are unordered

- GIVEN matching asset manifests are supplied in different orders
- WHEN presentation cue resolution runs for the same committed event
- THEN the returned cue sequence is identical.

### Requirement: Presentation timeline generation remains derived output

The application layer SHALL build a deterministic presentation timeline from committed gameplay event records and asset manifests without using it as replay source data.

#### Scenario: Timeline derives cue and silence entries deterministically

- GIVEN committed gameplay events and asset manifests in arbitrary order
- WHEN presentation timeline generation runs
- THEN the application returns timeline entries ordered by source event sequence, asset manifest id, and cue id
- AND missing cues produce deterministic silence/fallback entries when fallback allows silence
- AND the operation does not mutate gameplay state, execute gameplay commands, or call external services.

### Requirement: API exposes presentation timeline generation as read-only output

`Game.Api` SHALL expose a read-only endpoint that accepts committed gameplay events and asset manifests and returns a derived presentation timeline.

#### Scenario: Timeline endpoint returns derived presentation output

- GIVEN a caller posts committed event records and asset manifests
- WHEN the presentation timeline endpoint is invoked
- THEN the API returns a presentation timeline and presentation timeline hash
- AND the endpoint does not start a match, execute gameplay commands, mutate gameplay state, or calculate gameplay state hashes.

### Requirement: Command envelopes project deterministic string refs

The application command envelope SHALL project gameplay object string refs into deterministic command payload refs while keeping transport and request metadata outside replay.

#### Scenario: Envelope projects play card refs

- GIVEN an application command envelope carries `cardInstanceId` and `entityInstanceId` refs
- WHEN it is projected to a deterministic command payload
- THEN the payload carries those refs in deterministic order
- AND the payload does not contain command id, timestamps, correlation id, auth session id, transport metadata, or raw request references.

### Requirement: Deterministic bot simulation runner

The application layer SHALL expose a bootstrap bot simulation runner that executes bot-selected commands deterministically from a match state and verifies the resulting canonical command log against the final gameplay state hash.

#### Scenario: Bots play generated decks through canonical commands

- GIVEN two ready decks built from generated and published card versions
- AND a match start state is created from those decks
- WHEN deterministic bots run with a fixed opening hand size and command limit
- THEN the simulation setup draws opening cards through the mutation pipeline
- AND bot decisions produce a contiguous canonical command log containing only replayable gameplay commands
- AND replay verification from setup state reaches the same final gameplay state hash.

### Requirement: Bot simulation replays generated on-play effects

The application bot simulation and command replay path SHALL execute supported published card on-play effects from the supplied catalog snapshot deterministically through domain effect compilation and the mutation pipeline.

#### Scenario: Complex generated on-play effects replay with matching hash

- GIVEN two generated decks contain a published minion with a `SIMPLE_PLAY_EFFECT` from `HAND` at `ON_PLAY`
- AND that effect contains fixed `DEAL_DAMAGE` and fixed `DRAW_CARD` actions accepted by the generation context and engine capability registry
- WHEN deterministic bots play a match and replay verifies the resulting canonical command log
- THEN the on-play effect damage and draw are applied through mutation batches
- AND replay reaches the same final gameplay state hash.

### Requirement: Durable artifact stores preserve production artifacts

The application SHALL support durable store adapters for catalog snapshots, decks, resolved ruleset profiles, asset manifests, and production acceptance reports without adding storage concerns to `Game.Domain`.

#### Scenario: Stored artifacts survive store re-instantiation

- GIVEN a durable store root path is configured
- AND catalog, deck, profile, asset manifest, and production acceptance report artifacts are stored
- WHEN new store instances are created for the same root path
- THEN each artifact can be queried by id and latest insertion order
- AND duplicate ids are rejected without replacing the original artifact.

### Requirement: API can use durable stores when configured

The API SHALL keep in-memory stores as the default local behavior and use durable file-backed stores when `Storage:RootPath` is configured.

#### Scenario: Durable API configuration selects file-backed stores

- GIVEN the API starts with a non-empty `Storage:RootPath`
- WHEN store services are resolved
- THEN catalog, deck, resolved profile, asset manifest, and production acceptance report stores are backed by durable adapters
- AND gameplay execution, replay hashes, and presentation hashes are not changed by the storage mode.

### Requirement: Acceptance reports are queryable as audit evidence

The API SHALL expose read-only acceptance report queries for current and by-id report retrieval.

#### Scenario: Acceptance report endpoint returns stored evidence

- GIVEN a production acceptance report has been stored
- WHEN a caller requests the current report or a report by id
- THEN the API returns the stored acceptance report
- AND the endpoint does not publish cards, start matches, execute commands, mutate gameplay state, or call external providers.

### Requirement: Production artifacts are publishable through API storage boundaries

The API SHALL expose storage-backed publish endpoints for catalog snapshots, decks, resolved ruleset profiles, asset manifests, and production acceptance reports without adding storage concerns to `Game.Domain`.

#### Scenario: Publish endpoint stores one artifact per id

- GIVEN an artifact store is registered
- WHEN a caller publishes a new artifact with a unique id
- THEN the API stores the artifact and returns success
- AND when the same artifact id is published again
- THEN the API returns conflict and preserves the original stored artifact.

### Requirement: Replay evidence is exposed as audit data

The API SHALL expose stored match and replay verification evidence from production acceptance reports as read-only audit data.

#### Scenario: Replay evidence endpoint returns stored match evidence

- GIVEN a production acceptance report has been stored with replay-verified match evidence
- WHEN a caller requests match evidence for that report id
- THEN the API returns the stored match evidence
- AND the endpoint does not replay commands, execute gameplay, mutate gameplay state, or call external providers.
