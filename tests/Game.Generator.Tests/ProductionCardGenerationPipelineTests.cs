using Game.Generator.Generation;
using Game.Generator.Providers;
using Game.Generator.Validation;
using Game.Shared.Dsl;
using Game.Shared.Mechanics;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Generator.Tests;

public sealed class ProductionCardGenerationPipelineTests
{
  private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-06-26T00:00:00Z");

  [Fact]
  public void PipelineProducesPublishedCardAssetsAndAuditEvidence()
  {
    var structuredProvider = new SequenceStructuredProvider([CreateOutput()]);
    var imageProvider = new DeterministicAssetProvider("image-provider", "image-model", "png", "PASSED");
    var animationProvider = new DeterministicAssetProvider("animation-provider", "animation-model", "gif", "PASSED");
    var pipeline = new ProductionCardGenerationPipeline(structuredProvider, imageProvider, animationProvider);

    var result = pipeline.Run(CreateRequest());

    Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(d => $"{d.Code}:{d.Path}:{d.Message}")));
    Assert.NotNull(result.CardVersion);
    Assert.NotNull(result.CatalogSnapshot);
    Assert.Equal("card_arcane_apprentice_v1", result.CardVersion.CardVersionId);
    Assert.Equal("catalog-production-generation", result.CatalogSnapshot.SnapshotId);
    Assert.Equal(2, result.AssetManifests.Count);
    Assert.Contains(result.AssetManifests, manifest =>
      manifest.VisualAssetMode == Game.Shared.Assets.VisualAssetMode.StaticImage
      && manifest.VisualManifest?.Format == "png"
      && manifest.ModerationStatus == Game.Shared.Assets.AssetModerationStatus.Passed);
    Assert.Contains(result.AssetManifests, manifest =>
      manifest.VisualAssetMode == Game.Shared.Assets.VisualAssetMode.IdleAnimation
      && manifest.VisualManifest?.Format == "gif"
      && manifest.ModerationStatus == Game.Shared.Assets.AssetModerationStatus.Passed);
    Assert.NotNull(result.AuditLogEntry);
    Assert.Equal("raw://generation-production-001/001", result.AuditLogEntry.RawStructuredOutputRef);
    Assert.Equal("asset:generation-production-001:StaticImage", result.AuditLogEntry.ImageAssetManifestRef);
    Assert.Equal("asset:generation-production-001:IdleAnimation", result.AuditLogEntry.AnimationAssetManifestRef);
    Assert.Contains(result.AuditLogEntry.ProviderProvenanceRefs, providerRef =>
      providerRef.Contains("structured-contract-provider", StringComparison.Ordinal));
    Assert.Equal("card_arcane_apprentice_v1", result.AuditLogEntry.PublicationRef);
  }

  [Fact]
  public void PipelineProducesPlayableHeroCardAndBindsGeneratedPortrait()
  {
    var output=CreateOutput() with
    {
      Name="Cinder Ascendant",CardType=CardType.HeroCard,Rarity=Rarity.Legendary,ManaCost=7,
      Attack=null,Health=null,CanAttack=false,RulesText="Gain 6 Armor. Replace your Hero Power.",
      HeroArmorGain=6,HeroPowerCost=2,HeroPowerActivationTiming="ACTIVE_PLAYER_MAIN"
    };
    var pipeline=new ProductionCardGenerationPipeline(
      new SequenceStructuredProvider([output]),
      new DeterministicAssetProvider("image-provider","image-model","png","PASSED"),
      new DeterministicAssetProvider("animation-provider","animation-model","gif","PASSED"));

    var result=pipeline.Run(CreateHeroRequest());

    Assert.True(result.Succeeded,string.Join(Environment.NewLine,result.Diagnostics.Select(d=>$"{d.Code}:{d.Path}:{d.Message}")));
    var hero=Assert.IsType<HeroCardDefinition>(result.CardVersion!.Definition.Entity.HeroCard);
    Assert.Equal(6,hero.ArmorGain);
    Assert.Equal(2,hero.HeroPowerReplacement!.Cost);
    Assert.Equal("asset://generation-production-hero/static_image",hero.PortraitAssetRef);
  }

  [Fact]
  public void PipelineUsesBoundedRepairAttemptsForInvalidStructuredOutput()
  {
    var structuredProvider = new SequenceStructuredProvider([
      CreateOutput() with { ManaCost = -1 },
      CreateOutput()
    ]);
    var pipeline = new ProductionCardGenerationPipeline(
      structuredProvider,
      new DeterministicAssetProvider("image-provider", "image-model", "png", "PASSED"),
      new DeterministicAssetProvider("animation-provider", "animation-model", "gif", "PASSED"));

    var result = pipeline.Run(CreateRequest(maxRepairAttempts: 1));

    Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(d => $"{d.Code}:{d.Path}:{d.Message}")));
    Assert.Single(result.RepairAttemptRefs);
    Assert.Equal(2, structuredProvider.CallCount);
    Assert.Contains(result.AuditLogEntry!.RepairAttemptRefs, repair => repair == "repair:generation-production-001:001");
  }

  [Fact]
  public void PipelineProducesAudioManifestAndAuditEvidenceWhenAudioProviderIsConfigured()
  {
    var structuredProvider = new SequenceStructuredProvider([CreateOutput() with
    {
      AudioPrompt = new AssetGenerationPrompt("Short arcane play sound", "style:audio-core", "safety:standard"),
      RequestedAudioCueTypes = ["ON_PLAY", "ON_ATTACK"]
    }]);
    var imageProvider = new DeterministicAssetProvider("image-provider", "image-model", "png", "PASSED");
    var animationProvider = new DeterministicAssetProvider("animation-provider", "animation-model", "gif", "PASSED");
    var audioProvider = new DeterministicAssetProvider("audio-provider", "audio-model", "ogg", "PASSED");
    var pipeline = new ProductionCardGenerationPipeline(
      structuredProvider,
      imageProvider,
      animationProvider,
      audioProvider);

    var result = pipeline.Run(CreateRequest());

    Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(d => $"{d.Code}:{d.Path}:{d.Message}")));
    var audioManifest = Assert.Single(result.AssetManifests, manifest =>
      manifest.AudioAssetMode == Game.Shared.Assets.AudioAssetMode.SfxOnly);
    Assert.NotNull(audioManifest.AudioManifest);
    Assert.Equal(["audio:generation-production-001:OnAttack", "audio:generation-production-001:OnPlay"],
      audioManifest.AudioManifest.Cues.Select(cue => cue.CueId).Order(StringComparer.Ordinal));
    Assert.Equal(audioManifest.AudioManifest.AudioManifestId, result.CardVersion!.Definition.AssetMetadata.AudioManifestId);
    Assert.Equal(audioManifest.AssetManifestId, result.AuditLogEntry!.AudioAssetManifestRef);
    Assert.Contains(result.AuditLogEntry.ProviderProvenanceRefs, providerRef =>
      providerRef.Contains("audio-provider:audio-model", StringComparison.Ordinal));
  }

  [Fact]
  public void PipelineSkipsAudioProviderWhenRequestedAudioModeIsNone()
  {
    var structuredProvider = new SequenceStructuredProvider([CreateOutput() with
    {
      AudioPrompt = new AssetGenerationPrompt("Do not generate card audio", "style:audio-core", "safety:standard"),
      RequestedAudioAssetMode = "NONE",
      RequestedAudioCueTypes = ["ON_PLAY"]
    }]);
    var audioProvider = new DeterministicAssetProvider("audio-provider", "audio-model", "ogg", "PASSED");
    var pipeline = new ProductionCardGenerationPipeline(
      structuredProvider,
      new DeterministicAssetProvider("image-provider", "image-model", "png", "PASSED"),
      new DeterministicAssetProvider("animation-provider", "animation-model", "gif", "PASSED"),
      audioProvider);

    var result = pipeline.Run(CreateRequest());

    Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(d => $"{d.Code}:{d.Path}:{d.Message}")));
    Assert.Equal(2, result.AssetManifests.Count);
    Assert.Empty(audioProvider.Requests);
    Assert.Null(result.CardVersion!.Definition.AssetMetadata.AudioManifestId);
    Assert.Null(result.AuditLogEntry!.AudioAssetManifestRef);
  }

  [Fact]
  public void PipelineProducesVoiceLineAudioModeWithDefaultInspectCue()
  {
    var structuredProvider = new SequenceStructuredProvider([CreateOutput() with
    {
      AudioPrompt = new AssetGenerationPrompt("Short inspect voice line", "style:voice-core", "safety:standard"),
      RequestedAudioAssetMode = "VOICE_LINE_ONLY"
    }]);
    var audioProvider = new DeterministicAssetProvider("audio-provider", "audio-model", "ogg", "PASSED");
    var pipeline = new ProductionCardGenerationPipeline(
      structuredProvider,
      new DeterministicAssetProvider("image-provider", "image-model", "png", "PASSED"),
      new DeterministicAssetProvider("animation-provider", "animation-model", "gif", "PASSED"),
      audioProvider);

    var result = pipeline.Run(CreateRequest());

    Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(d => $"{d.Code}:{d.Path}:{d.Message}")));
    var audioManifest = Assert.Single(result.AssetManifests, manifest =>
      manifest.AudioAssetMode == Game.Shared.Assets.AudioAssetMode.VoiceLineOnly);
    var cue = Assert.Single(audioManifest.AudioManifest!.Cues);
    Assert.Equal(Game.Shared.Assets.AudioCueType.OnInspect, cue.CueType);
    Assert.Equal("VOICE_LINE_AUDIO", Assert.Single(audioProvider.Requests).AssetKind);
  }

  [Fact]
  public void PipelineProducesHybridAudioModeWithSfxAndVoiceDefaults()
  {
    var structuredProvider = new SequenceStructuredProvider([CreateOutput() with
    {
      AudioPrompt = new AssetGenerationPrompt("Short play sound and inspect voice", "style:audio-core", "safety:standard"),
      RequestedAudioAssetMode = "SFX_AND_VOICE"
    }]);
    var audioProvider = new DeterministicAssetProvider("audio-provider", "audio-model", "ogg", "PASSED");
    var pipeline = new ProductionCardGenerationPipeline(
      structuredProvider,
      new DeterministicAssetProvider("image-provider", "image-model", "png", "PASSED"),
      new DeterministicAssetProvider("animation-provider", "animation-model", "gif", "PASSED"),
      audioProvider);

    var result = pipeline.Run(CreateRequest());

    Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(d => $"{d.Code}:{d.Path}:{d.Message}")));
    var audioManifest = Assert.Single(result.AssetManifests, manifest =>
      manifest.AudioAssetMode == Game.Shared.Assets.AudioAssetMode.SfxAndVoice);
    Assert.Equal(
      [Game.Shared.Assets.AudioCueType.OnInspect, Game.Shared.Assets.AudioCueType.OnPlay],
      audioManifest.AudioManifest!.Cues.Select(cue => cue.CueType).OrderBy(cueType => cueType.ToString()).ToArray());
    Assert.Equal("SFX_AND_VOICE_AUDIO", Assert.Single(audioProvider.Requests).AssetKind);
  }

  [Fact]
  public void RealProviderHarnessBlocksWhenProviderConfigurationIsMissing()
  {
    var missing = new GenerationProviderConfiguration(
      "provider",
      ModelId: "",
      Endpoint: null,
      ApiKey: null,
      TimeSpan.FromSeconds(60),
      MaxRetryAttempts: 1);
    var harness = new RealProviderGenerationHarness(
      new SequenceStructuredProvider([CreateOutput()]),
      new DeterministicAssetProvider("image-provider", "image-model", "png", "PASSED"),
      new DeterministicAssetProvider("animation-provider", "animation-model", "gif", "PASSED"));

    var result = harness.Run(new RealProviderGenerationHarnessRequest(
      missing,
      missing,
      missing,
      CreateRequest()));

    Assert.Equal(ProductionCardGenerationStatus.Blocked, result.Status);
    Assert.Null(result.GenerationResult);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "PROVIDER_CONFIGURATION_MISSING"
      && diagnostic.Path == "providers.structured.endpoint");
    Assert.All(result.RedactedProviderConfigurations, configuration =>
      Assert.Null(configuration.ApiKey));
  }

  [Fact]
  public void RealProviderHarnessValidatesOptionalAudioProviderConfiguration()
  {
    var missing = new GenerationProviderConfiguration(
      "provider",
      ModelId: "",
      Endpoint: null,
      ApiKey: null,
      TimeSpan.FromSeconds(60),
      MaxRetryAttempts: 1);
    var harness = new RealProviderGenerationHarness(
      new SequenceStructuredProvider([CreateOutput()]),
      new DeterministicAssetProvider("image-provider", "image-model", "png", "PASSED"),
      new DeterministicAssetProvider("animation-provider", "animation-model", "gif", "PASSED"),
      new DeterministicAssetProvider("audio-provider", "audio-model", "ogg", "PASSED"));

    var result = harness.Run(new RealProviderGenerationHarnessRequest(
      new GenerationProviderConfiguration("structured", "model", new Uri("https://example.test/structured"), "secret", TimeSpan.FromSeconds(60), 1),
      new GenerationProviderConfiguration("image", "model", new Uri("https://example.test/image"), "secret", TimeSpan.FromSeconds(60), 1),
      new GenerationProviderConfiguration("animation", "model", new Uri("https://example.test/animation"), "secret", TimeSpan.FromSeconds(60), 1),
      CreateRequest(),
      AudioProvider: missing));

    Assert.Equal(ProductionCardGenerationStatus.Blocked, result.Status);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "PROVIDER_CONFIGURATION_MISSING"
      && diagnostic.Path == "providers.audio.endpoint");
    Assert.DoesNotContain(result.RedactedProviderConfigurations, configuration =>
      string.Equals(configuration.ApiKey, "secret", StringComparison.Ordinal));
    Assert.Contains(result.RedactedProviderConfigurations, configuration =>
      string.Equals(configuration.ProviderName, "provider", StringComparison.Ordinal)
      && configuration.ApiKey is null);
  }

  private static ProductionCardGenerationRequest CreateRequest(int maxRepairAttempts = 0)
  {
    var profile = TestFixtures.CreateProfile(
      allowedActions: ["DEAL_DAMAGE"],
      allowedEffectKinds: ["SIMPLE_PLAY_EFFECT"]);
    var context = new GenerationContextBuilder().Build(
      profile,
      [TestFixtures.Mechanic("BATTLECRY", MechanicStatus.ApprovedForRuleset)]);

    return new ProductionCardGenerationRequest(
      "generation-production-001",
      "pipeline-production-v1",
      "context-production-v1",
      "prompt-v1",
      "structured-model-v1",
      "Create a deterministic apprentice minion.",
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
      "catalog-production-generation",
      context,
      FixedNow,
      maxRepairAttempts,
      TestEngineCapabilityRegistryFactory.Create());
  }

  private static StructuredCardOutput CreateOutput() =>
    new(
      "Arcane Apprentice",
      CardType.MinionCard,
      ["mage"],
      Rarity.Rare,
      2,
      ["APPRENTICE"],
      ["BATTLECRY"],
      Collectible: true,
      ["mvp"],
      Attack: 2,
      Health: 2,
      CanAttack: true,
      RequiresTarget: false,
      "A disciplined apprentice with stable generated assets.",
      Effects: [],
      ComplexityScore: 4,
      RandomnessScore: 0,
      RiskFlags: [],
      new AssetGenerationPrompt("Arcane apprentice card art", "style:core", "safety:standard"),
      new AssetGenerationPrompt("Idle animation for arcane apprentice", "style:core", "safety:standard"));

  private static ProductionCardGenerationRequest CreateHeroRequest()
  {
    var profile=TestFixtures.CreateProfile(
      allowedActions:["DEAL_DAMAGE"],allowedEffectKinds:["SIMPLE_PLAY_EFFECT"],
      allowedCardTypes:["HERO_CARD"],allowedRarities:["LEGENDARY"]);
    var context=new GenerationContextBuilder().Build(profile,[TestFixtures.Mechanic("BATTLECRY",MechanicStatus.ApprovedForRuleset)]);
    return new ProductionCardGenerationRequest(
      "generation-production-hero","pipeline-production-v1","context-production-v1","prompt-v1","structured-model-v1",
      "Create a deterministic hero card.","card_cinder_ascendant","card_cinder_ascendant_v1","catalog-production-generation",
      context,FixedNow,0,TestEngineCapabilityRegistryFactory.Create());
  }

  private sealed class SequenceStructuredProvider(IReadOnlyList<StructuredCardOutput> outputs) : IStructuredCardGenerationProvider
  {
    private int index;

    public int CallCount { get; private set; }

    public StructuredCardGenerationResult Generate(StructuredCardGenerationRequest request)
    {
      CallCount++;
      var output = outputs[Math.Min(index, outputs.Count - 1)];
      index++;
      return StructuredCardGenerationResult.Success(
        output,
        "structured-contract-provider",
        request.ModelId,
        $"raw://{request.GenerationId}/{CallCount:000}");
    }
  }

  private sealed class DeterministicAssetProvider(
    string provider,
    string modelId,
    string format,
    string moderationStatus) : IGeneratedAssetProvider
  {
    private readonly List<GeneratedAssetProviderRequest> requests = [];

    public IReadOnlyList<GeneratedAssetProviderRequest> Requests => requests;

    public GeneratedAssetProviderResult Generate(GeneratedAssetProviderRequest request)
    {
      requests.Add(request);
      var assetRef = $"asset://{request.GenerationId}/{request.AssetKind.ToLowerInvariant()}";
      return GeneratedAssetProviderResult.Success(
        provider,
        modelId,
        assetRef,
        format,
        $"sha256:{request.AssetKind.ToLowerInvariant()}-{request.CardId}",
        moderationStatus,
        $"raw://{request.GenerationId}/{request.AssetKind.ToLowerInvariant()}");
    }
  }
}
