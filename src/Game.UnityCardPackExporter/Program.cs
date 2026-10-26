using System.IO.Compression;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Game.Application.Capabilities;
using Game.Generator.Generation;
using Game.Generator.Providers;
using Game.Generator.Publication;
using Game.Generator.Validation;
using Game.Infrastructure.Generation;
using Game.Shared.Assets;
using Game.Shared.Catalog;
using Game.Shared.Dsl;
using Game.Shared.Engine;
using Game.Shared.Mechanics;
using Game.Shared.Primitives;
using Game.Shared.Ruleset;

namespace Game.UnityCardPackExporter;

public static class Program
{
  private const string PipelineVersion = "unity-test-card-pack-pipeline-v1";
  private const string ContextVersion = "unity-test-card-pack-context-v1";

  public static async Task<int> Main(string[] args)
  {
    var options = UnityCardPackOptions.Parse(args);
    var runStopwatch = Stopwatch.StartNew();
    var runStartedAt = DateTimeOffset.UtcNow;
    var runTiming = new UnityPackRunTiming(runStartedAt, null, 0, [], null, null, 0, 0, 0);
    var providerCallTraces = new List<UnityPackProviderCallTrace>();
    var cardTimings = new List<UnityPackCardTiming>();
    string? lastPrompt = null;
    string? validationStage = "startup";
    string? lastProviderOutput = null;
    var runExecutionStatus = "completed";
    string? failureArtifactPath = null;
    var fullOutputRoot = Path.GetFullPath(options.OutputRoot);
    try
    {
      if (Path.GetPathRoot(fullOutputRoot) == fullOutputRoot)
      {
        throw new InvalidOperationException("Unity card pack output root cannot be a filesystem root.");
      }
      if (Directory.Exists(options.OutputRoot))
      {
        Directory.Delete(options.OutputRoot, recursive: true);
      }
      Directory.CreateDirectory(options.OutputRoot);
      Directory.CreateDirectory(Path.Combine(options.OutputRoot, "cards"));
      Directory.CreateDirectory(Path.Combine(options.OutputRoot, "assets", "images"));
      Directory.CreateDirectory(Path.Combine(options.OutputRoot, "assets", "animations"));
      Directory.CreateDirectory(Path.Combine(options.OutputRoot, "assets", "audio"));

      var createdAt = DateTimeOffset.UtcNow;
      var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
      {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
      };

      validationStage = "provider-health";
      var profile = CreateProfile();
      var context = new GenerationContextBuilder().Build(profile, CreateMechanics(profile.EnabledMechanics));
      var providerHealth = await CheckProviderHealth(options);
      var providerFailures = new List<UnityPackProviderFailure>();
      var structuredProvider = new HttpStructuredCardGenerationProvider(
        new HttpClient { Timeout = TimeSpan.FromSeconds(options.ProviderTimeoutSeconds) },
        Config("local-ollama-structured", options.StructuredModel, options.StructuredEndpoint, options.ProviderTimeoutSeconds, maxRetries: options.MaxProviderRetries));
      var imageProvider = new HttpGeneratedAssetProvider(new HttpClient(), Config("local-stable-diffusion-image", options.ImageModel, options.ImageEndpoint, options.AssetProviderTimeoutSeconds, maxRetries: options.Mode is "production" ? 1 : 0));
      var animationProvider = new HttpGeneratedAssetProvider(new HttpClient(), Config("local-stable-diffusion-animation", options.AnimationModel, options.AnimationEndpoint, options.AssetProviderTimeoutSeconds, maxRetries: options.Mode is "production" ? 1 : 0));
      var audioProvider = new HttpGeneratedAssetProvider(new HttpClient(), Config("local-procedural-audio", options.AudioModel, options.AudioEndpoint, options.AssetProviderTimeoutSeconds, maxRetries: options.Mode is "production" ? 1 : 0));
    var validator = GeneratedCardValidationPipeline.CreateDefault();
    var balanceEvaluator = new GeneratedCardBalanceEvaluator();
    var publisher = new GeneratedCardPublisher();
    var manifestBuilder = new GeneratedAssetManifestBuilder();
    var registry = new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry();

    var scenarioCatalog = options.Mode is "production" ? [] : UnityCardPackScenarioCatalog.CreateDefault();
    var requested = options.Mode is "production"
      ? CreateProductionRollSlots(options.Count)
      : ExpandScenarios(scenarioCatalog, options.Count);
    var cards = new List<UnityPackCardReport>();
    var publishedCardVersions = new List<CardVersion>();
    var rejected = new List<UnityPackRejectedCard>();
    var rawValidImmediately = 0;
    var repairedCount = 0;
    var effectTypes = new SortedSet<string>(StringComparer.Ordinal);
    var cardTypes = new SortedSet<string>(StringComparer.Ordinal);
    var runLocalMechanicalKeys = new HashSet<string>(StringComparer.Ordinal);

    foreach (var scenarioRun in requested)
    {
      var scenario = scenarioRun.Scenario;
      var requestSlug = scenarioRun.Slug;
      var generationId = $"unity-pack-{requestSlug}";
      var diagnostics = new List<string>();
      var rollReference = options.Mode is "production"
        ? RollCreativeReference(requestSlug, scenarioRun.Index)
        : UnityPackRollReference.Curated(scenario!.Coverage);
      var generationReference = options.Mode is "production"
        ? BuildGenerationReference(rollReference)
        : $"Curated Unity scenario coverage: {string.Join(", ", scenario!.Coverage)}.";
      var creativeEntropyLetters = options.Mode is "production"
        ? BuildCreativeEntropyLetters(requestSlug, scenarioRun.Index)
        : [];
      var creativeEntropyText = creativeEntropyLetters.Count == 0
        ? string.Empty
        : string.Join(", ", creativeEntropyLetters);

      if (runStopwatch.Elapsed > TimeSpan.FromSeconds(options.RunTimeoutSeconds))
      {
        runExecutionStatus = "timeout";
        providerFailures.Add(new UnityPackProviderFailure(requestSlug, "run", "run_timeout", $"Run exceeded {options.RunTimeoutSeconds}s before card {scenarioRun.Index + 1}.", "timeout"));
        break;
      }

      var providerPrompt = options.Mode is "production"
        ? $"Generate one original playable CCG card as compact JSON. Mechanical direction:\n{generationReference}\nName seed letters: {creativeEntropyText}. Use only supported primitives. Final rules text will be canonicalized from DSL; do not include lore or internal terms."
        : $"Generate a production-like CCG card seed for Unity scenario '{scenario!.Slug}'. Return strict JSON only.";
      lastPrompt = providerPrompt;
      var providerContext = new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["generationMode"] = options.Mode is "production" ? "production-native" : "curated-unity-export",
        ["scenario"] = options.Mode is "production" ? "production-roll-slot" : scenario!.Slug,
        ["scenarioRun"] = requestSlug,
        ["scenarioIndex"] = scenarioRun.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["diversitySeed"] = DiversitySeed(requestSlug, scenarioRun.Index),
        ["generationReference"] = generationReference,
        ["roll.effectPresence"] = rollReference.EffectPresence,
        ["roll.effectComplexity"] = rollReference.EffectComplexity,
        ["roll.primaryPrimitive"] = rollReference.PrimaryPrimitive,
        ["roll.secondaryPrimitive"] = rollReference.SecondaryPrimitive ?? string.Empty,
        ["roll.cardTypeDirection"] = rollReference.CardTypeDirection,
        ["roll.costBand"] = rollReference.CostBand,
        ["roll.targetPattern"] = rollReference.TargetPattern,
        ["roll.timingWindow"] = rollReference.TimingWindow,
        ["creativeEntropy.nameSeedLetters"] = creativeEntropyText,
        ["rulesetProfileId"] = context.RulesetProfileId,
        ["catalogSnapshotId"] = options.CatalogSnapshotId,
        ["provider.timeoutSeconds"] = options.ProviderTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["provider.maxAttempts"] = options.MaxGenerationAttemptsPerCard.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["provider.maxRepairAttempts"] = options.MaxRepairAttemptsPerCard.ToString(System.Globalization.CultureInfo.InvariantCulture)
      };

      var cardStartedAt = DateTimeOffset.UtcNow;
      var cardStopwatch = Stopwatch.StartNew();
      var providerStopwatch = Stopwatch.StartNew();
      validationStage = $"structured-provider:{requestSlug}";
      var providerStructured = structuredProvider.Generate(new StructuredCardGenerationRequest(
        generationId,
        options.Mode is "production" ? "production-native-roll-reference-v1" : "unity-test-card-pack-structured-provider-v1",
        options.StructuredModel,
        providerPrompt,
        providerContext,
        TimeSpan.FromSeconds(options.ProviderTimeoutSeconds)));
      providerStopwatch.Stop();
      providerCallTraces.Add(new UnityPackProviderCallTrace(
        requestSlug,
        "structured",
        options.StructuredEndpoint,
        options.StructuredModel,
        providerPrompt.Length,
        EstimateTokenCount(providerPrompt),
        providerContext.Sum(pair => pair.Key.Length + pair.Value.Length),
        options.ProviderTimeoutSeconds,
        options.MaxProviderRetries,
        providerStructured.Succeeded,
        providerStructured.ErrorCode,
        ClassifyFailureCode(providerStructured.ErrorCode),
        Math.Round(providerStopwatch.Elapsed.TotalMilliseconds, 2),
        providerStructured.RawStructuredOutputRef));
      lastProviderOutput = providerStructured.RawStructuredOutputRef;

      if (!providerStructured.Succeeded)
      {
        var failure = ProviderFailure(requestSlug, "structured", providerStructured.ErrorCode, providerStructured.ErrorMessage);
        providerFailures.Add(failure);
        diagnostics.Add($"structured-provider:{failure.Classification}:{providerStructured.ErrorCode}:{providerStructured.ErrorMessage}");
        rejected.Add(new UnityPackRejectedCard(requestSlug, $"unity_test_{requestSlug.Replace("-", "_", StringComparison.Ordinal)}", scenario?.Output.Name ?? requestSlug, diagnostics));
        cardStopwatch.Stop();
        cardTimings.Add(new UnityPackCardTiming(requestSlug, cardStartedAt, DateTimeOffset.UtcNow, Math.Round(cardStopwatch.Elapsed.TotalMilliseconds, 2), false, diagnostics));
        if (cards.Count == 0)
        {
          runExecutionStatus = failure.Classification is "timeout" ? "timeout" : "failed";
          break;
        }
        continue;
      }

      var output = options.Mode is "production"
        ? providerStructured.Output ?? throw new InvalidOperationException("Production mode requires provider structured output.")
        : scenario!.Output;
      var trace = ReadStructuredTrace(providerStructured.RawStructuredOutputRef);
      if (options.Mode is "production")
      {
        output = ResolveRunLocalMechanicalDuplicate(output, scenarioRun.Index, runLocalMechanicalKeys, diagnostics);
      }
      output = CanonicalizePlayerFacingRulesText(output, diagnostics);
      var initialMetadata = options.Mode is "production"
        ? DeriveProductionMetadata(output, scenarioRun.Index, "pending")
        : UnityPackDerivedMetadata.Curated(scenarioRun.Slug, scenario!.Coverage, scenario.UnityComment);
      var slug = initialMetadata.Slug;
      var cardId = $"unity_test_{slug.Replace("-", "_", StringComparison.Ordinal)}";
      var cardVersionId = $"{cardId}_v1";
      if (trace?.RawLlmOutputValidImmediately is true)
      {
        rawValidImmediately++;
      }
      if (trace?.Repaired is true)
      {
        repairedCount++;
      }
      cardTypes.Add(output.CardType.ToString());
      foreach (var effectKind in output.Effects.Select(effect => effect.EffectKind.ToString()))
      {
        effectTypes.Add(effectKind);
      }

      var candidate = BuildCandidate(generationId, cardId, cardVersionId, options.CatalogSnapshotId, options.StructuredModel, createdAt, context, output);
      var balanceReport = new GeneratedCardBalanceReport(
        $"balance-report:{generationId}",
        context.BalancePolicy.PolicyId,
        SimulationWinrateDelta: 0,
        output.ComplexityScore,
        output.RandomnessScore,
        EventCountDelta: 0,
        RiskFlags: output.RiskFlags);
      var validationRequest = new GeneratedCardValidationRequest(generationId, PipelineVersion, ContextVersion, candidate, context, balanceReport);
      var validation = validator.Validate(validationRequest);
      var balance = balanceEvaluator.Evaluate(new GeneratedCardBalanceEvaluationRequest($"balance:{generationId}", candidate, context, balanceReport));
      var publication = publisher.Publish(new GeneratedCardPublicationRequest(validationRequest, validation, cardVersionId, options.CatalogSnapshotId, createdAt, registry));
      var semanticMismatchCount = validation.Diagnostics.Count(diagnostic => diagnostic.Code is GeneratedCardValidationCode.SemanticContractMismatch);
      var rollConformance = EvaluateRollConformance(rollReference, output, validation);
      var derivedMetadata = options.Mode is "production"
        ? DeriveProductionMetadata(output, scenarioRun.Index, rollConformance.Status)
        : initialMetadata;
      var metadataConsistency = CheckScenarioMetadataConsistency(derivedMetadata, output, rollConformance);
      var gameplaySmokeStatus = validation.FinalDecision is GeneratedCardValidationDecision.Accepted && balance.Decision is not GeneratedCardBalanceDecision.Rejected
        ? "PLAYABILITY_SMOKE_PASSED"
        : "PLAYABILITY_SMOKE_REJECTED";
      var replayStatus = gameplaySmokeStatus is "PLAYABILITY_SMOKE_PASSED"
        ? "REPLAY_CONTRACT_VERIFIED"
        : "REPLAY_CONTRACT_NOT_VERIFIED";

      diagnostics.AddRange(validation.Diagnostics.Select(d => $"validation:{d.Code}:{d.Path}:{d.Message}"));
      diagnostics.AddRange(balance.Signals.Select(s => $"balance:{s.Kind}:{s.Path}:{s.Message}"));
      diagnostics.AddRange(publication.Diagnostics.Select(d => $"publication:{d.Code}:{d.Path}:{d.Message}"));

      UnityPackAssetRef? image = null;
      UnityPackAssetRef? animation = null;
      UnityPackAssetRef? audio = null;
      if (!options.SkipAssets)
      {
        image = GenerateAndCopyAsset(imageProvider, manifestBuilder, generationId, cardId, cardVersionId, "STATIC_IMAGE", output.VisualPrompt, VisualAssetMode.StaticImage, null, createdAt, options.OutputRoot, Path.Combine("assets", "images", $"{slug}.png"), diagnostics, providerFailures, slug, "image");
        animation = GenerateAndCopyAsset(animationProvider, manifestBuilder, generationId, cardId, cardVersionId, "IDLE_ANIMATION", output.AnimationPrompt, VisualAssetMode.IdleAnimation, null, createdAt, options.OutputRoot, Path.Combine("assets", "animations", $"{slug}.gif"), diagnostics, providerFailures, slug, "animation");
        var audioPrompt = output.AudioPrompt ?? output.VisualPrompt;
        audio = GenerateAndCopyAsset(audioProvider, manifestBuilder, generationId, cardId, cardVersionId, "AUDIO", audioPrompt, null, AudioAssetMode.SfxOnly, createdAt, options.OutputRoot, Path.Combine("assets", "audio", $"{slug}.wav"), diagnostics, providerFailures, slug, "audio");
      }
      else
      {
        diagnostics.Add("assets:skipped:asset generation intentionally skipped for LLM-side naturalness run.");
      }

      var cardJsonPath = Path.Combine("cards", $"{slug}.json");
      var fullAssets = !options.SkipAssets && image is not null && animation is not null && audio is not null;
      var assetReadyForRun = options.SkipAssets || fullAssets;
      var gameplayProof = BuildGameplayProof(slug, cardId, output, validation, publication.Succeeded);
      if (publication.CardVersion is not null)
      {
        publishedCardVersions.Add(publication.CardVersion);
      }

      var cardReport = new UnityPackCardReport(
        slug,
        cardId,
        cardVersionId,
        output.Name,
        output.RulesText,
        output.CardType.ToString(),
        output.ManaCost,
        output.Attack,
        output.Health,
        derivedMetadata.Labels,
        derivedMetadata.Comment,
        derivedMetadata,
        metadataConsistency,
        rollReference,
        generationReference,
        rollConformance,
        output,
        providerStructured,
        trace,
        validation,
        balance,
        publication.Succeeded,
        publication.CardVersion?.CatalogSnapshotId ?? options.CatalogSnapshotId,
        gameplayProof,
        gameplayProof.ProofStatus,
        gameplayProof.ReplayVerified ? "REPLAY_CONTRACT_VERIFIED" : "REPLAY_CONTRACT_NOT_VERIFIED",
        new UnityPackAssetRef?[] { image, animation, audio }.Where(asset => asset is not null).Cast<UnityPackAssetRef>().ToArray(),
        fullAssets,
        semanticMismatchCount,
        gameplayProof.EffectResolved,
        gameplayProof.ReplayVerified,
        publication.Succeeded && assetReadyForRun && semanticMismatchCount == 0 && gameplaySmokeStatus is "PLAYABILITY_SMOKE_PASSED"
          ? "playable"
          : "rejected",
        diagnostics,
        cardJsonPath);

      await File.WriteAllTextAsync(Path.Combine(options.OutputRoot, cardJsonPath), JsonSerializer.Serialize(cardReport, json));
      cardStopwatch.Stop();
      cardTimings.Add(new UnityPackCardTiming(slug, cardStartedAt, DateTimeOffset.UtcNow, Math.Round(cardStopwatch.Elapsed.TotalMilliseconds, 2), true, diagnostics));
      cards.Add(cardReport);

      if (validation.FinalDecision is not GeneratedCardValidationDecision.Accepted || balance.Decision is GeneratedCardBalanceDecision.Rejected || !publication.Succeeded || !assetReadyForRun)
      {
        rejected.Add(new UnityPackRejectedCard(slug, cardId, output.Name, diagnostics));
      }
    }

    await WriteContactSheet(options.OutputRoot, cards);

    var similarityReport = BuildSimilarityReport(cards);
    var diversityReport = BuildDiversityReport(cards);
    var complexReport = BuildComplexEffectsReport(cards);
    var botVsBotProof = BuildRepresentativeBotVsBotProof(cards);
    var providerReliability = BuildProviderReliabilitySummary(providerHealth, providerFailures);
    var balanceQualityReport = BuildBalanceQualityReport(cards);
    var scenarioMetadataReport = BuildScenarioMetadataReport(cards);
    var creativeQualityReport = BuildCreativeQualityReport(cards);
    var themeDiversityReport = BuildThemeDiversityReport(cards);
    var mechanicCoverageReport = BuildMechanicCoverageReport(registry);
    var showcaseReadinessReport = BuildShowcaseReadinessReport(cards, themeDiversityReport, mechanicCoverageReport, balanceQualityReport, creativeQualityReport, diversityReport);
    var repairGovernanceReport = BuildRepairGovernanceReport(cards);
    var repairRates = BuildRepairRates(cards, repairGovernanceReport);
    runStopwatch.Stop();
    runTiming = BuildRunTiming(runStartedAt, runStopwatch.Elapsed, cardTimings, providerCallTraces);

    var manifest = new UnityPackManifest(
      "unity-test-card-pack-v1",
      createdAt,
      options.CatalogSnapshotId,
      cards.Count == requested.Count
      && similarityReport.ViolationCount == 0
      && diversityReport.ViolationCount == 0
      && scenarioMetadataReport.MismatchCount == 0
      && balanceQualityReport.UnjustifiedFindingCount == 0
      && creativeQualityReport.ViolationCount == 0
      && themeDiversityReport.ViolationCount == 0
      && repairGovernanceReport.MaterialPreflightRewriteCount == 0
      && repairGovernanceReport.NameRewrittenByPreflight == 0
      && repairGovernanceReport.DeterministicNameFallbackUsed == 0
      && cards.All(c => !c.RollConformance.HardIgnoredDirection)
      && cards.All(c => c.Validation.FinalDecision is GeneratedCardValidationDecision.Accepted && c.Balance.Decision is not GeneratedCardBalanceDecision.Rejected && c.PublicationSucceeded)
      && (options.SkipAssets || cards.All(c => c.HasFullAssetSet)),
      cards.Select(c => new UnityPackManifestCard(c.Slug, c.CardId, c.CardVersionId, c.Name, c.Type, c.Scenarios, c.JsonPath, c.Assets, c.Validation.FinalDecision.ToString(), c.Balance.Decision.ToString(), c.PublicationSucceeded)).ToArray());

    var mediaReadiness = BuildMediaReadinessReport(options.SkipAssets, cards, providerFailures);
    var readinessVerdicts = BuildReadinessVerdicts(manifest.UnityBaselineUsable, rejected, cards, repairRates, repairGovernanceReport, balanceQualityReport, scenarioMetadataReport, creativeQualityReport, similarityReport, diversityReport, complexReport, botVsBotProof, mediaReadiness, providerReliability);
    var finalVerdict = ResolveFinalVerdict(readinessVerdicts);
    if (!manifest.UnityBaselineUsable && providerFailures.Any(failure => failure.Classification is "timeout"))
    {
      finalVerdict = "execution-blocked-provider-timeout";
      runExecutionStatus = "timeout";
    }
    var artifactAcceptanceStatus = manifest.UnityBaselineUsable ? "accepted" : "not_accepted";
    if (!manifest.UnityBaselineUsable)
    {
      failureArtifactPath = await WriteFailureArtifact(
        options,
        runExecutionStatus,
        artifactAcceptanceStatus,
        finalVerdict,
        failureCode: providerFailures.LastOrDefault()?.ErrorCode ?? "generation_budget_exceeded",
        failureMessage: rejected.LastOrDefault()?.Reasons.LastOrDefault() ?? "Run did not produce accepted evidence.",
        providerCallTraces,
        runTiming,
        lastPrompt,
        validationStage,
        lastProviderOutput,
        json);
    }
    var summary = new UnityPackSummary(
      requested.Count,
      cards.Count,
      cards.Count(c => c.Validation.FinalDecision is GeneratedCardValidationDecision.Accepted),
      cards.Count(c => c.PublicationSucceeded),
      options.SkipAssets ? 0 : cards.Count(c => c.HasFullAssetSet),
      rejected,
      rawValidImmediately,
      repairedCount,
      repairGovernanceReport.SafePreflightNormalizationCount,
      repairGovernanceReport.MaterialPreflightRewriteCount,
      repairGovernanceReport.NameRewrittenByPreflight,
      repairGovernanceReport.RulesTextRewrittenByPreflight,
      repairGovernanceReport.EffectsRewrittenByPreflight,
      repairGovernanceReport.ProviderRepairCount,
      repairGovernanceReport.CreativeQualityRepairCount,
      repairGovernanceReport.NameRepairCount,
      repairGovernanceReport.NameRerollCount,
      repairGovernanceReport.DeterministicNameFallbackUsed,
      rejected.Count,
      cards.Sum(c => c.SemanticWarningCount),
      cards.Count(c => c.GameplaySmokePassed),
      cards.Count(c => c.ReplayVerified),
      effectTypes.ToArray(),
      cardTypes.ToArray(),
      BuildStressReport(options.Count, cards, rejected, similarityReport, options.SkipAssets),
      balanceQualityReport,
      scenarioMetadataReport,
      BuildRollReferenceReport(cards),
      similarityReport,
      diversityReport,
      themeDiversityReport,
      mechanicCoverageReport,
      showcaseReadinessReport,
      creativeQualityReport,
      repairGovernanceReport,
      repairRates,
      complexReport,
      botVsBotProof,
      providerReliability,
      options.SkipAssets,
      cards.Sum(card => card.Assets.Count),
      options.SkipAssets ? "not_applicable" : cards.All(card => card.HasFullAssetSet).ToString().ToLowerInvariant(),
      mediaReadiness,
      readinessVerdicts,
      runExecutionStatus,
      artifactAcceptanceStatus,
      finalVerdict,
      failureArtifactPath is not null,
      failureArtifactPath,
      runTiming,
      providerCallTraces,
      finalVerdict,
      manifest.UnityBaselineUsable,
      options.Mode is "production"
        ? [
          "Production mode uses providerStructured.Output directly; no Unity curated scenario DSL is substituted.",
          "Gameplay smoke/replay evidence is deterministic validation/publication contract smoke, not a full bot-vs-bot per-card match.",
          options.SkipAssets
            ? "Asset generation was intentionally skipped for this LLM-side naturalness run; no image, animation, or audio provider calls were made."
            : "Generated presentation media is baseline-quality local provider output for client integration, not final art direction."
        ]
        : [
          "Structured provider is called for real local provenance; exported Unity scenario DSL is curated because the compatibility local structured gateway normalizes varied requests to simple minion output.",
          "Simulation/replay evidence is profile-level validation evidence, not a full bot-vs-bot per-card gameplay proof.",
          "Generated presentation media is baseline-quality local provider output for client integration, not final art direction."
        ]);

    await File.WriteAllTextAsync(Path.Combine(options.OutputRoot, "manifest.json"), JsonSerializer.Serialize(manifest, json));
    await File.WriteAllTextAsync(Path.Combine(options.OutputRoot, "summary-report.json"), JsonSerializer.Serialize(summary, json));
    await File.WriteAllTextAsync(
      Path.Combine(options.OutputRoot, "catalog-snapshot.json"),
      JsonSerializer.Serialize(new CatalogSnapshot(
        options.CatalogSnapshotId,
        profile.SourceProfileId,
        profile.ResolvedHash,
        createdAt,
        publishedCardVersions,
        [profile.SourceProfileId],
        [profile.SourceProfileVersion],
        string.Equals(profile.SourceProfileId, "ruleset-production-core-v1", StringComparison.Ordinal)), json));
    await File.WriteAllTextAsync(
      Path.Combine(options.OutputRoot, "resolved-ruleset-profile.json"),
      JsonSerializer.Serialize(profile, json));

    var zipPath = $"{options.OutputRoot}.zip";
    if (File.Exists(zipPath))
    {
      File.Delete(zipPath);
    }
    ZipFile.CreateFromDirectory(options.OutputRoot, zipPath);

    Console.WriteLine($"Unity test card pack: {Path.GetFullPath(options.OutputRoot)}");
    Console.WriteLine($"Unity test card pack zip: {Path.GetFullPath(zipPath)}");
    Console.WriteLine($"mode={options.Mode}; generated={summary.SuccessfullyGenerated}/{summary.RequestedCount}; rawValid={summary.RawLlmOutputsValidImmediately}; repaired={summary.RepairedCount}; validated={summary.ValidatedCount}; published={summary.PublishedCount}; fullAssets={summary.FullAssetSetCount}; unityBaselineUsable={summary.UnityBaselineUsable}");

    return summary.UnityBaselineUsable ? 0 : 2;
    }
    catch (Exception exception)
    {
      runStopwatch.Stop();
      runExecutionStatus = exception is OperationCanceledException ? "cancelled" : "failed";
      runTiming = BuildRunTiming(runStartedAt, runStopwatch.Elapsed, cardTimings, providerCallTraces);
      failureArtifactPath = await WriteFailureArtifact(
        options,
        runExecutionStatus,
        "not_accepted",
        "execution-blocked",
        ClassifyException(exception),
        exception.Message,
        providerCallTraces,
        runTiming,
        lastPrompt,
        validationStage,
        lastProviderOutput,
        new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter() } });
      Console.Error.WriteLine($"Unity card pack export failed: {exception.Message}");
      Console.Error.WriteLine($"Failure artifact: {Path.GetFullPath(failureArtifactPath)}");
      return 2;
    }
  }

  private static IReadOnlyList<UnityCardPackScenarioRun> ExpandScenarios(
    IReadOnlyList<UnityCardPackScenario> scenarios,
    int count)
  {
    var runs = new List<UnityCardPackScenarioRun>(count);
    for (var i = 0; i < count; i++)
    {
      var scenario = scenarios[i % scenarios.Count];
      var slug = count <= scenarios.Count
        ? scenario.Slug
        : $"{scenario.Slug}-{(i / scenarios.Count) + 1:00}";
      runs.Add(new UnityCardPackScenarioRun(slug, scenario, i));
    }

    return runs;
  }

  public static IReadOnlyList<UnityCardPackScenarioRun> CreateProductionRollSlots(int count) =>
    Enumerable.Range(0, count)
      .Select(index => new UnityCardPackScenarioRun($"production-roll-{index + 1:000}", null, index))
      .ToArray();

  private static UnityPackGameplayProof BuildGameplayProof(
    string slug,
    string cardId,
    StructuredCardOutput output,
    GeneratedCardValidationPipelineReport validation,
    bool publicationSucceeded)
  {
    var accepted = validation.FinalDecision is GeneratedCardValidationDecision.Accepted && publicationSucceeded;
    var commandType = output.CardType is CardType.LocationCard ? "ACTIVATE_LOCATION" : "PLAY_CARD";
    var effectActions = output.Effects.SelectMany(effect => effect.Actions).Select(ActionType).Distinct().Order(StringComparer.Ordinal).ToArray();
    var statOnlyResolved = string.IsNullOrWhiteSpace(output.RulesText)
      && output.Effects.All(effect => effect.Actions.Count == 0);
    var effectResolved = accepted && (output.Effects.Count == 0 || effectActions.Length > 0 || statOnlyResolved);
    var refs = new[]
    {
      $"deck:{slug}:{cardId}",
      $"command:{slug}:{commandType}",
      $"effect-actions:{string.Join("+", effectActions.DefaultIfEmpty("NONE"))}",
      $"replay:{slug}:contract-verified"
    };

    return new UnityPackGameplayProof(
      "export-profile-scripted-headless-proof-v1",
      accepted,
      accepted,
      effectResolved,
      accepted,
      commandType,
      effectActions,
      refs,
      accepted ? [] : validation.Diagnostics.Select(diagnostic => $"{diagnostic.Code}:{diagnostic.Path}:{diagnostic.Message}").ToArray());
  }

  public static UnityPackDerivedMetadata DeriveProductionMetadata(
    StructuredCardOutput output,
    int index,
    string rollConformanceStatus)
  {
    var cardType = CardTypeToken(output.CardType.ToString());
    var actionTypes = output.Effects.SelectMany(effect => effect.Actions).Select(ActionType).Distinct().Order(StringComparer.Ordinal).ToArray();
    var effectKinds = output.Effects.Select(effect => EffectKindToken(effect.EffectKind)).Distinct().Order(StringComparer.Ordinal).ToArray();
    var phases = output.Effects.Select(effect => PhaseToken(effect.TimingPolicy.Phase)).Distinct().Order(StringComparer.Ordinal).ToArray();
    var targetPattern = ActualTargetPattern(output);
    var role = ActualGameplayRole(output);
    var complexity = ActualComplexity(output);
    var family = actionTypes.FirstOrDefault() ?? effectKinds.FirstOrDefault() ?? "VANILLA";
    var slug = $"{Slugify(output.Name)}-{family.ToLowerInvariant().Replace("_", "-", StringComparison.Ordinal)}-{index + 1:00}";
    var labels = new SortedSet<string>(StringComparer.Ordinal)
    {
      $"type:{cardType}",
      $"role:{role}",
      $"complexity:{complexity}",
      $"target:{targetPattern}",
      $"roll-conformance:{rollConformanceStatus}"
    };
    foreach (var actionType in actionTypes)
    {
      labels.Add($"action:{actionType}");
    }
    foreach (var effectKind in effectKinds)
    {
      labels.Add($"effect:{effectKind}");
    }
    foreach (var phase in phases)
    {
      labels.Add($"timing:{phase}");
    }
    if (actionTypes.Length == 0)
    {
      labels.Add("action:VANILLA");
    }

    return new UnityPackDerivedMetadata(
      slug,
      labels.ToArray(),
      cardType,
      actionTypes,
      effectKinds,
      phases,
      targetPattern,
      role,
      complexity,
      $"Production-native generated {cardType} with {family} family, {targetPattern} targeting, {string.Join("+", phases.DefaultIfEmpty("NO_TIMING"))} timing, and {rollConformanceStatus} roll conformance.");
  }

  public static UnityPackScenarioMetadataConsistency CheckScenarioMetadataConsistency(
    UnityPackDerivedMetadata metadata,
    StructuredCardOutput output,
    UnityPackRollConformance rollConformance)
  {
    var diagnostics = new List<string>();
    var labels = metadata.Labels.ToHashSet(StringComparer.Ordinal);
    var actualCardType = CardTypeToken(output.CardType.ToString());
    if (!labels.Contains($"type:{actualCardType}"))
    {
      diagnostics.Add($"missing actual card type label {actualCardType}");
    }
    foreach (var actionType in output.Effects.SelectMany(effect => effect.Actions).Select(ActionType).Distinct())
    {
      if (!labels.Contains($"action:{actionType}"))
      {
        diagnostics.Add($"missing actual action label {actionType}");
      }
    }
    foreach (var effectKind in output.Effects.Select(effect => EffectKindToken(effect.EffectKind)).Distinct())
    {
      if (!labels.Contains($"effect:{effectKind}"))
      {
        diagnostics.Add($"missing actual effect label {effectKind}");
      }
    }
    foreach (var phase in output.Effects.Select(effect => PhaseToken(effect.TimingPolicy.Phase)).Distinct())
    {
      if (!labels.Contains($"timing:{phase}"))
      {
        diagnostics.Add($"missing actual timing label {phase}");
      }
    }
    if (output.Effects.Count == 0 && !labels.Contains("action:VANILLA"))
    {
      diagnostics.Add("missing actual vanilla action label");
    }
    var targetPattern = ActualTargetPattern(output);
    if (!labels.Contains($"target:{targetPattern}"))
    {
      diagnostics.Add($"missing actual target label {targetPattern}");
    }
    var gameplayRole = ActualGameplayRole(output);
    if (!labels.Contains($"role:{gameplayRole}"))
    {
      diagnostics.Add($"missing actual gameplay role label {gameplayRole}");
    }
    var complexity = ActualComplexity(output);
    if (!labels.Contains($"complexity:{complexity}"))
    {
      diagnostics.Add($"missing actual complexity label {complexity}");
    }
    if (!labels.Contains($"roll-conformance:{rollConformance.Status}"))
    {
      diagnostics.Add($"missing roll conformance label {rollConformance.Status}");
    }

    return new UnityPackScenarioMetadataConsistency(diagnostics.Count == 0, diagnostics);
  }

  private static UnityPackStressReport BuildStressReport(
    int requestedCount,
    IReadOnlyList<UnityPackCardReport> cards,
    IReadOnlyList<UnityPackRejectedCard> rejected,
    UnityPackSimilarityReport similarityReport,
    bool assetsSkipped = false)
  {
    var validationFailures = cards.Count(card => card.Validation.FinalDecision is not GeneratedCardValidationDecision.Accepted);
    var publicationFailures = cards.Count(card => !card.PublicationSucceeded);
    var assetFailures = assetsSkipped ? 0 : cards.Count(card => !card.HasFullAssetSet);
    return new UnityPackStressReport(
      requestedCount,
      cards.Count,
      cards.Count(card => card.StructuredTrace?.RawLlmOutputValidImmediately is true),
      cards.Count(card => card.StructuredTrace?.Repaired is true),
      rejected.Count,
      cards.Sum(card => card.SemanticWarningCount),
      validationFailures,
      publicationFailures,
      assetFailures,
      CountBy(cards.SelectMany(card => card.GameplayProof.EffectActionTypes)),
      CountBy(cards.Select(card => card.Type)),
      similarityReport.DuplicateSimilarScore,
      similarityReport.ViolationCount,
      similarityReport.Violations);
  }

  private static UnityPackBalanceQualityReport BuildBalanceQualityReport(IReadOnlyList<UnityPackCardReport> cards) =>
    BuildBalanceQualityReportCore(cards);

  private static UnityPackBalanceQualityReport BuildBalanceQualityReportCore(IReadOnlyList<UnityPackCardReport> cards)
  {
    var tooWeak = cards.Select(AssessBalanceQuality).Where(finding => finding.Kind is "tooWeak").OrderBy(finding => finding.CardName, StringComparer.Ordinal).ToArray();
    var tooStrong = cards.Select(AssessBalanceQuality).Where(finding => finding.Kind is "tooStrong").OrderBy(finding => finding.CardName, StringComparer.Ordinal).ToArray();
    var valueEstimates = cards.Select(EstimateBalanceValue).OrderBy(estimate => estimate.Slug, StringComparer.Ordinal).ToArray();
    return new UnityPackBalanceQualityReport(
      CountBy(cards.Select(card => ManaBucket(card.ManaCost))),
      CountBy(cards.Select(card => StatBucket(card.Attack))),
      CountBy(cards.Select(card => StatBucket(card.Health))),
      CountBy(cards.Select(card => card.Type)),
      CountBy(cards.SelectMany(card => card.GameplayProof.EffectActionTypes)),
      CountBy(cards.SelectMany(card => card.Dsl.RiskFlags)),
      FindDuplicateNames(cards),
      tooWeak.Where(finding => !finding.AcceptedWithJustification).Select(finding => finding.CardName).ToArray(),
      tooStrong.Where(finding => !finding.AcceptedWithJustification).Select(finding => finding.CardName).ToArray(),
      tooWeak.Concat(tooStrong).ToArray(),
      tooWeak.Concat(tooStrong).Count(finding => !finding.AcceptedWithJustification),
      valueEstimates);
  }

  private static UnityPackScenarioMetadataReport BuildScenarioMetadataReport(IReadOnlyList<UnityPackCardReport> cards)
  {
    var mismatches = cards
      .Where(card => !card.ScenarioMetadataConsistency.MatchesFinalDsl)
      .Select(card => new UnityPackScenarioMetadataMismatch(card.Slug, card.Name, card.ScenarioMetadataConsistency.Diagnostics))
      .OrderBy(mismatch => mismatch.Slug, StringComparer.Ordinal)
      .ToArray();
    return new UnityPackScenarioMetadataReport(
      cards.Count,
      mismatches.Length,
      CountBy(cards.Select(card => card.DerivedMetadata.ActualCardType)),
      CountBy(cards.SelectMany(card => card.DerivedMetadata.ActualActionFamilies.DefaultIfEmpty("VANILLA"))),
      CountBy(cards.SelectMany(card => card.DerivedMetadata.ActualEffectKinds.DefaultIfEmpty("NONE"))),
      CountBy(cards.SelectMany(card => card.DerivedMetadata.ActualTimingWindows.DefaultIfEmpty("NO_TIMING"))),
      CountBy(cards.Select(card => card.DerivedMetadata.ActualTargetPattern)),
      CountBy(cards.Select(card => card.DerivedMetadata.ActualGameplayRole)),
      CountBy(cards.Select(card => card.DerivedMetadata.ActualComplexity)),
      CountBy(cards.Select(card => card.RollConformance.Status)),
      CountBy(cards
        .Where(card => card.DerivedMetadata.ActualCardType is "LOCATION_CARD")
        .SelectMany(card => card.DerivedMetadata.ActualActionFamilies.DefaultIfEmpty("VANILLA"))),
      mismatches);
  }

  private static UnityPackCreativeQualityReport BuildCreativeQualityReport(IReadOnlyList<UnityPackCardReport> cards) =>
    AnalyzeCreativeQuality(cards.Select(card => new UnityPackCreativeQualityInput(
      card.Name,
      card.Description,
      card.ManaCost,
      $"{card.Type}:{card.Attack?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}:{card.Health?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}",
      card.DerivedMetadata.ActualTargetPattern,
      RoleSignature(card),
      card.RollReference.FlavorMood,
      card.RollReference.NamingStyle,
      card.RollReference.TextStyle,
      card.RollReference.MechanicalTwist,
      card.Dsl.VisualPrompt.Prompt)).ToArray());

  private static UnityPackRepairGovernanceReport BuildRepairGovernanceReport(IReadOnlyList<UnityPackCardReport> cards)
  {
    var traces = cards.Select(card => card.StructuredTrace).Where(trace => trace is not null).Select(trace => trace!).ToArray();
    var creativeQualityRepairCount = cards.Sum(card => card.Diagnostics.Count(diagnostic => diagnostic.StartsWith("creative-quality-repair:", StringComparison.Ordinal)));
    var repairDiffs = cards.SelectMany(card => card.StructuredTrace?.RepairDiff ?? []).ToArray();
    var materialExamples = cards
      .SelectMany(card => (card.StructuredTrace?.MaterialPreflightDiff ?? [])
        .Select(diff => new UnityPackMaterialPreflightRewriteExample(card.Slug, card.Name, diff.Path, diff.Change, diff.Before?.ToString(), diff.After?.ToString(), "material-preflight-rewrite", true)))
      .Take(20)
      .ToArray();
    return new UnityPackRepairGovernanceReport(
      SafePreflightNormalizationCount: traces.Sum(trace => trace.SafePreflightNormalizationCount),
      MaterialPreflightRewriteCount: traces.Sum(trace => trace.MaterialPreflightRewriteCount),
      NameRewrittenByPreflight: traces.Count(trace => trace.NameRewrittenByPreflight),
      RulesTextRewrittenByPreflight: traces.Count(trace => trace.RulesTextRewrittenByPreflight),
      EffectsRewrittenByPreflight: traces.Count(trace => trace.EffectsRewrittenByPreflight),
      ProviderRepairCount: traces.Sum(trace => trace.ProviderRepairCount),
      CreativeQualityRepairCount: creativeQualityRepairCount,
      NameRepairCount: traces.Sum(trace => trace.NameRepairCount),
      NameRerollCount: traces.Sum(trace => trace.NameRerollCount),
      DeterministicNameFallbackUsed: traces.Count(trace => trace.DeterministicNameFallbackUsed),
      RulesTextRepairCount: repairDiffs.Count(diff => IsRulesTextPath(diff.Path)),
      EffectsRepairCount: repairDiffs.Count(diff => IsEffectsPath(diff.Path)),
      StatsCostRepairCount: repairDiffs.Count(diff => IsStatsOrCostPath(diff.Path)),
      CardsWithRepairedEffects: cards.Count(card => (card.StructuredTrace?.RepairDiff ?? []).Any(diff => IsEffectsPath(diff.Path))),
      CardsWithRepairedRulesText: cards.Count(card => (card.StructuredTrace?.RepairDiff ?? []).Any(diff => IsRulesTextPath(diff.Path))),
      MaxRepairCountPerCard: traces.Length == 0 ? 0 : traces.Max(trace => trace.RepairCount),
      MaterialPreflightRewriteExamples: materialExamples);
  }

  private static UnityPackRepairRateReport BuildRepairRates(
    IReadOnlyList<UnityPackCardReport> cards,
    UnityPackRepairGovernanceReport repairGovernance)
  {
    var total = Math.Max(cards.Count, 1);
    return new UnityPackRepairRateReport(
      RawValidRate: Rate(cards.Count(card => card.StructuredTrace?.RawLlmOutputValidImmediately is true), total),
      ProviderRepairRate: Rate(repairGovernance.ProviderRepairCount, total),
      MaterialRepairRate: Rate(repairGovernance.MaterialPreflightRewriteCount + repairGovernance.EffectsRepairCount + repairGovernance.StatsCostRepairCount, total),
      NameRepairRate: Rate(repairGovernance.NameRepairCount, total),
      RulesTextRepairRate: Rate(repairGovernance.RulesTextRepairCount, total),
      EffectsRepairRate: Rate(repairGovernance.EffectsRepairCount, total),
      StatsCostRepairRate: Rate(repairGovernance.StatsCostRepairCount, total),
      RawValidThresholdLocal: 0.60,
      RawValidThresholdProduction: 0.80,
      ProviderRepairThresholdProduction: 0.20,
      MaterialRepairThresholdProduction: 0.05,
      EffectsRepairThresholdProduction: 0.05);
  }

  private static double Rate(int count, int total) =>
    Math.Round(count / (double)Math.Max(total, 1), 4);

  private static bool IsRulesTextPath(string path) =>
    path.Contains("rulesText", StringComparison.OrdinalIgnoreCase);

  private static bool IsEffectsPath(string path) =>
    path.Contains("effects", StringComparison.OrdinalIgnoreCase)
    || path.Contains("actions", StringComparison.OrdinalIgnoreCase);

  private static bool IsStatsOrCostPath(string path) =>
    path.Contains("manaCost", StringComparison.OrdinalIgnoreCase)
    || path.Contains("attack", StringComparison.OrdinalIgnoreCase)
    || path.Contains("health", StringComparison.OrdinalIgnoreCase);

  private static UnityPackMediaReadinessReport BuildMediaReadinessReport(
    bool assetsSkipped,
    IReadOnlyList<UnityPackCardReport> cards,
    IReadOnlyList<UnityPackProviderFailure> providerFailures)
  {
    var mediaFailures = providerFailures.Count(failure => failure.ProviderRole is "image" or "animation" or "audio");
    return assetsSkipped
      ? new UnityPackMediaReadinessReport(
          AssetsSkipped: true,
          MediaProviderCalls: 0,
          HasFullAssetSet: "not_applicable",
          MediaReadiness: "not_applicable",
          MediaFailureCount: 0)
      : new UnityPackMediaReadinessReport(
          AssetsSkipped: false,
          MediaProviderCalls: cards.Sum(card => card.Assets.Count),
          HasFullAssetSet: cards.All(card => card.HasFullAssetSet) ? "true" : "false",
          MediaReadiness: cards.All(card => card.HasFullAssetSet) && mediaFailures == 0 ? "ready" : "not_ready",
          MediaFailureCount: mediaFailures);
  }

  private static UnityPackRunTiming BuildRunTiming(
    DateTimeOffset startedAt,
    TimeSpan duration,
    IReadOnlyList<UnityPackCardTiming> cardTimings,
    IReadOnlyList<UnityPackProviderCallTrace> providerCalls)
  {
    var completedDurations = providerCalls
      .Where(call => call.Succeeded)
      .Select(call => call.DurationMs)
      .Order()
      .ToArray();
    var slowestCard = cardTimings.OrderByDescending(card => card.DurationMs).FirstOrDefault()?.Slug;
    var slowestProvider = providerCalls.OrderByDescending(call => call.DurationMs).FirstOrDefault()?.Slug;
    return new UnityPackRunTiming(
      startedAt,
      startedAt.Add(duration),
      Math.Round(duration.TotalMilliseconds, 2),
      cardTimings,
      slowestCard,
      slowestProvider,
      Percentile(completedDurations, 0.50),
      Percentile(completedDurations, 0.95),
      Percentile(completedDurations, 0.99));
  }

  private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
  {
    if (sortedValues.Count == 0)
    {
      return 0;
    }

    var index = Math.Clamp((int)Math.Ceiling(sortedValues.Count * percentile) - 1, 0, sortedValues.Count - 1);
    return Math.Round(sortedValues[index], 2);
  }

  public static async Task<string> WriteFailureArtifact(
    UnityCardPackOptions options,
    string runExecutionStatus,
    string artifactAcceptanceStatus,
    string generatorReadinessVerdict,
    string failureCode,
    string failureMessage,
    IReadOnlyList<UnityPackProviderCallTrace> providerCallTraces,
    UnityPackRunTiming timing,
    string? promptPreview,
    string? validationStage,
    string? lastProviderOutput,
    JsonSerializerOptions json)
  {
    var safeName = Regex.Replace(options.RunName, "[^A-Za-z0-9_.-]+", "-");
    var root = Path.Combine(options.FailureRoot, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{safeName}");
    Directory.CreateDirectory(root);
    var report = new UnityPackFailureReport(
      runExecutionStatus,
      artifactAcceptanceStatus,
      generatorReadinessVerdict,
      FailureEvidenceAvailable: true,
      failureCode,
      failureMessage,
      options.OutputRoot,
      providerCallTraces,
      timing);
    await File.WriteAllTextAsync(Path.Combine(root, "failure-report.json"), JsonSerializer.Serialize(report, json));
    await File.WriteAllTextAsync(Path.Combine(root, "failure-report.md"), FailureMarkdown(report));
    await File.WriteAllTextAsync(Path.Combine(root, "provider-call-trace.json"), JsonSerializer.Serialize(providerCallTraces, json));
    await File.WriteAllTextAsync(Path.Combine(root, "run-options.json"), JsonSerializer.Serialize(options, json));
    await File.WriteAllTextAsync(Path.Combine(root, "prompt-metadata.json"), JsonSerializer.Serialize(new
    {
      promptCharacterCount = promptPreview?.Length ?? 0,
      promptTokenEstimate = EstimateTokenCount(promptPreview ?? string.Empty),
      options.StructuredModel,
      options.StructuredEndpoint,
      options.ProviderTimeoutSeconds,
      options.MaxProviderRetries,
      streaming = false,
      maxOutputTokens = 4096
    }, json));
    await File.WriteAllTextAsync(Path.Combine(root, "prompt-preview.txt"), promptPreview ?? string.Empty);
    await File.WriteAllTextAsync(Path.Combine(root, "validation-stage.txt"), validationStage ?? "unknown");
    await File.WriteAllTextAsync(Path.Combine(root, "last-provider-output.txt"), lastProviderOutput ?? string.Empty);
    await File.WriteAllTextAsync(Path.Combine(root, "stderr.log"), $"{failureCode}: {failureMessage}{Environment.NewLine}");
    await File.WriteAllTextAsync(Path.Combine(root, "timing.json"), JsonSerializer.Serialize(timing, json));
    await File.WriteAllTextAsync(Path.Combine(root, "attempts.json"), JsonSerializer.Serialize(providerCallTraces, json));
    await File.WriteAllTextAsync(Path.Combine(root, "environment.json"), JsonSerializer.Serialize(new
    {
      machine = Environment.MachineName,
      os = Environment.OSVersion.VersionString,
      dotnet = Environment.Version.ToString(),
      cwd = Environment.CurrentDirectory
    }, json));
    return root;
  }

  private static string FailureMarkdown(UnityPackFailureReport report) =>
    $"""
    # Generator Failure Report

    - runExecutionStatus: `{report.RunExecutionStatus}`
    - artifactAcceptanceStatus: `{report.ArtifactAcceptanceStatus}`
    - generatorReadinessVerdict: `{report.GeneratorReadinessVerdict}`
    - failureCode: `{report.FailureCode}`
    - failureMessage: {report.FailureMessage}
    - outputRoot: `{report.OutputRoot}`
    - providerCalls: {report.ProviderCallTraces.Count}
    - durationMs: {report.Timing.DurationMs}
    """;

  private static int EstimateTokenCount(string text) =>
    (int)Math.Ceiling(Math.Max(0, text.Length) / 4.0);

  private static string ClassifyFailureCode(string? code) =>
    code?.ToUpperInvariant() switch
    {
      null or "" => "unknown_provider_failure",
      var value when value.Contains("TIMEOUT", StringComparison.Ordinal) => "provider_timeout",
      var value when value.Contains("EMPTY", StringComparison.Ordinal) => "provider_empty_response",
      var value when value.Contains("JSON", StringComparison.Ordinal) => "provider_invalid_json",
      var value when value.Contains("SCHEMA", StringComparison.Ordinal) => "provider_schema_nonconformance",
      var value when value.Contains("SEMANTIC", StringComparison.Ordinal) => "provider_semantic_nonconformance",
      var value when value.Contains("REPAIR", StringComparison.Ordinal) => "repair_budget_exceeded",
      _ => "unknown_provider_failure"
    };

  private static string ClassifyException(Exception exception) =>
    exception switch
    {
      TaskCanceledException or TimeoutException => "provider_timeout",
      JsonException => "provider_invalid_json",
      OperationCanceledException => "run_timeout",
      _ => "unknown_provider_failure"
    };

  public static UnityPackCreativeQualityReport AnalyzeCreativeQuality(IReadOnlyList<UnityPackCreativeQualityInput> cards)
  {
    var nameGroups = CountBy(cards.Select(card => card.Name));
    var exactRulesGroups = CountBy(cards.Select(card => NormalizeText(card.RulesText)));
    var normalizedRulesGroups = CountBy(cards.Select(card => NormalizeRulesPattern(card.RulesText, card.Name)));
    var exactRepeatedRules = exactRulesGroups
      .Where(pair => pair.Value > 1)
      .OrderByDescending(pair => pair.Value)
      .ThenBy(pair => pair.Key, StringComparer.Ordinal)
      .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    var repeatedNormalizedRules = normalizedRulesGroups
      .Where(pair => pair.Value > 1)
      .OrderByDescending(pair => pair.Value)
      .ThenBy(pair => pair.Key, StringComparer.Ordinal)
      .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    var tokenCounts = CountBy(cards.SelectMany(card => TokenizeName(card.Name)))
      .Where(pair => pair.Value > 1)
      .OrderByDescending(pair => pair.Value)
      .ThenBy(pair => pair.Key, StringComparer.Ordinal)
      .Take(20)
      .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    var nameStructures = CountBy(cards.Select(card => NameStructure(card.Name)));
    var formulaicCount = cards.Count(card => NameStructure(card.Name) is "theme-modifier-noun");
    var formulaicPercent = cards.Count == 0 ? 0 : Math.Round((double)formulaicCount / cards.Count * 100, 2);
    var effectSignatureGroups = CountBy(cards.Select(card => card.RoleSignature));
    var mechanicalGroups = cards
      .GroupBy(card => $"{card.RoleSignature}|shape:{card.CardShape}|cost:{card.ManaCost}|target:{card.TargetPattern}|rules:{NormalizeText(card.RulesText)}", StringComparer.Ordinal)
      .Where(group => group.Count() > 1)
      .ToArray();
    var mechanicallyIdenticalCards = mechanicalGroups.Sum(group => group.Count() - 1);
    var nameWordCounts = cards.Select(card => NameWordCount(card.Name)).ToArray();
    var averageNameWords = nameWordCounts.Length == 0 ? 0 : Math.Round(nameWordCounts.Average(), 2);
    var maxNameWords = nameWordCounts.Length == 0 ? 0 : nameWordCounts.Max();
    var longNameCount = nameWordCounts.Count(count => count >= 8);
    var veryLongNameCount = nameWordCounts.Count(count => count >= 10);
    var naturalNameViolations = cards
      .SelectMany(card => NameNaturalnessViolations(card.Name).Select(violation => $"{card.Name}:{violation}"))
      .Order(StringComparer.Ordinal)
      .ToArray();
    var flavorLeakageRules = cards
      .Where(card => RulesTextHasFlavorLeakage(card.RulesText, card.Name))
      .Select(card => card.Name)
      .Order(StringComparer.Ordinal)
      .ToArray();
    var playerFacingRulesViolations = cards
      .SelectMany(card => PlayerFacingRulesTextViolations(card.RulesText, card.RoleSignature).Select(violation => $"{card.Name}:{violation}"))
      .Order(StringComparer.Ordinal)
      .ToArray();
    var mechanicalNameLeakage = cards
      .SelectMany(card => NameMechanicalLeakageViolations(card.Name).Select(violation => $"{card.Name}:{violation}"))
      .Order(StringComparer.Ordinal)
      .ToArray();
    var uniquePatternTarget = Math.Min(18, cards.Count);
    var maxRepeatedPatternCount = Math.Max(12, cards.Count / 6);
    var tokenThreshold = Math.Max(3, (int)Math.Floor(cards.Count * 0.05));
    var repeatedNameBigrams = CountBy(cards.SelectMany(card => NameBigrams(card.Name)))
      .Where(pair => pair.Value > 2)
      .OrderByDescending(pair => pair.Value)
      .ThenBy(pair => pair.Key, StringComparer.Ordinal)
      .ToArray();
    var matrixPatterns = cards
      .Select(card => MatrixNamePattern(card.Name))
      .Where(pattern => pattern is not null)
      .Select(pattern => pattern!)
      .ToArray();
    var matrixLikeNameCount = matrixPatterns.Length;
    var matrixPatternGroups = CountBy(matrixPatterns.Select(pattern => pattern.Structure));
    var matrixTerminalReuse = matrixPatterns
      .GroupBy(pattern => pattern.TerminalToken, StringComparer.Ordinal)
      .Where(group => group.Select(pattern => pattern.Structure).Distinct(StringComparer.Ordinal).Count() >= 3)
      .OrderByDescending(group => group.Count())
      .ThenBy(group => group.Key, StringComparer.Ordinal)
      .Take(8)
      .Select(group => $"{group.Key}:{group.Count()}/{group.Select(pattern => pattern.Structure).Distinct(StringComparer.Ordinal).Count()}structures")
      .ToArray();
    var matrixPrefixReuse = matrixPatterns
      .GroupBy(pattern => pattern.PrefixToken, StringComparer.Ordinal)
      .Where(group => group.Select(pattern => pattern.Structure).Distinct(StringComparer.Ordinal).Count() >= 3)
      .OrderByDescending(group => group.Count())
      .ThenBy(group => group.Key, StringComparer.Ordinal)
      .Take(8)
      .Select(group => $"{group.Key}:{group.Count()}/{group.Select(pattern => pattern.Structure).Distinct(StringComparer.Ordinal).Count()}structures")
      .ToArray();
    var matrixViolations = new List<string>();
    if (cards.Count >= 20 && matrixLikeNameCount >= Math.Ceiling(cards.Count * 0.35) && (matrixTerminalReuse.Length > 0 || matrixPrefixReuse.Length > 0))
    {
      matrixViolations.Add($"name-matrix-slot-reuse:{matrixLikeNameCount}/{cards.Count}:terminal={string.Join("|", matrixTerminalReuse)}:prefix={string.Join("|", matrixPrefixReuse)}");
    }
    if (cards.Count >= 20
      && matrixLikeNameCount >= Math.Ceiling(cards.Count * 0.60)
      && matrixPatterns.Select(pattern => pattern.TerminalToken).Distinct(StringComparer.Ordinal).Count() <= Math.Max(3, matrixLikeNameCount / 3))
    {
      matrixViolations.Add($"name-matrix-terminal-vocabulary:{matrixPatterns.Select(pattern => pattern.TerminalToken).Distinct(StringComparer.Ordinal).Count()}/{matrixLikeNameCount}");
    }
    var overusedNameTokens = tokenCounts
      .Where(pair => IsQualityToken(pair.Key) && pair.Value > tokenThreshold)
      .OrderByDescending(pair => pair.Value)
      .ThenBy(pair => pair.Key, StringComparer.Ordinal)
      .ToArray();
    var dominantNameStructures = nameStructures
      .Where(pair => cards.Count >= 80
        && IsTemplateLikeNameStructure(pair.Key)
        && pair.Value > Math.Max(3, (int)Math.Ceiling(cards.Count * 0.10)))
      .OrderByDescending(pair => pair.Value)
      .ThenBy(pair => pair.Key, StringComparer.Ordinal)
      .ToArray();
    var violations = new List<string>();
    if (nameGroups.Count != cards.Count)
    {
      violations.Add($"duplicate-names:{cards.Count - nameGroups.Count}");
    }
    if (averageNameWords > 5.2)
    {
      violations.Add($"average-name-words:{averageNameWords}");
    }
    if (longNameCount > Math.Max(1, (int)Math.Ceiling(cards.Count * 0.05)))
    {
      violations.Add($"long-names:{longNameCount}");
    }
    if (veryLongNameCount > 0)
    {
      violations.Add($"very-long-names:{veryLongNameCount}");
    }
    if (naturalNameViolations.Length > 0)
    {
      violations.Add($"unnatural-names:{naturalNameViolations.Length}");
    }
    if (mechanicalNameLeakage.Length > 0)
    {
      violations.Add($"mechanical-name-leakage:{mechanicalNameLeakage.Length}");
    }
    if (cards.Count >= 80 && overusedNameTokens.Length > 0)
    {
      violations.Add($"overused-name-tokens:{string.Join(",", overusedNameTokens.Select(pair => $"{pair.Key}:{pair.Value}"))}");
    }
    if (cards.Count >= 80 && repeatedNameBigrams.Length > 0)
    {
      violations.Add($"repeated-name-bigrams:{string.Join(",", repeatedNameBigrams.Select(pair => $"{pair.Key}:{pair.Value}"))}");
    }
    if (dominantNameStructures.Length > 0)
    {
      violations.Add($"dominant-name-structures:{string.Join(",", dominantNameStructures.Select(pair => $"{pair.Key}:{pair.Value}"))}");
    }
    violations.AddRange(matrixViolations);
    if (formulaicPercent > 35)
    {
      violations.Add($"formulaic-name-percent:{formulaicPercent}");
    }
    if (normalizedRulesGroups.Count < uniquePatternTarget)
    {
      violations.Add($"normalized-rules-patterns:{normalizedRulesGroups.Count}/{uniquePatternTarget}");
    }
    var overRepeatedPatterns = repeatedNormalizedRules.Where(pair => pair.Value > maxRepeatedPatternCount).ToArray();
    if (overRepeatedPatterns.Length > 0)
    {
      violations.Add($"over-repeated-normalized-rules:{string.Join(",", overRepeatedPatterns.Select(pair => $"{pair.Key}:{pair.Value}"))}");
    }
    if (flavorLeakageRules.Length > 0)
    {
      violations.Add($"rules-flavor-leakage:{flavorLeakageRules.Length}");
    }
    if (playerFacingRulesViolations.Length > 0)
    {
      violations.Add($"rules-player-facing-violations:{playerFacingRulesViolations.Length}");
    }
    if (mechanicallyIdenticalCards > 0)
    {
      violations.Add($"mechanically-identical-cards:{mechanicallyIdenticalCards}");
    }

    var naming = new UnityPackNamingDiversityReport(
      cards.Count,
      nameGroups.Count,
      formulaicCount,
      formulaicPercent,
      averageNameWords,
      maxNameWords,
      longNameCount,
      veryLongNameCount,
      naturalNameViolations.Length + mechanicalNameLeakage.Length,
      naturalNameViolations.Concat(mechanicalNameLeakage).Take(40).ToArray(),
      tokenCounts,
      nameStructures,
      CountBy(cards.Select(card => FirstToken(card.Name))).OrderByDescending(pair => pair.Value).Take(12).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
      CountBy(cards.Select(card => LastToken(card.Name))).OrderByDescending(pair => pair.Value).Take(12).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
      matrixLikeNameCount,
      matrixPatternGroups,
      matrixViolations.ToArray());
    var rules = new UnityPackRulesTextDiversityReport(
      cards.Count,
      exactRulesGroups.Count,
      exactRepeatedRules.Sum(pair => pair.Value - 1),
      exactRepeatedRules.Take(12).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
      normalizedRulesGroups.Count,
      repeatedNormalizedRules.Sum(pair => pair.Value - 1),
      repeatedNormalizedRules.Take(12).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
      flavorLeakageRules.Length + playerFacingRulesViolations.Length,
      flavorLeakageRules.Concat(playerFacingRulesViolations).Take(40).ToArray(),
      effectSignatureGroups,
      mechanicallyIdenticalCards);
    var flavor = new UnityPackFlavorDiversityReport(
      CountBy(cards.Select(card => card.FlavorMood)),
      CountBy(cards.Select(card => card.NamingStyle)),
      CountBy(cards.Select(card => card.TextStyle)),
      CountBy(cards.Select(card => card.MechanicalTwist)),
      cards.Select(card => card.VisualPrompt).Distinct(StringComparer.Ordinal).Count());
    return new UnityPackCreativeQualityReport(naming, rules, flavor, violations.Count, violations.Order(StringComparer.Ordinal).ToArray());
  }

  private static StructuredCardOutput ResolveRunLocalMechanicalDuplicate(
    StructuredCardOutput output,
    int scenarioIndex,
    ISet<string> seenMechanicalKeys,
    List<string> diagnostics)
  {
    var key = RunLocalMechanicalKey(output);
    if (seenMechanicalKeys.Add(key))
    {
      return output;
    }

    var originalKey = key;
    for (var attempt = 1; attempt <= 8; attempt++)
    {
      var repaired = ApplyRunLocalMechanicalVariation(output, scenarioIndex, attempt);
      key = RunLocalMechanicalKey(repaired);
      if (seenMechanicalKeys.Add(key))
      {
        diagnostics.Add($"creative-quality-repair:mechanical-duplicate-adjusted:attempt={attempt}:from={originalKey}:to={key}");
        return repaired;
      }

      output = repaired;
    }

    diagnostics.Add($"creative-quality-repair:mechanical-duplicate-unresolved:{originalKey}");
    seenMechanicalKeys.Add($"{key}:unresolved:{scenarioIndex}");
    return output;
  }

  private static StructuredCardOutput CanonicalizePlayerFacingRulesText(
    StructuredCardOutput output,
    List<string> diagnostics)
  {
    var canonical = BuildCanonicalRulesText(output);
    if (!string.Equals(output.RulesText ?? string.Empty, canonical, StringComparison.Ordinal))
    {
      diagnostics.Add($"rulesText-canonicalized:from={output.RulesText ?? string.Empty}:to={canonical}");
    }

    return output with { RulesText = canonical };
  }

  private static string BuildCanonicalRulesText(StructuredCardOutput output)
  {
    var effectTexts = output.Effects
      .Select(effect => BuildCanonicalEffectText(effect, output.CardType))
      .Where(text => !string.IsNullOrWhiteSpace(text))
      .ToArray();
    return string.Join(" ", effectTexts);
  }

  private static string BuildCanonicalEffectText(EffectDefinition effect, CardType cardType)
  {
    var actions = effect.Actions
      .Select(BuildCanonicalActionText)
      .Where(text => !string.IsNullOrWhiteSpace(text))
      .ToArray();
    if (actions.Length == 0)
    {
      return string.Empty;
    }

    var body = string.Join(" ", actions);
    return effect.EffectKind switch
    {
      EffectKind.DeathrattleEffect => $"Deathrattle: {body}",
      EffectKind.LocationAbilityEffect => $"Activate: {body}",
      EffectKind.SimplePlayEffect when cardType is CardType.MinionCard => $"Battlecry: {body}",
      _ => body
    };
  }

  private static string BuildCanonicalActionText(ActionDefinition action) =>
    action switch
    {
      DealDamageActionDefinition damage => $"Deal {FixedAmount(damage.Amount)} damage to a minion.",
      DamageDistributionActionDefinition damage => $"Randomly split {FixedAmount(damage.TotalAmount)} damage among enemy minions.",
      DrawCardActionDefinition draw => $"Draw {CardCountText(FixedAmount(draw.Count))}.",
      HealActionDefinition heal => $"Restore {FixedAmount(heal.Amount)} Health to a friendly minion.",
      PreventDamageActionDefinition prevent => $"Prevent the next {FixedAmount(prevent.Amount)} damage dealt to a friendly minion.",
      DiscoverActionDefinition => "Discover a card.",
      ResurrectActionDefinition resurrect => $"Resurrect a {FixedAmount(resurrect.Attack)}/{FixedAmount(resurrect.Health)} friendly minion.",
      ApplyAuraActionDefinition aura => $"Other friendly minions have +{FixedAmount(aura.Aura.Modifier.Value)} Attack.",
      _ => string.Empty
    };

  private static string CardCountText(int count) =>
    count == 1 ? "a card" : $"{count} cards";

  private static int FixedAmount(ValueExpression value) =>
    value.FixedValue ?? 0;

  private static string RunLocalMechanicalKey(StructuredCardOutput output)
  {
    var role = string.Join("+", output.Effects.SelectMany(effect => effect.Actions).Select(ActionType).DefaultIfEmpty("VANILLA").Order(StringComparer.Ordinal));
    var shape = $"{CardTypeToken(output.CardType.ToString())}:{output.Attack?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}:{output.Health?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}";
    return $"{role}|shape:{shape}|cost:{output.ManaCost}|target:{ActualTargetPattern(output)}|rules:{NormalizeText(output.RulesText)}";
  }

  private static StructuredCardOutput ApplyRunLocalMechanicalVariation(StructuredCardOutput output, int scenarioIndex, int attempt)
  {
    var amount = 1 + ((scenarioIndex * 11 + attempt * 3) % 5);
    var costDelta = attempt % 3 - 1;
    var nextCost = Math.Clamp(output.ManaCost + costDelta, 1, 7);
    var actions = output.Effects.SelectMany(effect => effect.Actions).Select(ActionType).ToArray();

    if (actions.Contains("DEAL_DAMAGE", StringComparer.Ordinal))
    {
      return output with
      {
        ManaCost = nextCost,
        RulesText = RunLocalRulesText("damage", amount, scenarioIndex, attempt),
        Effects = ReplaceActions(output.Effects, action => action is DealDamageActionDefinition deal
          ? deal with { Amount = FixedValue(amount) }
          : action)
      };
    }

    if (actions.Contains("DAMAGE_DISTRIBUTION", StringComparer.Ordinal))
    {
      amount = Math.Max(2, amount);
      return output with
      {
        ManaCost = nextCost,
        RulesText = RunLocalRulesText("random", amount, scenarioIndex, attempt),
        Effects = ReplaceActions(output.Effects, action => action is DamageDistributionActionDefinition damage
          ? damage with { TotalAmount = FixedValue(amount) }
          : action)
      };
    }

    if (actions.Contains("HEAL", StringComparer.Ordinal))
    {
      amount = Math.Max(2, amount);
      var hasDraw = actions.Contains("DRAW_CARD", StringComparer.Ordinal);
      return output with
      {
        ManaCost = nextCost,
        RulesText = RunLocalRulesText("heal", amount, scenarioIndex, attempt)
          + (hasDraw ? " " + RunLocalRulesText("draw", 1, scenarioIndex, attempt + 1) : string.Empty),
        Effects = ReplaceActions(output.Effects, action => action is HealActionDefinition heal
          ? heal with { Amount = FixedValue(amount) }
          : action)
      };
    }

    if (actions.Contains("PREVENT_DAMAGE", StringComparer.Ordinal))
    {
      return output with
      {
        ManaCost = nextCost,
        RulesText = RunLocalRulesText("prevent", amount, scenarioIndex, attempt),
        Effects = ReplaceActions(output.Effects, action => action is PreventDamageActionDefinition prevent
          ? prevent with { Amount = FixedValue(amount), Duration = "NEXT_DAMAGE", PreventionPolicy = "PREVENT_UP_TO_AMOUNT" }
          : action)
      };
    }

    if (actions.Contains("APPLY_AURA", StringComparer.Ordinal))
    {
      amount = 1 + ((scenarioIndex + attempt) % 3);
      return output with
      {
        ManaCost = nextCost,
        Attack = output.Attack.HasValue ? Math.Clamp(output.Attack.Value + (attempt % 2), 1, 7) : output.Attack,
        Health = output.Health.HasValue ? Math.Clamp(output.Health.Value + ((attempt + 1) % 2), 1, 8) : output.Health,
        RulesText = RunLocalRulesText("aura", amount, scenarioIndex, attempt),
        Effects = ReplaceActions(output.Effects, action => action is ApplyAuraActionDefinition aura
          ? aura with { Aura = aura.Aura with { Modifier = aura.Aura.Modifier with { Value = FixedValue(amount) } } }
          : action)
      };
    }

    if (actions.Contains("DRAW_CARD", StringComparer.Ordinal))
    {
      var drawCount = 1;
      return output with
      {
        ManaCost = nextCost,
        RulesText = output.Effects.Any(effect => effect.EffectKind is EffectKind.DeathrattleEffect)
          ? RunLocalRulesText("deathrattle", drawCount, scenarioIndex, attempt)
          : RunLocalRulesText("draw", drawCount, scenarioIndex, attempt),
        Effects = ReplaceActions(output.Effects, action => action is DrawCardActionDefinition draw
          ? draw with { Count = FixedValue(drawCount) }
          : action)
      };
    }

    if (actions.Contains("RESURRECT", StringComparer.Ordinal))
    {
      amount = 1 + ((scenarioIndex + attempt) % 3);
      return output with
      {
        ManaCost = nextCost,
        RulesText = RunLocalRulesText("resurrect", amount, scenarioIndex, attempt),
        Effects = ReplaceActions(output.Effects, action => action is ResurrectActionDefinition resurrect
          ? resurrect with { Attack = FixedValue(amount), Health = FixedValue(amount) }
          : action)
      };
    }

    if (actions.Contains("DISCOVER", StringComparer.Ordinal))
    {
      return output with
      {
        ManaCost = nextCost,
        RulesText = RunLocalRulesText("discover", 1, scenarioIndex, attempt)
      };
    }

    var attack = output.Attack.HasValue ? Math.Clamp(output.Attack.Value + (attempt % 3) - 1, 1, 7) : output.Attack;
    var health = output.Health.HasValue ? Math.Clamp(output.Health.Value + ((attempt + 1) % 3) - 1, 1, 8) : output.Health;
    var body = output.CardType is CardType.WeaponCard
      ? $"{attack}/{health} weapon"
      : $"{attack}/{health} minion";
    return output with
    {
      ManaCost = nextCost,
      Attack = attack,
      Health = health,
      RulesText = RunLocalRulesText("vanilla", 0, scenarioIndex, attempt, body)
    };
  }

  private static IReadOnlyList<EffectDefinition> ReplaceActions(
    IReadOnlyList<EffectDefinition> effects,
    Func<ActionDefinition, ActionDefinition> replace) =>
    effects.Select(effect => effect with { Actions = effect.Actions.Select(replace).ToArray() }).ToArray();

  private static ValueExpression FixedValue(int value) => new("FIXED", value, null);

  private static string RunLocalRulesText(string kind, int amount, int scenarioIndex, int attempt, string? extra = null)
  {
    var variants = kind switch
    {
      "damage" => new[]
      {
        $"Deal {amount} damage to a minion.",
        $"Deal {amount} damage to a chosen minion.",
        $"Mark a minion; deal {amount} damage to it.",
        $"Deal {amount} damage to any minion."
      },
      "random" => new[]
      {
        $"Deal {amount} damage randomly split among enemy minions.",
        $"Randomly split {amount} damage among enemy minions.",
        $"Deal {amount} total damage randomly among enemy minions.",
        $"Split {amount} damage randomly among enemy minions."
      },
      "heal" => new[]
      {
        $"Restore {amount} Health to a friendly minion.",
        $"Heal a friendly minion for {amount}.",
        $"Choose a friendly minion and restore {amount} Health to it.",
        $"Restore {amount} Health to one of your minions."
      },
      "prevent" => new[]
      {
        $"Prevent the next {amount} damage to a friendly minion.",
        $"Prevent {amount} damage to a friendly minion.",
        $"Choose a friendly minion; prevent the next {amount} damage to it.",
        $"Prevent the next {amount} damage dealt to a friendly minion."
      },
      "aura" => new[]
      {
        $"Your other minions have +{amount} Attack.",
        $"Your other minions have +{amount} Attack while this is in play.",
        $"Other friendly minions have +{amount} Attack.",
        $"Friendly minions other than this have +{amount} Attack."
      },
      "draw" => new[]
      {
        $"Draw {AmountText(amount)}.",
        $"Draw {AmountText(amount)} from your deck.",
        $"Put {AmountText(amount)} from your deck into your hand.",
        $"Draw {AmountText(amount)} into your hand."
      },
      "deathrattle" => new[]
      {
        $"Deathrattle: Draw {AmountText(amount)}.",
        $"Deathrattle: Draw {AmountText(amount)} from your deck.",
        $"Deathrattle: Put {AmountText(amount)} from your deck into your hand.",
        $"Deathrattle: Draw {AmountText(amount)} into your hand."
      },
      "discover" => new[]
      {
        "Discover a card.",
        "Discover one of three cards.",
        "Discover a card and add it to your hand.",
        "Discover a card."
      },
      "resurrect" => new[]
      {
        $"Resurrect a {amount}/{amount} friendly minion that died this game.",
        $"Resurrect the last friendly minion that died as a {amount}/{amount}.",
        $"Resurrect a friendly minion that died this game as a {amount}/{amount}.",
        $"Resurrect a {amount}/{amount} friendly minion from your graveyard."
      },
      "vanilla" => new[]
      {
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty
      },
      _ => [extra ?? "Resolve this effect."]
    };
    return variants[(scenarioIndex * 17 + attempt * 5 + amount) % variants.Length];
  }

  private static string AmountText(int amount) => amount == 1 ? "a card" : $"{amount} cards";

  private static UnityPackBalanceQualityFinding AssessBalanceQuality(UnityPackCardReport card)
  {
    var statTotal = (card.Attack ?? 0) + (card.Health ?? 0);
    var actionCount = card.GameplayProof.EffectActionTypes.Count;
    var hasValueEffect = card.GameplayProof.EffectActionTypes.Any(action => action is "DRAW_CARD" or "DISCOVER" or "RESURRECT" or "APPLY_AURA" or "PREVENT_DAMAGE" or "DAMAGE_DISTRIBUTION" or "DEAL_DAMAGE" or "HEAL");
    if (card.Balance.RiskTier is GeneratedCardBalanceRiskTier.High or GeneratedCardBalanceRiskTier.Critical)
    {
      return new UnityPackBalanceQualityFinding(card.Slug, card.Name, "tooStrong", false, $"Balance risk tier is {card.Balance.RiskTier}.");
    }
    if (card.Type is "SpellCard" && card.ManaCost >= 6 && !hasValueEffect)
    {
      return new UnityPackBalanceQualityFinding(card.Slug, card.Name, "tooWeak", false, "High-cost spell has no compensating supported value effect.");
    }
    if (card.Type is "SpellCard")
    {
      return new UnityPackBalanceQualityFinding(card.Slug, card.Name, "ok", true, "No obvious tooWeak/tooStrong signal.");
    }
    if (card.ManaCost >= 6 && statTotal <= 5 && !hasValueEffect)
    {
      return new UnityPackBalanceQualityFinding(card.Slug, card.Name, "tooWeak", false, "High-cost card has low stats and no compensating supported value effect.");
    }
    if (card.ManaCost >= 6 && statTotal <= 5 && hasValueEffect)
    {
      return new UnityPackBalanceQualityFinding(card.Slug, card.Name, "tooWeak", true, $"Accepted because low stats are compensated by supported effect family {string.Join("+", card.GameplayProof.EffectActionTypes)}.");
    }
    if (card.Type is "WeaponCard" && card.ManaCost >= 5 && statTotal <= 5)
    {
      return new UnityPackBalanceQualityFinding(card.Slug, card.Name, "tooWeak", false, "High-cost weapon has low attack plus durability and no extra effect.");
    }

    return new UnityPackBalanceQualityFinding(card.Slug, card.Name, "ok", true, "No obvious tooWeak/tooStrong signal.");
  }

  private static UnityPackBalanceEstimate EstimateBalanceValue(UnityPackCardReport card)
  {
    var statValue = card.Type switch
    {
      "MinionCard" => ((card.Attack ?? 0) + (card.Health ?? 0)) / 2.0,
      "WeaponCard" => (card.Attack ?? 0) * Math.Max(1, card.Health ?? 1) * 0.8,
      "LocationCard" => Math.Max(1, card.Health ?? 1) * 0.9,
      _ => 0
    };
    var actions = card.GameplayProof.EffectActionTypes;
    var effectValue = actions.Sum(action => action switch
    {
      "DRAW_CARD" => 1.8,
      "DISCOVER" => 2.1,
      "DEAL_DAMAGE" => 1.4,
      "DAMAGE_DISTRIBUTION" => 1.8,
      "HEAL" => 1.0,
      "PREVENT_DAMAGE" => 1.2,
      "RESURRECT" => 2.4,
      "APPLY_AURA" => 2.0,
      _ => 0.4
    });
    var repeatabilityModifier = card.Type is "LocationCard" ? 1.35 : 1.0;
    var targetFlexibilityModifier = card.DerivedMetadata.ActualTargetPattern is "none-or-auto" or "fixed-pool-choice" ? 1.0 : 1.15;
    var randomnessModifier = actions.Contains("DAMAGE_DISTRIBUTION", StringComparer.Ordinal) ? 0.9 : 1.0;
    var cardAdvantageModifier = actions.Any(action => action is "DRAW_CARD" or "DISCOVER" or "RESURRECT") ? 1.2 : 1.0;
    var boardImpactModifier = actions.Any(action => action is "DEAL_DAMAGE" or "DAMAGE_DISTRIBUTION" or "APPLY_AURA") ? 1.15 : 1.0;
    var survivabilityModifier = actions.Any(action => action is "HEAL" or "PREVENT_DAMAGE") ? 1.05 : 1.0;
    var riskModifier = repeatabilityModifier * targetFlexibilityModifier * cardAdvantageModifier;
    var total = Math.Round((statValue + effectValue) * randomnessModifier * boardImpactModifier * survivabilityModifier * repeatabilityModifier, 2);
    var target = Math.Round(Math.Max(1, card.ManaCost) * 1.45 + 0.5, 2);
    var delta = Math.Round(total - target, 2);
    var reasons = new List<string>();
    var category = Math.Abs(delta) <= 2.25 ? "low-risk" : delta < 0 ? "watchlist-weak" : "watchlist-strong";
    if (card.ManaCost <= 1 && ((card.Attack ?? 0) + (card.Health ?? 0)) >= 7)
    {
      category = "rejected-too-strong";
      reasons.Add("1-cost huge stats");
    }
    if (card.ManaCost <= 1 && actions.Count(action => action is "DRAW_CARD") >= 2)
    {
      category = "rejected-too-strong";
      reasons.Add("1-cost large draw");
    }
    if (card.Type is "LocationCard" && card.ManaCost <= 2 && actions.Any(action => action is "DEAL_DAMAGE" or "DAMAGE_DISTRIBUTION"))
    {
      category = "watchlist-strong";
      reasons.Add("cheap repeatable removal location");
    }
    if (card.Type is "WeaponCard" && card.ManaCost <= 3 && (card.Attack ?? 0) * Math.Max(1, card.Health ?? 1) >= 12)
    {
      category = "rejected-too-strong";
      reasons.Add("overpowered weapon efficiency");
    }
    if (card.ManaCost <= 2 && actions.Contains("RESURRECT", StringComparer.Ordinal))
    {
      category = "watchlist-strong";
      reasons.Add("too-cheap resurrection");
    }
    if (card.ManaCost >= 6 && total < target - 3)
    {
      category = "rejected-too-weak";
      reasons.Add("expensive low-value card");
    }
    if (reasons.Count == 0)
    {
      reasons.Add(delta > 2.25 ? "above cost target" : delta < -2.25 ? "below cost target" : "inside cost target band");
    }

    return new UnityPackBalanceEstimate(
      card.Slug,
      card.Name,
      Math.Round(statValue, 2),
      Math.Round(effectValue, 2),
      total,
      target,
      delta,
      Math.Round(repeatabilityModifier, 2),
      Math.Round(targetFlexibilityModifier, 2),
      Math.Round(randomnessModifier, 2),
      Math.Round(cardAdvantageModifier, 2),
      Math.Round(boardImpactModifier, 2),
      Math.Round(survivabilityModifier, 2),
      Math.Round(riskModifier, 2),
      category,
      string.Join("; ", reasons),
      category.StartsWith("rejected", StringComparison.Ordinal) ? "Adjust cost/stats/effect amount before showcase publication." : "No mandatory repair; monitor in playtesting.");
  }

  private static IReadOnlyDictionary<string, int> CountBy(IEnumerable<string> values) =>
    values
      .Where(value => !string.IsNullOrWhiteSpace(value))
      .GroupBy(value => value, StringComparer.Ordinal)
      .OrderBy(group => group.Key, StringComparer.Ordinal)
      .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

  private static IReadOnlyList<string> FindDuplicateNames(IReadOnlyList<UnityPackCardReport> cards) =>
    cards
      .GroupBy(card => card.Name, StringComparer.OrdinalIgnoreCase)
      .Where(group => group.Count() > 1)
      .Select(group => $"{group.Key}:{group.Count()}")
      .Order(StringComparer.Ordinal)
      .ToArray();

  private static string ManaBucket(int manaCost) =>
    manaCost switch
    {
      <= 1 => "0-1",
      2 => "2",
      3 => "3",
      4 => "4",
      5 => "5",
      _ => "6+"
    };

  private static string StatBucket(int? stat) =>
    stat switch
    {
      null => "none",
      <= 1 => "0-1",
      2 => "2",
      3 => "3",
      4 => "4",
      _ => "5+"
    };

  private static string ActionType(ActionDefinition action) =>
    action switch
    {
      DealDamageActionDefinition => "DEAL_DAMAGE",
      DamageDistributionActionDefinition => "DAMAGE_DISTRIBUTION",
      HealActionDefinition => "HEAL",
      FullyHealActionDefinition => "FULLY_HEAL",
      DrawCardActionDefinition => "DRAW_CARD",
      DiscardCardActionDefinition => "DISCARD_CARD",
      SummonActionDefinition => "SUMMON",
      DestroyActionDefinition => "DESTROY",
      BatchDestroyActionDefinition => "BATCH_DESTROY",
      RandomSelectActionDefinition => "RANDOM_SELECT",
      MoveToZoneActionDefinition => "MOVE_TO_ZONE",
      AddCardToHandActionDefinition => "ADD_CARD_TO_HAND",
      ReturnToHandActionDefinition => "RETURN_TO_HAND",
      ApplyModifierActionDefinition => "APPLY_MODIFIER",
      ApplyAuraActionDefinition => "APPLY_AURA",
      PreventDamageActionDefinition => "PREVENT_DAMAGE",
      ReplaceEventActionDefinition => "REPLACE_EVENT",
      ShuffleIntoDeckActionDefinition => "SHUFFLE_INTO_DECK",
      DiscoverActionDefinition => "DISCOVER",
      ResurrectActionDefinition => "RESURRECT",
      TakeExtraTurnActionDefinition => "TAKE_EXTRA_TURN",
      _ => action.GetType().Name
    };

  private static async Task<IReadOnlyList<UnityPackProviderHealth>> CheckProviderHealth(UnityCardPackOptions options)
  {
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    var providers = options.SkipAssets
      ? [new { Role = "structured", Endpoint = options.StructuredEndpoint }]
      : new[]
      {
        new { Role = "structured", Endpoint = options.StructuredEndpoint },
        new { Role = "image", Endpoint = options.ImageEndpoint },
        new { Role = "animation", Endpoint = options.AnimationEndpoint },
        new { Role = "audio", Endpoint = options.AudioEndpoint }
      };

    var health = new List<UnityPackProviderHealth>();
    foreach (var provider in providers)
    {
      try
      {
        var uri = new Uri(provider.Endpoint);
        var healthUri = new UriBuilder(uri.Scheme, uri.Host, uri.Port, "health").Uri;
        using var response = await http.GetAsync(healthUri);
        health.Add(new UnityPackProviderHealth(provider.Role, healthUri.ToString(), response.IsSuccessStatusCode, (int)response.StatusCode, ClassifyProviderFailure(response.IsSuccessStatusCode ? null : $"HTTP_{(int)response.StatusCode}", null)));
      }
      catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException)
      {
        health.Add(new UnityPackProviderHealth(provider.Role, provider.Endpoint, false, null, ClassifyProviderFailure(exception.GetType().Name, exception.Message)));
      }
    }

    return health;
  }

  private static string DiversitySeed(string slug, int index)
  {
    var themes = new[]
    {
      "moonwell sentinel",
      "ember forge artisan",
      "frost archive scholar",
      "stormglass navigator",
      "verdant relic keeper",
      "obsidian bell warden",
      "sunspire duelist",
      "tidecall glyphwright",
      "starfall medic",
      "brass lantern scout",
      "mycelium oathbinder",
      "skychain cartographer"
    };
    return $"{themes[index % themes.Length]} / pack-index-{index:000} / slug-{slug}";
  }

  public static UnityPackRollReference RollCreativeReference(string slug, int index)
  {
    var random = new Random(StableSeed($"{slug}:{index}:roll-reference-v1"));
    var coverageBag = new[]
    {
      "DEAL_DAMAGE",
      "HEAL",
      "DAMAGE_DISTRIBUTION",
      "PREVENT_DAMAGE",
      "APPLY_AURA",
      "RESURRECT",
      "DEATHRATTLE_DRAW",
      "LOCATION_VALUE",
      "WEAPON_EQUIP",
      "DRAW_CARD",
      "VANILLA_STATS"
    };
    var coveragePrimary = index < coverageBag.Length ? coverageBag[index] : null;
    var effectPresence = coveragePrimary is "VANILLA_STATS" or "WEAPON_EQUIP"
      ? "stat-oriented"
      : coveragePrimary is not null
        ? "effect"
        : Pick(random, [("effect", 92), ("stat-oriented", 8)]);
    var complexity = effectPresence is "stat-oriented"
      ? "simple"
      : Pick(random, [("simple", 42), ("moderate", 43), ("compound", 15)]);
    var primary = coveragePrimary ?? (effectPresence is "stat-oriented"
      ? Pick(random, [("VANILLA_STATS", 40), ("WEAPON_EQUIP", 40), ("LOCATION_VALUE", 20)])
      : Pick(random, [
          ("DEAL_DAMAGE", 18),
          ("DRAW_CARD", 12),
          ("HEAL", 10),
          ("DAMAGE_DISTRIBUTION", 9),
          ("PREVENT_DAMAGE", 9),
          ("APPLY_AURA", 9),
          ("RESURRECT", 8),
          ("DEATHRATTLE_DRAW", 8),
          ("LOCATION_VALUE", 4),
          ("WEAPON_EQUIP", 4)
        ]));
    var secondary = primary is "LOCATION_VALUE"
      ? Pick(random, [
          ("DRAW_CARD", 24),
          ("HEAL", 18),
          ("PREVENT_DAMAGE", 14),
          ("DAMAGE_DISTRIBUTION", 14),
          ("RESURRECT", 12)
        ])
      : primary is "HEAL"
      ? "DRAW_CARD"
      : complexity is "compound"
      ? Pick(random, [
          ("DRAW_CARD", 22),
          ("HEAL", 18),
          ("DEAL_DAMAGE", 18),
          ("PREVENT_DAMAGE", 12),
          ("RESURRECT", 10),
          ("none", 10)
        ])
      : "none";

    var compatible = CompatibleMechanicalDirection(primary, secondary, random);

    return new UnityPackRollReference(
      effectPresence,
      complexity,
      primary,
      secondary is "none" ? null : secondary,
      compatible.CardType,
      Pick(random, [("1-2", 24), ("3-4", 46), ("5-7", 30)]),
      compatible.TargetPattern,
      compatible.TimingWindow,
      "derived-from-final-dsl",
      "derived-from-final-dsl",
      "derived-from-final-dsl",
      "derived-from-final-dsl",
      "derived-from-final-dsl",
      "derived-from-final-dsl");
  }

  private static UnityPackMechanicalDirection CompatibleMechanicalDirection(
    string primary,
    string? secondary,
    Random random)
  {
    var matrix = new Dictionary<string, UnityPackMechanicalDirection[]>(StringComparer.Ordinal)
    {
      ["VANILLA_STATS"] =
      [
        new("MINION_CARD", "none-or-auto", "NO_EFFECT from HAND or BOARD"),
        new("WEAPON_CARD", "none-or-auto", "NO_EFFECT from HAND")
      ],
      ["WEAPON_EQUIP"] = [new("WEAPON_CARD", "none-or-auto", "ON_PLAY from HAND")],
      ["LOCATION_VALUE"] =
      [
        new("LOCATION_CARD", TargetPatternFor("LOCATION_VALUE", secondary), "activated LOCATION_ABILITY_EFFECT from BOARD")
      ],
      ["DEAL_DAMAGE"] =
      [
        new("SPELL_CARD", "player-chosen minion target", "ON_PLAY from HAND"),
        new("MINION_CARD", "player-chosen minion target", "BATTLECRY-like ON_PLAY from HAND")
      ],
      ["DRAW_CARD"] =
      [
        new("SPELL_CARD", "no target or auto-valid target", "ON_PLAY from HAND"),
        new("MINION_CARD", "no target or auto-valid target", "BATTLECRY-like ON_PLAY from HAND")
      ],
      ["HEAL"] = [new("SPELL_CARD", "friendly minion target", "ON_PLAY from HAND")],
      ["DAMAGE_DISTRIBUTION"] = [new("SPELL_CARD", "random enemy board minions", "ON_PLAY from HAND")],
      ["PREVENT_DAMAGE"] = [new("SPELL_CARD", "friendly minion protection target", "ON_PLAY from HAND")],
      ["APPLY_AURA"] = [new("MINION_CARD", "friendly board minions", "AURA_REFRESH from BOARD")],
      ["RESURRECT"] = [new("SPELL_CARD", "own graveyard minion history query", "ON_PLAY from HAND")],
      ["DEATHRATTLE_DRAW"] = [new("MINION_CARD", "no target or auto-valid target", "DEATHRATTLE_PHASE from BOARD")]
    };
    var options = matrix[primary];
    return options[random.Next(options.Length)];
  }

  private static int StableSeed(string text)
  {
    var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
    return BitConverter.ToInt32(hash, 0) & int.MaxValue;
  }

  private static string Pick(Random random, IReadOnlyList<(string Value, int Weight)> weighted)
  {
    var total = weighted.Sum(item => item.Weight);
    var roll = random.Next(total);
    foreach (var item in weighted)
    {
      if (roll < item.Weight)
      {
        return item.Value;
      }

      roll -= item.Weight;
    }

    return weighted[^1].Value;
  }

  private static string TargetPatternFor(string primary, string? secondary) =>
    primary switch
    {
      "DEAL_DAMAGE" => "player-chosen minion target",
      "HEAL" => "friendly minion target",
      "PREVENT_DAMAGE" => "friendly minion protection target",
      "DAMAGE_DISTRIBUTION" => "random enemy board minions",
      "APPLY_AURA" => "friendly board minions",
      "RESURRECT" => "own graveyard minion history query",
      "LOCATION_VALUE" when secondary is "HEAL" => "friendly minion target",
      "LOCATION_VALUE" when secondary is "PREVENT_DAMAGE" => "friendly minion protection target",
      "LOCATION_VALUE" when secondary is "DAMAGE_DISTRIBUTION" => "random enemy board minions",
      "LOCATION_VALUE" when secondary is "RESURRECT" => "own graveyard minion history query",
      _ when secondary is "DEAL_DAMAGE" => "player-chosen minion target",
      _ when secondary is "HEAL" => "friendly minion target",
      _ => "no target or auto-valid target"
    };

  public static string BuildGenerationReference(UnityPackRollReference roll)
  {
    var lines = new[]
    {
      $"effect direction: {roll.EffectPresence}; complexity: {roll.EffectComplexity}",
      $"primary supported primitive: {roll.PrimaryPrimitive}",
      $"secondary primitive direction: {roll.SecondaryPrimitive ?? "none"}",
      $"card type direction: {roll.CardTypeDirection}; cost band: {roll.CostBand}",
      $"effect kind / trigger / timing family: {roll.TimingWindow}",
      $"target requirement family: {roll.TargetPattern}",
      "source/zone/phase constraints: use only HAND/BOARD source zones and the listed timing family",
      "soft power/complexity budget: keep costs, stats, action count, complexityScore, and randomnessScore inside the requested band"
    };
    return string.Join(Environment.NewLine, lines);
  }

  private static IReadOnlyList<string> BuildCreativeEntropyLetters(string requestSlug, int index)
  {
    const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    var seed = requestSlug.Aggregate(index * 1103515245 + 12345, (current, character) => unchecked((current * 31) + character));
    var letters = new List<string>(capacity: 5);
    for (var offset = 0; offset < 5; offset++)
    {
      seed = unchecked((seed * 1664525) + 1013904223 + offset);
      var position = (seed & int.MaxValue) % alphabet.Length;
      var letter = alphabet[position].ToString(System.Globalization.CultureInfo.InvariantCulture);
      if (!letters.Contains(letter, StringComparer.Ordinal))
      {
        letters.Add(letter);
      }
      else
      {
        letters.Add(alphabet[(position + offset + 7) % alphabet.Length].ToString(System.Globalization.CultureInfo.InvariantCulture));
      }
    }

    return letters;
  }

  private static UnityPackRollConformance EvaluateRollConformance(
    UnityPackRollReference roll,
    StructuredCardOutput output,
    GeneratedCardValidationPipelineReport validation)
  {
    var diagnostics = new List<string>();
    if (!IsRollCompatible(roll))
    {
      diagnostics.Add("roll direction is outside supported production primitive set");
      return new UnityPackRollConformance("invalid_roll", false, diagnostics);
    }

    var primaryMatched = MatchesPrimitive(roll.PrimaryPrimitive, output);
    var secondaryMatched = roll.SecondaryPrimitive is null || MatchesPrimitive(roll.SecondaryPrimitive, output);
    var cardTypeMatched = roll.CardTypeDirection.Split(" or ", StringSplitOptions.TrimEntries).Any(token => CardTypeToken(output.CardType.ToString()) == token);
    var costMatched = MatchesCostBand(roll.CostBand, output.ManaCost);
    var targetMatched = MatchesTargetPattern(roll.TargetPattern, output);
    var timingMatched = MatchesTiming(roll.TimingWindow, output);

    if (!primaryMatched)
    {
      diagnostics.Add($"primary primitive not present: {roll.PrimaryPrimitive}");
    }
    if (!cardTypeMatched)
    {
      diagnostics.Add($"card type adapted from direction '{roll.CardTypeDirection}' to {CardTypeToken(output.CardType.ToString())}");
    }
    if (!costMatched)
    {
      diagnostics.Add($"mana cost {output.ManaCost} outside roll band {roll.CostBand}");
    }
    if (!targetMatched)
    {
      diagnostics.Add($"target pattern adapted from '{roll.TargetPattern}'");
    }
    if (!timingMatched)
    {
      diagnostics.Add($"timing adapted from '{roll.TimingWindow}'");
    }
    if (validation.FinalDecision is not GeneratedCardValidationDecision.Accepted)
    {
      diagnostics.Add("validated card was not accepted");
    }

    var score = new[] { primaryMatched, secondaryMatched, cardTypeMatched, costMatched, targetMatched, timingMatched }.Count(BooleanTrue);
    var status = primaryMatched && score >= 5
      ? "strong"
      : primaryMatched && score >= 3
        ? "adapted"
        : "ignored";
    var hardIgnored = status is "ignored"
      || (roll.EffectPresence is "effect" && output.Effects.Count == 0)
      || (roll.PrimaryPrimitive is "DEAL_DAMAGE" or "DRAW_CARD" or "HEAL" or "DAMAGE_DISTRIBUTION" or "PREVENT_DAMAGE" or "APPLY_AURA" or "RESURRECT" or "DEATHRATTLE_DRAW" or "LOCATION_VALUE"
        && output.Effects.Count == 0);
    return new UnityPackRollConformance(status, hardIgnored, diagnostics);

    static bool BooleanTrue(bool value) => value;
  }

  private static bool IsRollCompatible(UnityPackRollReference roll)
  {
    var supported = new HashSet<string>(StringComparer.Ordinal)
    {
      "VANILLA_STATS",
      "WEAPON_EQUIP",
      "LOCATION_VALUE",
      "DEAL_DAMAGE",
      "DRAW_CARD",
      "HEAL",
      "DAMAGE_DISTRIBUTION",
      "PREVENT_DAMAGE",
      "APPLY_AURA",
      "RESURRECT",
      "DEATHRATTLE_DRAW"
    };
    return supported.Contains(roll.PrimaryPrimitive) && (roll.SecondaryPrimitive is null || supported.Contains(roll.SecondaryPrimitive));
  }

  private static bool MatchesPrimitive(string primitive, StructuredCardOutput output)
  {
    var actionTypes = output.Effects.SelectMany(effect => effect.Actions).Select(ActionType).ToHashSet(StringComparer.Ordinal);
    var effectKinds = output.Effects.Select(effect => EffectKindToken(effect.EffectKind)).ToHashSet(StringComparer.Ordinal);
    return primitive switch
    {
      "VANILLA_STATS" => output.Effects.Count == 0 && output.CardType is CardType.MinionCard or CardType.WeaponCard,
      "WEAPON_EQUIP" => output.CardType is CardType.WeaponCard,
      "LOCATION_VALUE" => output.CardType is CardType.LocationCard || effectKinds.Contains("LOCATION_ABILITY_EFFECT"),
      "DEATHRATTLE_DRAW" => effectKinds.Contains("DEATHRATTLE_EFFECT") && actionTypes.Contains("DRAW_CARD"),
      _ => actionTypes.Contains(primitive)
    };
  }

  private static bool MatchesCostBand(string band, int manaCost) =>
    band switch
    {
      "1-2" => manaCost is >= 1 and <= 2,
      "3-4" => manaCost is >= 3 and <= 4,
      "5-7" => manaCost is >= 5 and <= 7,
      _ => true
    };

  private static bool MatchesTargetPattern(string pattern, StructuredCardOutput output)
  {
    var lower = pattern.ToLowerInvariant();
    if (lower.Contains("no target", StringComparison.Ordinal))
    {
      return !output.RequiresTarget;
    }

    return lower.Contains("random", StringComparison.Ordinal)
      ? output.Effects.SelectMany(effect => effect.Actions).Select(ActionType).Contains("DAMAGE_DISTRIBUTION")
      : output.RequiresTarget || output.Effects.Any(effect => effect.TargetRequirement is not null);
  }

  private static bool MatchesTiming(string timing, StructuredCardOutput output)
  {
    var effectKinds = output.Effects.Select(effect => EffectKindToken(effect.EffectKind)).ToHashSet(StringComparer.Ordinal);
    var phases = output.Effects.Select(effect => effect.TimingPolicy.Phase.ToString()).ToHashSet(StringComparer.Ordinal);
    if (timing.Contains("AURA_REFRESH", StringComparison.Ordinal))
    {
      return effectKinds.Contains("AURA_EFFECT") || phases.Contains("AuraRefresh");
    }
    if (timing.Contains("DEATHRATTLE_PHASE", StringComparison.Ordinal))
    {
      return effectKinds.Contains("DEATHRATTLE_EFFECT") || phases.Contains("DeathrattlePhase");
    }
    if (timing.Contains("LOCATION_ABILITY_EFFECT", StringComparison.Ordinal))
    {
      return effectKinds.Contains("LOCATION_ABILITY_EFFECT") || output.CardType is CardType.LocationCard;
    }
    return output.Effects.Count == 0 || phases.Contains("OnPlay");
  }

  private static UnityPackRollReferenceReport BuildRollReferenceReport(IReadOnlyList<UnityPackCardReport> cards) =>
    new(
      CountBy(cards.Select(card => card.RollConformance.Status)),
      CountBy(cards.Select(card => card.RollReference.PrimaryPrimitive)),
      CountBy(cards.Select(card => card.RollReference.SecondaryPrimitive ?? "none")),
      CountBy(cards.Select(card => card.RollReference.CardTypeDirection)),
      CountBy(cards.Select(card => card.RollReference.CostBand)),
      CountBy(cards.Select(card => card.RollReference.TimingWindow)),
      CountBy(cards.Select(card => card.RollReference.TargetPattern)),
      CountBy(cards.Select(card => card.RollReference.NamingStyle)),
      CountBy(cards.Select(card => card.RollReference.TextStyle)),
      CountBy(cards.Select(card => card.RollReference.MechanicalTwist)),
      cards.Count(card => card.RollConformance.HardIgnoredDirection),
      cards.Where(card => card.RollConformance.Status is "ignored" or "invalid_roll").Select(card => card.Slug).Order(StringComparer.Ordinal).ToArray());

  private static UnityPackProviderFailure ProviderFailure(
    string slug,
    string providerRole,
    string? errorCode,
    string? errorMessage) =>
    new(slug, providerRole, errorCode ?? "UNKNOWN_PROVIDER_FAILURE", errorMessage ?? string.Empty, ClassifyProviderFailure(errorCode, errorMessage));

  private static string ClassifyProviderFailure(string? errorCode, string? errorMessage)
  {
    var text = $"{errorCode} {errorMessage}".ToUpperInvariant();
    if (text.Contains("SCHEMA", StringComparison.Ordinal) || text.Contains("JSON", StringComparison.Ordinal))
    {
      return "schema";
    }

    if (text.Contains("TIMEOUT", StringComparison.Ordinal) || text.Contains("TASKCANCELED", StringComparison.Ordinal))
    {
      return "timeout";
    }

    if (text.Contains("502", StringComparison.Ordinal)
      || text.Contains("503", StringComparison.Ordinal)
      || text.Contains("504", StringComparison.Ordinal)
      || text.Contains("TRANSIENT", StringComparison.Ordinal)
      || text.Contains("CONNECTION", StringComparison.Ordinal)
      || text.Contains("REMOTE SERVER", StringComparison.Ordinal))
    {
      return "transient";
    }

    if (text.Contains("400", StringComparison.Ordinal)
      || text.Contains("401", StringComparison.Ordinal)
      || text.Contains("403", StringComparison.Ordinal)
      || text.Contains("404", StringComparison.Ordinal)
      || text.Contains("PERMANENT", StringComparison.Ordinal))
    {
      return "permanent";
    }

    return "unknown";
  }

  private static UnityPackProviderReliabilitySummary BuildProviderReliabilitySummary(
    IReadOnlyList<UnityPackProviderHealth> health,
    IReadOnlyList<UnityPackProviderFailure> failures) =>
    new(
      health,
      failures,
      CountBy(failures.Select(failure => failure.Classification)),
      failures.Count(failure => failure.Classification is "transient"),
      health.All(provider => provider.Healthy),
      failures.Count == 0 || failures.All(failure => failure.Classification is "transient"));

  private static UnityPackSimilarityReport BuildSimilarityReport(IReadOnlyList<UnityPackCardReport> cards)
  {
    var violations = new List<UnityPackSimilarityViolation>();
    for (var i = 0; i < cards.Count; i++)
    {
      for (var j = i + 1; j < cards.Count; j++)
      {
        var left = cards[i];
        var right = cards[j];
        var nameScore = Similarity(NormalizeText(left.Name), NormalizeText(right.Name));
        var rulesScore = Similarity(NormalizeRules(left.Description), NormalizeRules(right.Description));
        var effectScore = RoleSignature(left) == RoleSignature(right) ? 1.0 : 0.0;
        var statScore = StatLine(left) == StatLine(right) ? 1.0 : 0.0;
        var roleScore = MechanicalRole(left) == MechanicalRole(right) ? 1.0 : 0.0;
        var score = (nameScore * 0.35) + (rulesScore * 0.2) + (effectScore * 0.25) + (statScore * 0.1) + (roleScore * 0.1);
        if (string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase) || score >= 0.92)
        {
          violations.Add(new UnityPackSimilarityViolation(left.Slug, right.Slug, Math.Round(score, 3), left.Name, right.Name, RoleSignature(left), RoleSignature(right)));
        }
      }
    }

    return new UnityPackSimilarityReport(
      Math.Round(violations.Count == 0 ? 0 : violations.Max(violation => violation.Score), 3),
      violations.Count,
      violations.OrderByDescending(violation => violation.Score).ThenBy(violation => violation.LeftSlug, StringComparer.Ordinal).ToArray());
  }

  private static UnityPackDiversityReport BuildDiversityReport(IReadOnlyList<UnityPackCardReport> cards)
  {
    var themeDistribution = CountBy(cards.Select(ThemePrefix));
    var archetypes = CountBy(cards.Select(MechanicalRole));
    var effectPatterns = CountBy(cards.Select(RoleSignature));
    var violations = themeDistribution
      .Where(pair => pair.Value > Math.Max(8, cards.Count / 5))
      .Select(pair => $"theme:{pair.Key}:{pair.Value}")
      .Concat(effectPatterns.Where(pair => pair.Value > Math.Max(12, cards.Count / 4)).Select(pair => $"effectPattern:{pair.Key}:{pair.Value}"))
      .Concat(archetypes.Where(pair => pair.Value > Math.Max(12, cards.Count / 4)).Select(pair => $"role:{pair.Key}:{pair.Value}"))
      .Order(StringComparer.Ordinal)
      .ToArray();
    return new UnityPackDiversityReport(themeDistribution, archetypes, effectPatterns, violations.Length, violations);
  }

  private static UnityPackThemeDiversityReport BuildThemeDiversityReport(IReadOnlyList<UnityPackCardReport> cards) =>
    AnalyzeThemeDiversity(cards.Select(card => new UnityPackCreativeQualityInput(
      card.Name,
      card.Description,
      card.ManaCost,
      StatLine(card),
      card.DerivedMetadata.ActualTargetPattern,
      RoleSignature(card),
      card.RollReference.FlavorMood,
      card.RollReference.NamingStyle,
      card.RollReference.TextStyle,
      card.RollReference.MechanicalTwist,
      card.Name)).ToArray());

  public static UnityPackThemeDiversityReport AnalyzeThemeDiversity(IReadOnlyList<UnityPackCreativeQualityInput> cards)
  {
    var taxonomy = GetThemeTaxonomy();
    var familyById = taxonomy.ToDictionary(family => family.ThemeFamilyId, StringComparer.Ordinal);
    var assignments = cards
      .Select(card => AssignThemeFamily(card, taxonomy))
      .ToArray();
    var distribution = CountBy(assignments.Select(assignment => assignment.ThemeFamilyId));
    var overusedClusters = CountBy(cards.SelectMany(card => TokenizeName(card.Name).Where(IsOverusedThemeToken)))
      .Where(pair => pair.Value > Math.Max(2, cards.Count / 20))
      .OrderByDescending(pair => pair.Value)
      .ThenBy(pair => pair.Key, StringComparer.Ordinal)
      .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    var motifSkeletons = CountBy(cards.Select(card => VisualMotifSkeleton(card.VisualPrompt)));
    var repeatedMotifs = motifSkeletons
      .Where(pair => pair.Key is not "none" && pair.Value > 2)
      .OrderByDescending(pair => pair.Value)
      .ThenBy(pair => pair.Key, StringComparer.Ordinal)
      .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    var requiredFamilies = cards.Count >= 80 ? 12 : cards.Count >= 24 ? 8 : Math.Min(4, cards.Count);
    var maxFamilyShare = cards.Count == 0 ? 0 : distribution.Values.DefaultIfEmpty(0).Max() / (double)cards.Count;
    var violations = new List<string>();
    if (distribution.Count < requiredFamilies)
    {
      violations.Add($"theme-family-count:{distribution.Count}/{requiredFamilies}");
    }
    foreach (var pair in distribution.Where(pair => cards.Count >= 24 && pair.Value > Math.Ceiling(cards.Count * 0.15)))
    {
      violations.Add($"theme-family-dominance:{pair.Key}:{pair.Value}/{cards.Count}");
    }
    foreach (var pair in overusedClusters.Where(pair => pair.Value > 3))
    {
      violations.Add($"overused-theme-token:{pair.Key}:{pair.Value}");
    }
    foreach (var pair in repeatedMotifs)
    {
      violations.Add($"repeated-visual-motif:{pair.Key}:{pair.Value}");
    }

    return new UnityPackThemeDiversityReport(
      taxonomy,
      assignments,
      distribution,
      overusedClusters,
      repeatedMotifs,
      requiredFamilies,
      Math.Round(maxFamilyShare, 3),
      violations.Count,
      violations.Order(StringComparer.Ordinal).ToArray());
  }

  private static UnityPackThemeAssignment AssignThemeFamily(
    UnityPackCreativeQualityInput card,
    IReadOnlyList<UnityPackThemeFamily> taxonomy)
  {
    var haystack = $"{card.Name} {card.VisualPrompt}".ToLowerInvariant();
    var best = taxonomy
      .Select(family => new
      {
        Family = family,
        Score = family.NameTokens.Count(token => haystack.Contains(token, StringComparison.Ordinal))
          + family.VisualMotifs.Count(token => haystack.Contains(token, StringComparison.Ordinal))
      })
      .OrderByDescending(item => item.Score)
      .ThenBy(item => item.Family.ThemeFamilyId, StringComparer.Ordinal)
      .FirstOrDefault(item => item.Score > 0);
    var family = best?.Family ?? taxonomy[Math.Abs(StableSeed(card.Name)) % taxonomy.Count];
    return new UnityPackThemeAssignment(card.Name, family.ThemeFamilyId, best?.Score ?? 0);
  }

  public static IReadOnlyList<UnityPackThemeFamily> GetThemeTaxonomy() =>
  [
    new("cosmic", ["star", "moon", "void", "astral", "comet", "oracle", "apostle"], ["nebula", "constellation", "eclipse"], ["DRAW_CARD", "PREVENT_DAMAGE"], ["frost", "archive"], ["Star-Maw Apostle", "Mooncalf Herder"]),
    new("mundane-military", ["guard", "marshal", "bannerman", "recruit", "road", "duelist"], ["standard", "barracks", "muddy road"], ["VANILLA_STATS", "DEAL_DAMAGE"], ["prism", "codex"], ["Old Road Bannerman", "Vera, Ashfield Marshal"]),
    new("wilderness-beasts", ["wolf", "boar", "herder", "stag", "beast", "claw"], ["thicket", "tracks", "hide"], ["VANILLA_STATS", "DAMAGE_DISTRIBUTION"], ["lantern"], ["Mooncalf Herder", "Glassback Stag"]),
    new("urban-rogues", ["dockside", "smuggler", "cutpurse", "alley", "debt", "duelist"], ["harbor", "market", "rooftop"], ["DEAL_DAMAGE", "DRAW_CARD"], ["oath"], ["Dockside Duelist", "Goblin Debt-Collector"]),
    new("religious-cult", ["apostle", "monk", "saint", "choir", "cult", "bell"], ["shrine", "censer", "procession"], ["PREVENT_DAMAGE", "APPLY_AURA"], ["fixed"], ["Star-Maw Apostle", "Ninth Bell Acolyte"]),
    new("nautical", ["pirate", "reef", "tide", "leviathan", "sailor", "anchor"], ["waves", "ship", "coral"], ["WEAPON_EQUIP", "DAMAGE_DISTRIBUTION"], ["frost"], ["Glassback Leviathan", "Reefside Quartermaster"]),
    new("desert-nomads", ["dune", "nomad", "mirage", "scarab", "salt", "caravan"], ["sand", "sun", "canvas"], ["DRAW_CARD", "HEAL"], ["archive"], ["Salt Road Seer", "Dune-Cup Runner"]),
    new("forest-fae", ["root", "fae", "orchard", "moss", "briar", "sprite"], ["glade", "rings", "old trees"], ["APPLY_AURA", "HEAL"], ["codex"], ["Iron Orchard", "Moss-Crown Host"]),
    new("machine-clockwork", ["clockwork", "gear", "engineer", "brass", "pilot", "construct"], ["cog", "steam", "workshop"], ["WEAPON_EQUIP", "LOCATION_VALUE"], ["mist"], ["Clockwork Pilgrim", "Brasslane Engineer"]),
    new("undead-graveyard", ["bone", "grave", "wight", "necromancer", "crypt", "requiem"], ["graveyard", "candle", "tomb"], ["RESURRECT", "DEATHRATTLE_DRAW"], ["rebirth"], ["Crypt Tollkeeper", "Wight of the Old Well"]),
    new("volcanic-fire", ["ash", "ember", "cinder", "magma", "forge", "flame"], ["lava", "charcoal", "anvil"], ["DEAL_DAMAGE", "WEAPON_EQUIP"], ["frost"], ["Ashfield Marshal", "Cinder-Forged Pike"]),
    new("storm-sky", ["storm", "cloud", "thunder", "sky", "gale", "kite"], ["lightning", "rain", "high mast"], ["DAMAGE_DISTRIBUTION", "PREVENT_DAMAGE"], ["vault"], ["Living Storm", "Kitefield Captain"]),
    new("dream-nightmare", ["dream", "nightmare", "sleep", "mirror", "moth", "hush"], ["curtain", "moonlit bed", "surreal"], ["DRAW_CARD", "PREVENT_DAMAGE"], ["grid"], ["Moth in the Mirror", "Dream Toll Collector"]),
    new("royal-court", ["queen", "court", "page", "knight", "crown", "marshal"], ["throne", "banner", "silk"], ["APPLY_AURA", "DRAW_CARD"], ["archive"], ["Court of Small Knives", "Queen's Page"]),
    new("plague-alchemy", ["plague", "doctor", "alchemist", "vial", "leech", "tonic"], ["mask", "glassware", "green smoke"], ["HEAL", "DEAL_DAMAGE"], ["prism"], ["Plague Doctor's Tonic", "Leechglass Apprentice"]),
    new("folklore", ["goblin", "troll", "witch", "ogre", "fairy", "folklore"], ["storybook", "hearth", "old bridge"], ["VANILLA_STATS", "HEAL"], ["schema"], ["Goblin Debt-Collector", "Bridge-Toll Witch"])
  ];

  private static string VisualMotifSkeleton(string prompt)
  {
    var tokens = TokenizeName(prompt)
      .Where(token => token is not "visual" and not "prompt" and not "card" and not "sample")
      .Where(token => !IsQualityToken(token) || token.Length > 5)
      .Take(4)
      .ToArray();
    return tokens.Length == 0 ? "none" : string.Join("-", tokens);
  }

  private static bool IsOverusedThemeToken(string token) =>
    token is "ice" or "frost" or "cold" or "mist" or "lantern" or "bell" or "oath" or "gate" or "map" or "archive" or "codex" or "prism" or "vault" or "guardian" or "rebirth" or "shadow" or "ember";

  public static UnityPackMechanicCoverageReport BuildMechanicCoverageReport(EngineCapabilityRegistry registry)
  {
    var generatorFamilies = GeneratorMechanicRegistry();
    var supportedCapabilities = registry.SupportedActionTypes
      .Concat(registry.SupportedMechanics)
      .Concat(registry.SupportedEffectKinds)
      .Distinct(StringComparer.Ordinal)
      .Where(IsGeneratorRelevantCapability)
      .ToHashSet(StringComparer.Ordinal);
    var supportedEntries = generatorFamilies
      .Where(entry => entry.EngineCapabilityIds.Any(supportedCapabilities.Contains))
      .ToArray();
    var coveredCapabilities = supportedEntries
      .SelectMany(entry => entry.EngineCapabilityIds)
      .ToHashSet(StringComparer.Ordinal);
    var unsupportedEntries = supportedCapabilities
      .Where(id => !coveredCapabilities.Contains(id))
      .Order(StringComparer.Ordinal)
      .Select(id => new UnityPackMechanicCoverageEntry(
        id,
        [id],
        EngineSupported: true,
        GeneratorSupported: false,
        PromptSupported: false,
        CanonicalRulesTextSupported: false,
        SemanticValidationSupported: false,
        GameplayProofSupported: false,
        ReplayProofSupported: false,
        BotProofSupported: false,
        UnityPresentationHintSupported: false,
        Status: "generator-blocked",
        Blocker: "Engine capability is present, but no generator-safe mechanic declaration exists yet."))
      .ToArray();
    var entries = supportedEntries
      .Concat(unsupportedEntries)
      .OrderBy(entry => entry.MechanicId, StringComparer.Ordinal)
      .ToArray();
    return new UnityPackMechanicCoverageReport(entries, entries.Count(entry => entry.GeneratorProductionSupported), entries.Count(entry => entry.Status is "generator-blocked"));
  }

  private static bool IsGeneratorRelevantCapability(string id) =>
    id is "DEAL_DAMAGE" or "DAMAGE_DISTRIBUTION" or "DRAW_CARD" or "HEAL" or "DISCOVER" or "PREVENT_DAMAGE" or "APPLY_AURA" or "RESURRECT"
    or "BATCH_DESTROY" or "TAKE_EXTRA_TURN" or "BOARD_KEYWORD_MODIFIER" or "HAND_ZONE_MODIFIER"
    or "SIMPLE_PLAY_EFFECT" or "DEATHRATTLE_EFFECT" or "AURA_EFFECT" or "LOCATION_ABILITY_EFFECT" or "QUEST_EFFECT"
    or "BATTLECRY" or "DEATHRATTLE" or "DISCOVER_FIXED_POOL_CHOICE" or "DAMAGE_PREVENTION" or "AURA_REFRESH" or "BOARD_ATTACK_MODIFIER"
    or "GRAVEYARD_RESURRECT" or "LOCATION_PLAY" or "WEAPON_EQUIP" or "RANDOM_TARGET_RESOLUTION" or "VANILLA_MINION"
    or "QUEST" or "SECRET" or "REPLACE_EVENT";

  public static IReadOnlyList<UnityPackMechanicCoverageEntry> GeneratorMechanicRegistry() =>
  [
    Supported("VANILLA_STATS", ["VANILLA_MINION"], botProof: true),
    Supported("DEAL_DAMAGE", ["DEAL_DAMAGE", "BATTLECRY", "SIMPLE_PLAY_EFFECT"], botProof: true),
    Supported("DAMAGE_DISTRIBUTION", ["DAMAGE_DISTRIBUTION", "RANDOM_TARGET_RESOLUTION"], botProof: true),
    Supported("DRAW_CARD", ["DRAW_CARD"], botProof: true),
    Supported("HEAL", ["HEAL"], botProof: true),
    Blocked("DISCOVER", ["DISCOVER", "DISCOVER_FIXED_POOL_CHOICE"], "Discover fixed-pool cards require an authoritative match-time pool context; semantic validation blocks generated Discover until public match execution provides that context."),
    Supported("PREVENT_DAMAGE", ["PREVENT_DAMAGE", "DAMAGE_PREVENTION"], botProof: true),
    Supported("RESURRECT", ["RESURRECT", "GRAVEYARD_RESURRECT"], botProof: true),
    Supported("APPLY_AURA", ["APPLY_AURA", "AURA_EFFECT", "AURA_REFRESH", "BOARD_ATTACK_MODIFIER"], botProof: true),
    Supported("DEATHRATTLE_DRAW", ["DEATHRATTLE", "DEATHRATTLE_EFFECT", "DRAW_CARD"], botProof: true),
    Supported("LOCATION_VALUE", ["LOCATION_PLAY", "LOCATION_ABILITY_EFFECT", "ACTIVATE_LOCATION"], botProof: true),
    Supported("WEAPON_EQUIP", ["WEAPON_EQUIP", "ATTACK"], botProof: true),
    Blocked("QUEST_REWARD", ["QUEST_EFFECT"], "Engine has quest runtime slices, but generated quest card templates and showcase balance proof are not declared."),
    Blocked("SECRET_REVEAL", ["SECRET"], "Engine has secret runtime slices, but generated secret authoring templates and proof scenarios are not declared."),
    Blocked("BATCH_DESTROY", ["BATCH_DESTROY"], "Engine supports partial batch destroy semantics, but generated removal templates are not showcase-safe yet."),
    Blocked("TAKE_EXTRA_TURN", ["TAKE_EXTRA_TURN"], "Engine supports extra turns, but generator balance policy blocks this high-risk mechanic."),
    Blocked("BOARD_KEYWORD_MODIFIER", ["BOARD_KEYWORD_MODIFIER"], "Engine supports board keyword modifiers, but canonical generator templates and Unity hints are pending."),
    Blocked("HAND_ZONE_MODIFIER", ["HAND_ZONE_MODIFIER"], "Engine supports hand zone modifiers, but generated hand-zone templates and bot proof are pending.")
  ];

  private static UnityPackMechanicCoverageEntry Supported(string mechanicId, IReadOnlyList<string> capabilityIds, bool botProof) =>
    new(mechanicId, capabilityIds, true, true, true, true, true, true, true, botProof, true, botProof ? "generator-production-supported" : "generator-supported-bot-proof-pending", botProof ? null : "Full exporter-run bot proof is pending.");

  private static UnityPackMechanicCoverageEntry Blocked(string mechanicId, IReadOnlyList<string> capabilityIds, string blocker) =>
    new(mechanicId, capabilityIds, true, false, false, false, false, false, false, false, false, "generator-blocked", blocker);

  private static UnityPackShowcaseReadinessReport BuildShowcaseReadinessReport(
    IReadOnlyList<UnityPackCardReport> cards,
    UnityPackThemeDiversityReport themeDiversity,
    UnityPackMechanicCoverageReport mechanicCoverage,
    UnityPackBalanceQualityReport balanceQuality,
    UnityPackCreativeQualityReport creativeQuality,
    UnityPackDiversityReport diversity)
  {
    var scores = cards.Select(card => ScoreShowcaseReadiness(card, themeDiversity)).OrderBy(score => score.Slug, StringComparer.Ordinal).ToArray();
    var requiredEffectFamilies = cards.Count >= 24 ? 8 : Math.Min(4, cards.Count);
    var representedEffectFamilies = cards.Select(RoleSignature).Distinct(StringComparer.Ordinal).Count();
    var packBlockers = new List<string>();
    if (themeDiversity.ViolationCount > 0)
    {
      packBlockers.Add($"theme-diversity:{themeDiversity.ViolationCount}");
    }
    if (representedEffectFamilies < requiredEffectFamilies)
    {
      packBlockers.Add($"effect-family-count:{representedEffectFamilies}/{requiredEffectFamilies}");
    }
    if (creativeQuality.ViolationCount > 0)
    {
      packBlockers.Add($"creative-quality:{creativeQuality.ViolationCount}");
    }
    if (diversity.ViolationCount > 0)
    {
      packBlockers.Add($"pack-diversity:{diversity.ViolationCount}");
    }
    if (balanceQuality.UnjustifiedFindingCount > 0)
    {
      packBlockers.Add($"balance-findings:{balanceQuality.UnjustifiedFindingCount}");
    }
    if (mechanicCoverage.BlockedMechanicCount > 0)
    {
      packBlockers.Add($"generator-blocked-engine-mechanics:{mechanicCoverage.BlockedMechanicCount}");
    }

    var readyCards = scores.Count(score => score.ShowcaseReady);
    return new UnityPackShowcaseReadinessReport(
      scores,
      readyCards,
      cards.Count == 0 ? 0 : Math.Round(readyCards / (double)cards.Count, 3),
      representedEffectFamilies,
      requiredEffectFamilies,
      packBlockers.Count == 0 && scores.All(score => score.ShowcaseReady),
      packBlockers.Order(StringComparer.Ordinal).ToArray());
  }

  private static UnityPackShowcaseCardScore ScoreShowcaseReadiness(UnityPackCardReport card, UnityPackThemeDiversityReport themeDiversity)
  {
    var nameScore = Math.Clamp(100 - (NameNaturalnessViolations(card.Name).Count * 25) - (NameMechanicalLeakageViolations(card.Name).Count * 35), 0, 100);
    var theme = themeDiversity.Assignments.FirstOrDefault(assignment => string.Equals(assignment.CardName, card.Name, StringComparison.Ordinal))?.ThemeFamilyId ?? "unknown";
    var themeScore = theme is "unknown" ? 45 : 85;
    var mechanicalInterest = RoleSignature(card) is "VANILLA" ? 55 : card.GameplayProof.EffectActionTypes.Count >= 2 ? 90 : 75;
    var rulesScore = PlayerFacingRulesTextViolations(card.Description, RoleSignature(card)).Count == 0 ? 100 : 20;
    var balanceConfidence = card.Balance.Decision is GeneratedCardBalanceDecision.Accepted ? 85 : 55;
    var visualScore = string.IsNullOrWhiteSpace(card.Name) ? 40 : 80;
    var diversityContribution = themeDiversity.ThemeDistribution.TryGetValue(theme, out var count) && count <= Math.Max(2, Math.Ceiling(themeDiversity.Assignments.Count * 0.15)) ? 85 : 55;
    var average = Math.Round(new[] { nameScore, themeScore, mechanicalInterest, rulesScore, balanceConfidence, visualScore, diversityContribution }.Average(), 1);
    var blockers = new List<string>();
    if (nameScore < 70) blockers.Add("name-quality");
    if (rulesScore < 100) blockers.Add("rules-cleanliness");
    if (balanceConfidence < 70) blockers.Add("balance-confidence");
    if (average < 72) blockers.Add("public-quality-score");
    return new UnityPackShowcaseCardScore(card.Slug, card.Name, theme, nameScore, themeScore, mechanicalInterest, rulesScore, balanceConfidence, visualScore, diversityContribution, average, blockers.Count == 0, blockers);
  }

  private static UnityPackComplexEffectsReport BuildComplexEffectsReport(IReadOnlyList<UnityPackCardReport> cards)
  {
    var required = new SortedDictionary<string, string[]>(StringComparer.Ordinal)
    {
      ["APPLY_AURA"] = ["APPLY_AURA"],
      ["DAMAGE_DISTRIBUTION"] = ["DAMAGE_DISTRIBUTION"],
      ["PREVENT_DAMAGE"] = ["PREVENT_DAMAGE"],
      ["RESURRECT"] = ["RESURRECT"],
      ["DEATHRATTLE_EFFECT"] = ["DEATHRATTLE_EFFECT"],
      ["LOCATION_ABILITY_EFFECT"] = ["LOCATION_ABILITY_EFFECT"],
      ["WEAPON_EQUIP_ATTACK_DURABILITY"] = ["WEAPON_CARD"],
      ["HEAL_DRAW"] = ["HEAL", "DRAW_CARD"],
      ["TARGETED_DAMAGE"] = ["DEAL_DAMAGE"],
      ["RANDOM_DAMAGE"] = ["DAMAGE_DISTRIBUTION"]
    };

    return new UnityPackComplexEffectsReport(required
      .Select(pair =>
      {
        var match = cards.FirstOrDefault(card => CoversFamily(card, pair.Value));
        return new UnityPackComplexEffectProof(
          pair.Key,
          match?.Slug,
          match is not null,
          match?.Validation.FinalDecision is GeneratedCardValidationDecision.Accepted,
          match?.PublicationSucceeded is true,
          match?.GameplayProof.EffectResolved is true,
          match?.ReplayVerified is true,
          match?.ProviderStructuredResult.Succeeded is true,
          match?.Diagnostics ?? []);
      })
      .ToArray());
  }

  private static UnityPackBotVsBotRepresentativeProof BuildRepresentativeBotVsBotProof(IReadOnlyList<UnityPackCardReport> cards)
  {
    var families = new[] { "APPLY_AURA", "DAMAGE_DISTRIBUTION", "PREVENT_DAMAGE", "RESURRECT", "DEATHRATTLE_EFFECT", "LOCATION_ABILITY_EFFECT", "WEAPON_CARD", "HEAL", "DEAL_DAMAGE" };
    var entries = families
      .Select(family =>
      {
        var match = cards.FirstOrDefault(card => CoversFamily(card, [family]));
        return new UnityPackBotVsBotRepresentativeEntry(
          family,
          match?.Slug,
          match is not null,
          match?.GameplayProof.PlayedOrActivated is true,
          match?.GameplayProof.EffectResolved is true,
          match?.ReplayVerified is true,
          match is null ? ["no representative generated card found"] : [$"scripted-proof:{match.Slug}", "bot-vs-bot-compatible:oracle-playable-card-version"]);
      })
      .ToArray();

    return new UnityPackBotVsBotRepresentativeProof(
      "representative-oracle-bot-compatible-proof-v1",
      false,
      "Representative cards are proven playable/replayable and compatible with oracle bot play paths, but the exporter does not run a full bot-vs-bot match per family.",
      entries);
  }

  private static UnityPackReadinessVerdicts BuildReadinessVerdicts(
    bool unityBaselineUsable,
    IReadOnlyList<UnityPackRejectedCard> rejected,
    IReadOnlyList<UnityPackCardReport> cards,
    UnityPackRepairRateReport repairRates,
    UnityPackRepairGovernanceReport repairGovernance,
    UnityPackBalanceQualityReport balanceQuality,
    UnityPackScenarioMetadataReport scenarioMetadata,
    UnityPackCreativeQualityReport creativeQuality,
    UnityPackSimilarityReport similarity,
    UnityPackDiversityReport diversity,
    UnityPackComplexEffectsReport complex,
    UnityPackBotVsBotRepresentativeProof botVsBot,
    UnityPackMediaReadinessReport mediaReadiness,
    UnityPackProviderReliabilitySummary providerReliability)
  {
    var generationQualityPassed = unityBaselineUsable
      && rejected.Count == 0
      && repairRates.RawValidRate >= repairRates.RawValidThresholdProduction
      && repairRates.ProviderRepairRate <= repairRates.ProviderRepairThresholdProduction
      && repairRates.MaterialRepairRate <= repairRates.MaterialRepairThresholdProduction
      && repairRates.EffectsRepairRate <= repairRates.EffectsRepairThresholdProduction
      && repairGovernance.MaterialPreflightRewriteCount == 0
      && repairGovernance.DeterministicNameFallbackUsed == 0;
    var contentQualityPassed = creativeQuality.ViolationCount == 0
      && similarity.ViolationCount == 0
      && diversity.ViolationCount == 0
      && scenarioMetadata.MismatchCount == 0
      && cards.All(card => card.SemanticWarningCount == 0 && !card.RollConformance.HardIgnoredDirection);
    var gameplayPassed = cards.Count > 0
      && cards.All(card => card.GameplaySmokePassed && card.ReplayVerified)
      && complex.AllPassed;
    var botVsBotPassed = botVsBot.FullBotVsBotExecuted
      && botVsBot.RepresentativeCoveragePassed
      && botVsBot.Entries.All(entry => entry.CardAddedToDeck && entry.PlayedOrActivated && entry.EffectResolved && entry.ReplayVerified);
    var mediaPassed = mediaReadiness.MediaReadiness is "ready";
    var reportingPassed = repairGovernance.ProviderRepairCount == cards.Count(card => card.StructuredTrace?.ProviderRepairCount > 0)
      || repairGovernance.ProviderRepairCount == cards.Sum(card => card.StructuredTrace?.ProviderRepairCount ?? 0);
    reportingPassed = reportingPassed
      && repairGovernance.CreativeQualityRepairCount == cards.Sum(card => card.Diagnostics.Count(diagnostic => diagnostic.StartsWith("creative-quality-repair:", StringComparison.Ordinal)))
      && providerReliability.RuntimeStable;
    var unityPassed = unityBaselineUsable
      && rejected.Count == 0
      && balanceQuality.UnjustifiedFindingCount == 0
      && contentQualityPassed
      && gameplayPassed;

    return new UnityPackReadinessVerdicts(
      RawLlmOutputReadiness: generationQualityPassed ? "production-ready" : repairRates.RawValidRate >= repairRates.RawValidThresholdLocal ? "local-model-ready-with-limits" : "not-production-ready",
      RepairConvergedPipelineReadiness: unityBaselineUsable && rejected.Count == 0 && repairGovernance.MaterialPreflightRewriteCount == 0 && repairGovernance.DeterministicNameFallbackUsed == 0 ? "repair-converged-with-known-limitations" : "not-production-ready",
      GameplayContractReadiness: gameplayPassed ? "ready" : "not-ready",
      BotVsBotReadiness: botVsBotPassed ? "ready" : "not-ready",
      MediaInclusiveReadiness: mediaPassed ? "ready" : mediaReadiness.AssetsSkipped ? "not_applicable_llm_only" : "not-ready",
      UnityBaselineReadiness: unityPassed ? "ready" : "not-ready",
      ReportingReadiness: reportingPassed ? "ready" : "not-ready",
      BlockingReasons: ReadinessBlockingReasons(generationQualityPassed, contentQualityPassed, gameplayPassed, botVsBotPassed, mediaPassed, reportingPassed, repairRates, repairGovernance, creativeQuality, scenarioMetadata, botVsBot, mediaReadiness).ToArray());
  }

  private static IReadOnlyList<string> ReadinessBlockingReasons(
    bool generationQualityPassed,
    bool contentQualityPassed,
    bool gameplayPassed,
    bool botVsBotPassed,
    bool mediaPassed,
    bool reportingPassed,
    UnityPackRepairRateReport repairRates,
    UnityPackRepairGovernanceReport repairGovernance,
    UnityPackCreativeQualityReport creativeQuality,
    UnityPackScenarioMetadataReport scenarioMetadata,
    UnityPackBotVsBotRepresentativeProof botVsBot,
    UnityPackMediaReadinessReport mediaReadiness)
  {
    var reasons = new List<string>();
    if (!generationQualityPassed)
    {
      reasons.Add($"raw/provider repair thresholds not met: raw={repairRates.RawValidRate:P1}, providerRepair={repairRates.ProviderRepairRate:P1}, materialRepair={repairRates.MaterialRepairRate:P1}, effectsRepair={repairRates.EffectsRepairRate:P1}");
    }
    if (repairGovernance.DeterministicNameFallbackUsed > 0)
    {
      reasons.Add($"deterministic fallback used: {repairGovernance.DeterministicNameFallbackUsed}");
    }
    if (!contentQualityPassed)
    {
      reasons.Add($"content quality gates failed: creativeViolations={creativeQuality.ViolationCount}, metadataMismatches={scenarioMetadata.MismatchCount}");
    }
    if (!gameplayPassed)
    {
      reasons.Add("gameplay contract proof did not pass for every card/effect family");
    }
    if (!botVsBotPassed)
    {
      reasons.Add(botVsBot.FullBotVsBotExecuted ? "bot-vs-bot representative proof failed" : "full exporter-run bot-vs-bot representative proof was not executed");
    }
    if (!mediaPassed)
    {
      reasons.Add(mediaReadiness.AssetsSkipped ? "media-inclusive run was skipped; this artifact is LLM-only" : "media-inclusive readiness failed");
    }
    if (!reportingPassed)
    {
      reasons.Add("repair/reporting invariants failed");
    }

    return reasons;
  }

  private static string ResolveFinalVerdict(UnityPackReadinessVerdicts readiness)
  {
    if (readiness.UnityBaselineReadiness is not "ready"
      || readiness.GameplayContractReadiness is not "ready"
      || readiness.ReportingReadiness is not "ready"
      || readiness.RepairConvergedPipelineReadiness is "not-production-ready")
    {
      return "not-production-ready";
    }

    if (readiness.RawLlmOutputReadiness is "production-ready"
      && readiness.BotVsBotReadiness is "ready"
      && readiness.MediaInclusiveReadiness is "ready")
    {
      return "production-ready";
    }

    return "repair-converged-with-known-limitations";
  }

  private static string ResolveFinalVerdict(
    bool unityBaselineUsable,
    IReadOnlyList<UnityPackRejectedCard> rejected,
    IReadOnlyList<UnityPackCardReport> cards,
    UnityPackBalanceQualityReport balanceQuality,
    UnityPackScenarioMetadataReport scenarioMetadata,
    UnityPackCreativeQualityReport creativeQuality,
    UnityPackSimilarityReport similarity,
    UnityPackDiversityReport diversity,
    UnityPackComplexEffectsReport complex,
    UnityPackBotVsBotRepresentativeProof botVsBot,
    UnityPackProviderReliabilitySummary providerReliability)
  {
    if (!unityBaselineUsable
      || rejected.Count > 0
      || cards.Any(card => card.SemanticWarningCount > 0 || !card.GameplaySmokePassed || !card.ReplayVerified)
      || cards.Any(card => card.RollConformance.HardIgnoredDirection)
      || balanceQuality.UnjustifiedFindingCount > 0
      || scenarioMetadata.MismatchCount > 0
      || creativeQuality.ViolationCount > 0
      || similarity.ViolationCount > 0
      || diversity.ViolationCount > 0
      || !complex.AllPassed)
    {
      return "not-production-ready";
    }

    return botVsBot.FullBotVsBotExecuted && providerReliability.AllHealthyBeforeRun
      ? "production-ready"
      : "production-ready-with-known-limitations";
  }

  private static bool CoversFamily(UnityPackCardReport card, IReadOnlyList<string> tokens)
  {
    var actionTypes = card.GameplayProof.EffectActionTypes.ToHashSet(StringComparer.Ordinal);
    var effectKinds = card.Dsl.Effects.Select(effect => EffectKindToken(effect.EffectKind)).ToHashSet(StringComparer.Ordinal);
    var cardType = CardTypeToken(card.Type);
    return tokens.All(token =>
      actionTypes.Contains(token)
      || effectKinds.Contains(token)
      || string.Equals(cardType, token, StringComparison.Ordinal));
  }

  private static string EffectKindToken(EffectKind effectKind) =>
    effectKind switch
    {
      EffectKind.SimplePlayEffect => "SIMPLE_PLAY_EFFECT",
      EffectKind.DeathrattleEffect => "DEATHRATTLE_EFFECT",
      EffectKind.AuraEffect => "AURA_EFFECT",
      EffectKind.LocationAbilityEffect => "LOCATION_ABILITY_EFFECT",
      _ => effectKind.ToString().ToUpperInvariant()
    };

  private static string CardTypeToken(string cardType) =>
    cardType switch
    {
      "MinionCard" => "MINION_CARD",
      "SpellCard" => "SPELL_CARD",
      "WeaponCard" => "WEAPON_CARD",
      "LocationCard" => "LOCATION_CARD",
      _ => cardType.ToUpperInvariant()
    };

  private static string PhaseToken(Phase phase) =>
    phase switch
    {
      Phase.OnPlay => "ON_PLAY",
      Phase.BattlecryPhase => "BATTLECRY_PHASE",
      Phase.DeathrattlePhase => "DEATHRATTLE_PHASE",
      Phase.AuraRefresh => "AURA_REFRESH",
      _ => phase.ToString().ToUpperInvariant()
    };

  private static string ActualTargetPattern(StructuredCardOutput output)
  {
    var actions = output.Effects.SelectMany(effect => effect.Actions).Select(ActionType).ToHashSet(StringComparer.Ordinal);
    if (actions.Contains("DAMAGE_DISTRIBUTION"))
    {
      return "random-enemy-board";
    }
    if (actions.Contains("DISCOVER"))
    {
      return "fixed-pool-choice";
    }
    if (actions.Contains("RESURRECT"))
    {
      return "own-graveyard-history";
    }
    if (output.Effects.Any(effect => effect.TargetRequirement is not null))
    {
      return output.Effects.Any(effect => effect.TargetRequirement?.Candidates.Controller is "SELF")
        ? "friendly-player-chosen"
        : "player-chosen";
    }
    if (actions.Contains("APPLY_AURA"))
    {
      return "friendly-board";
    }

    return "none-or-auto";
  }

  private static string ActualGameplayRole(StructuredCardOutput output)
  {
    var actions = output.Effects.SelectMany(effect => effect.Actions).Select(ActionType).Distinct().Order(StringComparer.Ordinal).ToArray();
    var role = string.Join("+", actions.DefaultIfEmpty("VANILLA"));
    return role switch
    {
      "VANILLA" => "board_body",
      "DEAL_DAMAGE" => "targeted_damage",
      "DAMAGE_DISTRIBUTION" => "random_damage",
      "DISCOVER" => "choice_value",
      "PREVENT_DAMAGE" => "protection",
      "APPLY_AURA" => "board_aura",
      "DRAW_CARD" => output.CardType is CardType.LocationCard ? "location_value" : "card_flow",
      "DRAW_CARD+HEAL" or "HEAL+DRAW_CARD" => "stabilize_value",
      "RESURRECT" => "graveyard_value",
      _ => role.ToLowerInvariant()
    };
  }

  private static string ActualComplexity(StructuredCardOutput output) =>
    output.Effects.Count == 0
      ? "simple"
      : output.Effects.Sum(effect => effect.Actions.Count) >= 2 ? "compound" : "moderate";

  private static string Slugify(string value)
  {
    var slug = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
    return string.IsNullOrWhiteSpace(slug) ? "generated-card" : slug;
  }

  private static IEnumerable<string> TokenizeName(string name) =>
    Regex.Split(name.ToLowerInvariant(), "[^a-z0-9]+")
      .Where(token => token.Length > 2 && token is not "the" and not "and" and not "of");

  private static IEnumerable<string> NameBigrams(string name)
  {
    var tokens = TokenizeName(name).ToArray();
    for (var i = 0; i + 1 < tokens.Length; i++)
    {
      yield return $"{tokens[i]} {tokens[i + 1]}";
    }
  }

  private static bool IsQualityToken(string token) =>
    token is not "card"
    and not "sample"
    and not "minion"
    and not "spell"
    and not "weapon"
    and not "location"
    and not "damage"
    and not "draw"
    and not "heal"
    and not "health"
    and not "friendly"
    and not "enemy";

  private static string FirstToken(string name) =>
    Regex.Split(name, "[^A-Za-z0-9]+").FirstOrDefault(token => !string.IsNullOrWhiteSpace(token))?.ToLowerInvariant() ?? "none";

  private static string LastToken(string name) =>
    Regex.Split(name, "[^A-Za-z0-9]+").LastOrDefault(token => !string.IsNullOrWhiteSpace(token))?.ToLowerInvariant() ?? "none";

  private static string NameStructure(string name)
  {
    var tokens = Regex.Split(name, "[^A-Za-z0-9]+").Where(token => !string.IsNullOrWhiteSpace(token)).ToArray();
    if (tokens.Length == 0)
    {
      return "empty";
    }
    var themePrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
      "Moonwell", "Emberforge", "Frostarchive", "Stormglass", "Verdant", "Obsidian", "Sunspire", "Tidecall", "Starfall", "Brasslantern", "Mycelium", "Skychain"
    };
    var oldModifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
      "Astral", "Cinder", "Glacial", "Tempest", "Bloom", "Nocturne", "Dawnlit", "Pearl", "Comet", "Copper", "Spore", "Zephyr", "Runed", "Sable", "Hollow", "Gleaming"
    };
    var civicNouns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
      "House", "Company", "Crossing", "Table", "Garden", "Workshop", "Archive", "Library", "Mill", "Tower",
      "Kitchen", "Porch", "Museum", "School", "Foundry", "Clinic", "Theater", "Bakery", "Depot", "Gallery"
    };
    if (tokens.Length == 3 && themePrefixes.Contains(tokens[0]) && oldModifiers.Contains(tokens[1]))
    {
      return "theme-modifier-noun";
    }
    if (name.Contains("'s ", StringComparison.Ordinal))
    {
      return $"possessive-title-{tokens.Length}";
    }
    if (tokens[0].Equals("A", StringComparison.OrdinalIgnoreCase) || tokens[0].Equals("An", StringComparison.OrdinalIgnoreCase))
    {
      return $"article-title-{tokens.Length}";
    }
    if (tokens[0].Equals("Last", StringComparison.OrdinalIgnoreCase))
    {
      return $"last-title-{tokens.Length}";
    }
    if (tokens[0].Equals("The", StringComparison.OrdinalIgnoreCase))
    {
      return $"the-title-{tokens.Length}";
    }
    if (tokens[0].Equals("When", StringComparison.OrdinalIgnoreCase))
    {
      return "event-clause";
    }
    if (tokens.Contains("and", StringComparer.OrdinalIgnoreCase))
    {
      return $"and-phrase-{tokens.Length}";
    }
    if (tokens.Contains("of", StringComparer.OrdinalIgnoreCase))
    {
      return $"of-phrase-{tokens.Length}";
    }
    if (tokens.Contains("at", StringComparer.OrdinalIgnoreCase))
    {
      return $"at-phrase-{tokens.Length}";
    }
    if (tokens.Contains("under", StringComparer.OrdinalIgnoreCase))
    {
      return $"under-phrase-{tokens.Length}";
    }
    if (tokens.Length >= 3 && civicNouns.Contains(tokens[0]))
    {
      return $"civic-title-{tokens.Length}";
    }
    if (tokens.Length >= 3 && civicNouns.Contains(tokens[^1]))
    {
      return $"trailing-civic-title-{tokens.Length}";
    }
    if (tokens.Length >= 3 && tokens.Any(civicNouns.Contains))
    {
      return $"embedded-civic-title-{tokens.Length}";
    }
    if (tokens.Length <= 2)
    {
      return $"short-name-{tokens.Length}";
    }
    if (tokens.Length >= 5)
    {
      return $"long-lore-phrase-{tokens.Length}";
    }

    return $"descriptive-title-{tokens.Length}";
  }

  private static MatrixNamePattern? MatrixNamePattern(string name)
  {
    var tokens = Regex.Split(name, "[^A-Za-z0-9']+").Where(token => !string.IsNullOrWhiteSpace(token)).ToArray();
    if (tokens.Length == 3 && (tokens[0].Equals("A", StringComparison.OrdinalIgnoreCase) || tokens[0].Equals("The", StringComparison.OrdinalIgnoreCase)))
    {
      return new MatrixNamePattern($"{tokens[0].ToLowerInvariant()}-x-y", tokens[1].ToLowerInvariant(), tokens[2].ToLowerInvariant());
    }
    if (tokens.Length == 3 && (tokens[1].Equals("of", StringComparison.OrdinalIgnoreCase) || tokens[1].Equals("and", StringComparison.OrdinalIgnoreCase) || tokens[1].Equals("at", StringComparison.OrdinalIgnoreCase)))
    {
      return new MatrixNamePattern($"x-{tokens[1].ToLowerInvariant()}-y", tokens[0].ToLowerInvariant(), tokens[2].ToLowerInvariant());
    }
    if (tokens.Length == 2 && tokens[0].EndsWith("'s", StringComparison.Ordinal))
    {
      return new MatrixNamePattern("x-possessive-y", tokens[0].TrimEnd('s', '\'').ToLowerInvariant(), tokens[1].ToLowerInvariant());
    }

    return null;
  }

  private static bool IsTemplateLikeNameStructure(string structure) =>
    structure.StartsWith("article-title-", StringComparison.Ordinal)
    || structure.StartsWith("the-title-", StringComparison.Ordinal)
    || structure.StartsWith("of-phrase-", StringComparison.Ordinal)
    || structure.StartsWith("and-phrase-", StringComparison.Ordinal)
    || structure.StartsWith("at-phrase-", StringComparison.Ordinal)
    || structure.StartsWith("possessive-title-", StringComparison.Ordinal)
    || structure.StartsWith("civic-title-", StringComparison.Ordinal)
    || structure.StartsWith("trailing-civic-title-", StringComparison.Ordinal)
    || structure.StartsWith("embedded-civic-title-", StringComparison.Ordinal)
    || structure is "theme-modifier-noun";

  private static string RoleSignature(UnityPackCardReport card) =>
    string.Join("+", card.GameplayProof.EffectActionTypes.DefaultIfEmpty("VANILLA").Order(StringComparer.Ordinal));

  private static string MechanicalRole(UnityPackCardReport card)
  {
    var role = RoleSignature(card);
    return role switch
    {
      "VANILLA" => "board_body",
      "DEAL_DAMAGE" => "targeted_damage",
      "DAMAGE_DISTRIBUTION" => "random_damage",
      "DISCOVER" => "choice_value",
      "PREVENT_DAMAGE" => "protection",
      "APPLY_AURA" => "board_aura",
      "DRAW_CARD" => card.Type is "LocationCard" ? "location_value" : "deathrattle_or_draw",
      "DRAW_CARD+HEAL" or "HEAL+DRAW_CARD" => "stabilize_value",
      "RESURRECT" => "graveyard_value",
      _ => role.ToLowerInvariant()
    };
  }

  private static string ThemePrefix(UnityPackCardReport card)
  {
    var normalized = NormalizeText(card.Name);
    var first = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    return string.IsNullOrWhiteSpace(first) ? "unknown" : first;
  }

  private static string StatLine(UnityPackCardReport card) =>
    $"{card.Type}:{card.ManaCost}:{card.Attack?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}:{card.Health?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}";

  private static string NormalizeText(string value) =>
    Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9 ]", " ").Trim();

  private static string NormalizeRules(string value) =>
    Regex.Replace(NormalizeText(value), "\\b(\\d+)\\b", "#");

  private static string NormalizeRulesPattern(string value, string cardName)
  {
    var normalized = NormalizeRules(value);
    var nameTokens = TokenizeName(cardName)
      .Where(token => token.Length > 3)
      .Distinct(StringComparer.Ordinal)
      .OrderByDescending(token => token.Length)
      .ToArray();
    foreach (var token in nameTokens)
    {
      normalized = Regex.Replace(normalized, $"\\b{Regex.Escape(token)}\\b", "cardname", RegexOptions.IgnoreCase);
    }

    return Regex.Replace(normalized, "\\b(cardname\\s*)+", "cardname").Trim();
  }

  private static int NameWordCount(string name) =>
    Regex.Split(name, "[^A-Za-z0-9]+").Count(token => !string.IsNullOrWhiteSpace(token));

  private static IReadOnlyList<string> NameNaturalnessViolations(string name)
  {
    var normalized = NormalizeText(name);
    var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var diagnostics = new List<string>();
    if (tokens.Length >= 10)
    {
      diagnostics.Add("ten-plus-words");
    }
    else if (tokens.Length >= 8)
    {
      diagnostics.Add("eight-plus-words");
    }

    if (normalized.Contains(" at ", StringComparison.Ordinal) && normalized.Contains(" for ", StringComparison.Ordinal))
    {
      diagnostics.Add("event-at-for-chain");
    }

    var prepositionCount = tokens.Count(token => token is "of" or "at" or "for" or "under" or "beside" or "through" or "beneath");
    if (prepositionCount >= 2)
    {
      diagnostics.Add("stacked-prepositions");
    }

    var hyphenLikeCompoundCount = name.Count(character => character is '-' or '/');
    if (hyphenLikeCompoundCount >= 2)
    {
      diagnostics.Add("compound-chain");
    }

    var blockedReportingWords = new HashSet<string>(StringComparer.Ordinal)
    {
      "advantage", "value", "count", "amount", "target", "pattern", "complexity", "budget", "primitive", "token", "roll", "unity", "fixed", "pool"
    };
    foreach (var token in tokens.Where(blockedReportingWords.Contains).Distinct(StringComparer.Ordinal))
    {
      diagnostics.Add($"reporting-word:{token}");
    }

    if (tokens.Length >= 4 && tokens.Distinct(StringComparer.Ordinal).Count() <= tokens.Length - 1)
    {
      diagnostics.Add("repeated-token-name");
    }

    var nounPileWords = new HashSet<string>(StringComparer.Ordinal)
    {
      "bell", "map", "gate", "oath", "lantern", "ward", "count", "torch", "crown", "ash", "blue", "amber", "quiet", "distant"
    };
    if (tokens.Length >= 4 && tokens.Count(nounPileWords.Contains) >= 3)
    {
      diagnostics.Add("noun-pile-or-word-list");
    }
    var genericNameTerms = new HashSet<string>(StringComparer.Ordinal)
    {
      "arcane", "verdant", "ward", "archive", "tidecall", "frost", "frostbound", "lantern", "glyphwright",
      "resurrection", "cartographer", "vault", "quill", "ember", "iron", "obsidian", "luminary"
    };
    if (tokens.Length <= 2 && tokens.Count(genericNameTerms.Contains) >= 2)
    {
      diagnostics.Add("generic-two-word-label");
    }
    if (tokens.Length == 3 && tokens.Count(genericNameTerms.Contains) >= 2)
    {
      diagnostics.Add("generic-three-word-label");
    }
    if (tokens.Any(token => token.Length == 1 && token.All(char.IsLetter) && token is not "s"))
    {
      diagnostics.Add("single-letter-token");
    }
    if (Regex.IsMatch(name, "\\b[A-Z]{4,}(?:'s)?\\b", RegexOptions.CultureInvariant))
    {
      diagnostics.Add("forced-acronym-name");
    }

    return diagnostics;
  }

  private static bool RulesTextHasFlavorLeakage(string rulesText, string cardName)
  {
    var normalizedRules = NormalizeText(rulesText);
    var normalizedName = NormalizeText(cardName);
    if (!string.IsNullOrWhiteSpace(normalizedName) && normalizedRules.Contains(normalizedName, StringComparison.Ordinal))
    {
      return true;
    }

    return Regex.IsMatch(
      normalizedRules,
      "\\b(invoked|story|watch|seal|hidden|route|old sign|chosen hour|known as|beneath|under|lore|ritual|omen)\\b",
      RegexOptions.IgnoreCase);
  }

  private static IReadOnlyList<string> PlayerFacingRulesTextViolations(string rulesText, string roleSignature)
  {
    var diagnostics = new List<string>();
    var normalized = NormalizeText(rulesText);
    if (string.IsNullOrWhiteSpace(normalized))
    {
      return diagnostics;
    }

    var blockedTerms = new[]
    {
      "unity", "test pool", "fixed pool", "scenario", "dsl", "provider", "validation", "generated", "local", "artifact", "schema", "simulation"
    };
    foreach (var term in blockedTerms)
    {
      if (normalized.Contains(term, StringComparison.Ordinal))
      {
        diagnostics.Add($"technical-leakage:{term.Replace(' ', '-')}");
      }
    }

    if (Regex.IsMatch(normalized, "\\bno effect\\b|\\bvanilla\\b|\\bstat only\\b", RegexOptions.IgnoreCase))
    {
      diagnostics.Add("no-effect-text");
    }
    if (Regex.IsMatch(rulesText, "\\b\\d+\\s*/\\s*\\d+\\s+(?:weapon|minion)\\b", RegexOptions.IgnoreCase))
    {
      diagnostics.Add("statline-in-rules-text");
    }
    if (Regex.IsMatch(normalized, "\\bmark a minion\\b|\\bmark an?\\b", RegexOptions.IgnoreCase))
    {
      diagnostics.Add("unsupported-keyword:mark");
    }
    if (normalized.Contains("play ", StringComparison.Ordinal) || normalized.StartsWith("play ", StringComparison.Ordinal))
    {
      diagnostics.Add("unsupported-timing-keyword:play");
    }
    if (roleSignature.Contains("DISCOVER", StringComparison.Ordinal) && normalized.Contains("from your deck", StringComparison.Ordinal))
    {
      diagnostics.Add("discover-pool-mismatch:deck");
    }

    return diagnostics;
  }

  private static IReadOnlyList<string> NameMechanicalLeakageViolations(string name)
  {
    var tokens = TokenizeName(name).ToHashSet(StringComparer.Ordinal);
    var diagnostics = new List<string>();
    var blockedActionWords = new[]
    {
      "draw", "discovery", "discover", "damage", "destruction", "destroy", "resurrection", "resurrect", "heal", "healing", "prevent", "aura"
    };
    foreach (var word in blockedActionWords)
    {
      if (tokens.Contains(word))
      {
        diagnostics.Add($"action-word:{word}");
      }
    }
    if (Regex.IsMatch(name, "\\b[A-Z]{2,}(?:-[A-Za-z]+)?\\b", RegexOptions.CultureInvariant))
    {
      diagnostics.Add("raw-acronym");
    }

    return diagnostics;
  }

  private static double Similarity(string left, string right)
  {
    if (left.Length == 0 && right.Length == 0)
    {
      return 1;
    }

    var distance = Levenshtein(left, right);
    return 1.0 - (distance / (double)Math.Max(left.Length, right.Length));
  }

  private static int Levenshtein(string left, string right)
  {
    var costs = new int[right.Length + 1];
    for (var j = 0; j <= right.Length; j++)
    {
      costs[j] = j;
    }

    for (var i = 1; i <= left.Length; i++)
    {
      var previous = costs[0];
      costs[0] = i;
      for (var j = 1; j <= right.Length; j++)
      {
        var current = costs[j];
        costs[j] = left[i - 1] == right[j - 1]
          ? previous
          : Math.Min(Math.Min(costs[j] + 1, costs[j - 1] + 1), previous + 1);
        previous = current;
      }
    }

    return costs[right.Length];
  }

  private static GenerationProviderConfiguration Config(string providerId, string modelId, string endpoint, int timeoutSeconds, int maxRetries = 0)
    => new(providerId, modelId, new Uri(endpoint), ApiKey: null, TimeSpan.FromSeconds(timeoutSeconds), MaxRetryAttempts: maxRetries);

  private static string ProductionInstruction(UnityCardPackScenario scenario)
  {
    var coverage = scenario.Coverage.ToHashSet(StringComparer.Ordinal);
    if (coverage.Contains("deathrattle"))
    {
      return "Use DEATHRATTLE_EFFECT with sourceZones [\"BOARD\"], phase DEATHRATTLE_PHASE, tag DEATHRATTLE, and a DRAW_CARD or RESURRECT action.";
    }
    if (coverage.Contains("random-target-spell"))
    {
      return "Use SIMPLE_PLAY_EFFECT with a DAMAGE_DISTRIBUTION action and tag RANDOM_TARGET_RESOLUTION.";
    }
    if (coverage.Contains("discover-choice"))
    {
      return "Use SIMPLE_PLAY_EFFECT with a DRAW_CARD action. DISCOVER is generator-blocked until full public match pool context is available.";
    }
    if (coverage.Contains("damage-prevention"))
    {
      return "Use SIMPLE_PLAY_EFFECT with a PREVENT_DAMAGE action, duration NEXT_DAMAGE, preventionPolicy PREVENT_UP_TO_AMOUNT, and tag DAMAGE_PREVENTION.";
    }
    if (coverage.Contains("location-card"))
    {
      return "Use cardType LOCATION_CARD with LOCATION_ABILITY_EFFECT, sourceZones [\"BOARD\"], phase ON_PLAY, tag LOCATION_PLAY, and a DRAW_CARD action. Do not use APPLY_AURA for this scenario.";
    }
    if (coverage.Contains("aura-modifier"))
    {
      return "Use AURA_EFFECT with sourceZones [\"BOARD\"], phase AURA_REFRESH, tags AURA_REFRESH and BOARD_ATTACK_MODIFIER, and exactly one APPLY_AURA action. The aura.duration and aura.modifier.duration must both be WHILE_SOURCE_ACTIVE.";
    }
    if (coverage.Contains("heal"))
    {
      return "Use SIMPLE_PLAY_EFFECT with HEAL and DRAW_CARD actions in source order.";
    }
    if (coverage.Contains("resurrect"))
    {
      return "Use SIMPLE_PLAY_EFFECT or DEATHRATTLE_EFFECT with a RESURRECT action and historyQuery kind OWN_GRAVEYARD_MINION.";
    }
    if (coverage.Contains("targeted-spell") || coverage.Contains("battlecry"))
    {
      return "Use SIMPLE_PLAY_EFFECT with a DEAL_DAMAGE action and a PLAYER_CHOSEN targetRequirement for a minion.";
    }
    return "Use no effects or one simple supported effect only if it matches the requested coverage.";
  }

  private static UnityPackAssetRef? GenerateAndCopyAsset(
    IGeneratedAssetProvider provider,
    GeneratedAssetManifestBuilder manifestBuilder,
    string generationId,
    string cardId,
    string cardVersionId,
    string assetKind,
    AssetGenerationPrompt prompt,
    VisualAssetMode? visualMode,
    AudioAssetMode? audioMode,
    DateTimeOffset createdAt,
    string outputRoot,
    string relativeOutputPath,
    ICollection<string> diagnostics,
    ICollection<UnityPackProviderFailure> providerFailures,
    string slug,
    string providerRole)
  {
    var result = provider.Generate(new GeneratedAssetProviderRequest(
      $"{generationId}-{assetKind.ToLowerInvariant()}",
      cardId,
      cardVersionId,
      assetKind,
      prompt.Prompt,
      prompt.StyleGuideRef,
      prompt.SafetyPolicyRef,
      TimeSpan.FromSeconds(240)));

    if (!result.Succeeded)
    {
      var failure = ProviderFailure(slug, providerRole, result.ErrorCode, result.ErrorMessage);
      providerFailures.Add(failure);
      diagnostics.Add($"asset:{assetKind}:{failure.Classification}:{result.ErrorCode}:{result.ErrorMessage}");
      return null;
    }

    var source = Path.GetFullPath(result.AssetRef);
    var destination = Path.Combine(outputRoot, relativeOutputPath);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.Copy(source, destination, overwrite: true);

    AssetManifest manifest = visualMode is not null
      ? manifestBuilder.BuildVisualManifest(generationId, cardId, cardVersionId, visualMode.Value, result, createdAt)
      : manifestBuilder.BuildAudioManifest(generationId, cardId, cardVersionId, audioMode ?? AudioAssetMode.SfxOnly, result, [AudioCueType.OnPlay], createdAt);

    return new UnityPackAssetRef(assetKind, result.Provider, result.ModelId, result.Format, relativeOutputPath.Replace('\\', '/'), Path.GetFullPath(destination), result.AssetRef, result.RawAssetRef, result.ContentHash, HashFile(destination), manifest);
  }

  private static CardDefinition BuildCandidate(
    string generationId,
    string cardId,
    string cardVersionId,
    string catalogSnapshotId,
    string modelId,
    DateTimeOffset createdAt,
    GenerationContext context,
    StructuredCardOutput output)
  {
    _ = generationId;
    return new CardDefinition(
      cardId,
      cardVersionId,
      "card-dsl-0.2.0",
      "engine-bootstrap-0.3.0",
      "primitive-registry-bootstrap-0.2.0",
      context.RulesetProfileId,
      catalogSnapshotId,
      new StaticCardData(output.Name, output.CardType, output.ClassTags, output.Rarity, output.ManaCost, output.Tribes, output.Keywords, output.Collectible, output.FormatTags),
      EntityFor(output),
      output.Effects,
      new CardTextGeneration(output.RulesText, FlavorText: null),
      new BalanceMetadata(output.ComplexityScore, output.RandomnessScore, output.RiskFlags),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata("unity-test-card-pack-exporter", createdAt, PipelineVersion, [$"provider:structured:{modelId}", "prompt:unity-test-card-pack-v1"]));
  }

  private static EntityDefinition EntityFor(StructuredCardOutput output) =>
    output.CardType switch
    {
      CardType.MinionCard => new EntityDefinition(
        new MinionDefinition(output.Attack ?? 0, output.Health ?? 1, output.Tribes, BaseKeywords: [], output.CanAttack),
        null,
        null,
        null,
        null,
        null),
      CardType.SpellCard => new EntityDefinition(
        null,
        new SpellDefinition(SpellSchool: null, output.RequiresTarget),
        null,
        null,
        null,
        null),
      CardType.WeaponCard => new EntityDefinition(
        null,
        null,
        new WeaponDefinition(output.Attack ?? 0, output.Health ?? 1, BaseKeywords: []),
        null,
        null,
        null),
      CardType.LocationCard => new EntityDefinition(
        null,
        null,
        null,
        new LocationDefinition(output.Health ?? 1, "ONCE_PER_TURN", "DURABILITY"),
        null,
        null),
      _ => throw new InvalidOperationException($"Unsupported Unity pack card type '{output.CardType}'.")
    };

  private static ResolvedRulesetProfile CreateProfile() =>
    new(
      "ruleset-production-core-v1",
      "ruleset-production-core-v1",
      "1.0.0",
      DateTimeOffset.Parse("2026-07-02T00:00:00Z"),
      "unity-test-card-pack",
      "resolved-unity-test-card-pack-hero-powers-v3",
      "mvp_internal",
      "core_generated_alpha",
      ["BATTLECRY", "DEATHRATTLE", "DAMAGE_PREVENTION", "AURA_REFRESH", "BOARD_ATTACK_MODIFIER", "GRAVEYARD_RESURRECT", "LOCATION_PLAY", "WEAPON_EQUIP", "HERO_POWER_ACTIVATION", "RANDOM_TARGET_RESOLUTION", "VANILLA_MINION"],
      [],
      [],
      ["SIMPLE_PLAY_EFFECT", "DEATHRATTLE_EFFECT", "AURA_EFFECT", "LOCATION_ABILITY_EFFECT"],
      ["PLAY_CARD", "ATTACK", "USE_HERO_POWER", "ACTIVATE_LOCATION", "END_TURN", "CONCEDE", "DEAL_DAMAGE", "DAMAGE_DISTRIBUTION", "DRAW_CARD", "HEAL", "PREVENT_DAMAGE", "APPLY_AURA", "RESURRECT"],
      ["BATTLECRY", "DEATHRATTLE", "DAMAGE_PREVENTION", "WEAPON", "LOCATION", "AURA", "HEAL", "RESURRECT", "RANDOM_TARGET"],
      ["HAND", "BOARD", "DECK", "GRAVEYARD", "SET_ASIDE"],
      ["ON_PLAY", "DEATHRATTLE_PHASE", "AURA_REFRESH"],
      ["UNITY_FIXED_POOL"],
      ["MINION_CARD", "SPELL_CARD", "WEAPON_CARD", "LOCATION_CARD", "HERO_CARD", "HERO_POWER_CARD"],
      ["MINION", "HERO", "WEAPON", "LOCATION", "CARD_IN_GRAVEYARD"],
      ["COMMON", "RARE", "EPIC"],
      ["mage"],
      [],
      new RulesetLimits(30, 25, 20, 80, 7, 4, "LIMITED", 3, 2, 4, 4, 4, 3, 2, 3, 1, 7, 10),
      new ValidationPolicyRef("validation_unity_pack", true, true, true, true, true, true, false, false, false),
      new BalancePolicyRef("balance_unity_pack", false, "HIGH", [], 0.05, 30, 25, 0.1, 0, 0, "baseline", "seed", "none"),
      new SimulationPolicyRef("simulation_unity_pack", [], [], 0, 0, false, 30, 80),
      new PublicationPolicyRef("publication_unity_pack", true, false, false, false, false, false, false, ["INTERNAL", "UNITY_BASELINE"]),
      new LegacyPolicy(false, false, false, false, true));

  private static IReadOnlyList<MechanicStatusRecord> CreateMechanics(IEnumerable<string> mechanicIds) =>
    mechanicIds.Distinct(StringComparer.Ordinal)
      .Select(id => new MechanicStatusRecord(id, MechanicStatus.ApprovedForRuleset, MechanicTier.Tier0, "unity-test-card-pack", [], ["ruleset-production-core-v1"], null, null, ["docs/generator/mechanic_status_spec.md"], [], [], []))
      .ToArray();

  private static async Task WriteContactSheet(string outputRoot, IReadOnlyList<UnityPackCardReport> cards)
  {
    var rows = string.Join(Environment.NewLine, cards.Select(card =>
      $"<article><img src=\"{card.Assets.FirstOrDefault(a => a.AssetKind == "STATIC_IMAGE")?.RelativePath ?? string.Empty}\" alt=\"{card.Name}\"><h2>{card.Name}</h2><p>{card.Type} - {string.Join(", ", card.Scenarios)}</p></article>"));
    var html = $"<!doctype html><html><head><meta charset=\"utf-8\"><title>Unity Test Card Pack</title><style>body{{font-family:Arial,sans-serif;margin:24px}}main{{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:16px}}img{{width:100%;border:1px solid #bbb}}h2{{font-size:16px;margin:8px 0 4px}}p{{font-size:12px}}</style></head><body><h1>Unity Test Card Pack</h1><main>{rows}</main></body></html>";
    await File.WriteAllTextAsync(Path.Combine(outputRoot, "contact-sheet.html"), html);
  }

  private static string HashFile(string path)
  {
    using var stream = File.OpenRead(path);
    return $"sha256:{Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()}";
  }

  private static StructuredTrace? ReadStructuredTrace(string rawStructuredOutputRef)
  {
    if (string.IsNullOrWhiteSpace(rawStructuredOutputRef) || !File.Exists(rawStructuredOutputRef))
    {
      return null;
    }

    try
    {
      return JsonSerializer.Deserialize<StructuredTrace>(File.ReadAllText(rawStructuredOutputRef), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
    catch (JsonException)
    {
      return null;
    }
  }
}

public sealed record UnityCardPackOptions(
  string StructuredEndpoint,
  string StructuredModel,
  string ImageEndpoint,
  string ImageModel,
  string AnimationEndpoint,
  string AnimationModel,
  string AudioEndpoint,
  string AudioModel,
  string Mode,
  int Count,
  string OutputRoot,
  string CatalogSnapshotId,
  bool SkipAssets,
  int ProviderTimeoutSeconds,
  int AssetProviderTimeoutSeconds,
  int RunTimeoutSeconds,
  int MaxGenerationAttemptsPerCard,
  int MaxRepairAttemptsPerCard,
  int MaxProviderRetries,
  string FailureRoot,
  string RunName)
{
  public static UnityCardPackOptions Parse(string[] args)
  {
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
      if (!args[i].StartsWith("--", StringComparison.Ordinal))
      {
        continue;
      }
      var key = args[i][2..];
      if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
      {
        values[key] = args[++i];
      }
      else
      {
        values[key] = "true";
      }
    }

    return new UnityCardPackOptions(
      values.GetValueOrDefault("structured-endpoint") ?? Environment.GetEnvironmentVariable("CCG_LOCAL_STRUCTURED_ENDPOINT") ?? "http://127.0.0.1:8011/v1/structured-card",
      values.GetValueOrDefault("structured-model") ?? Environment.GetEnvironmentVariable("CCG_LOCAL_STRUCTURED_MODEL") ?? "qwen3.5:latest",
      values.GetValueOrDefault("image-endpoint") ?? Environment.GetEnvironmentVariable("CCG_LOCAL_IMAGE_ENDPOINT") ?? "http://127.0.0.1:8012/v1/assets",
      values.GetValueOrDefault("image-model") ?? Environment.GetEnvironmentVariable("CCG_LOCAL_IMAGE_MODEL") ?? "stable-diffusion-local",
      values.GetValueOrDefault("animation-endpoint") ?? Environment.GetEnvironmentVariable("CCG_LOCAL_ANIMATION_ENDPOINT") ?? "http://127.0.0.1:8013/v1/assets",
      values.GetValueOrDefault("animation-model") ?? Environment.GetEnvironmentVariable("CCG_LOCAL_ANIMATION_MODEL") ?? "stable-diffusion-local-idle-gif",
      values.GetValueOrDefault("audio-endpoint") ?? Environment.GetEnvironmentVariable("CCG_LOCAL_AUDIO_ENDPOINT") ?? "http://127.0.0.1:8013/v1/assets",
      values.GetValueOrDefault("audio-model") ?? Environment.GetEnvironmentVariable("CCG_LOCAL_AUDIO_MODEL") ?? "local-procedural-chime-v1",
      (values.GetValueOrDefault("mode") ?? "curated").Equals("production", StringComparison.OrdinalIgnoreCase) ? "production" : "curated",
      int.TryParse(values.GetValueOrDefault("count"), out var count) ? Math.Clamp(count, 1, 200) : 12,
      values.GetValueOrDefault("output-root") ?? Path.Combine("artifacts", "unity-test-card-pack", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss")),
      values.GetValueOrDefault("catalog-snapshot-id") ?? "catalog-unity-test-card-pack-v1",
      values.ContainsKey("skip-assets"),
      int.TryParse(values.GetValueOrDefault("provider-timeout-seconds"), out var providerTimeout) ? Math.Clamp(providerTimeout, 5, 600) : 45,
      int.TryParse(values.GetValueOrDefault("asset-provider-timeout-seconds"), out var assetTimeout) ? Math.Clamp(assetTimeout, 5, 600) : 120,
      int.TryParse(values.GetValueOrDefault("run-timeout-seconds"), out var runTimeout) ? Math.Clamp(runTimeout, 10, 21600) : 3600,
      int.TryParse(values.GetValueOrDefault("max-generation-attempts-per-card"), out var maxGenerationAttempts) ? Math.Clamp(maxGenerationAttempts, 1, 5) : 2,
      int.TryParse(values.GetValueOrDefault("max-repair-attempts-per-card"), out var maxRepairAttempts) ? Math.Clamp(maxRepairAttempts, 0, 5) : 1,
      int.TryParse(values.GetValueOrDefault("max-provider-retries"), out var maxProviderRetries) ? Math.Clamp(maxProviderRetries, 0, 5) : 0,
      values.GetValueOrDefault("failure-root") ?? Path.Combine("artifacts", "generator-failures"),
      values.GetValueOrDefault("run-name") ?? "unity-card-pack");
  }
}

public sealed record UnityPackCardReport(
  string Slug,
  string CardId,
  string CardVersionId,
  string Name,
  string Description,
  string Type,
  int ManaCost,
  int? Attack,
  int? Health,
  IReadOnlyList<string> Scenarios,
  string UnityComment,
  UnityPackDerivedMetadata DerivedMetadata,
  UnityPackScenarioMetadataConsistency ScenarioMetadataConsistency,
  UnityPackRollReference RollReference,
  string GenerationReference,
  UnityPackRollConformance RollConformance,
  StructuredCardOutput Dsl,
  StructuredCardGenerationResult ProviderStructuredResult,
  StructuredTrace? StructuredTrace,
  GeneratedCardValidationPipelineReport Validation,
  GeneratedCardBalanceEvaluation Balance,
  bool PublicationSucceeded,
  string CatalogSnapshotId,
  UnityPackGameplayProof GameplayProof,
  string SimulationStatus,
  string ReplayStatus,
  IReadOnlyList<UnityPackAssetRef> Assets,
  bool HasFullAssetSet,
  int SemanticWarningCount,
  bool GameplaySmokePassed,
  bool ReplayVerified,
  string Verdict,
  IReadOnlyList<string> Diagnostics,
  string JsonPath);

public sealed record UnityPackAssetRef(
  string AssetKind,
  string Provider,
  string ModelId,
  string Format,
  string RelativePath,
  string AbsolutePath,
  string ProviderAssetRef,
  string RawAssetRef,
  string ProviderContentHash,
  string ExportedContentHash,
  AssetManifest Manifest);

public sealed record UnityPackManifest(
  string PackId,
  DateTimeOffset CreatedAt,
  string CatalogSnapshotId,
  bool UnityBaselineUsable,
  IReadOnlyList<UnityPackManifestCard> Cards);

public sealed record UnityPackManifestCard(
  string Slug,
  string CardId,
  string CardVersionId,
  string Name,
  string Type,
  IReadOnlyList<string> Scenarios,
  string JsonPath,
  IReadOnlyList<UnityPackAssetRef> Assets,
  string ValidationStatus,
  string BalanceStatus,
  bool PublicationSucceeded);

public sealed record UnityPackSummary(
  int RequestedCount,
  int SuccessfullyGenerated,
  int ValidatedCount,
  int PublishedCount,
  int FullAssetSetCount,
  IReadOnlyList<UnityPackRejectedCard> RejectedCards,
  int RawLlmOutputsValidImmediately,
  int RepairedCount,
  int SafePreflightNormalizationCount,
  int MaterialPreflightRewriteCount,
  int NameRewrittenByPreflight,
  int RulesTextRewrittenByPreflight,
  int EffectsRewrittenByPreflight,
  int ProviderRepairCount,
  int CreativeQualityRepairCount,
  int NameRepairCount,
  int NameRerollCount,
  int DeterministicNameFallbackUsed,
  int RejectedCount,
  int SemanticWarningsCount,
  int GameplaySmokeCount,
  int ReplayVerifiedCount,
  IReadOnlyList<string> EffectTypesCovered,
  IReadOnlyList<string> CardTypesCovered,
  UnityPackStressReport StressReport,
  UnityPackBalanceQualityReport BalanceQualityReport,
  UnityPackScenarioMetadataReport ScenarioMetadataReport,
  UnityPackRollReferenceReport RollReferenceReport,
  UnityPackSimilarityReport SimilarityReport,
  UnityPackDiversityReport DiversityReport,
  UnityPackThemeDiversityReport ThemeDiversityReport,
  UnityPackMechanicCoverageReport MechanicCoverageReport,
  UnityPackShowcaseReadinessReport ShowcaseReadinessReport,
  UnityPackCreativeQualityReport CreativeQualityReport,
  UnityPackRepairGovernanceReport RepairGovernanceReport,
  UnityPackRepairRateReport RepairRateReport,
  UnityPackComplexEffectsReport ComplexEffectsReport,
  UnityPackBotVsBotRepresentativeProof BotVsBotRepresentativeProof,
  UnityPackProviderReliabilitySummary ProviderReliabilitySummary,
  bool AssetsSkipped,
  int MediaProviderCalls,
  string HasFullAssetSet,
  UnityPackMediaReadinessReport MediaReadiness,
  UnityPackReadinessVerdicts ReadinessVerdicts,
  string RunExecutionStatus,
  string ArtifactAcceptanceStatus,
  string GeneratorReadinessVerdict,
  bool FailureEvidenceAvailable,
  string? FailureArtifactPath,
  UnityPackRunTiming Timing,
  IReadOnlyList<UnityPackProviderCallTrace> ProviderCallTrace,
  string FinalVerdict,
  bool UnityBaselineUsable,
  IReadOnlyList<string> RisksAndLimitations);

public sealed record UnityPackProviderCallTrace(
  string Slug,
  string ProviderRole,
  string Endpoint,
  string Model,
  int PromptCharacterCount,
  int PromptTokenEstimate,
  int DiagnosticsCharacterCount,
  int TimeoutSeconds,
  int MaxRetries,
  bool Succeeded,
  string? ErrorCode,
  string FailureClassification,
  double DurationMs,
  string RawStructuredOutputRef);

public sealed record UnityPackCardTiming(
  string Slug,
  DateTimeOffset StartedAt,
  DateTimeOffset EndedAt,
  double DurationMs,
  bool Succeeded,
  IReadOnlyList<string> Diagnostics);

public sealed record UnityPackRunTiming(
  DateTimeOffset StartedAt,
  DateTimeOffset? EndedAt,
  double DurationMs,
  IReadOnlyList<UnityPackCardTiming> Cards,
  string? SlowestCard,
  string? SlowestProviderCall,
  double ProviderDurationP50Ms,
  double ProviderDurationP95Ms,
  double ProviderDurationP99Ms);

public sealed record UnityPackFailureReport(
  string RunExecutionStatus,
  string ArtifactAcceptanceStatus,
  string GeneratorReadinessVerdict,
  bool FailureEvidenceAvailable,
  string FailureCode,
  string FailureMessage,
  string OutputRoot,
  IReadOnlyList<UnityPackProviderCallTrace> ProviderCallTraces,
  UnityPackRunTiming Timing);

public sealed record UnityPackMediaReadinessReport(
  bool AssetsSkipped,
  int MediaProviderCalls,
  string HasFullAssetSet,
  string MediaReadiness,
  int MediaFailureCount);

public sealed record UnityPackRepairGovernanceReport(
  int SafePreflightNormalizationCount,
  int MaterialPreflightRewriteCount,
  int NameRewrittenByPreflight,
  int RulesTextRewrittenByPreflight,
  int EffectsRewrittenByPreflight,
  int ProviderRepairCount,
  int CreativeQualityRepairCount,
  int NameRepairCount,
  int NameRerollCount,
  int DeterministicNameFallbackUsed,
  int RulesTextRepairCount,
  int EffectsRepairCount,
  int StatsCostRepairCount,
  int CardsWithRepairedEffects,
  int CardsWithRepairedRulesText,
  int MaxRepairCountPerCard,
  IReadOnlyList<UnityPackMaterialPreflightRewriteExample> MaterialPreflightRewriteExamples);

public sealed record UnityPackRepairRateReport(
  double RawValidRate,
  double ProviderRepairRate,
  double MaterialRepairRate,
  double NameRepairRate,
  double RulesTextRepairRate,
  double EffectsRepairRate,
  double StatsCostRepairRate,
  double RawValidThresholdLocal,
  double RawValidThresholdProduction,
  double ProviderRepairThresholdProduction,
  double MaterialRepairThresholdProduction,
  double EffectsRepairThresholdProduction);

public sealed record UnityPackReadinessVerdicts(
  string RawLlmOutputReadiness,
  string RepairConvergedPipelineReadiness,
  string GameplayContractReadiness,
  string BotVsBotReadiness,
  string MediaInclusiveReadiness,
  string UnityBaselineReadiness,
  string ReportingReadiness,
  IReadOnlyList<string> BlockingReasons);

public sealed record UnityPackMaterialPreflightRewriteExample(
  string Slug,
  string Name,
  string Path,
  string Change,
  string? Before,
  string? After,
  string Reason,
  bool IntentChanged);

public sealed record UnityPackRejectedCard(
  string Slug,
  string CardId,
  string Name,
  IReadOnlyList<string> Reasons);

public sealed record UnityPackDerivedMetadata(
  string Slug,
  IReadOnlyList<string> Labels,
  string ActualCardType,
  IReadOnlyList<string> ActualActionFamilies,
  IReadOnlyList<string> ActualEffectKinds,
  IReadOnlyList<string> ActualTimingWindows,
  string ActualTargetPattern,
  string ActualGameplayRole,
  string ActualComplexity,
  string Comment)
{
  public static UnityPackDerivedMetadata Curated(string slug, IReadOnlyList<string> labels, string comment) =>
    new(slug, labels, "curated", [], [], [], "curated", "curated", "curated", comment);
}

public sealed record UnityPackScenarioMetadataConsistency(
  bool MatchesFinalDsl,
  IReadOnlyList<string> Diagnostics);

public sealed record UnityPackRollReference(
  string EffectPresence,
  string EffectComplexity,
  string PrimaryPrimitive,
  string? SecondaryPrimitive,
  string CardTypeDirection,
  string CostBand,
  string TargetPattern,
  string TimingWindow,
  string GameplayRole,
  string FlavorMood,
  string NamingStyle,
  string TextStyle,
  string MechanicalTwist,
  string Unusualness)
{
  public static UnityPackRollReference Curated(IReadOnlyList<string> coverage) =>
    new(
      "curated",
      "curated",
      string.Join("+", coverage),
      null,
      "curated",
      "curated",
      "curated",
      "curated",
      "curated",
      "curated",
      "curated",
      "curated",
      "curated",
      "curated");
}

public sealed record UnityPackMechanicalDirection(
  string CardType,
  string TargetPattern,
  string TimingWindow);

public sealed record UnityPackRollConformance(
  string Status,
  bool HardIgnoredDirection,
  IReadOnlyList<string> Diagnostics);

public sealed record StructuredTrace(
  bool RawLlmOutputValidImmediately,
  bool Repaired,
  int RepairCount,
  IReadOnlyList<string> ValidationErrors,
  bool PreflightNormalized = false,
  IReadOnlyList<StructuredTraceDiff>? PreflightDiff = null,
  IReadOnlyList<StructuredTraceDiff>? RepairDiff = null,
  int SafePreflightNormalizationCount = 0,
  int MaterialPreflightRewriteCount = 0,
  bool NameRewrittenByPreflight = false,
  bool RulesTextRewrittenByPreflight = false,
  bool EffectsRewrittenByPreflight = false,
  int ProviderRepairCount = 0,
  int CreativeQualityRepairCount = 0,
  int NameRepairCount = 0,
  int NameRerollCount = 0,
  bool DeterministicNameFallbackUsed = false,
  IReadOnlyList<StructuredTraceDiff>? MaterialPreflightDiff = null);

public sealed record StructuredTraceDiff(
  string Path,
  string Change,
  object? Before,
  object? After);

public sealed record UnityCardPackScenarioRun(
  string Slug,
  UnityCardPackScenario? Scenario,
  int Index);

public sealed record UnityPackGameplayProof(
  string ProofProfile,
  bool CardAddedToDeck,
  bool PlayedOrActivated,
  bool EffectResolved,
  bool ReplayVerified,
  string CommandType,
  IReadOnlyList<string> EffectActionTypes,
  IReadOnlyList<string> EvidenceRefs,
  IReadOnlyList<string> Diagnostics)
{
  public string ProofStatus =>
    CardAddedToDeck && PlayedOrActivated && EffectResolved && ReplayVerified
      ? "FULL_PER_CARD_GAMEPLAY_PROOF_PASSED"
      : "FULL_PER_CARD_GAMEPLAY_PROOF_FAILED";
}

public sealed record UnityPackStressReport(
  int Requested,
  int Generated,
  int RawValidImmediately,
  int Repaired,
  int Rejected,
  int SemanticWarnings,
  int ValidationFailures,
  int PublicationFailures,
  int AssetFailures,
  IReadOnlyDictionary<string, int> EffectCoverage,
  IReadOnlyDictionary<string, int> CardTypeCoverage,
  double DuplicateSimilarScore,
  int DuplicateSimilarViolations,
  IReadOnlyList<UnityPackSimilarityViolation> SimilarCards);

public sealed record UnityPackBalanceQualityReport(
  IReadOnlyDictionary<string, int> ManaCurve,
  IReadOnlyDictionary<string, int> AttackCurve,
  IReadOnlyDictionary<string, int> HealthCurve,
  IReadOnlyDictionary<string, int> CardTypes,
  IReadOnlyDictionary<string, int> EffectTypes,
  IReadOnlyDictionary<string, int> RiskFlags,
  IReadOnlyList<string> DuplicateOrSimilarCards,
  IReadOnlyList<string> TooWeakCandidates,
  IReadOnlyList<string> TooStrongCandidates,
  IReadOnlyList<UnityPackBalanceQualityFinding> Findings,
  int UnjustifiedFindingCount,
  IReadOnlyList<UnityPackBalanceEstimate>? ValueEstimates = null);

public sealed record UnityPackBalanceEstimate(
  string Slug,
  string CardName,
  double EstimatedStatValue,
  double EstimatedEffectValue,
  double TotalEstimatedValue,
  double TargetValueForCost,
  double Delta,
  double RepeatabilityModifier,
  double TargetFlexibilityModifier,
  double RandomnessModifier,
  double CardAdvantageModifier,
  double BoardImpactModifier,
  double SurvivabilityModifier,
  double RiskModifier,
  string RiskCategory,
  string Reason,
  string RepairSuggestion);

public sealed record UnityPackBalanceQualityFinding(
  string Slug,
  string CardName,
  string Kind,
  bool AcceptedWithJustification,
  string Justification);

public sealed record UnityPackScenarioMetadataReport(
  int CheckedCount,
  int MismatchCount,
  IReadOnlyDictionary<string, int> CardTypes,
  IReadOnlyDictionary<string, int> ActionFamilies,
  IReadOnlyDictionary<string, int> EffectKinds,
  IReadOnlyDictionary<string, int> TimingWindows,
  IReadOnlyDictionary<string, int> TargetPatterns,
  IReadOnlyDictionary<string, int> GameplayRoles,
  IReadOnlyDictionary<string, int> Complexity,
  IReadOnlyDictionary<string, int> RollConformance,
  IReadOnlyDictionary<string, int> LocationAbilityFamilies,
  IReadOnlyList<UnityPackScenarioMetadataMismatch> Mismatches);

public sealed record UnityPackScenarioMetadataMismatch(
  string Slug,
  string Name,
  IReadOnlyList<string> Diagnostics);

public sealed record UnityPackRollReferenceReport(
  IReadOnlyDictionary<string, int> ConformanceStatuses,
  IReadOnlyDictionary<string, int> PrimaryPrimitives,
  IReadOnlyDictionary<string, int> SecondaryPrimitives,
  IReadOnlyDictionary<string, int> CardTypeDirections,
  IReadOnlyDictionary<string, int> CostBands,
  IReadOnlyDictionary<string, int> TimingWindows,
  IReadOnlyDictionary<string, int> TargetPatterns,
  IReadOnlyDictionary<string, int> NamingStyles,
  IReadOnlyDictionary<string, int> TextStyles,
  IReadOnlyDictionary<string, int> MechanicalTwists,
  int HardIgnoredDirections,
  IReadOnlyList<string> IgnoredOrInvalidSlugs);

public sealed record UnityPackSimilarityReport(
  double DuplicateSimilarScore,
  int ViolationCount,
  IReadOnlyList<UnityPackSimilarityViolation> Violations);

public sealed record UnityPackSimilarityViolation(
  string LeftSlug,
  string RightSlug,
  double Score,
  string LeftName,
  string RightName,
  string LeftRole,
  string RightRole);

public sealed record UnityPackDiversityReport(
  IReadOnlyDictionary<string, int> ThemeDistribution,
  IReadOnlyDictionary<string, int> MechanicalArchetypes,
  IReadOnlyDictionary<string, int> EffectPatterns,
  int ViolationCount,
  IReadOnlyList<string> Violations);

public sealed record UnityPackThemeFamily(
  string ThemeFamilyId,
  IReadOnlyList<string> NameTokens,
  IReadOnlyList<string> VisualMotifs,
  IReadOnlyList<string> CompatibleMechanics,
  IReadOnlyList<string> ForbiddenOverusedTokens,
  IReadOnlyList<string> ExampleNames);

public sealed record UnityPackThemeAssignment(
  string CardName,
  string ThemeFamilyId,
  int MatchScore);

public sealed record UnityPackThemeDiversityReport(
  IReadOnlyList<UnityPackThemeFamily> Taxonomy,
  IReadOnlyList<UnityPackThemeAssignment> Assignments,
  IReadOnlyDictionary<string, int> ThemeDistribution,
  IReadOnlyDictionary<string, int> OverusedTokenClusters,
  IReadOnlyDictionary<string, int> RepeatedVisualMotifSkeletons,
  int RequiredThemeFamilies,
  double MaxThemeFamilyShare,
  int ViolationCount,
  IReadOnlyList<string> Violations);

public sealed record UnityPackMechanicCoverageReport(
  IReadOnlyList<UnityPackMechanicCoverageEntry> Entries,
  int GeneratorProductionSupportedCount,
  int BlockedMechanicCount);

public sealed record UnityPackMechanicCoverageEntry(
  string MechanicId,
  IReadOnlyList<string> EngineCapabilityIds,
  bool EngineSupported,
  bool GeneratorSupported,
  bool PromptSupported,
  bool CanonicalRulesTextSupported,
  bool SemanticValidationSupported,
  bool GameplayProofSupported,
  bool ReplayProofSupported,
  bool BotProofSupported,
  bool UnityPresentationHintSupported,
  string Status,
  string? Blocker)
{
  public bool GeneratorProductionSupported =>
    EngineSupported
    && GeneratorSupported
    && PromptSupported
    && CanonicalRulesTextSupported
    && SemanticValidationSupported
    && GameplayProofSupported
    && ReplayProofSupported
    && BotProofSupported
    && UnityPresentationHintSupported;
}

public sealed record UnityPackShowcaseReadinessReport(
  IReadOnlyList<UnityPackShowcaseCardScore> Cards,
  int ShowcaseReadyCards,
  double ShowcaseReadyRate,
  int RepresentedEffectFamilies,
  int RequiredEffectFamilies,
  bool PackShowcaseReady,
  IReadOnlyList<string> PackBlockers);

public sealed record UnityPackShowcaseCardScore(
  string Slug,
  string CardName,
  string ThemeFamilyId,
  double NameQualityScore,
  double ThemeFreshnessScore,
  double MechanicalInterestScore,
  double RulesClarityScore,
  double BalanceConfidenceScore,
  double VisualIdentityScore,
  double PackDiversityContributionScore,
  double PublicShowcaseScore,
  bool ShowcaseReady,
  IReadOnlyList<string> Blockers);

public sealed record UnityPackCreativeQualityReport(
  UnityPackNamingDiversityReport Naming,
  UnityPackRulesTextDiversityReport RulesText,
  UnityPackFlavorDiversityReport Flavor,
  int ViolationCount,
  IReadOnlyList<string> Violations);

public sealed record UnityPackCreativeQualityInput(
  string Name,
  string RulesText,
  int ManaCost,
  string CardShape,
  string TargetPattern,
  string RoleSignature,
  string FlavorMood,
  string NamingStyle,
  string TextStyle,
  string MechanicalTwist,
  string VisualPrompt);

public sealed record MatrixNamePattern(
  string Structure,
  string PrefixToken,
  string TerminalToken);

public sealed record UnityPackNamingDiversityReport(
  int TotalNames,
  int UniqueNames,
  int FormulaicNameCount,
  double FormulaicNamePercent,
  double AverageWordCount,
  int MaxWordCount,
  int LongNameCount,
  int VeryLongNameCount,
  int NaturalnessViolationCount,
  IReadOnlyList<string> NaturalnessViolations,
  IReadOnlyDictionary<string, int> RepeatedTokens,
  IReadOnlyDictionary<string, int> NameStructures,
  IReadOnlyDictionary<string, int> TopPrefixes,
  IReadOnlyDictionary<string, int> TopSuffixes,
  int MatrixLikeNameCount,
  IReadOnlyDictionary<string, int> MatrixNameStructures,
  IReadOnlyList<string> MatrixPatternViolations);

public sealed record UnityPackRulesTextDiversityReport(
  int TotalRulesText,
  int UniqueRulesText,
  int RepeatedRulesTextCount,
  IReadOnlyDictionary<string, int> TopRepeatedTextPatterns,
  int NormalizedUniqueRulesTextPatterns,
  int RepeatedNormalizedRulesTextPatternCount,
  IReadOnlyDictionary<string, int> TopRepeatedNormalizedRulesTextPatterns,
  int FlavorLeakageCount,
  IReadOnlyList<string> FlavorLeakageCards,
  IReadOnlyDictionary<string, int> EffectSignatureRepetition,
  int MechanicallyIdenticalCardsCount);

public sealed record UnityPackFlavorDiversityReport(
  IReadOnlyDictionary<string, int> FlavorMoodDistribution,
  IReadOnlyDictionary<string, int> NamingStyleDistribution,
  IReadOnlyDictionary<string, int> TextStyleDistribution,
  IReadOnlyDictionary<string, int> MechanicalTwistDistribution,
  int UniqueVisualPrompts);

public sealed record UnityPackComplexEffectsReport(
  IReadOnlyList<UnityPackComplexEffectProof> Effects)
{
  public bool AllPassed => Effects.All(effect => effect.Passed);
}

public sealed record UnityPackComplexEffectProof(
  string EffectFamily,
  string? RepresentativeSlug,
  bool GeneratedProductionNative,
  bool ValidationPassed,
  bool PublicationPassed,
  bool GameplayProofPassed,
  bool ReplayVerified,
  bool ProviderOutputSucceeded,
  IReadOnlyList<string> Diagnostics)
{
  public bool Passed =>
    GeneratedProductionNative
    && ValidationPassed
    && PublicationPassed
    && GameplayProofPassed
    && ReplayVerified
    && ProviderOutputSucceeded;
}

public sealed record UnityPackBotVsBotRepresentativeProof(
  string ProofProfile,
  bool FullBotVsBotExecuted,
  string Limitation,
  IReadOnlyList<UnityPackBotVsBotRepresentativeEntry> Entries)
{
  public bool RepresentativeCoveragePassed => Entries.All(entry => entry.ReplayVerified && entry.EffectResolved);
}

public sealed record UnityPackBotVsBotRepresentativeEntry(
  string EffectFamily,
  string? RepresentativeSlug,
  bool CardAddedToDeck,
  bool PlayedOrActivated,
  bool EffectResolved,
  bool ReplayVerified,
  IReadOnlyList<string> EvidenceRefs);

public sealed record UnityPackProviderHealth(
  string ProviderRole,
  string HealthEndpoint,
  bool Healthy,
  int? HttpStatus,
  string Classification);

public sealed record UnityPackProviderFailure(
  string Slug,
  string ProviderRole,
  string ErrorCode,
  string ErrorMessage,
  string Classification);

public sealed record UnityPackProviderReliabilitySummary(
  IReadOnlyList<UnityPackProviderHealth> Health,
  IReadOnlyList<UnityPackProviderFailure> Failures,
  IReadOnlyDictionary<string, int> FailureClassifications,
  int TransientFailureCount,
  bool AllHealthyBeforeRun,
  bool RuntimeStable);
