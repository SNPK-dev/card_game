using Game.Generator.Validation;
using Game.Shared.Catalog;
using Game.Shared.Dsl;
using Game.Shared.Engine;
using Game.Shared.Primitives;

namespace Game.Generator.Publication;

public sealed class GeneratedCardPublisher
{
  public GeneratedCardPublicationResult Publish(GeneratedCardPublicationRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.ValidationRequest);
    ArgumentNullException.ThrowIfNull(request.ValidationReport);
    ArgumentNullException.ThrowIfNull(request.EngineCapabilityRegistry);

    var diagnostics = new List<GeneratedCardPublicationDiagnostic>();
    var candidate = request.ValidationRequest.Candidate;
    var report = request.ValidationReport;

    if (!string.Equals(request.ValidationRequest.GenerationId, report.GenerationId, StringComparison.Ordinal))
    {
      diagnostics.Add(new GeneratedCardPublicationDiagnostic(
        GeneratedCardPublicationDiagnosticCode.ValidationReportGenerationIdMismatch,
        "validationReport.generationId",
        "Validation report generation id must match publication request generation id."));
    }

    if (report.FinalDecision is not GeneratedCardValidationDecision.Accepted)
    {
      diagnostics.Add(new GeneratedCardPublicationDiagnostic(
        GeneratedCardPublicationDiagnosticCode.ValidationRejected,
        "validationReport.finalDecision",
        "Rejected validation reports cannot be published."));
    }

    AddRequired(diagnostics, candidate.CardId, "card.cardId", GeneratedCardPublicationDiagnosticCode.CardIdRequired);
    AddRequired(diagnostics, request.CardVersionId, "publication.cardVersionId", GeneratedCardPublicationDiagnosticCode.CardVersionIdRequired);
    AddRequired(diagnostics, request.CatalogSnapshotId, "publication.catalogSnapshotId", GeneratedCardPublicationDiagnosticCode.CatalogSnapshotIdRequired);
    AddCapabilityDiagnostics(diagnostics, candidate, request.EngineCapabilityRegistry);

    if (diagnostics.Count > 0)
    {
      return GeneratedCardPublicationResult.Failure(diagnostics);
    }

    var cardVersion = new CardVersion(
      candidate.CardId!,
      request.CardVersionId,
      candidate.SchemaVersion,
      candidate.EngineVersion,
      request.CatalogSnapshotId,
      report.PipelineVersion,
      report.RulesetProfileId,
      report.RulesetProfileHash,
      request.PublishedAt,
      candidate with
      {
        CardVersion = request.CardVersionId,
        CatalogSnapshotId = request.CatalogSnapshotId,
        AuthoringMetadata = candidate.AuthoringMetadata with
        {
          GenerationPipelineVersion = report.PipelineVersion
        }
      },
      Published: true);

    var catalogSnapshot = new CatalogSnapshot(
      request.CatalogSnapshotId,
      report.RulesetProfileId,
      report.RulesetProfileHash,
      request.PublishedAt,
      [cardVersion],
      [report.RulesetProfileId],
      [RulesetVersion(report.RulesetProfileId)],
      string.Equals(report.RulesetProfileId, "ruleset-production-core-v1", StringComparison.Ordinal));

    return GeneratedCardPublicationResult.Success(cardVersion, catalogSnapshot);
  }

  private static void AddRequired(
    ICollection<GeneratedCardPublicationDiagnostic> diagnostics,
    string? value,
    string path,
    GeneratedCardPublicationDiagnosticCode code)
  {
    if (!string.IsNullOrWhiteSpace(value))
    {
      return;
    }

    diagnostics.Add(new GeneratedCardPublicationDiagnostic(
      code,
      path,
      $"Required field '{path}' is missing."));
  }

  private static void AddCapabilityDiagnostics(
    ICollection<GeneratedCardPublicationDiagnostic> diagnostics,
    CardDefinition candidate,
    EngineCapabilityRegistry registry)
  {
    CheckOne(diagnostics, registry.EngineVersion, candidate.EngineVersion, "card.engineVersion");
    CheckOne(diagnostics, registry.SupportedSchemaVersions, candidate.SchemaVersion, "card.schemaVersion");
    CheckOne(diagnostics, registry.SupportedPrimitiveRegistryVersions, candidate.PrimitiveRegistryVersion, "card.primitiveRegistryVersion");
    CheckOne(diagnostics, registry.SupportedRulesetProfileVersions, candidate.RulesetProfileId, "card.rulesetProfileId");

    for (var effectIndex = 0; effectIndex < candidate.Effects.Count; effectIndex++)
    {
      var effect = candidate.Effects[effectIndex];
      var effectPath = $"card.effects[{effectIndex}]";
      CheckOne(diagnostics, registry.SupportedEffectKinds, EffectKindId(effect.EffectKind), $"{effectPath}.effectKind");
      CheckOne(diagnostics, registry.SupportedPhases, PhaseId(effect.TimingPolicy.Phase), $"{effectPath}.timingPolicy.phase");
      CheckAll(diagnostics, registry.SupportedSourceZones, effect.SourceZones.Select(SourceZoneId), $"{effectPath}.sourceZones");
      CheckOne(diagnostics, registry.SupportedResolutionPolicies, effect.ResolutionPolicy.Kind, $"{effectPath}.resolutionPolicy.kind");
      CheckOne(diagnostics, registry.SupportedFizzlePolicies, effect.FizzlePolicy.Kind, $"{effectPath}.fizzlePolicy.kind");
      CheckAll(diagnostics, registry.SupportedMechanics, effect.Tags, $"{effectPath}.tags");

      for (var actionIndex = 0; actionIndex < effect.Actions.Count; actionIndex++)
      {
        CheckOne(
          diagnostics,
          registry.SupportedActionTypes,
          ActionTypeId(effect.Actions[actionIndex]),
          $"{effectPath}.actions[{actionIndex}]");
      }
    }
  }

  private static void CheckOne(
    ICollection<GeneratedCardPublicationDiagnostic> diagnostics,
    string supported,
    string actual,
    string path)
  {
    if (!StringComparer.Ordinal.Equals(supported, actual))
    {
      AddUnsupported(diagnostics, path, actual);
    }
  }

  private static void CheckOne(
    ICollection<GeneratedCardPublicationDiagnostic> diagnostics,
    IReadOnlyList<string> supported,
    string actual,
    string path)
  {
    if (!supported.Contains(actual, StringComparer.Ordinal))
    {
      AddUnsupported(diagnostics, path, actual);
    }
  }

  private static void CheckAll(
    ICollection<GeneratedCardPublicationDiagnostic> diagnostics,
    IReadOnlyList<string> supported,
    IEnumerable<string> actual,
    string path)
  {
    foreach (var identifier in actual)
    {
      CheckOne(diagnostics, supported, identifier, path);
    }
  }

  private static void AddUnsupported(
    ICollection<GeneratedCardPublicationDiagnostic> diagnostics,
    string path,
    string identifier)
  {
    diagnostics.Add(new GeneratedCardPublicationDiagnostic(
      GeneratedCardPublicationDiagnosticCode.UnsupportedEngineCapability,
      path,
      $"Identifier '{identifier}' is not supported by the active engine capability registry."));
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

  private static string RulesetVersion(string rulesetProfileId) =>
    string.Equals(rulesetProfileId, "ruleset-production-core-v1", StringComparison.Ordinal)
      ? "1.0.0"
      : "0.1.0";
}
