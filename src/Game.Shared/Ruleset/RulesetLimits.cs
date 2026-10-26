namespace Game.Shared.Ruleset;

public sealed record RulesetLimits(
  int MaxComplexityScore,
  int MaxRandomnessScore,
  int MaxEventCountPerCard,
  int MaxEventCountPerTurn,
  int MaxRepeatCount,
  int MaxGeneratedCardsPerEffect,
  string MaxHistoryQueryScope,
  int MaxActionsPerEffect,
  int MaxEffectsPerCard,
  int MaxChoiceCount,
  int MaxDiscoverOfferCount,
  int MaxBoardSummonsPerAction,
  int MaxDrawPerAction,
  int MaxManaCostReduction,
  int MaxTemporaryCostReduction,
  int MaxPermanentCostReduction,
  int MaxBoardSize = 7,
  int MaxHandSize = 10);
