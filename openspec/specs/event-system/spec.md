# event-system Specification

## Purpose

Define deterministic runtime event queue requirements while preserving command replay as the source of truth and keeping presentation events outside gameplay processing.

## Requirements

### Requirement: Runtime event queue drains deterministically

The engine SHALL provide a runtime-only event queue that drains queued gameplay events in deterministic order.

#### Scenario: Root events drain in stable FIFO order

- GIVEN root gameplay events are enqueued in a specific order
- WHEN the queue is drained
- THEN the events are returned in the same order
- AND assigned event sequence numbers are strictly increasing.

#### Scenario: Child events drain by canonical order

- GIVEN child gameplay events share the same parent event sequence
- AND each child has canonical ordering keys
- WHEN the children are enqueued together
- THEN they are drained by canonical ordering keys
- AND ties preserve the input order.

### Requirement: Runtime event queue enforces limits

The runtime event queue SHALL enforce configured event depth and event count limits before accepting events.

#### Scenario: Queue rejects event count overflow

- GIVEN a runtime event queue has a maximum event count
- WHEN enqueuing another event would exceed that count
- THEN enqueue fails before the event is accepted.

#### Scenario: Queue rejects event depth overflow

- GIVEN a runtime event queue has a maximum event depth
- WHEN enqueuing an event deeper than the configured limit
- THEN enqueue fails before the event is accepted.

### Requirement: Presentation events cannot enter gameplay event queue

Presentation-only events SHALL NOT be accepted into the gameplay runtime event queue.

#### Scenario: Presentation event is rejected

- GIVEN a queued event category is `PRESENTATION`
- WHEN code attempts to enqueue it into the gameplay runtime queue
- THEN enqueue fails
- AND no queued gameplay event is produced.

### Requirement: Event queue is not a replay source

The runtime event queue SHALL NOT replace the canonical command log as replay source and SHALL NOT mutate gameplay state by itself.

#### Scenario: Queue drains without changing gameplay state

- GIVEN gameplay state exists
- AND gameplay events are queued and drained
- THEN gameplay state remains unchanged
- AND command replay continues to depend on canonical command payloads rather than queued events.

### Requirement: Committed event factory maps applied mutations

The engine SHALL provide a deterministic event factory that maps an `AppliedMutationBatch` into committed gameplay event records after state mutation has succeeded.

#### Scenario: Applied mutation becomes committed event

- GIVEN an applied mutation batch contains a supported gameplay mutation
- WHEN committed events are created from the applied batch
- THEN the factory returns a committed event for the mutation
- AND the event carries mutation batch ref, mutation refs, command sequence, phase, source refs, target refs, pre-state hash, and post-state hash.

#### Scenario: Event factory preserves applied mutation order

- GIVEN an applied mutation batch contains multiple applied mutations
- WHEN committed events are created from the applied batch
- THEN event sequence numbers are assigned in applied mutation order.

### Requirement: Event factory rejects unmapped gameplay mutations

The committed event factory SHALL fail before producing incomplete committed events for mutation types without explicit event mapping.

#### Scenario: Unmapped mutation type is rejected

- GIVEN an applied mutation batch contains a mutation type without an event mapping
- WHEN committed events are created from the applied batch
- THEN event creation fails
- AND no partial committed event list is returned.

### Requirement: Event factory is not replay source

Committed event creation SHALL remain derived output and SHALL NOT replace canonical command payload replay.

#### Scenario: Command replay remains payload-driven

- GIVEN committed events can be derived from applied mutation batches
- WHEN command replay runs
- THEN replay still executes canonical command payloads
- AND does not require persisted committed event records as input.

### Requirement: Application batch execution composes mutation, committed event creation, and runtime queue

The application layer SHALL provide a deterministic mutation batch execution pipeline that applies a proposed mutation batch, creates committed events from the applied batch, and processes those committed events through the runtime event queue.

#### Scenario: Batch execution returns state, applied batch, committed events, and processed events

- GIVEN a proposed mutation batch is valid for the current gameplay state
- WHEN the application batch executor runs
- THEN it returns the next gameplay state
- AND the applied mutation batch
- AND committed event records derived from the applied batch
- AND runtime processed events drained from the event queue.

#### Scenario: Committed event and queue event sequences align

- GIVEN a batch executor is called with a first event sequence
- WHEN committed events are created and queued
- THEN committed event sequence numbers match processed event sequence numbers.

### Requirement: Application batch execution preserves replay boundary

The application batch execution pipeline SHALL NOT replace command-log replay as the replay source.

#### Scenario: Command replay remains independent

- GIVEN application batch execution can produce committed events
- WHEN command-log replay runs
- THEN replay still uses canonical command payloads
- AND does not require application batch execution output as input.

### Requirement: Basic gameplay mutations map to committed events

The committed event factory SHALL map bootstrap draw card, summon entity, apply damage, and heal entity mutations to committed gameplay events after mutation application succeeds.

#### Scenario: Basic mutation creates committed event

- GIVEN an applied mutation batch contains a supported basic gameplay mutation
- WHEN committed events are created
- THEN the factory returns a committed event carrying the mutation ref, command sequence, source refs, target refs, pre-state hash, and post-state hash.

### Requirement: Play card command derived events remain mutation-derived output

The bootstrap `PLAY_CARD` command path SHALL rely on committed events derived from applied movement and summon mutations and SHALL NOT treat committed events as replay input.

#### Scenario: Play card mutations create committed movement and summon events

- GIVEN a bootstrap `PLAY_CARD` command applies card movement and summon mutations
- WHEN committed events are created from the applied batch
- THEN the derived events include card moved zone and minion summoned event records.

### Requirement: History store consumes committed gameplay events

The engine SHALL provide a deterministic history store projection that consumes committed gameplay event records only.

#### Scenario: History records committed gameplay facts in event order

- GIVEN committed gameplay event records are supplied out of order
- WHEN history projection runs
- THEN history records are stored in ascending event sequence order
- AND each record preserves event type, event sequence, command sequence, player reference, source refs, target refs, and payload entries.

#### Scenario: History excludes presentation records

- GIVEN a presentation-category event record is supplied to history projection
- WHEN history projection runs
- THEN no gameplay history record is created for that presentation event.

### Requirement: Trigger resolver matches committed events deterministically

The engine SHALL provide a deterministic trigger resolver that matches registered trigger definitions against committed gameplay event records without executing trigger actions.

#### Scenario: Matching triggers are ordered canonically

- GIVEN committed gameplay events and registered trigger definitions are supplied in arbitrary order
- WHEN trigger resolution runs
- THEN trigger matches are returned ordered by source event sequence and canonical trigger ordering keys
- AND the resolver does not mutate gameplay state.

#### Scenario: Presentation records do not trigger gameplay

- GIVEN a presentation-category event record and a registered trigger definition
- WHEN trigger resolution runs
- THEN no trigger match is produced for the presentation event.

#### Scenario: Unsupported trigger filters are rejected explicitly

- GIVEN a registered trigger definition contains an event filter that the bootstrap resolver cannot evaluate
- WHEN trigger resolution runs
- THEN the result is rejected with a deterministic unsupported-filter diagnostic.

### Requirement: Replacement resolver consumes proposed events before mutation

The engine SHALL expose a replacement/prevention resolver boundary that consumes proposed event records before final mutation application.

#### Scenario: Bootstrap replacement resolver returns unchanged batch

- GIVEN a proposed mutation batch and proposed event records
- WHEN bootstrap replacement resolution runs without registered replacement effects
- THEN the resolver returns the original proposed mutation batch as the final batch
- AND records deterministic unchanged decisions ordered by proposed event sequence.

#### Scenario: Replacement resolver enforces deterministic limits

- GIVEN proposed event records exceed the configured replacement event limit
- WHEN replacement resolution runs
- THEN the result is rejected before final mutation application.
