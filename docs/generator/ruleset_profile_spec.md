# RulesetProfile Specification

## Controlled event-driven CCG engine

**Version:** v0.2-draft  
**Status:** Draft  
**Owner:** Product Owner + Game Systems Architect  
**Depends on:** `architecture_contract.md`, `dsl_reference.md`, `mechanic_status_spec.md`, `primitive_registry.md`, `balance_model_spec.md`, `catalog_publication_spec.md`

---

## 0. Changelog v0.2

Changes from v0.1:

- Added `ResolvedRulesetProfile`.
- Added `resolvedHash` for replay/catalog pinning.
- Added `MechanicDependencyGraph`.
- Added profile contradiction validation.
- Clarified that production profiles may only tighten parent limits by default.
- Moved format enablement logic out of `MechanicStatus`.
- Added `FormatMechanicState`.
- Added stricter generation context filtering rules.

---

## 1. Purpose

`RulesetProfile` defines which mechanics, actions, effects, card types and complexity limits are allowed in a specific product ruleset, format or card set.

It solves the central product problem:

```text
Engine can support a mechanic.
Architecture can describe a mechanic.
But a specific format may still forbid it.
```

---

## 2. Core principle

LLM must only see mechanics enabled by the active resolved profile.

```text
ResolvedRulesetProfile
+ Primitive Registry
+ MechanicStatus Registry
+ MechanicDependencyGraph
+ CatalogSnapshot
+ BalancePolicy
+ GenerationIntent
= GenerationContext
```

If a mechanic is disabled by `RulesetProfile`, it must not appear in GenerationContext, generated candidates, schemas, or published cards.

---

## 3. RulesetProfile model

```yaml
RulesetProfile:
  id: string
  name: string
  description: string
  version: string
  status: RulesetProfileStatus

  formatId: string
  cardSetId: string | null
  parentProfileId: string | null

  enabledMechanics: string[]
  disabledMechanics: string[]

  formatMechanicStates: FormatMechanicState[]

  allowedEffectKinds: EffectKind[]
  allowedActions: ActionType[]
  allowedKeywords: Keyword[]
  allowedSourceZones: SourceZone[]
  allowedPhases: Phase[]
  allowedPools: PoolType[]
  allowedCardTypes: CardType[]
  allowedEntityTypes: EntityType[]
  allowedRarities: Rarity[]
  allowedClasses: string[]
  allowedTribes: string[]

  limits: RulesetLimits
  validationPolicy: ValidationPolicyRef
  balancePolicy: BalancePolicyRef
  simulationPolicy: SimulationPolicyRef
  publicationPolicy: PublicationPolicyRef
  legacyPolicy: LegacyPolicy

  metadata: RulesetProfileMetadata
```

---

## 4. ResolvedRulesetProfile

Active catalog snapshots must store or pin the resolved profile, not only parent references.

```yaml
ResolvedRulesetProfile:
  id: string
  sourceProfileId: string
  sourceProfileVersion: string
  resolvedAt: datetime
  resolvedBy: string
  resolvedHash: string

  formatId: string
  cardSetId: string | null

  enabledMechanics: string[]
  disabledMechanics: string[]
  formatMechanicStates: FormatMechanicState[]

  allowedEffectKinds: EffectKind[]
  allowedActions: ActionType[]
  allowedKeywords: Keyword[]
  allowedSourceZones: SourceZone[]
  allowedPhases: Phase[]
  allowedPools: PoolType[]
  allowedCardTypes: CardType[]
  allowedEntityTypes: EntityType[]
  allowedRarities: Rarity[]
  allowedClasses: string[]
  allowedTribes: string[]

  limits: RulesetLimits
  validationPolicy: ValidationPolicyRef
  balancePolicy: BalancePolicyRef
  simulationPolicy: SimulationPolicyRef
  publicationPolicy: PublicationPolicyRef
  legacyPolicy: LegacyPolicy
```

### 4.1 resolvedHash

`resolvedHash` is calculated from canonical serialization of:

- enabled/disabled mechanics;
- format mechanic states;
- allowed effects/actions/keywords/phases/zones/pools;
- limits;
- policies;
- parent resolution result.

Replay and catalog publication must pin `resolvedHash`.

---

## 5. RulesetProfileStatus

```text
DRAFT
REVIEW
APPROVED
ACTIVE
LOCKED
DEPRECATED
ARCHIVED
```

Meaning:

- `DRAFT`: editable, cannot publish cards.
- `REVIEW`: internal tests only.
- `APPROVED`: approved but not active.
- `ACTIVE`: can be used for generation and publication.
- `LOCKED`: immutable profile used by catalog snapshots/replay.
- `DEPRECATED`: no new cards should target it, replay may depend on it.
- `ARCHIVED`: audit only.

---

## 6. FormatMechanicState

Format enablement is profile-level state, not mechanic maturity state.

```yaml
FormatMechanicState:
  mechanicId: string
  rulesetProfileId: string
  state: FormatMechanicStateKind
  reason: string | null
  effectiveFrom: string | null
  effectiveUntil: string | null
```

```text
FormatMechanicStateKind:
  ENABLED
  DISABLED
  LEGACY_ONLY
  EXPERIMENTAL_ONLY
```

Rules:

- `ENABLED`: allowed for new generation/publication if maturity allows.
- `DISABLED`: hidden from GenerationContext.
- `LEGACY_ONLY`: allowed only for old replay/catalog compatibility.
- `EXPERIMENTAL_ONLY`: allowed for internal test catalogs; publication policy decides whether public publication is forbidden.

---

## 7. MechanicDependencyGraph

A profile must satisfy mechanic dependencies.

```yaml
MechanicDependency:
  mechanicId: string
  requiredMechanics: string[]
  requiredActions: ActionType[]
  requiredEffectKinds: EffectKind[]
  requiredKeywords: Keyword[]
  requiredSourceZones: SourceZone[]
  requiredPhases: Phase[]
  requiredPools: PoolType[]
  incompatibleMechanics: string[]
  requiredLimits:
    maxActionsPerEffectAtLeast: integer | null
    maxRepeatCountAtLeast: integer | null
    maxGeneratedCardsPerEffectAtLeast: integer | null
```

### 7.1 Example: BASIC_DISCOVER

```yaml
mechanicId: BASIC_DISCOVER
requiredActions:
  - DISCOVER
requiredEffectKinds:
  - POOL_BASED_EFFECT
requiredPools:
  - FIXED_POOL
  - CATALOG_QUERY_POOL
requiredPhases:
  - ON_PLAY
incompatibleMechanics: []
```

### 7.2 Example: BASIC_SECRET

```yaml
mechanicId: BASIC_SECRET
requiredEffectKinds:
  - SECRET_EFFECT
requiredSourceZones:
  - SECRET_ZONE
requiredPhases:
  - SECRET_WINDOW
requiredActions:
  - PREVENT_DAMAGE
  - DEAL_DAMAGE
```

### 7.3 Example: BATCH_DESTROY

```yaml
mechanicId: BATCH_DESTROY
requiredActions:
  - BATCH_DESTROY
  - RANDOM_SELECT
requiredEffectKinds:
  - BATCH_EFFECT
requiredPhases:
  - ON_PLAY
  - DEATH_PROCESSING
```

---

## 8. RulesetLimits

```yaml
RulesetLimits:
  maxComplexityScore: integer
  maxRandomnessScore: integer
  maxEventCountPerCard: integer
  maxEventCountPerTurn: integer
  maxRepeatCount: integer
  maxGeneratedCardsPerEffect: integer
  maxHistoryQueryScope: HistoryQueryScope
  maxActionsPerEffect: integer
  maxEffectsPerCard: integer
  maxChoiceCount: integer
  maxDiscoverOfferCount: integer
  maxBoardSummonsPerAction: integer
  maxDrawPerAction: integer
  maxManaCostReduction: integer
  maxTemporaryCostReduction: integer
  maxPermanentCostReduction: integer
```

MVP starting limits:

```yaml
maxComplexityScore: 30
maxRandomnessScore: 25
maxEventCountPerCard: 20
maxEventCountPerTurn: 80
maxRepeatCount: 5
maxGeneratedCardsPerEffect: 1
maxHistoryQueryScope: LIMITED
maxActionsPerEffect: 3
maxEffectsPerCard: 2
maxChoiceCount: 3
maxDiscoverOfferCount: 3
maxBoardSummonsPerAction: 3
maxDrawPerAction: 2
maxManaCostReduction: 2
maxTemporaryCostReduction: 3
maxPermanentCostReduction: 1
```

---

## 9. Policies

### 9.1 ValidationPolicyRef

```yaml
ValidationPolicyRef:
  policyId: string
  schemaValidation: boolean
  deserializationValidation: boolean
  semanticValidation: boolean
  ruleValidation: boolean
  staticAnalysis: boolean
  failOnUnknownMechanic: boolean
  failOnDeprecatedMechanic: boolean
  failOnDisabledMechanic: boolean
  allowLegacyDeprecatedMechanics: boolean
```

### 9.2 BalancePolicyRef

```yaml
BalancePolicyRef:
  policyId: string
  requireBalanceReport: boolean
  requireHumanReviewAboveRisk: RiskLevel
  autoRejectRiskFlags: string[]
  maxSimulationWinrateDelta: float
  maxComplexityScore: integer
  maxRandomnessSwingScore: integer
  maxEventCountDelta: float
  minSimulationSampleSize: integer
  confidenceInterval: float
  baselineDeckSetId: string
  seedSetId: string
  outlierHandlingPolicy: string
```

### 9.3 SimulationPolicyRef

```yaml
SimulationPolicyRef:
  policyId: string
  requiredScenarioSets: string[]
  requiredEdgeCases: string[]
  minRandomBotGames: integer
  minBaselineDeckGames: integer
  requireReplayHashConsistency: boolean
  maxTurns: integer
  maxEventCount: integer
```

### 9.4 PublicationPolicyRef

```yaml
PublicationPolicyRef:
  policyId: string
  requireValidationPass: boolean
  requireSimulationPass: boolean
  requireReplayPass: boolean
  requireBalanceReview: boolean
  requireHumanApproval: boolean
  allowAutoPublish: boolean
  allowExperimentalPublication: boolean
  allowedPublicationStatuses: string[]
```

### 9.5 LegacyPolicy

```yaml
LegacyPolicy:
  allowDeprecatedMechanicsForReplay: boolean
  allowDeprecatedMechanicsForNewCards: boolean
  allowDisabledMechanicsForReplay: boolean
  allowDisabledMechanicsForNewCards: boolean
  replayProfilePinningRequired: boolean
```

---

## 10. Profile inheritance

Inheritance rules:

- Child starts with resolved parent profile.
- Child may disable parent mechanics.
- Child may add mechanics only if maturity and policy allow.
- Production child profiles may only tighten parent limits by default.
- Experimental child profiles may loosen limits only with explicit publication policy.
- Active catalog snapshot stores `ResolvedRulesetProfile` or its canonical hash.

---

## 11. Resolution algorithm

```pseudo
function resolveRulesetProfile(profileId): ResolvedRulesetProfile
  profile = load(profileId)

  if profile.parentProfileId != null:
    parent = resolveRulesetProfile(profile.parentProfileId)
    merged = merge(parent, profile)
  else:
    merged = profile

  assertProfileStatusAllowsResolution(merged)
  assertNoUnknownMechanics(merged)
  assertMechanicStatusesAllow(merged)
  assertFormatMechanicStatesAreConsistent(merged)
  assertMechanicDependenciesSatisfied(merged)
  assertNoIncompatibleMechanicsEnabled(merged)
  assertLimitsAreValid(merged)

  resolved = freeze(merged)
  resolved.resolvedHash = hash(canonicalSerialize(resolved))
  return resolved
```

---

## 12. IsMechanicEnabled

```pseudo
function isMechanicEnabled(mechanicId, profile): boolean
  state = profile.formatMechanicStates[mechanicId]

  if state == DISABLED:
    return false

  if state == LEGACY_ONLY and mode != REPLAY:
    return false

  if mechanicId in profile.disabledMechanics:
    return false

  if mechanicId not in profile.enabledMechanics:
    return false

  maturity = MechanicStatusRegistry.get(mechanicId).currentStatus

  if maturity in [DEPRECATED, REJECTED]:
    return false

  return maturity == APPROVED_FOR_RULESET
```

---

## 13. BuildGenerationContext

```pseudo
function buildGenerationContext(intent, rulesetProfileId): GenerationContext
  profile = resolveRulesetProfile(rulesetProfileId)
  registry = loadPrimitiveRegistry(profile.version)
  catalog = loadCatalogSnapshot(intent.catalogSnapshotId)

  allowed = intersect(profile, registry, catalog, intent)
  allowed = removeDisabledMechanics(allowed, profile)
  allowed = removeDeprecatedMechanics(allowed)
  allowed = removeUnapprovedMechanics(allowed)

  return GenerationContext(
    rulesetProfileId = profile.id,
    rulesetProfileHash = profile.resolvedHash,
    allowedMechanics = allowed.mechanics,
    allowedActions = allowed.actions,
    allowedEffectKinds = allowed.effectKinds,
    allowedPools = allowed.pools,
    limits = profile.limits,
    whitelistManifests = buildWhitelists(profile, catalog, intent)
  )
```

---

## 14. Profile contradiction validation

A profile is invalid if:

- `enabledMechanics` includes mechanic missing required action.
- `enabledMechanics` includes mechanic missing required effect kind.
- `enabledMechanics` includes mechanic missing required source zone.
- `enabledMechanics` includes mechanic missing required phase.
- two enabled mechanics are incompatible.
- a mechanic is both enabled and disabled.
- a mechanic is enabled but its `FormatMechanicState` is `DISABLED`.
- a production profile enables a mechanic below `APPROVED_FOR_RULESET`.
- a profile loosens parent limits without permission.
- `allowedActions` contains actions with no supporting mechanic unless explicitly allowed.
- `allowedEffectKinds` contains effect kinds with no supporting mechanic unless explicitly allowed.

---

## 15. Example: MVP RulesetProfile

```yaml
id: ruleset_mvp_core_v1
name: MVP Core Ruleset
version: 0.2.0
status: ACTIVE
formatId: mvp_internal
cardSetId: core_generated_alpha
parentProfileId: null

enabledMechanics:
  - VANILLA_MINION
  - SIMPLE_SPELL
  - BATTLECRY
  - DEATHRATTLE
  - TRIGGERED_BOARD_EFFECT
  - BASIC_SECRET
  - BASIC_QUEST
  - BASIC_AURA
  - BASIC_MODIFIER
  - BASIC_DISCOVER
  - SUMMON_TOKEN
  - FIXED_POOL_SUMMON
  - BASIC_HISTORY_RESURRECT
  - BATCH_DESTROY
  - DETERMINISTIC_RNG

disabledMechanics:
  - QUESTLINE
  - TAKE_EXTRA_TURN
  - SMART_OFFER
  - FULL_CARD_CRAFTING
  - MAGNETIC
  - INFUSE
  - START_OF_GAME
  - REEXECUTABLE_CARD_PLAY_HISTORY
  - PERSISTENT_ENCHANTMENT_CARRY

formatMechanicStates:
  - mechanicId: BATTLECRY
    state: ENABLED
  - mechanicId: QUESTLINE
    state: DISABLED
  - mechanicId: SMART_OFFER
    state: DISABLED

allowedCardTypes:
  - MINION_CARD
  - SPELL_CARD
  - TOKEN_CARD

allowedEntityTypes:
  - MINION
  - HERO
  - CARD_IN_HAND
  - CARD_IN_DECK
  - CARD_IN_GRAVEYARD

allowedEffectKinds:
  - SIMPLE_PLAY_EFFECT
  - TRIGGERED_BOARD_EFFECT
  - DEATHRATTLE_EFFECT
  - SECRET_EFFECT
  - QUEST_EFFECT
  - AURA_EFFECT
  - MODIFIER_EFFECT
  - HISTORY_BASED_EFFECT
  - POOL_BASED_EFFECT
  - BATCH_EFFECT
  - COMPOSITE_EFFECT

allowedActions:
  - DEAL_DAMAGE
  - HEAL
  - FULLY_HEAL
  - DRAW_CARD
  - SUMMON
  - DESTROY
  - BATCH_DESTROY
  - RANDOM_SELECT
  - DISCOVER
  - APPLY_MODIFIER
  - APPLY_AURA
  - RESURRECT
  - RETURN_TO_HAND
  - ADD_CARD_TO_HAND

allowedSourceZones:
  - BOARD
  - HAND
  - DECK
  - SECRET_ZONE
  - QUEST_ZONE
  - GRAVEYARD
  - GLOBAL

allowedPhases:
  - ON_PLAY
  - BATTLECRY_PHASE
  - AFTER_PLAY
  - TURN_START
  - TURN_END
  - DEATH_CHECK
  - DEATH_PROCESSING
  - DEATHRATTLE_PHASE
  - SECRET_WINDOW
  - AURA_REFRESH

limits:
  maxComplexityScore: 30
  maxRandomnessScore: 25
  maxEventCountPerCard: 20
  maxEventCountPerTurn: 80
  maxRepeatCount: 5
  maxGeneratedCardsPerEffect: 1
  maxHistoryQueryScope: LIMITED
```

---

## 16. Invariants

1. Active profile is immutable once used by a catalog snapshot.
2. Generated cards store `rulesetProfileId` and `rulesetProfileHash`.
3. Replay uses original `rulesetProfileId` and `resolvedHash`.
4. GenerationContext cannot expose disabled mechanics.
5. Publishing validates candidate against target resolved profile.
6. Deprecated mechanics can be replayed but not used for new cards unless explicitly allowed.
7. Format legality does not mutate `CardVersion`.
8. Profile contradictions fail CI.

---

## 17. Test requirements

### 17.1 Unit tests

- profile inheritance merge;
- disabled mechanics override enabled mechanics;
- unknown mechanic rejected;
- deprecated mechanic rejected for new card;
- legacy replay allowed for deprecated mechanic;
- dependency graph detects missing actions;
- dependency graph detects incompatible mechanics;
- limits correctly enforced;
- GenerationContext excludes disabled mechanics.

### 17.2 Integration tests

- candidate using disabled mechanic is rejected;
- candidate valid under experimental profile but invalid under MVP profile;
- catalog publication pins `rulesetProfileId` and `resolvedHash`;
- replay succeeds with deprecated mechanic if old profile allows it;
- replay fails if profile hash mismatch is not allowed.

---

## 18. Open questions

1. Should resolved profile be stored fully in catalog snapshot or referenced by hash + immutable registry?
2. Should experimental profiles allow temporary mechanics below `APPROVED_FOR_RULESET`?
3. How strict should production inheritance be after alpha?
4. Should profile validation be part of CI only or runtime startup too?
