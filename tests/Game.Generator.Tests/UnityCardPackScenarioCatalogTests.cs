using Game.Shared.Primitives;
using Game.Application.Capabilities;
using Game.UnityCardPackExporter;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Game.Generator.Tests;

public sealed class UnityCardPackScenarioCatalogTests
{
  [Fact]
  public void DefaultCatalogCoversRequiredUnityBaselineScenarios()
  {
    var scenarios = UnityCardPackScenarioCatalog.CreateDefault();

    Assert.Equal(12, scenarios.Count);
    Assert.Equal(scenarios.Count, scenarios.Select(scenario => scenario.Slug).Distinct(StringComparer.Ordinal).Count());

    var coverage = scenarios.SelectMany(scenario => scenario.Coverage).ToHashSet(StringComparer.Ordinal);
    foreach (var required in new[]
             {
               "vanilla-minion",
               "battlecry",
               "deathrattle",
               "targeted-spell",
               "random-target-spell",
               "discover-choice",
               "damage-prevention",
               "weapon-card",
               "location-card",
               "aura-modifier",
               "heal",
               "resurrect"
             })
    {
      Assert.Contains(required, coverage);
    }
  }

  [Fact]
  public void DefaultCatalogIncludesDisplayRelevantCardTypesAndMediaPrompts()
  {
    var scenarios = UnityCardPackScenarioCatalog.CreateDefault();
    var cardTypes = scenarios.Select(scenario => scenario.Output.CardType).ToHashSet();

    Assert.Contains(CardType.MinionCard, cardTypes);
    Assert.Contains(CardType.SpellCard, cardTypes);
    Assert.Contains(CardType.WeaponCard, cardTypes);
    Assert.Contains(CardType.LocationCard, cardTypes);

    foreach (var scenario in scenarios)
    {
      Assert.False(string.IsNullOrWhiteSpace(scenario.Output.VisualPrompt.Prompt));
      Assert.False(string.IsNullOrWhiteSpace(scenario.Output.AnimationPrompt.Prompt));
      Assert.NotNull(scenario.Output.AudioPrompt);
      Assert.False(string.IsNullOrWhiteSpace(scenario.Output.AudioPrompt!.Prompt));
      Assert.False(string.IsNullOrWhiteSpace(scenario.UnityComment));
    }
  }

  [Fact]
  public void ExporterOptionsDefaultToCuratedModeAndAllowExplicitProductionMode()
  {
    var defaults = UnityCardPackOptions.Parse([]);
    var production = UnityCardPackOptions.Parse(["--mode", "production"]);
    var unknown = UnityCardPackOptions.Parse(["--mode", "native"]);
    var stress = UnityCardPackOptions.Parse(["--mode", "production", "--count", "50"]);
    var llmOnly = UnityCardPackOptions.Parse(["--mode", "production", "--skip-assets"]);

    Assert.Equal("curated", defaults.Mode);
    Assert.Equal("production", production.Mode);
    Assert.Equal("curated", unknown.Mode);
    Assert.Equal(50, stress.Count);
    Assert.True(llmOnly.SkipAssets);
    Assert.True(defaults.ProviderTimeoutSeconds > 0);
    Assert.Equal(0, defaults.MaxProviderRetries);
    Assert.Equal(Path.Combine("artifacts", "generator-failures"), defaults.FailureRoot);
  }

  [Fact]
  public async Task FailureArtifactWriterPreservesBoundedFailureDiagnostics()
  {
    var root = Path.Combine(Path.GetTempPath(), $"ccg-generator-failure-test-{Guid.NewGuid():N}");
    var options = UnityCardPackOptions.Parse([
      "--output-root", Path.Combine(root, "pack"),
      "--failure-root", Path.Combine(root, "failures"),
      "--run-name", "timeout-smoke",
      "--provider-timeout-seconds", "5",
      "--max-provider-retries", "0"
    ]);
    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
      WriteIndented = true,
      Converters = { new JsonStringEnumConverter() }
    };
    var trace = new UnityPackProviderCallTrace(
      "production-roll-001",
      "structured",
      "http://127.0.0.1:8011/v1/structured-card",
      "qwen3.5:latest",
      1200,
      300,
      80,
      5,
      0,
      false,
      "TIMEOUT_PROVIDER_REQUEST",
      "provider_timeout",
      5000,
      "raw:timeout");
    var timing = new UnityPackRunTiming(
      DateTimeOffset.Parse("2026-07-07T00:00:00Z"),
      DateTimeOffset.Parse("2026-07-07T00:00:05Z"),
      5000,
      [],
      null,
      "production-roll-001",
      0,
      0,
      0);

    var failureRoot = await Program.WriteFailureArtifact(
      options,
      "timeout",
      "not_accepted",
      "execution-blocked-provider-timeout",
      "provider_timeout",
      "Provider call exceeded timeout.",
      [trace],
      timing,
      "prompt preview",
      "structured-provider",
      "raw:timeout",
      json);

    Assert.True(File.Exists(Path.Combine(failureRoot, "failure-report.json")));
    Assert.True(File.Exists(Path.Combine(failureRoot, "provider-call-trace.json")));
    Assert.True(File.Exists(Path.Combine(failureRoot, "prompt-preview.txt")));
    Assert.True(File.Exists(Path.Combine(failureRoot, "timing.json")));
    Assert.False(File.Exists(Path.Combine(failureRoot, "summary-report.json")));

    var report = JsonSerializer.Deserialize<UnityPackFailureReport>(
      await File.ReadAllTextAsync(Path.Combine(failureRoot, "failure-report.json"), TestContext.Current.CancellationToken),
      json);
    Assert.NotNull(report);
    Assert.Equal("timeout", report!.RunExecutionStatus);
    Assert.Equal("not_accepted", report.ArtifactAcceptanceStatus);
    Assert.True(report.FailureEvidenceAvailable);
    Assert.Equal("provider_timeout", report.FailureCode);
  }

  [Fact]
  public void StructuredTraceCarriesPreflightAndRepairDiffGovernance()
  {
    var trace = new StructuredTrace(
      RawLlmOutputValidImmediately: true,
      Repaired: false,
      RepairCount: 0,
      ValidationErrors: [],
      PreflightNormalized: true,
      PreflightDiff: [new StructuredTraceDiff("output.classTags", "replaced", "neutral", "mage")],
      RepairDiff: [],
      SafePreflightNormalizationCount: 1,
      MaterialPreflightRewriteCount: 0,
      NameRewrittenByPreflight: false,
      RulesTextRewrittenByPreflight: false,
      EffectsRewrittenByPreflight: false,
      ProviderRepairCount: 0,
      CreativeQualityRepairCount: 0,
      NameRepairCount: 0,
      NameRerollCount: 0,
      DeterministicNameFallbackUsed: false);

    Assert.True(trace.RawLlmOutputValidImmediately);
    Assert.True(trace.PreflightNormalized);
    Assert.Equal(1, trace.SafePreflightNormalizationCount);
    Assert.Equal(0, trace.MaterialPreflightRewriteCount);
    Assert.False(trace.NameRewrittenByPreflight);
    Assert.Equal(0, trace.ProviderRepairCount);
    Assert.Equal(0, trace.NameRepairCount);
    Assert.False(trace.DeterministicNameFallbackUsed);
    Assert.NotNull(trace.PreflightDiff);
    Assert.Contains(trace.PreflightDiff, diff => diff.Path == "output.classTags");
    Assert.Empty(trace.RepairDiff ?? []);
  }

  [Fact]
  public void RollReferenceEvidenceCarriesConformanceAndDistributions()
  {
    var roll = new UnityPackRollReference(
      EffectPresence: "effect",
      EffectComplexity: "moderate",
      PrimaryPrimitive: "DISCOVER",
      SecondaryPrimitive: "DRAW_CARD",
      CardTypeDirection: "SPELL_CARD",
      CostBand: "3-4",
      TargetPattern: "fixed pool choice without target",
      TimingWindow: "ON_PLAY from HAND",
      GameplayRole: "value",
      FlavorMood: "frost archive scholarship",
      NamingStyle: "named object",
      TextStyle: "flavored rules",
      MechanicalTwist: "amount variation",
      Unusualness: "uncommon twist");
    var conformance = new UnityPackRollConformance("adapted", HardIgnoredDirection: false, Diagnostics: ["card type adapted"]);
    var report = new UnityPackRollReferenceReport(
      new Dictionary<string, int> { ["adapted"] = 1 },
      new Dictionary<string, int> { ["DISCOVER"] = 1 },
      new Dictionary<string, int> { ["DRAW_CARD"] = 1 },
      new Dictionary<string, int> { ["SPELL_CARD"] = 1 },
      new Dictionary<string, int> { ["3-4"] = 1 },
      new Dictionary<string, int> { ["ON_PLAY from HAND"] = 1 },
      new Dictionary<string, int> { ["fixed pool choice without target"] = 1 },
      new Dictionary<string, int> { ["named object"] = 1 },
      new Dictionary<string, int> { ["flavored rules"] = 1 },
      new Dictionary<string, int> { ["amount variation"] = 1 },
      HardIgnoredDirections: 0,
      IgnoredOrInvalidSlugs: []);

    Assert.Equal("DISCOVER", roll.PrimaryPrimitive);
    Assert.Equal("adapted", conformance.Status);
    Assert.False(conformance.HardIgnoredDirection);
    Assert.Equal(1, report.PrimaryPrimitives["DISCOVER"]);
    Assert.Empty(report.IgnoredOrInvalidSlugs);
  }

  [Fact]
  public void GenerationReferenceContainsOnlyMechanicalDirection()
  {
    var roll = Program.RollCreativeReference("production-roll-001", 0);

    var reference = Program.BuildGenerationReference(roll);

    Assert.Contains("primary supported primitive", reference);
    Assert.Contains("target requirement family", reference);
    Assert.DoesNotContain("flavor", reference, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("naming style", reference, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("text style", reference, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("gameplay role", reference, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("unusualness", reference, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void ProductionMetadataDerivesLabelsFromFinalDsl()
  {
    var scenario = UnityCardPackScenarioCatalog.CreateDefault().Single(item => item.Slug == "archive-choice");

    var metadata = Program.DeriveProductionMetadata(scenario.Output, index: 3, rollConformanceStatus: "strong");
    var consistency = Program.CheckScenarioMetadataConsistency(
      metadata,
      scenario.Output,
      new UnityPackRollConformance("strong", HardIgnoredDirection: false, Diagnostics: []));

    Assert.Contains("type:SPELL_CARD", metadata.Labels);
    Assert.Contains("action:DISCOVER", metadata.Labels);
    Assert.Contains("effect:SIMPLE_PLAY_EFFECT", metadata.Labels);
    Assert.Contains("timing:ON_PLAY", metadata.Labels);
    Assert.Contains("target:fixed-pool-choice", metadata.Labels);
    Assert.DoesNotContain("aura-modifier", metadata.Labels);
    Assert.True(consistency.MatchesFinalDsl);
    Assert.Empty(consistency.Diagnostics);
  }

  [Fact]
  public void ProductionMetadataDetectsStaleScenarioLabels()
  {
    var scenario = UnityCardPackScenarioCatalog.CreateDefault().Single(item => item.Slug == "archive-choice");
    var stale = new UnityPackDerivedMetadata(
      "archive-choice-01",
      ["type:MINION_CARD", "action:APPLY_AURA", "roll-conformance:strong"],
      "MINION_CARD",
      ["APPLY_AURA"],
      ["AURA_EFFECT"],
      ["AURA_REFRESH"],
      "friendly-board",
      "board_aura",
      "moderate",
      "stale metadata");

    var consistency = Program.CheckScenarioMetadataConsistency(
      stale,
      scenario.Output,
      new UnityPackRollConformance("strong", HardIgnoredDirection: false, Diagnostics: []));

    Assert.False(consistency.MatchesFinalDsl);
    Assert.Contains(consistency.Diagnostics, diagnostic => diagnostic.Contains("SPELL_CARD", StringComparison.Ordinal));
    Assert.Contains(consistency.Diagnostics, diagnostic => diagnostic.Contains("DISCOVER", StringComparison.Ordinal));
  }

  [Fact]
  public void LocationRollsUseSupportedAbilityVariants()
  {
    var locationRolls = Enumerable.Range(0, 80)
      .Select(index => Program.RollCreativeReference($"roll-{index}", index))
      .Where(roll => roll.PrimaryPrimitive == "LOCATION_VALUE")
      .ToArray();

    Assert.NotEmpty(locationRolls);
    Assert.Contains(locationRolls, roll => roll.SecondaryPrimitive is not null and not "DRAW_CARD");
    Assert.All(locationRolls, roll => Assert.Contains(roll.SecondaryPrimitive, new[] { "DRAW_CARD", "HEAL", "DISCOVER", "PREVENT_DAMAGE", "DAMAGE_DISTRIBUTION", "RESURRECT" }));
  }

  [Fact]
  public void RollReferenceUsesCompatibleMechanicalMatrix()
  {
    var rolls = Enumerable.Range(0, 120)
      .Select(index => Program.RollCreativeReference($"roll-{index}", index))
      .ToArray();

    Assert.All(rolls, roll =>
    {
      if (roll.PrimaryPrimitive == "APPLY_AURA")
      {
        Assert.Equal("MINION_CARD", roll.CardTypeDirection);
        Assert.Contains("AURA_REFRESH", roll.TimingWindow);
      }
      if (roll.PrimaryPrimitive == "DEATHRATTLE_DRAW")
      {
        Assert.Equal("MINION_CARD", roll.CardTypeDirection);
        Assert.Contains("DEATHRATTLE_PHASE", roll.TimingWindow);
      }
      if (roll.PrimaryPrimitive == "LOCATION_VALUE")
      {
        Assert.Equal("LOCATION_CARD", roll.CardTypeDirection);
        Assert.Contains("LOCATION_ABILITY_EFFECT", roll.TimingWindow);
      }
    });
  }

  [Fact]
  public void ProductionRollSlotsDoNotCarryCuratedUnityScenarios()
  {
    var slots = Program.CreateProductionRollSlots(3);

    Assert.Equal(["production-roll-001", "production-roll-002", "production-roll-003"], slots.Select(slot => slot.Slug).ToArray());
    Assert.All(slots, slot => Assert.Null(slot.Scenario));
    Assert.Equal([0, 1, 2], slots.Select(slot => slot.Index).ToArray());
  }

  [Fact]
  public void BalanceQualityReportCarriesJustifiedFindings()
  {
    var finding = new UnityPackBalanceQualityFinding(
      "slow-value-card",
      "Slow Value Card",
      "tooWeak",
      AcceptedWithJustification: true,
      "Accepted because low stats are compensated by supported effect family DRAW_CARD.");
    var report = new UnityPackBalanceQualityReport(
      new Dictionary<string, int> { ["6+"] = 1 },
      new Dictionary<string, int> { ["0-1"] = 1 },
      new Dictionary<string, int> { ["2"] = 1 },
      new Dictionary<string, int> { ["SpellCard"] = 1 },
      new Dictionary<string, int> { ["DRAW_CARD"] = 1 },
      new Dictionary<string, int>(),
      [],
      [],
      [],
      [finding],
      UnjustifiedFindingCount: 0);

    Assert.Empty(report.TooWeakCandidates);
    Assert.Single(report.Findings);
    Assert.True(report.Findings[0].AcceptedWithJustification);
    Assert.Equal(0, report.UnjustifiedFindingCount);
  }

  [Fact]
  public void CreativeQualityReportBlocksRepeatedNamesRulesAndMechanicalDuplicates()
  {
    var cards = new[]
    {
      CreativeInput("Astral Cinder Sentinel", "Deal 3 damage to a minion.", 3, "player-chosen", "DEAL_DAMAGE"),
      CreativeInput("Astral Cinder Sentinel", "Deal 3 damage to a minion.", 3, "player-chosen", "DEAL_DAMAGE")
    };

    var report = Program.AnalyzeCreativeQuality(cards);

    Assert.Contains(report.Violations, violation => violation.StartsWith("duplicate-names:", StringComparison.Ordinal));
    Assert.Contains(report.Violations, violation => violation.StartsWith("mechanically-identical-cards:", StringComparison.Ordinal));
    Assert.Equal(1, report.RulesText.RepeatedRulesTextCount);
    Assert.Equal(1, report.RulesText.RepeatedNormalizedRulesTextPatternCount);
  }

  [Fact]
  public void CreativeQualityReportAllowsCanonicalRepeatedRulesWhenMechanicalShapeDiffers()
  {
    var rules = new[]
    {
      "Discover a card.",
      "Discover a card.",
      "Discover one of three cards.",
      "Draw a card.",
      "Draw a card from your deck.",
      "Put a card from your deck into your hand.",
      "Deal 2 damage to a minion.",
      "Deal 3 damage to a chosen minion.",
      "Deal 4 damage to any minion.",
      "Deal 1 damage to a minion.",
      "Prevent the next 2 damage to a friendly minion.",
      "Prevent 3 damage to a friendly minion.",
      "Choose a friendly minion; prevent the next 1 damage to it.",
      "Restore 2 Health to a friendly minion.",
      "Heal a friendly minion for 3.",
      "Choose a friendly minion and restore 4 Health to it.",
      "Randomly split 3 damage among enemy minions.",
      "Deal 4 total damage randomly among enemy minions.",
      "Resurrect a 2/2 friendly minion that died this game.",
      "Deathrattle: Draw a card."
    };
    var roles = new[]
    {
      "DISCOVER", "DISCOVER", "DISCOVER", "DRAW_CARD", "DRAW_CARD", "DRAW_CARD",
      "DEAL_DAMAGE", "DEAL_DAMAGE", "DEAL_DAMAGE", "DEAL_DAMAGE",
      "PREVENT_DAMAGE", "PREVENT_DAMAGE", "PREVENT_DAMAGE",
      "HEAL", "HEAL", "HEAL", "DAMAGE_DISTRIBUTION", "DAMAGE_DISTRIBUTION",
      "RESURRECT", "DRAW_CARD"
    };
    var cards = Enumerable.Range(0, rules.Length)
      .Select(index => CreativeInput(
        $"Sample Card {index}",
        rules[index],
        1 + index % 7,
        index == 1 ? "none-or-auto" : "player-chosen",
        roles[index]))
      .ToArray();

    var report = Program.AnalyzeCreativeQuality(cards);

    Assert.Equal(1, report.RulesText.RepeatedRulesTextCount);
    Assert.Equal(0, report.RulesText.MechanicallyIdenticalCardsCount);
    Assert.True(report.ViolationCount == 0, string.Join(", ", report.Violations));
  }

  [Fact]
  public void CreativeQualityReportBlocksRulesTextFlavorLeakage()
  {
    var cards = Enumerable.Range(0, 20)
      .Select(index => CreativeInput(
        $"Sample Card {index}",
        index == 0 ? "Sample Card 0 lets you draw a card." : $"Deal {index} damage to any minion.",
        1 + index % 7,
        index == 0 ? "none-or-auto" : "player-chosen",
        index == 0 ? "DRAW_CARD" : "DEAL_DAMAGE"))
      .ToArray();

    var report = Program.AnalyzeCreativeQuality(cards);

    Assert.Equal(1, report.RulesText.FlavorLeakageCount);
    Assert.Contains(report.Violations, violation => violation.StartsWith("rules-flavor-leakage:", StringComparison.Ordinal));
  }

  [Fact]
  public void CreativeQualityReportBlocksProceduralNameNaturalness()
  {
    var cards = Enumerable.Range(0, 20)
      .Select(index => CreativeInput(
        index == 0 ? "Amber Bell Quiet Advantage" : $"Natural Card {index}",
        $"Deal {index + 1} damage to any minion.",
        1 + index % 7,
        "player-chosen",
        "DEAL_DAMAGE"))
      .ToArray();

    var report = Program.AnalyzeCreativeQuality(cards);

    Assert.Contains(report.Naming.NaturalnessViolations, violation => violation.Contains("Amber Bell Quiet Advantage", StringComparison.Ordinal));
    Assert.Contains(report.Violations, violation => violation.StartsWith("unnatural-names:", StringComparison.Ordinal));
  }

  [Fact]
  public void CreativeQualityReportBlocksPackLevelNameMatrix()
  {
    var left = new[] { "Dessa", "Briar", "Eld", "Fenna", "Galen" };
    var right = new[] { "Fen", "Kettle", "Meadow", "Gate" };
    var cards = Enumerable.Range(0, 40)
      .Select(index =>
      {
        var l = left[index % left.Length];
        var r = right[(index / left.Length) % right.Length];
        var name = (index % 4) switch
        {
          0 => $"A {l} {r}",
          1 => $"{l} of {r}",
          2 => $"{l} and {r}",
          _ => $"{l} at {r}"
        };
        return CreativeInput(
          name,
          $"Deal {index % 4 + 1} damage to any minion.",
          1 + index % 7,
          "player-chosen",
          $"ROLE_{index}");
      })
      .ToArray();

    var report = Program.AnalyzeCreativeQuality(cards);

    Assert.True(report.Naming.MatrixLikeNameCount >= 35);
    Assert.NotEmpty(report.Naming.MatrixPatternViolations);
    Assert.Contains(report.Violations, violation => violation.StartsWith("name-matrix-", StringComparison.Ordinal));
  }

  [Fact]
  public void CreativeQualityReportAcceptsDistinctNamesRulesAndVisualIdentity()
  {
    var names = new[]
    {
      "Orla of the Low Tide", "Merrin Sparkhand", "Cael Under Glass", "Vessa Rootspeaker", "Irin the Patient",
      "Tovan Fieldwright", "Sella at First Light", "Branik Cloudrunner", "Elyr Keeps the Door", "Noma of Copperleaf",
      "Rusk Emberhand", "Aven Tide-Scribe", "Maelis of the Quiet Mile", "Korrin Lanternless", "Tessa at the Ford",
      "Yorr of the Ninth Step", "Luma Glasswake", "Cairn the Rain-Wise", "The Small Bright Key", "The Orchard Standard"
    };
    var rulePatterns = new[]
    {
      "Deal 1 damage to any minion.",
      "Deal 2 damage to a chosen minion.",
      "Deal 3 damage to a minion.",
      "Draw a card.",
      "Draw a card from your deck.",
      "Put a card from your deck into your hand.",
      "Restore 2 Health to a friendly minion.",
      "Heal a friendly minion for 3.",
      "Choose a friendly minion and restore 4 Health to it.",
      "Prevent the next 2 damage to a friendly minion.",
      "Prevent 3 damage to a friendly minion.",
      "Choose a friendly minion; prevent the next 1 damage to it.",
      "Randomly split 3 damage among enemy minions.",
      "Deal 4 total damage randomly among enemy minions.",
      "Split 5 damage randomly among enemy minions.",
      "Discover a card.",
      "Discover one of three cards.",
      "Discover a card and add it to your hand.",
      "Resurrect a friendly minion that died this game as a 2/2.",
      "Deathrattle: Draw a card."
    };
    var cards = Enumerable.Range(0, 60)
      .Select(index => CreativeInput(
        $"{names[index % names.Length]} {UniqueWord(index)}",
        rulePatterns[index % rulePatterns.Length],
        2 + index % 4,
        "player-chosen",
        $"ROLE_{index}"))
      .ToArray();

    var report = Program.AnalyzeCreativeQuality(cards);

    Assert.Equal(60, report.Naming.UniqueNames);
    Assert.True(report.RulesText.NormalizedUniqueRulesTextPatterns >= 18);
    Assert.True(report.ViolationCount == 0, string.Join(", ", report.Violations));
  }

  [Theory]
  [InlineData("Unity Test Relic", "Discover a card from the Unity test pool.", "DISCOVER", "technical-leakage:unity")]
  [InlineData("Quiet Guard", "No effect. 3/1 minion.", "VANILLA", "no-effect-text")]
  [InlineData("Brass Lance", "3/4 weapon.", "VANILLA", "statline-in-rules-text")]
  [InlineData("Marked Bolt", "Mark a minion; deal 5 damage to it.", "DEAL_DAMAGE", "unsupported-keyword:mark")]
  [InlineData("False Compass", "Choose a card from your deck.", "DISCOVER", "discover-pool-mismatch:deck")]
  public void CreativeQualityReportRejectsBadPlayerFacingRulesText(
    string name,
    string rulesText,
    string roleSignature,
    string expectedDiagnostic)
  {
    var report = Program.AnalyzeCreativeQuality([
      CreativeInput(name, rulesText, 3, "fixed-pool-choice", roleSignature)
    ]);

    Assert.Contains(report.Violations, violation => violation.StartsWith("rules-player-facing-violations:", StringComparison.Ordinal));
    Assert.Contains(report.RulesText.FlavorLeakageCards, diagnostic => diagnostic.Contains(expectedDiagnostic, StringComparison.Ordinal));
  }

  [Theory]
  [InlineData("Azure Mist Prism Discovery", "action-word:discovery")]
  [InlineData("Luminous Unmaking Draw", "action-word:draw")]
  [InlineData("PZK-Guardian", "raw-acronym")]
  public void CreativeQualityReportRejectsMechanicalOrAcronymNames(string name, string expectedDiagnostic)
  {
    var report = Program.AnalyzeCreativeQuality([
      CreativeInput(name, "Draw a card.", 3, "none-or-auto", "DRAW_CARD")
    ]);

    Assert.Contains(report.Violations, violation => violation.StartsWith("mechanical-name-leakage:", StringComparison.Ordinal));
    Assert.Contains(report.Naming.NaturalnessViolations, diagnostic => diagnostic.Contains(expectedDiagnostic, StringComparison.Ordinal));
  }

  [Fact]
  public void ThemeTaxonomyCoversBroadPublicShowcaseFamilies()
  {
    var taxonomy = Program.GetThemeTaxonomy();
    var ids = taxonomy.Select(family => family.ThemeFamilyId).ToHashSet(StringComparer.Ordinal);

    Assert.True(taxonomy.Count >= 16);
    foreach (var required in new[] { "cosmic", "mundane-military", "urban-rogues", "nautical", "desert-nomads", "machine-clockwork", "undead-graveyard", "plague-alchemy", "folklore" })
    {
      Assert.Contains(required, ids);
    }
    Assert.All(taxonomy, family =>
    {
      Assert.NotEmpty(family.NameTokens);
      Assert.NotEmpty(family.VisualMotifs);
      Assert.NotEmpty(family.CompatibleMechanics);
      Assert.NotEmpty(family.ExampleNames);
    });
  }

  [Fact]
  public void ThemeDiversityReportBlocksFrostArchiveDominance()
  {
    var cards = Enumerable.Range(0, 36)
      .Select(index => CreativeInput(
        index < 18 ? $"Frost Archive Warden {index}" : $"Dockside Duelist {index}",
        index % 2 == 0 ? "Deal 2 damage to a minion." : "Draw a card.",
        2 + index % 4,
        "player-chosen",
        index % 2 == 0 ? "DEAL_DAMAGE" : "DRAW_CARD"))
      .ToArray();

    var report = Program.AnalyzeThemeDiversity(cards);

    Assert.Contains(report.OverusedTokenClusters, pair => pair.Key is "frost" or "archive");
    Assert.Contains(report.Violations, violation => violation.StartsWith("overused-theme-token:", StringComparison.Ordinal));
  }

  [Fact]
  public void ThemeDiversityReportAcceptsBroadShowcaseThemeMix()
  {
    var names = new[]
    {
      "Star-Maw Apostle", "Old Road Bannerman", "Mooncalf Herder", "Dockside Duelist", "Ninth Bell Acolyte",
      "Glassback Leviathan", "Salt Road Seer", "Iron Orchard", "Clockwork Pilgrim", "Crypt Tollkeeper",
      "Ashfield Marshal", "Living Storm", "Moth in the Mirror", "Queen's Page", "Plague Doctor Tonic", "Goblin Debt Collector",
      "Reefside Quartermaster", "Brasslane Engineer", "Dune-Cup Runner", "Moss-Crown Host", "Wight of the Old Well",
      "Court of Small Knives", "Leechglass Apprentice", "Bridge Toll Witch"
    };
    var cards = names.Select((name, index) => CreativeInput(
      name,
      index % 3 == 0 ? "Discover a card." : index % 3 == 1 ? "Deal 2 damage to a minion." : "Restore 2 Health to a friendly minion.",
      2 + index % 5,
      "player-chosen",
      index % 3 == 0 ? "DISCOVER" : index % 3 == 1 ? "DEAL_DAMAGE" : "HEAL")).ToArray();

    var report = Program.AnalyzeThemeDiversity(cards);

    Assert.True(report.ThemeDistribution.Count >= 8);
    Assert.True(report.ViolationCount == 0, string.Join(", ", report.Violations));
  }

  [Fact]
  public void MechanicCoverageReportsUnsupportedEngineMechanicsAsGeneratorBlocked()
  {
    var registry = new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry();

    var report = Program.BuildMechanicCoverageReport(registry);

    Assert.Contains(report.Entries, entry => entry.MechanicId is "DEAL_DAMAGE" && entry.GeneratorProductionSupported);
    Assert.Contains(report.Entries, entry => entry.Status is "generator-blocked" && entry.Blocker is not null);
    Assert.All(report.Entries.Where(entry => entry.Status is "generator-blocked"), entry => Assert.False(entry.GeneratorSupported));
  }

  [Fact]
  public void GeneratorMechanicRegistryRequiresAllProductionSupportPieces()
  {
    var entries = Program.GeneratorMechanicRegistry();
    var discover = entries.Single(entry => entry.MechanicId == "DISCOVER");
    var blocked = entries.Single(entry => entry.MechanicId == "TAKE_EXTRA_TURN");

    Assert.False(discover.GeneratorProductionSupported);
    Assert.Equal("generator-blocked", discover.Status);
    Assert.Contains("pool context", discover.Blocker, StringComparison.OrdinalIgnoreCase);
    Assert.False(blocked.GeneratorProductionSupported);
    Assert.Equal("generator-blocked", blocked.Status);
    Assert.False(blocked.CanonicalRulesTextSupported);
  }

  private static UnityPackCreativeQualityInput CreativeInput(
    string name,
    string rulesText,
    int manaCost,
    string targetPattern,
    string roleSignature) =>
    new(
      name,
      rulesText,
      manaCost,
      "SpellCard:-:-",
      targetPattern,
      roleSignature,
      "test flavor",
      "test naming",
      "test text",
      "test twist",
      $"visual {name}");

  private static string UniqueWord(int index)
  {
    const string letters = "abcdefghijklmnopqrstuvwxyz";
    return $"{letters[index / letters.Length]}{letters[index % letters.Length]}";
  }
}
