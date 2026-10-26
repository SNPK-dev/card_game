# MechanicStatus Specification

## Controlled event-driven CCG engine

**Version:** v0.2-draft  
**Status:** Draft  
**Owner:** Game Systems Architect + Product Owner  
**Depends on:** `architecture_contract.md`, `ruleset_profile_spec.md`, `primitive_registry.md`, `extension_rfc_process.md`, `coverage_matrix.md`

---

## 0. Changelog v0.2

Changes from v0.1:

- Removed `ENABLED_IN_FORMAT` and `DISABLED_IN_FORMAT` from `MechanicStatus`.
- Moved format-specific enablement to `FormatMechanicState` in `RulesetProfile`.
- Changed `REJECTED -> DISCOVERED` transition into new RFC/proposal flow.
- Added clearer maturity vs format availability distinction.
- Added simplified MVP runtime status mapping.
- Added consistency rules with `RulesetProfile`.

---

## 1. Purpose

`MechanicStatus` tracks the maturity of a mechanic across architecture, specification, implementation, validation, simulation, replay and balance review.

It prevents the team from confusing different meanings of “supported”.

```text
Architecture can express it.
DSL has a spec for it.
Engine implemented it.
Validators enforce it.
Simulation covered it.
Replay verifies it.
Balance reviewed it.
Ruleset approved it.
```

Format enablement is not stored here. It belongs to `RulesetProfile`.

---

## 2. Core distinction

### 2.1 MechanicStatus

`MechanicStatus` answers:

```text
How mature is this mechanic?
```

### 2.2 FormatMechanicState

`FormatMechanicState` answers:

```text
Is this mechanic enabled in this specific ruleset/profile/format?
```

Example:

```text
MAGNETIC_PLAY_MODE:
  MechanicStatus: SPEC_DRAFTED
  FormatMechanicState in MVP: DISABLED

BATTLECRY:
  MechanicStatus: APPROVED_FOR_RULESET
  FormatMechanicState in MVP: ENABLED
```

---

## 3. Mechanic identity

Every mechanic has a stable id.

```yaml
MechanicDefinition:
  mechanicId: string
  displayName: string
  description: string
  tier: MechanicTier
  status: MechanicStatus
  owner: string
  relatedEffectKinds: EffectKind[]
  relatedActions: ActionType[]
  relatedKeywords: Keyword[]
  relatedSpecs: string[]
  coverageExamples: string[]
  riskFlags: string[]
  createdAt: datetime
  updatedAt: datetime
```

Example ids:

```text
BATTLECRY
DEATHRATTLE
BASIC_SECRET
QUESTLINE
MAGNETIC_PLAY_MODE
INFUSE_PROGRESS_TRACKER
TAKE_EXTRA_TURN_ACTION
SMART_OFFER
```

---

## 4. MechanicStatus enum

```text
DISCOVERED
ARCHITECTURE_SUPPORTED
SPEC_DRAFTED
SPEC_APPROVED
IMPLEMENTED
VALIDATED
SIMULATION_VERIFIED
REPLAY_VERIFIED
BALANCE_REVIEWED
APPROVED_FOR_RULESET
DEPRECATED
REJECTED
```

Removed from v0.2:

```text
ENABLED_IN_FORMAT
DISABLED_IN_FORMAT
```

These are represented by `FormatMechanicState` in `RulesetProfile`.

---

## 5. Status definitions

### 5.1 DISCOVERED

The mechanic has been identified as potentially useful.

Required artifacts:

- mechanicId;
- short description;
- motivation;
- at least one card/example pattern.

Cannot be used by generation or engine execution.

### 5.2 ARCHITECTURE_SUPPORTED

The architecture can conceptually express the mechanic.

Required artifacts:

- architecture notes;
- affected subsystems;
- rough DSL shape;
- rough replay implications;
- coverage example.

Still missing:

- exact fields;
- final validators;
- implementation;
- tests.

### 5.3 SPEC_DRAFTED

A draft specification exists.

Required artifacts:

- dedicated spec section or child spec;
- draft DSL model;
- draft validation rules;
- draft simulation scenarios;
- draft replay representation;
- open questions.

### 5.4 SPEC_APPROVED

The spec has passed review.

Required artifacts:

- approved DSL model;
- approved action/effect/keyword mapping;
- validator implications;
- replay implications;
- simulation scenarios;
- edge cases;
- coverage matrix entry;
- owner approval.

### 5.5 IMPLEMENTED

Engine/DSL code exists.

Required artifacts:

- data model implementation;
- resolver implementation;
- schema support;
- basic unit tests;
- feature flag or profile gating.

Not enough for production.

### 5.6 VALIDATED

Validators exist and pass.

Required artifacts:

- schema validation;
- semantic validation;
- rule validation;
- static analysis where needed;
- invalid examples rejected;
- disabled profile usage rejected.

### 5.7 SIMULATION_VERIFIED

The mechanic has passed simulation scenarios.

Required artifacts:

- isolated effect scenario;
- generated card scenario;
- edge-case scenarios;
- random legal bot scenario if applicable;
- failure mode tests;
- event count tracking.

### 5.8 REPLAY_VERIFIED

The mechanic is deterministic under replay.

Required artifacts:

- command log replay test;
- deterministic RNG log test;
- state hash consistency;
- catalog snapshot pinning;
- rulesetProfileHash pinning;
- old replay compatibility if relevant.

### 5.9 BALANCE_REVIEWED

The mechanic has balance risk classification.

Required artifacts:

- risk flags;
- BalanceReport sample;
- simulation winrate delta where relevant;
- complexity score impact;
- randomness swing score impact;
- recommended usage limits.

Simulation-derived conclusions require:

- minimum sample size;
- confidence interval;
- seed set;
- baseline deck set.

### 5.10 APPROVED_FOR_RULESET

The mechanic may be used by one or more production rulesets.

Required artifacts:

- Product Owner approval;
- Game Systems Architect approval;
- RulesetProfile compatibility check;
- publication policy decision;
- coverage tests passing.

This does not mean enabled in every format.

### 5.11 DEPRECATED

The mechanic is no longer allowed for new cards but may be required for old cards and replay.

Required artifacts:

- deprecation reason;
- last allowed ruleset profile;
- replay support policy;
- migration note if applicable;
- replacement mechanic if available.

### 5.12 REJECTED

The mechanic is rejected as a product pattern.

Reasons may include:

- requires hardcoded engine exception;
- impossible to replay deterministically;
- not balanceable;
- too complex for players;
- too broad for LLM generation;
- creates unbounded loops;
- violates Card Design Constitution.

Rejected mechanics must not appear in GenerationContext.

---

## 6. Simplified MVP runtime statuses

The full status model may be too heavy for early runtime implementation.

MVP runtime may use:

```text
DRAFT
SPEC_APPROVED
IMPLEMENTED
VALIDATED
APPROVED_FOR_RULESET
DEPRECATED
REJECTED
```

Mapping:

```text
DISCOVERED / ARCHITECTURE_SUPPORTED / SPEC_DRAFTED -> DRAFT
SPEC_APPROVED -> SPEC_APPROVED
IMPLEMENTED -> IMPLEMENTED
VALIDATED / SIMULATION_VERIFIED / REPLAY_VERIFIED -> VALIDATED
BALANCE_REVIEWED / APPROVED_FOR_RULESET -> APPROVED_FOR_RULESET
DEPRECATED -> DEPRECATED
REJECTED -> REJECTED
```

Format enablement remains in `RulesetProfile`.

---

## 7. Allowed transitions

```text
DISCOVERED
  -> ARCHITECTURE_SUPPORTED
  -> REJECTED

ARCHITECTURE_SUPPORTED
  -> SPEC_DRAFTED
  -> REJECTED

SPEC_DRAFTED
  -> SPEC_APPROVED
  -> REJECTED

SPEC_APPROVED
  -> IMPLEMENTED
  -> REJECTED

IMPLEMENTED
  -> VALIDATED
  -> SPEC_DRAFTED        # if implementation exposed spec gap

VALIDATED
  -> SIMULATION_VERIFIED

SIMULATION_VERIFIED
  -> REPLAY_VERIFIED

REPLAY_VERIFIED
  -> BALANCE_REVIEWED

BALANCE_REVIEWED
  -> APPROVED_FOR_RULESET
  -> REJECTED

APPROVED_FOR_RULESET
  -> DEPRECATED

DEPRECATED
  -> REJECTED            # if replay support is intentionally removed in major version

REJECTED
  -> terminal
```

A rejected mechanic is not reopened by changing status back to `DISCOVERED`.

Reconsideration requires a new RFC/proposal id:

```text
OLD_MECHANIC: REJECTED
NEW_MECHANIC_PROPOSAL_V2: DISCOVERED
```

---

## 8. MechanicStatusRecord

```yaml
MechanicStatusRecord:
  mechanicId: string
  currentStatus: MechanicStatus
  tier: MechanicTier
  owner: string
  statusHistory: StatusTransition[]
  approvedRulesetProfileIds: string[]
  deprecatedSince: string | null
  rejectionReason: string | null
  linkedSpecs: string[]
  linkedTests: string[]
  linkedCoverageExamples: string[]
  riskFlags: string[]
```

### 8.1 StatusTransition

```yaml
StatusTransition:
  from: MechanicStatus | null
  to: MechanicStatus
  changedAt: datetime
  changedBy: string
  reason: string
  evidenceRefs: string[]
```

Status history is append-only.

---

## 9. Required evidence by status

| Target status | Required evidence |
|---|---|
| ARCHITECTURE_SUPPORTED | Architecture note, affected subsystems, rough DSL concept |
| SPEC_DRAFTED | Draft spec, draft examples, open questions |
| SPEC_APPROVED | Approved spec, validator implications, simulation/replay notes |
| IMPLEMENTED | Code implementation, schema support, unit tests |
| VALIDATED | Schema/semantic/rule/static validation tests |
| SIMULATION_VERIFIED | Simulation scenarios passed |
| REPLAY_VERIFIED | Replay hash consistency and deterministic RNG tests |
| BALANCE_REVIEWED | BalanceReport and risk flags |
| APPROVED_FOR_RULESET | Product + architecture approval |
| DEPRECATED | Replay compatibility policy |
| REJECTED | Rejection reason and alternatives |

---

## 10. Interaction with RulesetProfile

`RulesetProfile` may enable a mechanic for production only if:

```text
MechanicStatus == APPROVED_FOR_RULESET
```

Experimental profiles may expose lower maturity mechanics only if:

- profile status is not `ACTIVE` production;
- publication policy forbids public production publication;
- mechanic is at least `REPLAY_VERIFIED` or explicitly allowlisted for internal simulation;
- GenerationContext is marked experimental.

Format enablement is computed from:

```text
MechanicStatusRecord.currentStatus
+
RulesetProfile.formatMechanicStates
+
RulesetProfile.enabledMechanics/disabledMechanics
+
PublicationPolicy
```

---

## 11. Interaction with GenerationContext

GenerationContext Builder must:

1. Load active `ResolvedRulesetProfile`.
2. Load `MechanicStatusRegistry`.
3. Remove mechanics not enabled by profile.
4. Remove mechanics with insufficient maturity status.
5. Remove deprecated mechanics unless legacy replay mode.
6. Remove rejected mechanics always.
7. Generate schemas only for allowed mechanics/actions/effects.

Candidate using a disabled mechanic fails with:

```yaml
errorCode: MECHANIC_DISABLED_BY_RULESET
mechanicId: string
rulesetProfileId: string
```

Candidate using an immature mechanic fails with:

```yaml
errorCode: MECHANIC_NOT_APPROVED_FOR_RULESET
mechanicId: string
currentStatus: MechanicStatus
requiredStatus: APPROVED_FOR_RULESET
```

Candidate using an unknown mechanic fails with:

```yaml
errorCode: UNKNOWN_MECHANIC
mechanicId: string
```

---

## 12. Example records

### 12.1 Triggered board effect

```yaml
mechanicId: TRIGGERED_BOARD_EFFECT
currentStatus: APPROVED_FOR_RULESET
tier: TIER_0
owner: Game Systems Architect
approvedRulesetProfileIds:
  - ruleset_mvp_core_v1
linkedSpecs:
  - dsl_reference.md
  - phase_timing_registry.md
linkedCoverageExamples:
  - Ragnaros-like
riskFlags: []
```

### 12.2 Zephrys-like SmartOffer

```yaml
mechanicId: SMART_OFFER
currentStatus: ARCHITECTURE_SUPPORTED
tier: TIER_2
owner: Game Systems Architect
approvedRulesetProfileIds: []
linkedSpecs:
  - advisor_engine_spec.md
linkedCoverageExamples:
  - Zephrys-like
riskFlags:
  - HIGH_COMPLEXITY
  - REQUIRES_REGRESSION_SUITE
  - NOT_MVP
```

### 12.3 Magnetic

```yaml
mechanicId: MAGNETIC_PLAY_MODE
currentStatus: SPEC_DRAFTED
tier: TIER_1
owner: DSL Owner
approvedRulesetProfileIds: []
linkedSpecs:
  - magnetic_spec.md
linkedCoverageExamples:
  - Zilliax-like
riskFlags:
  - ENTITY_IDENTITY_COMPLEXITY
  - COPY_SEMANTICS_REQUIRED
  - SILENCE_SEMANTICS_REQUIRED
```

### 12.4 Deprecated mechanic

```yaml
mechanicId: OLD_RANDOM_SPELL_FIESTA
currentStatus: DEPRECATED
tier: TIER_2
deprecatedSince: 0.4.0
rejectionReason: null
riskFlags:
  - HAS_EXTREME_RANDOM_SWING
  - HAS_EXCESSIVE_EVENT_COUNT
```

---

## 13. CI and repository checks

Recommended CI checks:

- every mechanic in RulesetProfile exists in MechanicStatusRegistry;
- no RulesetProfile enables REJECTED mechanic;
- no production profile enables mechanic below APPROVED_FOR_RULESET;
- every enabled mechanic has linked spec;
- every enabled mechanic has linked tests;
- every deprecated mechanic has replay policy;
- generated schemas exclude disabled mechanics;
- coverage matrix includes every Tier 0 and Tier 1 mechanic;
- `FormatMechanicState` does not contradict `enabledMechanics`/`disabledMechanics`.

---

## 14. Governance responsibilities

```text
Product Owner
  Approves production ruleset use and rejection of product patterns.

Game Systems Architect
  Approves architecture support and engine capability.

DSL Owner
  Approves DSL model and schema representation.

Validation Owner
  Approves validator coverage.

Simulation Owner
  Approves simulation scenarios.

Replay Owner
  Approves replay determinism.

Balance Owner
  Approves risk flags and balance review.

QA / Test Owner
  Approves coverage matrix and regression tests.
```

---

## 15. Anti-patterns

Bad:

```text
MAGNETIC: supported
```

Good:

```text
MAGNETIC_PLAY_MODE:
  Architecture Coverage: supported
  MechanicStatus: SPEC_DRAFTED
  Production Readiness: not ready
  Enabled in MVP: false
```

Bad:

```text
Engine supports it, so generator can use it.
```

Good:

```text
Generator can use it only if RulesetProfile enables it and MechanicStatus allows it.
```

---

## 16. Test requirements

### 16.1 Unit tests

- invalid transition rejected;
- rejected mechanic cannot be enabled;
- deprecated mechanic allowed only for legacy replay;
- RulesetProfile cannot enable unknown mechanic;
- MVP profile cannot enable mechanic below approved status;
- status history is append-only;
- rejected mechanic cannot transition back to discovered.

### 16.2 Integration tests

- GenerationContext excludes disabled mechanic;
- generated candidate with disabled mechanic fails validation;
- generated candidate with immature mechanic fails validation;
- experimental profile can expose experimental mechanic in non-publication mode;
- old replay succeeds with deprecated mechanic;
- new card generation rejects deprecated mechanic.

---

## 17. Open questions

1. Should status registry be stored in git, database, or both?
2. Should maturity status be environment-specific?
3. What is the minimum evidence for `BALANCE_REVIEWED` in early alpha?
4. Should experimental profile exposure require `REPLAY_VERIFIED`, or can it start at `IMPLEMENTED` for sandbox-only tests?
