using Game.Shared.Dsl;
using Game.Shared.Primitives;

namespace Game.Application.Capabilities;

public sealed class EngineCapabilityCompatibilityValidator : IEngineCapabilityCompatibilityValidator
{
  public EngineCapabilityCompatibilityResult Validate(EngineCapabilityCompatibilityRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.Registry);
    ArgumentNullException.ThrowIfNull(request.CardDefinitions);

    var diagnostics = new List<EngineCapabilityCompatibilityDiagnostic>();
    ValidateRuleset(request, diagnostics);
    ValidateCards(request, diagnostics);

    return diagnostics.Count == 0
      ? EngineCapabilityCompatibilityResult.Compatible()
      : EngineCapabilityCompatibilityResult.Rejected(diagnostics);
  }

  private static void ValidateRuleset(
    EngineCapabilityCompatibilityRequest request,
    List<EngineCapabilityCompatibilityDiagnostic> diagnostics)
  {
    if (request.RulesetProfile is null)
    {
      return;
    }

    var registry = request.Registry;
    var profile = request.RulesetProfile;
    if (!ContainsAny(
      registry.SupportedRulesetProfileVersions,
      [profile.Id, profile.SourceProfileId, profile.SourceProfileVersion]))
    {
      Add(
        diagnostics,
        "UNSUPPORTED_RULESET_PROFILE",
        "rulesetProfile",
        profile.SourceProfileId,
        $"Ruleset profile '{profile.SourceProfileId}' version '{profile.SourceProfileVersion}' is not supported.");
    }

    CheckAll(diagnostics, registry.SupportedMechanics, profile.EnabledMechanics, "UNSUPPORTED_MECHANIC", "rulesetProfile.enabledMechanics");
    CheckAll(diagnostics, registry.SupportedActionTypes, profile.AllowedActions, "UNSUPPORTED_ACTION", "rulesetProfile.allowedActions");
    CheckAll(diagnostics, registry.SupportedEffectKinds, profile.AllowedEffectKinds, "UNSUPPORTED_EFFECT_KIND", "rulesetProfile.allowedEffectKinds");
    CheckAll(diagnostics, registry.SupportedPhases, profile.AllowedPhases, "UNSUPPORTED_PHASE", "rulesetProfile.allowedPhases");
    CheckAll(diagnostics, registry.SupportedSourceZones, profile.AllowedSourceZones, "UNSUPPORTED_SOURCE_ZONE", "rulesetProfile.allowedSourceZones");
  }

  private static void ValidateCards(
    EngineCapabilityCompatibilityRequest request,
    List<EngineCapabilityCompatibilityDiagnostic> diagnostics)
  {
    var registry = request.Registry;
    for (var cardIndex = 0; cardIndex < request.CardDefinitions.Count; cardIndex++)
    {
      var card = request.CardDefinitions[cardIndex];
      var cardPath = $"cardDefinitions[{cardIndex}]";
      var cardRef = string.IsNullOrWhiteSpace(card.CardId) ? cardPath : card.CardId;

      CheckOne(diagnostics, registry.EngineVersion, card.EngineVersion, "UNSUPPORTED_ENGINE_VERSION", $"{cardPath}.engineVersion");
      CheckOne(diagnostics, registry.SupportedSchemaVersions, card.SchemaVersion, "UNSUPPORTED_SCHEMA_VERSION", $"{cardPath}.schemaVersion");
      CheckOne(diagnostics, registry.SupportedPrimitiveRegistryVersions, card.PrimitiveRegistryVersion, "UNSUPPORTED_PRIMITIVE_REGISTRY", $"{cardPath}.primitiveRegistryVersion");
      CheckOne(diagnostics, registry.SupportedRulesetProfileVersions, card.RulesetProfileId, "UNSUPPORTED_RULESET_PROFILE", $"{cardPath}.rulesetProfileId");
      CheckAll(diagnostics, registry.SupportedMechanics, card.StaticData.Keywords, "UNSUPPORTED_MECHANIC", $"{cardPath}.staticData.keywords");

      for (var effectIndex = 0; effectIndex < card.Effects.Count; effectIndex++)
      {
        var effect = card.Effects[effectIndex];
        var effectPath = $"{cardPath}.effects[{effectIndex}]";
        CheckOne(diagnostics, registry.SupportedEffectKinds, EffectKindId(effect.EffectKind), "UNSUPPORTED_EFFECT_KIND", $"{effectPath}.effectKind");
        CheckOne(diagnostics, registry.SupportedPhases, PhaseId(effect.TimingPolicy.Phase), "UNSUPPORTED_PHASE", $"{effectPath}.timingPolicy.phase");
        CheckAll(diagnostics, registry.SupportedSourceZones, effect.SourceZones.Select(SourceZoneId), "UNSUPPORTED_SOURCE_ZONE", $"{effectPath}.sourceZones");
        CheckOne(diagnostics, registry.SupportedResolutionPolicies, effect.ResolutionPolicy.Kind, "UNSUPPORTED_RESOLUTION_POLICY", $"{effectPath}.resolutionPolicy.kind");
        CheckOne(diagnostics, registry.SupportedFizzlePolicies, effect.FizzlePolicy.Kind, "UNSUPPORTED_FIZZLE_POLICY", $"{effectPath}.fizzlePolicy.kind");
        CheckAll(diagnostics, registry.SupportedMechanics, effect.Tags, "UNSUPPORTED_MECHANIC", $"{effectPath}.tags");

        for (var actionIndex = 0; actionIndex < effect.Actions.Count; actionIndex++)
        {
          var action = effect.Actions[actionIndex];
          CheckOne(
            diagnostics,
            registry.SupportedActionTypes,
            ActionTypeId(action),
            "UNSUPPORTED_ACTION",
            $"{effectPath}.actions[{actionIndex}]");
        }
      }

      if (diagnostics.Any(diagnostic => diagnostic.Path.StartsWith(cardPath, StringComparison.Ordinal)))
      {
        Add(
          diagnostics,
          "CARD_REJECTED_BY_CAPABILITY_REGISTRY",
          cardPath,
          cardRef,
          $"Card '{cardRef}' requires unsupported engine capabilities.");
      }
    }
  }

  private static void CheckOne(
    List<EngineCapabilityCompatibilityDiagnostic> diagnostics,
    string supported,
    string actual,
    string code,
    string path)
  {
    if (!StringComparer.Ordinal.Equals(supported, actual))
    {
      Add(diagnostics, code, path, actual, $"Identifier '{actual}' is not supported at '{path}'.");
    }
  }

  private static void CheckOne(
    List<EngineCapabilityCompatibilityDiagnostic> diagnostics,
    IReadOnlyList<string> supported,
    string actual,
    string code,
    string path)
  {
    if (!supported.Contains(actual, StringComparer.Ordinal))
    {
      Add(diagnostics, code, path, actual, $"Identifier '{actual}' is not supported at '{path}'.");
    }
  }

  private static void CheckAll(
    List<EngineCapabilityCompatibilityDiagnostic> diagnostics,
    IReadOnlyList<string> supported,
    IEnumerable<string> actual,
    string code,
    string path)
  {
    foreach (var identifier in actual)
    {
      CheckOne(diagnostics, supported, identifier, code, path);
    }
  }

  private static bool ContainsAny(IReadOnlyList<string> supported, IReadOnlyList<string> candidates) =>
    candidates.Any(candidate => supported.Contains(candidate, StringComparer.Ordinal));

  private static void Add(
    List<EngineCapabilityCompatibilityDiagnostic> diagnostics,
    string code,
    string path,
    string identifier,
    string message)
  {
    diagnostics.Add(new EngineCapabilityCompatibilityDiagnostic(code, path, identifier, message));
  }

  private static string ActionTypeId(ActionDefinition action) =>
    action switch
    {
      BatchDestroyActionDefinition => "BATCH_DESTROY",
      DamageDistributionActionDefinition => "DAMAGE_DISTRIBUTION",
      DealDamageActionDefinition => "DEAL_DAMAGE",
      DestroyActionDefinition => "DESTROY",
      DiscardCardActionDefinition => "DISCARD_CARD",
      DiscoverActionDefinition => "DISCOVER",
      DrawCardActionDefinition => "DRAW_CARD",
      FullyHealActionDefinition => "FULLY_HEAL",
      HealActionDefinition => "HEAL",
      MoveToZoneActionDefinition => "MOVE_TO_ZONE",
      RandomSelectActionDefinition => "RANDOM_SELECT",
      ReturnToHandActionDefinition => "RETURN_TO_HAND",
      ResurrectActionDefinition => "RESURRECT",
      ApplyModifierActionDefinition => "APPLY_MODIFIER",
      ApplyAuraActionDefinition => "APPLY_AURA",
      PreventDamageActionDefinition => "PREVENT_DAMAGE",
      ReplaceEventActionDefinition => "REPLACE_EVENT",
      ShuffleIntoDeckActionDefinition => "SHUFFLE_INTO_DECK",
      SummonActionDefinition => "SUMMON",
      AddCardToHandActionDefinition => "ADD_CARD_TO_HAND",
      TakeExtraTurnActionDefinition => "TAKE_EXTRA_TURN",
      _ => action.GetType().Name
    };

  private static string EffectKindId(EffectKind effectKind) =>
    effectKind switch
    {
      EffectKind.SimplePlayEffect => "SIMPLE_PLAY_EFFECT",
      EffectKind.TriggeredBoardEffect => "TRIGGERED_BOARD_EFFECT",
      EffectKind.DeathrattleEffect => "DEATHRATTLE_EFFECT",
      EffectKind.SecretEffect => "SECRET_EFFECT",
      EffectKind.QuestEffect => "QUEST_EFFECT",
      EffectKind.AuraEffect => "AURA_EFFECT",
      EffectKind.LocationAbilityEffect => "LOCATION_ABILITY_EFFECT",
      EffectKind.ModifierEffect => "MODIFIER_EFFECT",
      EffectKind.PoolBasedEffect => "POOL_BASED_EFFECT",
      EffectKind.BatchEffect => "BATCH_EFFECT",
      EffectKind.CompositeEffect => "COMPOSITE_EFFECT",
      _ => effectKind.ToString()
    };

  private static string PhaseId(Phase phase) =>
    phase switch
    {
      Phase.OnPlay => "ON_PLAY",
      Phase.BattlecryPhase => "BATTLECRY_PHASE",
      Phase.OnSummon => "ON_SUMMON",
      Phase.AfterPlay => "AFTER_PLAY",
      Phase.SecretWindow => "SECRET_WINDOW",
      Phase.DeathrattlePhase => "DEATHRATTLE_PHASE",
      Phase.TurnStart => "TURN_START",
      Phase.TurnEnd => "TURN_END",
      Phase.AuraRefresh => "AURA_REFRESH",
      _ => phase.ToString()
    };

  private static string SourceZoneId(SourceZone zone) =>
    zone switch
    {
      SourceZone.Board => "BOARD",
      SourceZone.Hand => "HAND",
      SourceZone.Deck => "DECK",
      SourceZone.SecretZone => "SECRET_ZONE",
      SourceZone.QuestZone => "QUEST_ZONE",
      SourceZone.Graveyard => "GRAVEYARD",
      SourceZone.SetAside => "SET_ASIDE",
      SourceZone.Global => "GLOBAL",
      _ => zone.ToString()
    };
}
