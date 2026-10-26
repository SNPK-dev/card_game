using Game.Generator.Generation;
using Game.Shared.Dsl;
using Game.Shared.Primitives;

namespace Game.Generator.Validation;

public sealed class GeneratedCardCandidateValidator
{
  public GeneratedCardValidationResult Validate(
    CardDefinition candidate,
    GenerationContext context)
  {
    ArgumentNullException.ThrowIfNull(candidate);
    ArgumentNullException.ThrowIfNull(context);

    var diagnostics = new List<GeneratedCardValidationDiagnostic>();

    AddIfDisallowed(
      diagnostics,
      context.AllowedCardTypes,
      EnumToken.ToToken(candidate.StaticData.CardType),
      GeneratedCardValidationCode.CardTypeNotAllowed,
      "card.staticData.cardType");

    AddIfDisallowed(
      diagnostics,
      context.AllowedRarities,
      EnumToken.ToToken(candidate.StaticData.Rarity),
      GeneratedCardValidationCode.RarityNotAllowed,
      "card.staticData.rarity");

    foreach (var classTag in candidate.StaticData.ClassTags)
    {
      AddIfDisallowed(
        diagnostics,
        context.AllowedClasses,
        classTag,
        GeneratedCardValidationCode.ClassTagNotAllowed,
        "card.staticData.classTags");
    }

    foreach (var tribe in candidate.StaticData.Tribes)
    {
      AddIfDisallowed(
        diagnostics,
        context.AllowedTribes,
        tribe,
        GeneratedCardValidationCode.TribeNotAllowed,
        "card.staticData.tribes");
    }

    foreach (var keyword in candidate.StaticData.Keywords)
    {
      AddIfDisallowed(
        diagnostics,
        context.AllowedKeywords,
        keyword,
        GeneratedCardValidationCode.KeywordNotAllowed,
        "card.staticData.keywords");
    }

    AddIfLimitExceeded(
      diagnostics,
      candidate.BalanceMetadata.ComplexityScore,
      context.Limits.MaxComplexityScore,
      GeneratedCardValidationCode.ComplexityLimitExceeded,
      "card.balanceMetadata.complexityScore");

    AddIfLimitExceeded(
      diagnostics,
      candidate.BalanceMetadata.RandomnessScore,
      context.Limits.MaxRandomnessScore,
      GeneratedCardValidationCode.RandomnessLimitExceeded,
      "card.balanceMetadata.randomnessScore");

    AddIfLimitExceeded(
      diagnostics,
      candidate.Effects.Count,
      context.Limits.MaxEffectsPerCard,
      GeneratedCardValidationCode.EffectsPerCardLimitExceeded,
      "card.effects");

    for (var effectIndex = 0; effectIndex < candidate.Effects.Count; effectIndex++)
    {
      ValidateEffect(candidate.Effects[effectIndex], effectIndex, context, diagnostics);
    }

    return diagnostics.Count == 0
      ? GeneratedCardValidationResult.Valid
      : GeneratedCardValidationResult.Invalid(diagnostics);
  }

  private static void ValidateEffect(
    EffectDefinition effect,
    int effectIndex,
    GenerationContext context,
    List<GeneratedCardValidationDiagnostic> diagnostics)
  {
    var effectPath = $"card.effects[{effectIndex}]";

    AddIfDisallowed(
      diagnostics,
      context.AllowedEffectKinds,
      EnumToken.ToToken(effect.EffectKind),
      GeneratedCardValidationCode.EffectKindNotAllowed,
      $"{effectPath}.effectKind");

    AddIfDisallowed(
      diagnostics,
      context.AllowedPhases,
      EnumToken.ToToken(effect.TimingPolicy.Phase),
      GeneratedCardValidationCode.PhaseNotAllowed,
      $"{effectPath}.timingPolicy.phase");

    foreach (var sourceZone in effect.SourceZones)
    {
      AddIfDisallowed(
        diagnostics,
        context.AllowedSourceZones,
        EnumToken.ToToken(sourceZone),
        GeneratedCardValidationCode.SourceZoneNotAllowed,
        $"{effectPath}.sourceZones");
    }

    foreach (var mechanic in effect.Tags)
    {
      if (!context.AllowedMechanics.Contains(mechanic))
      {
        diagnostics.Add(new GeneratedCardValidationDiagnostic(
          GeneratedCardValidationCode.MechanicNotAvailable,
          GeneratedCardValidationSeverity.Error,
          $"{effectPath}.tags",
          mechanic,
          $"Mechanic '{mechanic}' is not available in generation context '{context.RulesetProfileId}'."));
      }
    }

    AddIfLimitExceeded(
      diagnostics,
      effect.Actions.Count,
      context.Limits.MaxActionsPerEffect,
      GeneratedCardValidationCode.ActionsPerEffectLimitExceeded,
      $"{effectPath}.actions");

    for (var actionIndex = 0; actionIndex < effect.Actions.Count; actionIndex++)
    {
      var actionType = GetActionType(effect.Actions[actionIndex]);
      AddIfDisallowed(
        diagnostics,
        context.AllowedActions,
        actionType,
        GeneratedCardValidationCode.ActionTypeNotAllowed,
        $"{effectPath}.actions[{actionIndex}]");
    }
  }

  private static string GetActionType(ActionDefinition action) =>
    action switch
    {
      DealDamageActionDefinition => EnumToken.ToToken(ActionType.DealDamage),
      DamageDistributionActionDefinition => EnumToken.ToToken(ActionType.DamageDistribution),
      DrawCardActionDefinition => EnumToken.ToToken(ActionType.DrawCard),
      HealActionDefinition => EnumToken.ToToken(ActionType.Heal),
      FullyHealActionDefinition => EnumToken.ToToken(ActionType.FullyHeal),
      DiscardCardActionDefinition => EnumToken.ToToken(ActionType.DiscardCard),
      SummonActionDefinition => EnumToken.ToToken(ActionType.Summon),
      AddCardToHandActionDefinition => EnumToken.ToToken(ActionType.AddCardToHand),
      DestroyActionDefinition => "DESTROY",
      BatchDestroyActionDefinition => "BATCH_DESTROY",
      MoveToZoneActionDefinition => "MOVE_TO_ZONE",
      ReturnToHandActionDefinition => EnumToken.ToToken(ActionType.ReturnToHand),
      RandomSelectActionDefinition => EnumToken.ToToken(ActionType.RandomSelect),
      ApplyModifierActionDefinition => EnumToken.ToToken(ActionType.ApplyModifier),
      ApplyAuraActionDefinition => EnumToken.ToToken(ActionType.ApplyAura),
      PreventDamageActionDefinition => EnumToken.ToToken(ActionType.PreventDamage),
      ReplaceEventActionDefinition => EnumToken.ToToken(ActionType.ReplaceEvent),
      ShuffleIntoDeckActionDefinition => EnumToken.ToToken(ActionType.ShuffleIntoDeck),
      DiscoverActionDefinition => EnumToken.ToToken(ActionType.Discover),
      ResurrectActionDefinition => EnumToken.ToToken(ActionType.Resurrect),
      TakeExtraTurnActionDefinition => EnumToken.ToToken(ActionType.TakeExtraTurn),
      _ => action.GetType().Name
    };

  private static void AddIfDisallowed(
    ICollection<GeneratedCardValidationDiagnostic> diagnostics,
    IReadOnlySet<string> allowed,
    string actual,
    GeneratedCardValidationCode code,
    string path)
  {
    if (allowed.Contains(actual))
    {
      return;
    }

    diagnostics.Add(new GeneratedCardValidationDiagnostic(
      code,
      GeneratedCardValidationSeverity.Error,
      path,
      actual,
      $"Value '{actual}' is not allowed by the generation context."));
  }

  private static void AddIfLimitExceeded(
    ICollection<GeneratedCardValidationDiagnostic> diagnostics,
    int actual,
    int maximum,
    GeneratedCardValidationCode code,
    string path)
  {
    if (actual <= maximum)
    {
      return;
    }

    diagnostics.Add(new GeneratedCardValidationDiagnostic(
      code,
      GeneratedCardValidationSeverity.Error,
      path,
      actual.ToString(System.Globalization.CultureInfo.InvariantCulture),
      $"Value '{actual}' exceeds limit '{maximum}'."));
  }
}
