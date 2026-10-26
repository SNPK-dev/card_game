using Game.Generator.Audit;
using Game.Generator.Providers;
using Game.Generator.Publication;
using Game.Generator.Skeletons;
using Game.Generator.Validation;
using Game.Shared.Assets;
using Game.Shared.Dsl;

namespace Game.Generator.Generation;

public sealed class ProductionCardGenerationPipeline(
  IStructuredCardGenerationProvider structuredProvider,
  IGeneratedAssetProvider imageProvider,
  IGeneratedAssetProvider animationProvider,
  IGeneratedAssetProvider? audioProvider = null,
  GeneratedCardValidationPipeline? validationPipeline = null,
  GeneratedCardPublisher? publisher = null,
  GeneratedAssetManifestBuilder? assetManifestBuilder = null,
  GenerationAuditLogEntryBuilder? auditLogEntryBuilder = null)
{
  private readonly GeneratedCardValidationPipeline _validationPipeline = validationPipeline ?? GeneratedCardValidationPipeline.CreateDefault();
  private readonly GeneratedCardPublisher _publisher = publisher ?? new GeneratedCardPublisher();
  private readonly GeneratedAssetManifestBuilder _assetManifestBuilder = assetManifestBuilder ?? new GeneratedAssetManifestBuilder();
  private readonly GenerationAuditLogEntryBuilder _auditLogEntryBuilder = auditLogEntryBuilder ?? new GenerationAuditLogEntryBuilder();

  public ProductionCardGenerationResult Run(ProductionCardGenerationRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    var diagnostics = new List<ProductionCardGenerationDiagnostic>();
    var repairAttemptRefs = new List<string>();
    StructuredCardGenerationResult? structuredResult = null;
    CardDefinition? candidate = null;
    GeneratedCardValidationPipelineReport? validationReport = null;

    var maxAttempts = Math.Max(1, request.MaxRepairAttempts + 1);
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
      structuredResult = structuredProvider.Generate(new StructuredCardGenerationRequest(
        request.GenerationId,
        request.PromptVersion,
        request.ModelId,
        BuildPrompt(request, diagnostics, attempt),
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["rulesetProfileId"] = request.Context.RulesetProfileId,
          ["rulesetProfileHash"] = request.Context.RulesetProfileHash,
          ["catalogSnapshotId"] = request.CatalogSnapshotId
        },
        TimeSpan.FromSeconds(60)));

      if (!structuredResult.Succeeded || structuredResult.Output is null)
      {
        Add(diagnostics, "STRUCTURED_PROVIDER_FAILED", "provider.structured", structuredResult.Provider, structuredResult.ErrorMessage ?? "Structured provider failed.");
        return Failure(ProductionCardGenerationStatus.FailedProvider, diagnostics, repairAttemptRefs);
      }

      try
      {
        candidate = BuildCandidate(request, structuredResult.Output);
      }
      catch (InvalidOperationException exception)
      {
        Add(diagnostics, "STRUCTURED_OUTPUT_INVALID", "provider.structured.output", structuredResult.RawStructuredOutputRef, exception.Message);
        repairAttemptRefs.Add($"repair:{request.GenerationId}:{attempt:000}");
        continue;
      }

      validationReport = _validationPipeline.Validate(new GeneratedCardValidationRequest(
        request.GenerationId,
        request.PipelineVersion,
        request.ContextVersion,
        candidate,
        request.Context,
        request.BalanceReport,
        request.SimulationReport,
        request.ReplayReport));

      if (validationReport.FinalDecision is GeneratedCardValidationDecision.Accepted)
      {
        break;
      }

      repairAttemptRefs.Add($"repair:{request.GenerationId}:{attempt:000}");
      diagnostics.AddRange(validationReport.Diagnostics.Select(diagnostic => new ProductionCardGenerationDiagnostic(
        $"VALIDATION_{diagnostic.Code}",
        diagnostic.Path,
        diagnostic.ActualValue,
        diagnostic.Message)));
    }

    if (candidate is null || validationReport is null || validationReport.FinalDecision is not GeneratedCardValidationDecision.Accepted)
    {
      return Failure(ProductionCardGenerationStatus.FailedValidation, diagnostics, repairAttemptRefs);
    }

    var acceptedOutput = structuredResult?.Output
      ?? throw new InvalidOperationException("Accepted generation attempt must carry structured output.");
    var image = GenerateAsset(imageProvider, request, candidate, "STATIC_IMAGE", acceptedOutput.VisualPrompt);
    var animation = GenerateAsset(animationProvider, request, candidate, "IDLE_ANIMATION", acceptedOutput.AnimationPrompt);
    var requestedAudioMode = ParseAudioAssetMode(acceptedOutput.RequestedAudioAssetMode);
    var audio = requestedAudioMode is AudioAssetMode.None || acceptedOutput.AudioPrompt is null || audioProvider is null
      ? null
      : GenerateAsset(audioProvider, request, candidate, AudioAssetKind(requestedAudioMode), acceptedOutput.AudioPrompt);
    if (!image.Succeeded || !animation.Succeeded || audio is { Succeeded: false })
    {
      if (!image.Succeeded)
      {
        Add(diagnostics, "IMAGE_PROVIDER_FAILED", "provider.image", image.Provider, image.ErrorMessage ?? "Image provider failed.");
      }

      if (!animation.Succeeded)
      {
        Add(diagnostics, "ANIMATION_PROVIDER_FAILED", "provider.animation", animation.Provider, animation.ErrorMessage ?? "Animation provider failed.");
      }

      if (audio is { Succeeded: false })
      {
        Add(diagnostics, "AUDIO_PROVIDER_FAILED", "provider.audio", audio.Provider, audio.ErrorMessage ?? "Audio provider failed.");
      }

      return Failure(ProductionCardGenerationStatus.FailedProvider, diagnostics, repairAttemptRefs);
    }

    var imageManifest = _assetManifestBuilder.BuildVisualManifest(
      request.GenerationId,
      request.CardId,
      request.CardVersionId,
      VisualAssetMode.StaticImage,
      image,
      request.CreatedAt);
    var animationManifest = _assetManifestBuilder.BuildVisualManifest(
      request.GenerationId,
      request.CardId,
      request.CardVersionId,
      VisualAssetMode.IdleAnimation,
      animation,
      request.CreatedAt);
    var audioManifest = audio is null
      ? null
      : _assetManifestBuilder.BuildAudioManifest(
        request.GenerationId,
        request.CardId,
        request.CardVersionId,
        requestedAudioMode,
        audio,
        ResolveAudioCueTypes(requestedAudioMode, acceptedOutput.RequestedAudioCueTypes),
        request.CreatedAt);

    var publication = _publisher.Publish(new GeneratedCardPublicationRequest(
      new GeneratedCardValidationRequest(
        request.GenerationId,
        request.PipelineVersion,
        request.ContextVersion,
        AttachGeneratedAssets(candidate,imageManifest.VisualManifest?.AssetRef,audioManifest?.AudioManifest?.AudioManifestId,imageManifest.AssetManifestId) with
        {
          AssetMetadata = new AssetMetadata(imageManifest.AssetManifestId,imageManifest.VisualManifest?.AssetRef,audioManifest?.AudioManifest?.AudioManifestId)
        },
        request.Context,
        request.BalanceReport,
        request.SimulationReport,
        request.ReplayReport),
      validationReport,
      request.CardVersionId,
      request.CatalogSnapshotId,
      request.CreatedAt,
      request.EngineCapabilityRegistry));

    if (!publication.Succeeded || publication.CardVersion is null || publication.CatalogSnapshot is null)
    {
      diagnostics.AddRange(publication.Diagnostics.Select(diagnostic => new ProductionCardGenerationDiagnostic(
        $"PUBLICATION_{diagnostic.Code}",
        diagnostic.Path,
        string.Empty,
        diagnostic.Message)));
      return Failure(ProductionCardGenerationStatus.FailedPublication, diagnostics, repairAttemptRefs);
    }

    var auditEntry = _auditLogEntryBuilder.Build(
      new GeneratedCardValidationRequest(
        request.GenerationId,
        request.PipelineVersion,
        request.ContextVersion,
        candidate,
        request.Context,
        request.BalanceReport,
        request.SimulationReport,
        request.ReplayReport),
      validationReport,
      new GenerationAuditLogEntryMetadata(
        request.CreatedAt,
        IntentRef: $"intent:{request.GenerationId}",
        RawStructuredOutputRef: structuredResult.RawStructuredOutputRef,
        SkeletonRef: $"skeleton:{request.GenerationId}",
        EffectSlotsRef: null,
        ExpandedEffectsRef: candidate.Effects.Count == 0 ? null : $"effects:{request.GenerationId}",
        repairAttemptRefs,
        BalanceReportRef: request.BalanceReport?.ReportId,
        SimulationReportRef: request.SimulationReport?.ReportId,
        ReplayReportRef: request.ReplayReport?.ReportId,
        ImageAssetManifestRef: imageManifest.AssetManifestId,
        AnimationAssetManifestRef: animationManifest.AssetManifestId,
        AudioAssetManifestRef: audioManifest?.AssetManifestId,
        ProviderProvenanceRefs:
        [
          $"{structuredResult.Provider}:{structuredResult.ModelId}:{structuredResult.RawStructuredOutputRef}",
          $"{image.Provider}:{image.ModelId}:{image.RawAssetRef}",
          $"{animation.Provider}:{animation.ModelId}:{animation.RawAssetRef}",
          .. audio is null ? [] : new[] { $"{audio.Provider}:{audio.ModelId}:{audio.RawAssetRef}" }
        ],
        PublicationRef: publication.CardVersion.CardVersionId));

    return new ProductionCardGenerationResult(
      ProductionCardGenerationStatus.Succeeded,
      publication.CardVersion,
      publication.CatalogSnapshot,
      audioManifest is null ? [imageManifest, animationManifest] : [imageManifest, animationManifest, audioManifest],
      auditEntry,
      repairAttemptRefs,
      diagnostics);
  }

  private static IReadOnlyList<AudioCueType> ParseAudioCueTypes(IReadOnlyList<string>? cueTypes)
  {
    var values = new List<AudioCueType>();
    foreach (var cueType in cueTypes ?? [])
    {
      if (Enum.TryParse<AudioCueType>(cueType, ignoreCase: true, out var parsed))
      {
        values.Add(parsed);
        continue;
      }

      var normalized = cueType.Replace("_", string.Empty, StringComparison.Ordinal);
      if (Enum.TryParse<AudioCueType>(normalized, ignoreCase: true, out parsed))
      {
        values.Add(parsed);
      }
    }

    return values
      .Distinct()
      .OrderBy(value => value.ToString(), StringComparer.Ordinal)
      .ToArray();
  }

  private static AudioAssetMode ParseAudioAssetMode(string? audioMode)
  {
    if (string.IsNullOrWhiteSpace(audioMode))
    {
      return AudioAssetMode.SfxOnly;
    }

    if (Enum.TryParse<AudioAssetMode>(audioMode, ignoreCase: true, out var parsed))
    {
      return parsed;
    }

    var normalized = audioMode.Replace("_", string.Empty, StringComparison.Ordinal);
    return Enum.TryParse<AudioAssetMode>(normalized, ignoreCase: true, out parsed)
      ? parsed
      : AudioAssetMode.SfxOnly;
  }

  private static IReadOnlyList<AudioCueType> ResolveAudioCueTypes(
    AudioAssetMode audioMode,
    IReadOnlyList<string>? requestedCueTypes)
  {
    var parsed = ParseAudioCueTypes(requestedCueTypes);
    if (parsed.Count > 0)
    {
      return parsed;
    }

    return audioMode switch
    {
      AudioAssetMode.SfxOnly => [AudioCueType.OnPlay],
      AudioAssetMode.VoiceLineOnly => [AudioCueType.OnInspect],
      AudioAssetMode.SfxAndVoice => [AudioCueType.OnPlay, AudioCueType.OnInspect],
      AudioAssetMode.FullAudioSet => Enum.GetValues<AudioCueType>()
        .OrderBy(value => value.ToString(), StringComparer.Ordinal)
        .ToArray(),
      _ => []
    };
  }

  private static string AudioAssetKind(AudioAssetMode audioMode) =>
    audioMode switch
    {
      AudioAssetMode.VoiceLineOnly => "VOICE_LINE_AUDIO",
      AudioAssetMode.SfxAndVoice => "SFX_AND_VOICE_AUDIO",
      AudioAssetMode.FullAudioSet => "FULL_AUDIO_SET_AUDIO",
      _ => "SFX_AUDIO"
    };

  private static GeneratedAssetProviderResult GenerateAsset(
    IGeneratedAssetProvider provider,
    ProductionCardGenerationRequest request,
    CardDefinition candidate,
    string assetKind,
    AssetGenerationPrompt prompt) =>
    provider.Generate(new GeneratedAssetProviderRequest(
      request.GenerationId,
      candidate.CardId ?? request.CardId,
      request.CardVersionId,
      assetKind,
      prompt.Prompt,
      prompt.StyleGuideRef,
      prompt.SafetyPolicyRef,
      TimeSpan.FromSeconds(120)));

  private static string BuildPrompt(
    ProductionCardGenerationRequest request,
    IReadOnlyList<ProductionCardGenerationDiagnostic> diagnostics,
    int attempt)
  {
    if (attempt == 1 || diagnostics.Count == 0)
    {
      return request.Prompt;
    }

    var repairNotes = string.Join("; ", diagnostics.Select(diagnostic => $"{diagnostic.Path}:{diagnostic.Code}").Distinct(StringComparer.Ordinal));
    return $"{request.Prompt}\nRepair required: {repairNotes}";
  }

  private static CardDefinition BuildCandidate(
    ProductionCardGenerationRequest request,
    StructuredCardOutput output)
  {
    var skeletonResult = new GeneratedCardSkeletonBuilder().Build(new SkeletonGenerationRequest(
      $"skeleton:{request.GenerationId}",
      "card-dsl-0.2.0",
      "engine-bootstrap-0.3.0",
      "primitive-registry-bootstrap-0.2.0",
      output.Name,
      output.CardType,
      output.ClassTags,
      output.Rarity,
      output.ManaCost,
      output.Tribes,
      output.Keywords,
      output.Collectible,
      output.FormatTags,
      output.Attack,
      output.Health,
      output.CanAttack,
      output.RequiresTarget,
      "structured-output-provider",
      request.CreatedAt,
      request.Context,
      output.HeroArmorGain,
      output.HeroPortraitAssetRef,
      output.HeroPowerCost,
      output.HeroPowerActivationTiming));

    if (!skeletonResult.Succeeded || skeletonResult.Skeleton is null)
    {
      throw new InvalidOperationException(
        string.Join("; ", skeletonResult.Diagnostics.Select(diagnostic => $"{diagnostic.Path}:{diagnostic.Message}")));
    }

    var compiled = new GeneratedCardSkeletonCompiler().Compile(skeletonResult.Skeleton);
    return compiled with
    {
      CardId = request.CardId,
      CardVersion = request.CardVersionId,
      CatalogSnapshotId = request.CatalogSnapshotId,
      Effects = output.Effects,
      CardText = new CardTextGeneration(output.RulesText, FlavorText: null),
      BalanceMetadata = new BalanceMetadata(output.ComplexityScore, output.RandomnessScore, output.RiskFlags),
      AuthoringMetadata = compiled.AuthoringMetadata with
      {
        GenerationPipelineVersion = request.PipelineVersion,
        SourceRefs =
        [
          ..compiled.AuthoringMetadata.SourceRefs,
          $"provider:structured:{request.ModelId}",
          $"prompt:{request.PromptVersion}"
        ]
      }
    };
  }

  private static CardDefinition AttachGeneratedAssets(CardDefinition candidate,string? visualAssetRef,string? audioManifestId,string assetManifestId)
  {
    if(candidate.StaticData.CardType is not Game.Shared.Primitives.CardType.HeroCard||candidate.Entity.HeroCard is null)return candidate;
    return candidate with
    {
      Entity=candidate.Entity with{HeroCard=candidate.Entity.HeroCard with{PortraitAssetRef=visualAssetRef}},
      AssetMetadata=new AssetMetadata(assetManifestId,visualAssetRef,audioManifestId)
    };
  }

  private static ProductionCardGenerationResult Failure(
    ProductionCardGenerationStatus status,
    IReadOnlyList<ProductionCardGenerationDiagnostic> diagnostics,
    IReadOnlyList<string> repairAttemptRefs) =>
    new(status, null, null, [], null, repairAttemptRefs, diagnostics);

  private static void Add(
    ICollection<ProductionCardGenerationDiagnostic> diagnostics,
    string code,
    string path,
    string identifier,
    string message) =>
    diagnostics.Add(new ProductionCardGenerationDiagnostic(code, path, identifier, message));
}
