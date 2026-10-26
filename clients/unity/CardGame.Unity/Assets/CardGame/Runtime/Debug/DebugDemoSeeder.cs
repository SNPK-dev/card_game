using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CardGame.Unity.Client.Api;
using Game.UnityClient.Contracts;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CardGame.Unity.Client.DebugTools
{
  public enum SeedMode
  {
    Ensure,
    ResetIfSupported,
    SkipIfExists,
    FailOnConflict
  }

  public enum SeedResourceStatus
  {
    Created,
    AlreadyCompatible,
    Reset,
    Skipped,
    Incompatible,
    Failed
  }

  public sealed class SeedResourceResult
  {
    public string ResourceKind { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public SeedResourceStatus Status { get; set; }
    public bool Compatible { get; set; }
    public string Message { get; set; } = string.Empty;
  }

  public sealed class DebugSeedReport
  {
    public SeedMode Mode { get; set; }
    public bool Succeeded { get; set; }
    public List<SeedResourceResult> Results { get; set; } = new();
    public List<string> CompatibilityChecks { get; set; } = new();

    public string Summary =>
      string.Join(", ", Results.Select(result => $"{result.ResourceKind}:{result.Status}"));
  }

  public sealed class DebugDemoSeeder
  {
    public const string CatalogSnapshotId = "catalog-unity-test-card-pack-v1";
    public const string RulesetProfileId = "ruleset-production-core-v1";
    public const string PlayerOneId = "player-1";
    public const string PlayerTwoId = "player-2";
    public const string SpectatorId = "spectator-1";
    public const string PlayerOneDeckId = "unity-debug-deck-p1";
    public const string PlayerTwoDeckId = "unity-debug-deck-p2";

    private readonly BackendApiClient api;

    public DebugDemoSeeder(BackendApiClient api)
    {
      this.api = api;
    }

    public async Task<bool> SeedAsync(CancellationToken cancellationToken)
    {
      return (await SeedAsync(SeedMode.Ensure, cancellationToken)).Succeeded;
    }

    public async Task<DebugSeedReport> SeedAsync(SeedMode mode, CancellationToken cancellationToken)
    {
      var report = new DebugSeedReport { Mode = mode };
      report.Results.Add(await EnsureRulesetProfileAsync(mode, cancellationToken));
      report.Results.Add(await EnsureCatalogAsync(mode, cancellationToken));
      var deckOneJson = BuildProductionDeckJson(PlayerOneDeckId, PlayerOneId);
      var deckTwoJson = BuildProductionDeckJson(PlayerTwoDeckId, PlayerTwoId);
      report.Results.Add(await EnsureDeckAsync(mode, PlayerOneDeckId, deckOneJson, PlayerOneId, cancellationToken));
      report.Results.Add(await EnsureDeckAsync(mode, PlayerTwoDeckId, deckTwoJson, PlayerTwoId, cancellationToken));
      report.CompatibilityChecks.AddRange(report.Results.Select(result =>
        $"{result.ResourceKind}:{result.ResourceId}:{(result.Compatible ? "compatible" : "not-compatible")}:{result.Status}"));
      report.Succeeded = report.Results.All(result =>
        result.Status is SeedResourceStatus.Created or SeedResourceStatus.AlreadyCompatible or SeedResourceStatus.Skipped or SeedResourceStatus.Reset);
      return report;
    }

    private async Task<SeedResourceResult> EnsureRulesetProfileAsync(SeedMode mode, CancellationToken cancellationToken)
    {
      var existing = await api.GetJsonAsync<ResolvedRulesetProfile>(
        "/rulesets/profiles/" + Uri.EscapeDataString(RulesetProfileId),
        cancellationToken: cancellationToken);
      if (existing.Succeeded && existing.Value != null)
      {
        var compatible = IsExpectedProfile(existing.Value, existing.RawBody);
        return ExistingResult("ruleset-profile", RulesetProfileId, compatible, mode);
      }

      if (mode == SeedMode.SkipIfExists)
      {
        return CreatedOrFailed("ruleset-profile", RulesetProfileId, await PostRulesetProfileAsync(cancellationToken));
      }

      if (mode == SeedMode.FailOnConflict)
      {
        return CreatedOrFailed("ruleset-profile", RulesetProfileId, await PostRulesetProfileAsync(cancellationToken));
      }

      return await CreatedOrCompatibleAfterPostAsync(
        "ruleset-profile",
        RulesetProfileId,
        await PostRulesetProfileAsync(cancellationToken),
        () => VerifyProfileAfterConflictAsync(cancellationToken));
    }

    private async Task<SeedResourceResult> EnsureCatalogAsync(SeedMode mode, CancellationToken cancellationToken)
    {
      var existing = await api.GetJsonAsync<CatalogSnapshot>(
        "/catalog/snapshots/" + Uri.EscapeDataString(CatalogSnapshotId),
        cancellationToken: cancellationToken);
      if (existing.Succeeded && existing.Value != null)
      {
        var compatible = IsExpectedCatalog(existing.Value, existing.RawBody);
        return ExistingResult("catalog-snapshot", CatalogSnapshotId, compatible, mode);
      }

      if (mode is SeedMode.SkipIfExists or SeedMode.FailOnConflict)
      {
        return CreatedOrFailed("catalog-snapshot", CatalogSnapshotId, await PostCatalogAsync(cancellationToken));
      }

      return await CreatedOrCompatibleAfterPostAsync(
        "catalog-snapshot",
        CatalogSnapshotId,
        await PostCatalogAsync(cancellationToken),
        () => VerifyCatalogAfterConflictAsync(cancellationToken));
    }

    private async Task<SeedResourceResult> EnsureDeckAsync(
      SeedMode mode,
      string deckId,
      string json,
      string playerId,
      CancellationToken cancellationToken)
    {
      var existing = await api.GetJsonAsync<DeckDefinition>(
        "/decks/" + Uri.EscapeDataString(deckId),
        cancellationToken: cancellationToken);
      if (existing.Succeeded && existing.Value != null)
      {
        var compatible = IsExpectedDeck(existing.Value, deckId, playerId, existing.RawBody);
        return ExistingResult("deck", deckId, compatible, mode);
      }

      if (mode is SeedMode.SkipIfExists or SeedMode.FailOnConflict)
      {
        return CreatedOrFailed("deck", deckId, await api.PostRawJsonAsync<StorageResult>("/decks", json, cancellationToken: cancellationToken));
      }

      return await CreatedOrCompatibleAfterPostAsync(
        "deck",
        deckId,
        await api.PostRawJsonAsync<StorageResult>("/decks", json, cancellationToken: cancellationToken),
        () => VerifyDeckAfterConflictAsync(deckId, playerId, cancellationToken));
    }

    private Task<UnityApiResult<StorageResult>> PostRulesetProfileAsync(CancellationToken cancellationToken) =>
      api.PostRawJsonAsync<StorageResult>("/rulesets/profiles", ReadSeedArtifact("resolved-ruleset-profile.json"), cancellationToken: cancellationToken);

    private Task<UnityApiResult<StorageResult>> PostCatalogAsync(CancellationToken cancellationToken) =>
      api.PostRawJsonAsync<StorageResult>("/catalog/snapshots", ReadSeedArtifact("catalog-snapshot.json"), cancellationToken: cancellationToken);

    private async Task<bool> VerifyProfileAfterConflictAsync(CancellationToken cancellationToken)
    {
      var existing = await api.GetJsonAsync<ResolvedRulesetProfile>(
        "/rulesets/profiles/" + Uri.EscapeDataString(RulesetProfileId),
        cancellationToken: cancellationToken);
      return existing.Succeeded && existing.Value != null && IsExpectedProfile(existing.Value, existing.RawBody);
    }

    private async Task<bool> VerifyCatalogAfterConflictAsync(CancellationToken cancellationToken)
    {
      var existing = await api.GetJsonAsync<CatalogSnapshot>(
        "/catalog/snapshots/" + Uri.EscapeDataString(CatalogSnapshotId),
        cancellationToken: cancellationToken);
      return existing.Succeeded && existing.Value != null && IsExpectedCatalog(existing.Value, existing.RawBody);
    }

    private async Task<bool> VerifyDeckAfterConflictAsync(string deckId, string playerId, CancellationToken cancellationToken)
    {
      var existing = await api.GetJsonAsync<DeckDefinition>(
        "/decks/" + Uri.EscapeDataString(deckId),
        cancellationToken: cancellationToken);
      return existing.Succeeded && existing.Value != null && IsExpectedDeck(existing.Value, deckId, playerId, existing.RawBody);
    }

    private static SeedResourceResult ExistingResult(string kind, string id, bool compatible, SeedMode mode)
    {
      if (!compatible)
      {
        return new SeedResourceResult
        {
          ResourceKind = kind,
          ResourceId = id,
          Status = SeedResourceStatus.Incompatible,
          Compatible = false,
          Message = "Existing resource does not match the deterministic Unity debug seed shape."
        };
      }

      return new SeedResourceResult
      {
        ResourceKind = kind,
        ResourceId = id,
        Status = mode switch
        {
          SeedMode.FailOnConflict => SeedResourceStatus.Failed,
          SeedMode.SkipIfExists => SeedResourceStatus.Skipped,
          SeedMode.ResetIfSupported => SeedResourceStatus.AlreadyCompatible,
          _ => SeedResourceStatus.AlreadyCompatible
        },
        Compatible = true,
        Message = mode == SeedMode.FailOnConflict
          ? "Resource already exists and FailOnConflict is active."
          : "Existing resource is compatible with the deterministic Unity debug seed shape."
      };
    }

    private static SeedResourceResult CreatedOrFailed(string kind, string id, UnityApiResult<StorageResult> result) =>
      new()
      {
        ResourceKind = kind,
        ResourceId = id,
        Status = result.Succeeded && result.Value?.Succeeded != false ? SeedResourceStatus.Created : SeedResourceStatus.Failed,
        Compatible = result.Succeeded && result.Value?.Succeeded != false,
        Message = result.Succeeded ? "Resource created." : $"{result.Error?.Code} {result.Error?.Message}"
      };

    private static async Task<SeedResourceResult> CreatedOrCompatibleAfterPostAsync(
      string kind,
      string id,
      UnityApiResult<StorageResult> result,
      Func<Task<bool>> verifyExisting)
    {
      if (result.Succeeded && result.Value?.Succeeded != false)
      {
        return CreatedOrFailed(kind, id, result);
      }

      var compatible = await verifyExisting();
      return new SeedResourceResult
      {
        ResourceKind = kind,
        ResourceId = id,
        Status = compatible ? SeedResourceStatus.AlreadyCompatible : SeedResourceStatus.Failed,
        Compatible = compatible,
        Message = compatible
          ? "Create returned a conflict/error, but the existing public resource is compatible."
          : $"{result.Error?.Code} {result.Error?.Message}"
      };
    }

    public static bool IsExpectedProfile(ResolvedRulesetProfile profile) =>
      profile != null &&
      string.Equals(profile.ProfileId, RulesetProfileId, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(profile.RulesetId, RulesetProfileId, StringComparison.OrdinalIgnoreCase) &&
      profile.AllowedCommands.Contains("PLAY_CARD") &&
      profile.AllowedCommands.Contains("END_TURN") &&
      profile.AllowedCommands.Contains("ATTACK") &&
      profile.AllowedCommands.Contains("USE_HERO_POWER") &&
      profile.AllowedCommands.Contains("ACTIVATE_LOCATION");

    public static bool IsExpectedProfile(ResolvedRulesetProfile profile, string rawJson) =>
      IsExpectedProfile(profile) || RawHas(rawJson, "id", RulesetProfileId) &&
      RawHas(rawJson, "sourceProfileId", RulesetProfileId) &&
      RawArrayContains(rawJson, "allowedActions", "PLAY_CARD") &&
      RawArrayContains(rawJson, "allowedActions", "END_TURN") &&
      RawArrayContains(rawJson, "allowedActions", "ATTACK") &&
      RawArrayContains(rawJson, "allowedActions", "USE_HERO_POWER") &&
      RawArrayContains(rawJson, "allowedActions", "ACTIVATE_LOCATION");

    public static bool IsExpectedCatalog(CatalogSnapshot catalog) =>
      catalog != null &&
      string.Equals(catalog.SnapshotId, CatalogSnapshotId, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(catalog.RulesetId, RulesetProfileId, StringComparison.OrdinalIgnoreCase) &&
      catalog.Cards.Count >= 15 &&
      catalog.Cards.All(card => !string.IsNullOrWhiteSpace(card.CardId)) &&
      catalog.Cards.Select(card => card.CardId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == catalog.Cards.Count;

    public static bool IsExpectedCatalog(CatalogSnapshot catalog, string rawJson) =>
      IsExpectedCatalog(catalog) || RawHas(rawJson, "snapshotId", CatalogSnapshotId) &&
      RawHas(rawJson, "rulesetProfileId", RulesetProfileId) &&
      (TryParse(rawJson)?["cards"] as JArray)?.Count >= 15;

    public static bool IsExpectedDeck(DeckDefinition deck, string deckId, string playerId) =>
      deck != null &&
      string.Equals(deck.DeckId, deckId, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(deck.OwnerPlayerId, playerId, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(deck.CatalogSnapshotId, CatalogSnapshotId, StringComparison.OrdinalIgnoreCase) &&
      deck.Cards.Sum(card => card.Count) == 30;

    public static bool IsExpectedDeck(DeckDefinition deck, string deckId, string playerId, string rawJson) =>
      IsExpectedDeck(deck, deckId, playerId) || RawHas(rawJson, "deckId", deckId) &&
      RawHas(rawJson, "playerId", playerId) &&
      RawHas(rawJson, "catalogSnapshotId", CatalogSnapshotId) &&
      (TryParse(rawJson)?["cards"] as JArray)?.Sum(card => card.Value<int?>("count") ?? 0) == 30;

    private static string ReadSeedArtifact(string fileName)
    {
      var path = Path.Combine(Application.streamingAssetsPath, "Seed", fileName);
      if (!File.Exists(path))
      {
        throw new FileNotFoundException("Required production seed artifact is missing.", path);
      }
      return File.ReadAllText(path);
    }

    public static IReadOnlyList<GameplayCardPresentation> LoadSeedPresentations()
    {
      var cards=JObject.Parse(ReadSeedArtifact("catalog-snapshot.json"))["cards"] as JArray;
      if(cards==null)return Array.Empty<GameplayCardPresentation>();
      return cards.OfType<JObject>().Select(card=>
      {
        var definition=card["definition"] as JObject;var data=definition?["staticData"] as JObject;
        var entity=definition?["entity"] as JObject;var minion=entity?["minion"] as JObject;
        var weapon=entity?["weapon"] as JObject;var location=entity?["location"] as JObject;
        var id=card.Value<string>("cardId")??string.Empty;
        var art=Resources.Load<Texture2D>("CardArt/"+id);
        return new GameplayCardPresentation(id,card.Value<string>("cardVersionId")??string.Empty,
          data?.Value<string>("name")??id,data?.Value<int?>("manaCost")??0,data?.Value<string>("cardType")??string.Empty,
          definition?["cardText"]?.Value<string>("rulesText")??string.Empty,art,
          minion?.Value<int?>("attack")??weapon?.Value<int?>("attack")??0,
          minion?.Value<int?>("health")??0,
          weapon?.Value<int?>("durability")??location?.Value<int?>("durability")??0);
      }).ToList();
    }

    private static string BuildProductionDeckJson(string deckId, string playerId)
    {
      var catalog = JObject.Parse(ReadSeedArtifact("catalog-snapshot.json"));
      var cards = catalog["cards"] as JArray ?? throw new InvalidOperationException("Seed catalog has no cards array.");
      var entries = new JArray(cards.OfType<JObject>().Select(card => new JObject
      {
        ["cardId"] = card.Value<string>("cardId"),
        ["cardVersionId"] = card.Value<string>("cardVersionId"),
        ["count"] = 2
      }));
      return new JObject
      {
        ["deckId"] = deckId,
        ["playerId"] = playerId,
        ["catalogSnapshotId"] = CatalogSnapshotId,
        ["rulesetProfileId"] = RulesetProfileId,
        ["cards"] = entries
      }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static bool RawHas(string rawJson, string property, string expected)
    {
      var token = TryParse(rawJson)?[property];
      return string.Equals(token?.ToString(), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool RawArrayContains(string rawJson, string property, string expected)
    {
      var token = TryParse(rawJson)?[property] as JArray;
      return token?.Any(item => string.Equals(item.ToString(), expected, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static bool RawNestedArrayObjectHas(string rawJson, string property, string nestedProperty, string expected)
    {
      var token = TryParse(rawJson)?[property] as JArray;
      return token?.OfType<JObject>().Any(item =>
        string.Equals(item[nestedProperty]?.ToString(), expected, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static JObject TryParse(string rawJson)
    {
      if (string.IsNullOrWhiteSpace(rawJson))
      {
        return null;
      }

      try
      {
        return JObject.Parse(rawJson);
      }
      catch
      {
        return null;
      }
    }

    private const string RulesetProfileJson = @"{
  ""id"": ""resolved-unity-debug"",
  ""sourceProfileId"": ""ruleset_mvp_core_v1"",
  ""sourceProfileVersion"": ""0.2.0"",
  ""resolvedAt"": ""2026-07-02T00:00:00Z"",
  ""resolvedBy"": ""unity-debug-client"",
  ""resolvedHash"": ""sha256:unity-debug-profile"",
  ""formatId"": ""mvp"",
  ""cardSetId"": ""unity-debug-set"",
  ""enabledMechanics"": [],
  ""disabledMechanics"": [],
  ""formatMechanicStates"": [],
  ""allowedEffectKinds"": [""SIMPLE_PLAY_EFFECT""],
  ""allowedActions"": [""PLAY_CARD"", ""END_TURN"", ""CONCEDE""],
  ""allowedKeywords"": [],
  ""allowedSourceZones"": [""HAND"", ""DECK"", ""BOARD"", ""GRAVEYARD"", ""SET_ASIDE""],
  ""allowedPhases"": [""ON_PLAY""],
  ""allowedPools"": [],
  ""allowedCardTypes"": [""MINION_CARD""],
  ""allowedEntityTypes"": [""MINION""],
  ""allowedRarities"": [""COMMON""],
  ""allowedClasses"": [""neutral""],
  ""allowedTribes"": [],
  ""limits"": {
    ""maxComplexityScore"": 5,
    ""maxRandomnessScore"": 0,
    ""maxEventCountPerCard"": 8,
    ""maxEventCountPerTurn"": 64,
    ""maxRepeatCount"": 1,
    ""maxGeneratedCardsPerEffect"": 0,
    ""maxHistoryQueryScope"": ""MATCH"",
    ""maxActionsPerEffect"": 1,
    ""maxEffectsPerCard"": 1,
    ""maxChoiceCount"": 0,
    ""maxDiscoverOfferCount"": 0,
    ""maxBoardSummonsPerAction"": 1,
    ""maxDrawPerAction"": 0,
    ""maxManaCostReduction"": 0,
    ""maxTemporaryCostReduction"": 0,
    ""maxPermanentCostReduction"": 0,
    ""maxBoardSize"": 7,
    ""maxHandSize"": 10
  },
  ""validationPolicy"": {
    ""policyId"": ""unity-debug-validation"",
    ""schemaValidation"": true,
    ""deserializationValidation"": true,
    ""semanticValidation"": true,
    ""ruleValidation"": true,
    ""staticAnalysis"": true,
    ""failOnUnknownMechanic"": true,
    ""failOnDeprecatedMechanic"": true,
    ""failOnDisabledMechanic"": true,
    ""allowLegacyDeprecatedMechanics"": false
  },
  ""balancePolicy"": {
    ""policyId"": ""unity-debug-balance"",
    ""requireBalanceReport"": false,
    ""requireHumanReviewAboveRisk"": ""HIGH"",
    ""autoRejectRiskFlags"": [],
    ""maxSimulationWinrateDelta"": 1.0,
    ""maxComplexityScore"": 5,
    ""maxRandomnessSwingScore"": 0,
    ""maxEventCountDelta"": 8.0,
    ""minSimulationSampleSize"": 0,
    ""confidenceInterval"": 0.95,
    ""baselineDeckSetId"": ""unity-debug"",
    ""seedSetId"": ""unity-debug"",
    ""outlierHandlingPolicy"": ""NONE""
  },
  ""simulationPolicy"": {
    ""policyId"": ""unity-debug-simulation"",
    ""requiredScenarioSets"": [],
    ""requiredEdgeCases"": [],
    ""minRandomBotGames"": 0,
    ""minBaselineDeckGames"": 0,
    ""requireReplayHashConsistency"": true,
    ""maxTurns"": 20,
    ""maxEventCount"": 256
  },
  ""publicationPolicy"": {
    ""policyId"": ""unity-debug-publication"",
    ""requireValidationPass"": true,
    ""requireSimulationPass"": false,
    ""requireReplayPass"": false,
    ""requireBalanceReview"": false,
    ""requireHumanApproval"": false,
    ""allowAutoPublish"": true,
    ""allowExperimentalPublication"": true,
    ""allowedPublicationStatuses"": [""PUBLISHED""]
  },
  ""legacyPolicy"": {
    ""allowDeprecatedMechanicsForReplay"": false,
    ""allowDeprecatedMechanicsForNewCards"": false,
    ""allowDisabledMechanicsForReplay"": false,
    ""allowDisabledMechanicsForNewCards"": false,
    ""replayProfilePinningRequired"": true
  }
}";

    private const string CatalogJson = @"{
  ""snapshotId"": ""unity-debug-snapshot"",
  ""rulesetProfileId"": ""ruleset_mvp_core_v1"",
  ""rulesetProfileHash"": ""sha256:unity-debug-profile"",
  ""createdAt"": ""2026-07-02T00:00:00Z"",
  ""cards"": [
    {
      ""cardId"": ""unity-debug-minion"",
      ""cardVersionId"": ""unity-debug-minion-v1"",
      ""schemaVersion"": ""card-dsl-0.2.0"",
      ""engineVersion"": ""engine-bootstrap-0.3.0"",
      ""catalogSnapshotId"": ""unity-debug-snapshot"",
      ""generationPipelineVersion"": ""manual-unity-debug"",
      ""rulesetProfileId"": ""ruleset_mvp_core_v1"",
      ""rulesetProfileHash"": ""sha256:unity-debug-profile"",
      ""publishedAt"": ""2026-07-02T00:00:00Z"",
      ""definition"": {
        ""cardId"": ""unity-debug-minion"",
        ""cardVersion"": ""unity-debug-minion-v1"",
        ""schemaVersion"": ""card-dsl-0.2.0"",
        ""engineVersion"": ""engine-bootstrap-0.3.0"",
        ""primitiveRegistryVersion"": ""primitive-registry-bootstrap-0.2.0"",
        ""rulesetProfileId"": ""ruleset_mvp_core_v1"",
        ""catalogSnapshotId"": ""unity-debug-snapshot"",
        ""staticData"": {
          ""name"": ""Unity Debug Minion"",
          ""cardType"": ""MINION_CARD"",
          ""classTags"": [""neutral""],
          ""rarity"": ""COMMON"",
          ""manaCost"": 1,
          ""keywords"": [],
          ""tribes"": [],
          ""collectible"": true,
          ""formatTags"": [""mvp""]
        },
        ""entity"": {
          ""minion"": {
            ""attack"": 1,
            ""health"": 1,
            ""baseKeywords"": [],
            ""tribes"": [],
            ""canAttack"": true
          },
          ""spell"": null,
          ""weapon"": null,
          ""location"": null,
          ""heroCard"": null,
          ""heroPower"": null
        },
        ""effects"": [],
        ""cardText"": {
          ""rulesText"": ""A public debug minion for Unity smoke play."",
          ""flavorText"": ""Debug only.""
        },
        ""balanceMetadata"": {
          ""complexityScore"": 1,
          ""randomnessScore"": 0,
          ""riskFlags"": []
        },
        ""assetMetadata"": {
          ""visualAssetRef"": ""placeholder://unity-debug-minion"",
          ""audioManifestId"": null,
          ""assetManifestId"": null
        },
        ""authoringMetadata"": {
          ""createdBy"": ""unity-debug-client"",
          ""createdAt"": ""2026-07-02T00:00:00Z"",
          ""generationPipelineVersion"": ""manual-unity-debug"",
          ""sourceRefs"": []
        }
      }
    }
  ]
}";

    private const string DeckOneJson = @"{
  ""deckId"": ""unity-debug-deck-p1"",
  ""playerId"": ""player-1"",
  ""catalogSnapshotId"": ""unity-debug-snapshot"",
  ""rulesetProfileId"": ""ruleset_mvp_core_v1"",
  ""cards"": [
    { ""cardId"": ""unity-debug-minion"", ""count"": 10 }
  ]
}";

    private const string DeckTwoJson = @"{
  ""deckId"": ""unity-debug-deck-p2"",
  ""playerId"": ""player-2"",
  ""catalogSnapshotId"": ""unity-debug-snapshot"",
  ""rulesetProfileId"": ""ruleset_mvp_core_v1"",
  ""cards"": [
    { ""cardId"": ""unity-debug-minion"", ""count"": 10 }
  ]
}";
  }
}
