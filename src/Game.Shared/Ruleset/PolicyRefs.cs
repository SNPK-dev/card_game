namespace Game.Shared.Ruleset;

public sealed record ValidationPolicyRef(
  string PolicyId,
  bool SchemaValidation,
  bool DeserializationValidation,
  bool SemanticValidation,
  bool RuleValidation,
  bool StaticAnalysis,
  bool FailOnUnknownMechanic,
  bool FailOnDeprecatedMechanic,
  bool FailOnDisabledMechanic,
  bool AllowLegacyDeprecatedMechanics);

public sealed record BalancePolicyRef(
  string PolicyId,
  bool RequireBalanceReport,
  string RequireHumanReviewAboveRisk,
  IReadOnlyList<string> AutoRejectRiskFlags,
  double MaxSimulationWinrateDelta,
  int MaxComplexityScore,
  int MaxRandomnessSwingScore,
  double MaxEventCountDelta,
  int MinSimulationSampleSize,
  double ConfidenceInterval,
  string BaselineDeckSetId,
  string SeedSetId,
  string OutlierHandlingPolicy);

public sealed record SimulationPolicyRef(
  string PolicyId,
  IReadOnlyList<string> RequiredScenarioSets,
  IReadOnlyList<string> RequiredEdgeCases,
  int MinRandomBotGames,
  int MinBaselineDeckGames,
  bool RequireReplayHashConsistency,
  int MaxTurns,
  int MaxEventCount);

public sealed record PublicationPolicyRef(
  string PolicyId,
  bool RequireValidationPass,
  bool RequireSimulationPass,
  bool RequireReplayPass,
  bool RequireBalanceReview,
  bool RequireHumanApproval,
  bool AllowAutoPublish,
  bool AllowExperimentalPublication,
  IReadOnlyList<string> AllowedPublicationStatuses);
