using Game.Shared.Dsl;
using Game.Shared.Primitives;

namespace Game.Generator.Validation;

public sealed class SemanticValidationStage : IGeneratedCardValidationStage
{
  public GeneratedCardValidationStageKind Stage => GeneratedCardValidationStageKind.Semantic;

  public GeneratedCardValidationStageReport Validate(GeneratedCardValidationRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    var diagnostics = new List<GeneratedCardValidationDiagnostic>();
    var effects = request.Candidate.Effects;

    ValidateCardShape(request.Candidate, diagnostics);
    ValidateRulesTextContract(request.Candidate, diagnostics);

    AddDuplicates(
      diagnostics,
      effects.Select(effect => effect.EffectId),
      "card.effects[].effectId");

    for (var effectIndex = 0; effectIndex < effects.Count; effectIndex++)
    {
      var effect = effects[effectIndex];
      AddDuplicates(
        diagnostics,
        effect.Actions.Select(action => action.ActionId),
        $"card.effects[{effectIndex}].actions[].actionId");

      for (var actionIndex = 0; actionIndex < effect.Actions.Count; actionIndex++)
      {
        ValidateActionTargetRefs(
          effect,
          effect.Actions[actionIndex],
          $"card.effects[{effectIndex}].actions[{actionIndex}]",
          diagnostics);
        ValidateActionValueExpressions(
          effect.Actions[actionIndex],
          $"card.effects[{effectIndex}].actions[{actionIndex}]",
          diagnostics);
      }
    }

    return CreateReport(diagnostics);
  }

  private static void ValidateCardShape(
    CardDefinition candidate,
    ICollection<GeneratedCardValidationDiagnostic> diagnostics)
  {
    switch (candidate.StaticData.CardType)
    {
      case CardType.MinionCard:
        if (candidate.Entity.Minion is null)
        {
          AddSemanticMismatch(diagnostics, "card.entity.minion", "missing", "MINION_CARD must declare minion entity metadata.");
          return;
        }
        if (candidate.Entity.Minion.Attack <= 0 || candidate.Entity.Minion.Health <= 0)
        {
          AddSemanticMismatch(diagnostics, "card.entity.minion", $"{candidate.Entity.Minion.Attack}/{candidate.Entity.Minion.Health}", "MINION_CARD must have positive attack and health.");
        }
        break;
      case CardType.WeaponCard:
        if (candidate.Entity.Weapon is null)
        {
          AddSemanticMismatch(diagnostics, "card.entity.weapon", "missing", "WEAPON_CARD must declare weapon entity metadata.");
          return;
        }
        if (candidate.Entity.Weapon.Attack <= 0 || candidate.Entity.Weapon.Durability <= 0)
        {
          AddSemanticMismatch(diagnostics, "card.entity.weapon", $"{candidate.Entity.Weapon.Attack}/{candidate.Entity.Weapon.Durability}", "WEAPON_CARD must have positive attack and durability.");
        }
        break;
      case CardType.LocationCard:
        if (candidate.Entity.Location is null)
        {
          AddSemanticMismatch(diagnostics, "card.entity.location", "missing", "LOCATION_CARD must declare location entity metadata.");
          return;
        }
        if (candidate.Entity.Location.Durability <= 0)
        {
          AddSemanticMismatch(diagnostics, "card.entity.location.durability", candidate.Entity.Location.Durability.ToString(System.Globalization.CultureInfo.InvariantCulture), "LOCATION_CARD must have positive durability.");
        }
        if (Mentions(candidate.CardText.RulesText, "location") || candidate.Effects.Count > 0)
        {
          if (!candidate.Effects.Any(effect => effect.EffectKind is EffectKind.LocationAbilityEffect))
          {
            AddSemanticMismatch(diagnostics, "card.effects", "missing LOCATION_ABILITY_EFFECT", "LOCATION_CARD with rules/effects must use LOCATION_ABILITY_EFFECT.");
          }
        }
        break;
    }
  }

  private static void ValidateRulesTextContract(
    CardDefinition candidate,
    ICollection<GeneratedCardValidationDiagnostic> diagnostics)
  {
    var text = candidate.CardText.RulesText;
    if (string.IsNullOrWhiteSpace(text))
    {
      return;
    }

    var actions = candidate.Effects.SelectMany(effect => effect.Actions).ToArray();
    if (candidate.StaticData.CardType is CardType.SpellCard
        && MentionsAny(text, "deal", "damage", "restore", "heal", "draw", "discover", "prevent", "resurrect")
        && actions.Length == 0)
    {
      AddSemanticMismatch(diagnostics, "card.effects", "empty", "Spell rules text declares gameplay action but DSL contains no actions.");
    }

    if (Mentions(text, "deathrattle"))
    {
      if (!candidate.Effects.Any(effect => effect.EffectKind is EffectKind.DeathrattleEffect))
      {
        AddSemanticMismatch(diagnostics, "card.effects", "missing DEATHRATTLE_EFFECT", "Deathrattle rules text must use DEATHRATTLE_EFFECT.");
      }
    }

    if (candidate.Effects.Any(effect => effect.EffectKind is EffectKind.DeathrattleEffect))
    {
      foreach (var effect in candidate.Effects.Where(effect => effect.EffectKind is EffectKind.DeathrattleEffect))
      {
        if (effect.Trigger?.EventType is not EventType.EntityDied || effect.TimingPolicy.Phase is not Phase.DeathrattlePhase || !effect.SourceZones.Contains(SourceZone.Board))
        {
          AddSemanticMismatch(diagnostics, "card.effects.deathrattle", $"{effect.Trigger?.EventType}/{effect.TimingPolicy.Phase}", "DEATHRATTLE_EFFECT must trigger from ENTITY_DIED on board during DEATHRATTLE_PHASE.");
        }
      }
    }

    if (MentionsAny(text, "deal", "damage") && !Mentions(text, "prevent") && !actions.Any(action => action is DealDamageActionDefinition or DamageDistributionActionDefinition))
    {
      AddSemanticMismatch(diagnostics, "card.effects.actions", "missing damage action", "Damage rules text must include DEAL_DAMAGE or DAMAGE_DISTRIBUTION.");
    }
    if (MentionsAny(text, "restore", "heal") && !actions.Any(action => action is HealActionDefinition or FullyHealActionDefinition))
    {
      AddSemanticMismatch(diagnostics, "card.effects.actions", "missing heal action", "Heal rules text must include HEAL or FULLY_HEAL.");
    }
    if (Mentions(text, "draw") && !actions.Any(action => action is DrawCardActionDefinition))
    {
      AddSemanticMismatch(diagnostics, "card.effects.actions", "missing draw action", "Draw rules text must include DRAW_CARD.");
    }
    if (Mentions(text, "discover") && !actions.Any(action => action is DiscoverActionDefinition))
    {
      AddSemanticMismatch(diagnostics, "card.effects.actions", "missing discover action", "Discover rules text must include DISCOVER.");
    }
    if (Mentions(text, "prevent") && !actions.Any(action => action is PreventDamageActionDefinition or ReplaceEventActionDefinition))
    {
      AddSemanticMismatch(diagnostics, "card.effects.actions", "missing prevention action", "Prevention rules text must include PREVENT_DAMAGE or REPLACE_EVENT.");
    }
    if (Mentions(text, "resurrect") && !actions.Any(action => action is ResurrectActionDefinition))
    {
      AddSemanticMismatch(diagnostics, "card.effects.actions", "missing resurrect action", "Resurrect rules text must include RESURRECT.");
    }
    if (MentionsAny(text, "start of", "end of", "turn") && candidate.Effects.Any(effect => effect.TimingPolicy.Phase is Phase.OnPlay))
    {
      AddSemanticMismatch(diagnostics, "card.effects.timingPolicy.phase", "ON_PLAY", "Turn-window rules text must not be represented as ON_PLAY.");
    }
  }

  private static void ValidateActionTargetRefs(
    EffectDefinition effect,
    ActionDefinition action,
    string path,
    ICollection<GeneratedCardValidationDiagnostic> diagnostics)
  {
    foreach (var targetRef in GetTargetRefs(action))
    {
      if (targetRef is null)
      {
        AddSemanticMismatch(diagnostics, $"{path}.target", string.Empty, "Action target must be present.");
        continue;
      }
      if (string.IsNullOrWhiteSpace(targetRef.Ref))
      {
        AddSemanticMismatch(diagnostics, $"{path}.target.ref", string.Empty, "Action target ref must be non-empty.");
        continue;
      }

      if (targetRef.Ref == "targetRequirement" && effect.TargetRequirement is null)
      {
        AddSemanticMismatch(diagnostics, $"{path}.target.ref", targetRef.Ref, "Action target ref points to targetRequirement but the effect has no targetRequirement.");
      }

      if (action is not ResurrectActionDefinition
          && targetRef.Ref is not "targetRequirement"
          && effect.TargetRequirement is null)
      {
        AddRuntimeActionDiagnostic(diagnostics, $"{path}.target.ref", targetRef.Ref);
      }
    }
  }

  private static void ValidateActionValueExpressions(
    ActionDefinition action,
    string path,
    ICollection<GeneratedCardValidationDiagnostic> diagnostics)
  {
    switch (action)
    {
      case DealDamageActionDefinition dealDamage:
        ValidateValueExpression(dealDamage.Amount, $"{path}.amount", diagnostics);
        break;
      case DamageDistributionActionDefinition damageDistribution:
        ValidateValueExpression(damageDistribution.TotalAmount, $"{path}.totalAmount", diagnostics);
        ValidateDamageDistribution(damageDistribution, path, diagnostics);
        break;
      case HealActionDefinition heal:
        ValidateValueExpression(heal.Amount, $"{path}.amount", diagnostics);
        break;
      case DrawCardActionDefinition drawCard:
        ValidateValueExpression(drawCard.Count, $"{path}.count", diagnostics);
        ValidateDrawCard(drawCard, path, diagnostics);
        break;
      case DiscoverActionDefinition discover:
        ValidateDiscover(discover, path, diagnostics);
        break;
      case SummonActionDefinition summon:
        ValidateValueExpression(summon.Count, $"{path}.count", diagnostics);
        break;
      case ApplyAuraActionDefinition applyAura:
        ValidateValueExpression(applyAura.Aura.Modifier.Value, $"{path}.aura.modifier.value", diagnostics);
        ValidateApplyAura(applyAura, path, diagnostics);
        break;
      case ApplyModifierActionDefinition applyModifier:
        ValidateValueExpression(applyModifier.Modifier.Value, $"{path}.modifier.value", diagnostics);
        break;
      case PreventDamageActionDefinition preventDamage:
        ValidateValueExpression(preventDamage.Amount, $"{path}.amount", diagnostics);
        ValidatePreventDamage(preventDamage, path, diagnostics);
        break;
      case ReplaceEventActionDefinition replaceEvent:
        ValidateReplaceEvent(replaceEvent, path, diagnostics);
        break;
      case TakeExtraTurnActionDefinition takeExtraTurn:
        ValidateTakeExtraTurn(takeExtraTurn, path, diagnostics);
        break;
      case ResurrectActionDefinition resurrect:
        ValidateValueExpression(resurrect.Attack, $"{path}.attack", diagnostics);
        ValidateValueExpression(resurrect.Health, $"{path}.health", diagnostics);
        ValidateResurrectHistoryQuery(resurrect.HistoryQuery, $"{path}.historyQuery", diagnostics);
        break;
    }
  }

  private static void ValidateResurrectHistoryQuery(
    HistoryQuerySpec? query,
    string path,
    ICollection<GeneratedCardValidationDiagnostic> diagnostics)
  {
    if (query is null)
    {
      return;
    }

    if (query.Kind is not ("OWN_GRAVEYARD_MINION" or "OPPONENT_GRAVEYARD_MINION" or "ANY_GRAVEYARD_MINION" or "ANY_DEAD_MINION"))
    {
      AddHistoryQueryDiagnostic(diagnostics, $"{path}.kind", query.Kind);
    }

    if (query.Ordering is not ("FIRST" or "LAST" or "LOWEST_CARD_INSTANCE_ID" or "RANDOM"))
    {
      AddHistoryQueryDiagnostic(diagnostics, $"{path}.ordering", query.Ordering);
    }

    if (query.MaxCandidates is <= 0 or > 30)
    {
      AddHistoryQueryDiagnostic(
        diagnostics,
        $"{path}.maxCandidates",
        query.MaxCandidates.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
  }

  private static void ValidateDamageDistribution(
    DamageDistributionActionDefinition action,
    string path,
    ICollection<GeneratedCardValidationDiagnostic> diagnostics)
  {
    if (action.DistributionMode is not ("SOURCE_ORDER" or "RANDOM_SPLIT"))
    {
      AddDamageDistributionDiagnostic(diagnostics, $"{path}.distributionMode", action.DistributionMode);
    }

    if (action.PerTargetInstancePolicy is not "SEPARATE_DAMAGE_INSTANCES")
    {
      AddDamageDistributionDiagnostic(diagnostics, $"{path}.perTargetInstancePolicy", action.PerTargetInstancePolicy);
    }

    if (action.LifestealAggregationPolicy is not ("NO_LIFESTEAL" or "AGGREGATE_LIFESTEAL"))
    {
      AddDamageDistributionDiagnostic(diagnostics, $"{path}.lifestealAggregationPolicy", action.LifestealAggregationPolicy);
    }

    if (action.OverkillPolicy is not ("ALLOW_OVERKILL" or "CAP_AT_REMAINING_HEALTH" or "SKIP_DEAD_TARGETS"))
    {
      AddDamageDistributionDiagnostic(diagnostics, $"{path}.overkillPolicy", action.OverkillPolicy);
    }

    if (action.DistributionMode is "RANDOM_SPLIT" && action.RngPolicy is not "RANDOM_TARGET")
    {
      AddDamageDistributionDiagnostic(diagnostics, $"{path}.rngPolicy", action.RngPolicy);
    }
  }

  private static void ValidateDiscover(
    DiscoverActionDefinition action,
    string path,
    ICollection<GeneratedCardValidationDiagnostic> diagnostics)
  {
    AddRuntimeActionDiagnostic(diagnostics, path, "DISCOVER");

    if (action.OfferCount is <= 0 or > 3)
    {
      AddRuntimeActionDiagnostic(
        diagnostics,
        $"{path}.offerCount",
        action.OfferCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    if (action.ChoicePolicy is not "PLAYER_CHOICE")
    {
      AddRuntimeActionDiagnostic(diagnostics, $"{path}.choicePolicy", action.ChoicePolicy);
    }

    if (action.AddToZone is not SourceZone.Hand)
    {
      AddRuntimeActionDiagnostic(diagnostics, $"{path}.addToZone", action.AddToZone.ToString());
    }

    if (action.PoolEmptyPolicy is not ("FIZZLE" or "SKIP" or "SKIP_ACTION"))
    {
      AddRuntimeActionDiagnostic(diagnostics, $"{path}.poolEmptyPolicy", action.PoolEmptyPolicy);
    }

    if (!StringComparer.Ordinal.Equals(action.PoolRef.Ref, "UNITY_FIXED_POOL"))
    {
      AddRuntimeActionDiagnostic(diagnostics, $"{path}.poolRef.ref", action.PoolRef.Ref);
    }
  }

  private static void ValidateDrawCard(
    DrawCardActionDefinition action,
    string path,
    ICollection<GeneratedCardValidationDiagnostic> diagnostics)
  {
    if (action.Count.Kind is not "FIXED" || action.Count.FixedValue is not 1)
    {
      AddRuntimeActionDiagnostic(
        diagnostics,
        $"{path}.count",
        action.Count.FixedValue?.ToString(System.Globalization.CultureInfo.InvariantCulture)
          ?? action.Count.Ref
          ?? action.Count.Kind);
    }
  }

  private static void ValidateApplyAura(
    ApplyAuraActionDefinition action,
    string path,
    ICollection<GeneratedCardValidationDiagnostic> diagnostics)
  {
    if (!IsSupportedAuraDuration(action.Aura.Duration))
    {
      AddRuntimeActionDiagnostic(diagnostics, $"{path}.aura.duration", action.Aura.Duration);
    }

    if (!IsSupportedAuraDuration(action.Aura.Modifier.Duration))
    {
      AddRuntimeActionDiagnostic(diagnostics, $"{path}.aura.modifier.duration", action.Aura.Modifier.Duration);
    }

    if (action.Aura.Modifier.ModifierType is not ("BOARD_ATTACK_MODIFIER" or "BOARD_HEALTH_MODIFIER" or "BOARD_KEYWORD_MODIFIER"))
    {
      AddRuntimeActionDiagnostic(diagnostics, $"{path}.aura.modifier.modifierType", action.Aura.Modifier.ModifierType);
    }
  }

  private static bool IsSupportedAuraDuration(string duration) =>
    duration is "PERMANENT" or "WHILE_SOURCE_ACTIVE";

  private static void ValidatePreventDamage(
    PreventDamageActionDefinition action,
    string path,
    ICollection<GeneratedCardValidationDiagnostic> diagnostics)
  {
    if (action.Duration is not "NEXT_DAMAGE")
      AddRuntimeActionDiagnostic(diagnostics, $"{path}.duration", action.Duration);
    if (action.PreventionPolicy is not "PREVENT_UP_TO_AMOUNT")
      AddRuntimeActionDiagnostic(diagnostics, $"{path}.preventionPolicy", action.PreventionPolicy);
  }

  private static void ValidateReplaceEvent(
    ReplaceEventActionDefinition action,
    string path,
    ICollection<GeneratedCardValidationDiagnostic> diagnostics)
  {
    if (action.EventType is not "DAMAGE_WOULD_BE_DEALT")
    {
      AddRuntimeActionDiagnostic(diagnostics, $"{path}.eventType", action.EventType);
    }

    if (action.ReplacementPolicy is not "PREVENT_DAMAGE")
    {
      AddRuntimeActionDiagnostic(diagnostics, $"{path}.replacementPolicy", action.ReplacementPolicy);
    }

    if (action.Duration is not "NEXT_DAMAGE")
    {
      AddRuntimeActionDiagnostic(diagnostics, $"{path}.duration", action.Duration);
    }
  }

  private static void AddDamageDistributionDiagnostic(
    ICollection<GeneratedCardValidationDiagnostic> diagnostics,
    string path,
    string actualValue)
  {
    diagnostics.Add(new GeneratedCardValidationDiagnostic(
      GeneratedCardValidationCode.DamageDistributionNotSupported,
      GeneratedCardValidationSeverity.Error,
      path,
      actualValue,
      $"Damage distribution policy at '{path}' is not supported by the current deterministic runtime slice."));
  }

  private static void ValidateTakeExtraTurn(
    TakeExtraTurnActionDefinition action,
    string path,
    ICollection<GeneratedCardValidationDiagnostic> diagnostics)
  {
    if (!action.CurrentTurnEndsNormally)
    {
      AddTakeExtraTurnDiagnostic(diagnostics, $"{path}.currentTurnEndsNormally", action.CurrentTurnEndsNormally.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    if (action.ExtraTurnStartPolicy is not "AFTER_CURRENT_TURN")
    {
      AddTakeExtraTurnDiagnostic(diagnostics, $"{path}.extraTurnStartPolicy", action.ExtraTurnStartPolicy);
    }

    if (action.TurnSchedulerPolicy is not "QUEUE_AFTER_CURRENT_TURN")
    {
      AddTakeExtraTurnDiagnostic(diagnostics, $"{path}.turnSchedulerPolicy", action.TurnSchedulerPolicy);
    }

    if (!string.IsNullOrWhiteSpace(action.CommandButtonReplacement))
    {
      AddTakeExtraTurnDiagnostic(diagnostics, $"{path}.commandButtonReplacement", action.CommandButtonReplacement);
    }

    if (action.ModifierDurationInteractionPolicy is not "EXPIRE_CURRENT_TURN_ONLY")
    {
      AddTakeExtraTurnDiagnostic(diagnostics, $"{path}.modifierDurationInteractionPolicy", action.ModifierDurationInteractionPolicy);
    }
  }

  private static void AddTakeExtraTurnDiagnostic(
    ICollection<GeneratedCardValidationDiagnostic> diagnostics,
    string path,
    string actualValue)
  {
    diagnostics.Add(new GeneratedCardValidationDiagnostic(
      GeneratedCardValidationCode.TakeExtraTurnNotSupported,
      GeneratedCardValidationSeverity.Error,
      path,
      actualValue,
      $"Extra-turn policy at '{path}' is not supported by the current deterministic runtime slice."));
  }

  private static void AddRuntimeActionDiagnostic(
    ICollection<GeneratedCardValidationDiagnostic> diagnostics,
    string path,
    string actionType)
  {
    diagnostics.Add(new GeneratedCardValidationDiagnostic(
      GeneratedCardValidationCode.RuntimeActionNotSupported,
      GeneratedCardValidationSeverity.Error,
      path,
      actionType,
      $"Action '{actionType}' is not supported by the current deterministic runtime slice."));
  }

  private static IEnumerable<TargetRef> GetTargetRefs(ActionDefinition action) =>
    action switch
    {
      DealDamageActionDefinition dealDamage => [dealDamage.Target],
      HealActionDefinition heal => [heal.Target],
      FullyHealActionDefinition fullyHeal => [fullyHeal.Target],
      DiscardCardActionDefinition discard => [discard.Target],
      DestroyActionDefinition destroy => [destroy.Target],
      BatchDestroyActionDefinition batchDestroy => [batchDestroy.Targets],
      MoveToZoneActionDefinition move => [move.Target],
      ReturnToHandActionDefinition returnToHand => [returnToHand.Target],
      PreventDamageActionDefinition preventDamage => [preventDamage.Target],
      ReplaceEventActionDefinition replaceEvent => [replaceEvent.Target],
      ShuffleIntoDeckActionDefinition shuffle => [shuffle.Target],
      ResurrectActionDefinition resurrect => [resurrect.Target],
      _ => []
    };

  private static bool Mentions(string text, string token) =>
    text.Contains(token, StringComparison.OrdinalIgnoreCase);

  private static bool MentionsAny(string text, params string[] tokens) =>
    tokens.Any(token => Mentions(text, token));

  private static void AddSemanticMismatch(
    ICollection<GeneratedCardValidationDiagnostic> diagnostics,
    string path,
    string actualValue,
    string message)
  {
    diagnostics.Add(new GeneratedCardValidationDiagnostic(
      GeneratedCardValidationCode.SemanticContractMismatch,
      GeneratedCardValidationSeverity.Error,
      path,
      actualValue,
      message));
  }

  private static void AddHistoryQueryDiagnostic(
    ICollection<GeneratedCardValidationDiagnostic> diagnostics,
    string path,
    string actualValue)
  {
    diagnostics.Add(new GeneratedCardValidationDiagnostic(
      GeneratedCardValidationCode.HistoryQueryNotSupported,
      GeneratedCardValidationSeverity.Error,
      path,
      actualValue,
      $"History query at '{path}' is not supported by the current deterministic runtime slice."));
  }

  private static void ValidateValueExpression(
    ValueExpression value,
    string path,
    ICollection<GeneratedCardValidationDiagnostic> diagnostics)
  {
    if (!string.IsNullOrWhiteSpace(value.Kind)
        && (value.FixedValue is not null || !string.IsNullOrWhiteSpace(value.Ref)))
    {
      return;
    }

    diagnostics.Add(new GeneratedCardValidationDiagnostic(
      GeneratedCardValidationCode.IncompleteValueExpression,
      GeneratedCardValidationSeverity.Error,
      path,
      value.Kind,
      $"Value expression at '{path}' must declare a kind and either a fixed value or a reference."));
  }

  private static void AddDuplicates(
    ICollection<GeneratedCardValidationDiagnostic> diagnostics,
    IEnumerable<string> identifiers,
    string path)
  {
    foreach (var duplicate in identifiers
      .Where(identifier => !string.IsNullOrWhiteSpace(identifier))
      .GroupBy(identifier => identifier, StringComparer.Ordinal)
      .Where(group => group.Count() > 1)
      .Select(group => group.Key)
      .OrderBy(identifier => identifier, StringComparer.Ordinal))
    {
      diagnostics.Add(new GeneratedCardValidationDiagnostic(
        GeneratedCardValidationCode.DuplicateIdentifier,
        GeneratedCardValidationSeverity.Error,
        path,
        duplicate,
        $"Identifier '{duplicate}' is duplicated at '{path}'."));
    }
  }

  private static GeneratedCardValidationStageReport CreateReport(
    IReadOnlyList<GeneratedCardValidationDiagnostic> diagnostics) =>
    new(
      GeneratedCardValidationStageKind.Semantic,
      diagnostics.Count == 0 ? GeneratedCardValidationDecision.Accepted : GeneratedCardValidationDecision.Rejected,
      diagnostics);
}
