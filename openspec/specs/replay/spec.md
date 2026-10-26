# replay Specification

## Purpose

Define the canonical command log and replay guarantees that keep command payloads as the source of truth while derived events remain regenerated output.

## Requirements
### Requirement: Canonical command log replay

The engine SHALL provide a canonical command log model that is the replay source for deterministic bootstrap replay.

#### Scenario: Replay orders commands by sequence

- GIVEN a canonical command log whose payloads are supplied out of insertion order
- WHEN replay runs
- THEN commands execute in ascending `commandSeq` order
- AND the final gameplay state hash matches replay of the same ordered payloads

#### Scenario: Replay rejects invalid command sequence

- GIVEN a command log with duplicate or non-contiguous command sequences
- WHEN the canonical command log is created
- THEN creation fails before replay executes

### Requirement: Debug commands require explicit debug replay flag

Deterministic command payloads SHALL classify commands as player, system, choice, or debug, and replay SHALL reject debug commands unless debug replay is explicitly enabled on the payload.

#### Scenario: Ordinary command payloads default to player category

- GIVEN a deterministic command payload created without a category
- WHEN replay inspects the payload
- THEN the payload is treated as a player command
- AND replay behavior remains unchanged.

#### Scenario: Debug command without replay flag is rejected

- GIVEN a canonical command log containing a debug command payload without `debugReplayFlag`
- WHEN replay runs
- THEN replay fails before executing that command.

#### Scenario: Debug command with replay flag is accepted

- GIVEN a canonical command log containing a debug command payload with `debugReplayFlag`
- WHEN replay runs
- THEN replay may execute the payload through the normal deterministic command execution path.

### Requirement: Command payload validation precedes execution

Replay and command admission SHALL validate deterministic command payloads before command execution mutates gameplay state.

#### Scenario: Invalid replay payload fails before mutation

- GIVEN a canonical command log containing a structurally invalid command payload
- WHEN replay runs
- THEN replay fails before executing that payload
- AND gameplay state remains unchanged by that payload.

#### Scenario: Command validator rejects structural command errors

- GIVEN a deterministic command payload with an invalid command sequence, missing command type, missing required player id, or invalid debug flag policy
- WHEN command validation runs
- THEN validation returns a rejected result with deterministic error codes.

#### Scenario: Envelope admission uses deterministic payload validation

- GIVEN an application command envelope
- WHEN command admission runs
- THEN the envelope is projected to a deterministic payload
- AND the payload is accepted or rejected by the same deterministic command validation contract used by replay.

### Requirement: Derived events are regenerated output

Replay SHALL regenerate derived events from canonical command payload execution and SHALL NOT require a persisted event log as replay input.

#### Scenario: Replay derives events from commands

- GIVEN a canonical command log without persisted events
- WHEN replay runs
- THEN derived events are produced by command execution
- AND replay completion depends on command payloads, not event log input

### Requirement: Command envelope metadata is excluded from replay

The application command envelope SHALL keep transport and request metadata outside deterministic replay payloads.

#### Scenario: Envelope projects only canonical replay fields

- GIVEN two command envelopes with identical deterministic command fields and different metadata
- WHEN each envelope is projected to a deterministic command payload
- THEN the projected payloads contain the same command sequence, player id, command type, command payload, and expected pre-state hash
- AND they do not contain command id, timestamps, correlation id, auth session id, transport metadata, or raw request references.

#### Scenario: Envelope metadata does not affect replay output

- GIVEN two command logs built from envelopes with identical deterministic command fields and different metadata
- WHEN replay runs for both command logs
- THEN the final gameplay state hashes match.

### Requirement: State mutations apply through proposed batches

Gameplay state changes SHALL be applied by `StateMutator` from a `ProposedMutationBatch`, not by direct executor mutation of state.

#### Scenario: Batch application records pre and post state hashes

- GIVEN a proposed mutation batch with a matching `preStateHash`
- WHEN `StateMutator` applies the batch
- THEN it returns the next gameplay state
- AND it returns an applied mutation batch containing the original pre-state hash and calculated post-state hash.

#### Scenario: Proposed batch has no post-state hash

- GIVEN a proposed mutation batch before state application
- WHEN code inspects the proposed batch contract
- THEN no post-state hash field is present.

### Requirement: Command execution uses action executor batches

Deterministic command execution SHALL resolve an action executor, use it to produce a `ProposedMutationBatch`, and apply that batch through `StateMutator`.

#### Scenario: Action executor produces proposed batch without mutating state

- GIVEN a valid command and current gameplay state
- WHEN the command action executor runs
- THEN it returns a proposed mutation batch
- AND the original gameplay state remains unchanged
- AND the proposed batch has a pre-state hash and no post-state hash.

#### Scenario: Command execution applies action batch through state mutator

- GIVEN deterministic command execution has an action executor registry
- WHEN a command executes
- THEN the selected action executor creates the proposed batch
- AND `StateMutator` applies that batch
- AND command execution does not use a direct command-to-state mutation bypass.

#### Scenario: Missing action executor is rejected before mutation

- GIVEN a command maps to an unsupported action type
- WHEN command execution runs
- THEN execution fails before gameplay state changes.

### Requirement: Mutation handler registry controls state mutation types

`StateMutator` SHALL dispatch each supported mutation type through exactly one deterministic mutation handler.

#### Scenario: Unsupported mutation is rejected before state changes

- GIVEN a proposed mutation batch containing an unsupported mutation type
- WHEN `StateMutator` applies the batch
- THEN application fails before returning a changed state.

#### Scenario: Presentation mutation is rejected

- GIVEN a proposed mutation batch containing an audio, visual, animation, or client playback mutation type
- WHEN `StateMutator` applies the batch
- THEN application fails because presentation-only changes are not gameplay state mutations.

### Requirement: Play card command execution remains deterministic

The bootstrap `PLAY_CARD` command path SHALL produce deterministic gameplay state when the same typed command is executed against the same starting state.

#### Scenario: Equivalent play card command yields same state hash

- GIVEN two equal starting states with the same card in hand
- WHEN equivalent bootstrap `PLAY_CARD` commands execute
- THEN the resulting gameplay state hashes match.

### Requirement: End turn command replay remains deterministic

The bootstrap `END_TURN` command path SHALL produce deterministic gameplay state when the same command log is replayed against the same starting state.

#### Scenario: Equivalent end turn logs yield same state hash

- GIVEN two equal starting states with the same players and active player
- WHEN equivalent canonical command logs containing `END_TURN` commands are replayed
- THEN the resulting gameplay state hashes match
- AND replay uses command payloads, not derived events, as input.

### Requirement: Deterministic command payload hash

The engine SHALL provide a deterministic command payload hasher that hashes only canonical replay payload fields.

#### Scenario: Equivalent command payload dictionaries hash equally

- GIVEN two deterministic command payloads with identical fields and differently ordered payload dictionaries
- WHEN command hashing runs
- THEN the command hashes match.

#### Scenario: Equivalent command payload refs hash equally

- GIVEN two deterministic command payloads with identical string refs supplied in different dictionary orders
- WHEN command hashing runs
- THEN the command hashes match.

#### Scenario: Different command payload refs change the hash

- GIVEN two deterministic command payloads that differ only by a string ref value
- WHEN command hashing runs
- THEN the command hashes differ.

### Requirement: Play card command replay materializes string refs

The canonical command payload SHALL carry deterministic string refs needed to replay typed commands whose gameplay object identities are strings.

#### Scenario: Play card payload materializes typed command

- GIVEN a canonical command log containing a `PLAY_CARD` payload with `cardInstanceId` and `entityInstanceId` refs plus fixed attack and health values
- WHEN replay executes the command log
- THEN replay materializes a typed play-card command
- AND the command moves the referenced card from hand and summons the referenced entity through the mutation pipeline.

#### Scenario: Missing play card refs reject before mutation

- GIVEN a canonical command log containing a `PLAY_CARD` payload without a required string ref
- WHEN replay executes the command log
- THEN replay fails before applying a mutation
- AND the initial gameplay state is unchanged.

### Requirement: Regenerated committed event log hash

The engine SHALL provide a deterministic committed event log hasher for regenerated event records used as comparison/debug output, not replay input.

#### Scenario: Equivalent committed event logs hash equally

- GIVEN two committed event logs with equivalent events and differently ordered nested payload/ref collections
- WHEN event log hashing runs
- THEN the event log hashes match.

#### Scenario: Presentation events are excluded from gameplay event log hash

- GIVEN a regenerated committed event log and an otherwise equivalent log with additional presentation-only event records
- WHEN gameplay event log hashing runs
- THEN the hashes match
- AND replay still uses the canonical command log as source.

### Requirement: Presentation timeline hash is separate from gameplay replay

Presentation timeline hashing SHALL be computed over derived presentation timeline data and SHALL NOT affect command replay, gameplay state hash, or gameplay event log hash.

#### Scenario: Presentation timeline hash changes independently

- GIVEN the same committed gameplay event records and two asset manifests with different audio content hashes
- WHEN presentation timeline hashing runs
- THEN the presentation timeline hashes differ
- AND the gameplay replay source remains the canonical command log.

### Requirement: History and trigger outputs remain derived replay output

History projections, replacement decisions, and trigger matches SHALL remain regenerated output from command execution and SHALL NOT become replay source data.

#### Scenario: Replay remains command-log driven

- GIVEN history records, replacement decisions, or trigger matches can be derived from proposed or committed events
- WHEN command replay runs
- THEN replay still depends on canonical command payloads
- AND it does not require persisted history records, replacement decisions, or trigger match records as input.

### Requirement: Canonical artifact snapshot tests

Canonical serialization for replay-related bootstrap artifacts SHALL be protected by reviewable snapshot tests while the reflection-based prototype serializer remains in use.

#### Scenario: Canonical snapshots detect contract drift

- GIVEN representative deterministic command payload, mutation batch, committed event, catalog snapshot, resolved ruleset profile, and engine capability registry artifacts
- WHEN canonical snapshot tests serialize those artifacts
- THEN the produced canonical JSON matches the reviewed snapshots
- AND a field, enum, or ordering change requires an explicit test update.
