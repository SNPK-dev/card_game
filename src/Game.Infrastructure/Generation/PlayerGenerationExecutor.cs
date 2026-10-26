using Game.Application.Capabilities;
using Game.Application.Players;
using Game.Application.Rulesets;
using Game.Generator.Generation;
using Game.Generator.Providers;
using Game.Generator.Publication;
using Game.Generator.Validation;
using Game.Shared.Catalog;
using Game.Shared.Dsl;
using Game.Shared.Mechanics;
using Game.Shared.Primitives;

namespace Game.Infrastructure.Generation;

public sealed class PlayerGenerationExecutor(
  HttpClient httpClient,
  string openAiEndpoint,
  string openAiModel,
  string openAiImageEndpoint,
  string openAiImageModel,
  string? openAiApiKey,
  string localStructuredEndpoint,
  string localStructuredModel,
  string localImageEndpoint,
  string localImageModel,
  string? storageRootPath,
  IResolvedRulesetProfileStore profiles,
  IEngineCapabilityRegistryProvider capabilities) : IPlayerGenerationExecutor
{
  public async Task<PlayerGenerationExecutionResult> ExecuteAsync(PlayerGenerationExecutionRequest request, CancellationToken cancellationToken)
  {
    var profile = profiles.Latest;
    if (profile is null) return PlayerGenerationExecutionResult.Failure("GENERATION_RULESET_NOT_READY");
    var context = new GenerationContextBuilder().Build(profile, profile.EnabledMechanics.Select(id => new MechanicStatusRecord(
      id, MechanicStatus.ApprovedForRuleset, MechanicTier.Tier0, "player-generation", [], [profile.Id], null, null, [], [], [], [])));
    var snapshotId = $"catalog-{request.JobId}-a{request.Attempt}";
    var now = DateTimeOffset.UtcNow;
    var published = new List<CardVersion>();
    var generatedImageHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var names = new GeneratedCardNameAllocator();
    var selectedStructuredModel=request.ProviderMode=="openai"?openAiModel:localStructuredModel;
    var directOpenAi = request.ProviderMode == "openai" && new Uri(openAiEndpoint).Host.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase);
    IStructuredCardGenerationProvider provider = request.ProviderMode == "openai"
      ? directOpenAi
        ? new OpenAiStructuredCardGenerationProvider(httpClient, new Uri(openAiEndpoint), openAiModel, openAiApiKey ?? string.Empty)
        : new HttpStructuredCardGenerationProvider(httpClient, Provider("openai-player-generation", openAiModel, openAiEndpoint, openAiApiKey))
      : new HttpStructuredCardGenerationProvider(httpClient,Provider("local-player-generation",localStructuredModel,localStructuredEndpoint,null));
    IGeneratedAssetProvider imageProvider = request.ProviderMode == "openai"
      ? directOpenAi
        ? new OpenAiGeneratedAssetProvider(httpClient, new Uri(openAiImageEndpoint), openAiImageModel, openAiApiKey ?? string.Empty, storageRootPath ?? Path.Combine(AppContext.BaseDirectory, "data"))
        : new HttpGeneratedAssetProvider(httpClient, Provider("openai-player-image", openAiImageModel, openAiImageEndpoint, openAiApiKey))
      : new HttpGeneratedAssetProvider(httpClient,Provider("local-player-image",localImageModel,localImageEndpoint,null));

    for (var index = 0; index < request.Count; index++)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var generationId = $"{request.JobId}:{index + 1:00}:a{request.Attempt}";
      var cardAccepted = false;
      for (var cardAttempt = 1; cardAttempt <= 3; cardAttempt++)
      {
      var attemptGenerationId = $"{generationId}:candidate-{cardAttempt}";
      StructuredCardOutput output;
      {
        var response = await Task.Run(() => provider.Generate(new StructuredCardGenerationRequest(
          attemptGenerationId, "player-generation-v1", selectedStructuredModel,
          $"{request.Prompt}\nGenerate card {index + 1} of {request.Count}. Return one distinct production-compatible card. Candidate attempt {cardAttempt}; correct every DSL field and include every required target reference.",
          new Dictionary<string, string> { ["rulesetProfileId"] = profile.Id, ["catalogSnapshotId"] = snapshotId, ["scenarioIndex"] = (index + 1).ToString(), ["generationMode"] = "production-native" },
          TimeSpan.FromMinutes(3))), cancellationToken);
        if (!response.Succeeded || response.Output is null)
        {
          Console.Error.WriteLine($"Player generation provider rejected {attemptGenerationId}: {response.ErrorCode} {response.ErrorMessage}");
          if (cardAttempt < 3) continue;
          return PlayerGenerationExecutionResult.Failure(response.ErrorCode ?? "GENERATION_PROVIDER_FAILED");
        }
        output = response.Output;
      }
      output = output with { Name = names.Allocate(output.Name, index + 1) };

      var cardId = $"player_{request.JobId.Replace('-', '_')}_{index + 1:00}";
      var versionId = $"{cardId}_v{request.Attempt}";
      string visualAssetRef; string assetManifestId;
      {
        GeneratedAssetProviderResult? acceptedAsset = null;
        string? lastAssetError = null;
        for (var artAttempt = 1; artAttempt <= 3; artAttempt++)
        {
          var distinctPrompt = $"{output.VisualPrompt.Prompt}\nCard identity: {output.Name}. Composition variant {index + 1}-{artAttempt}; create a visually distinct subject, silhouette, palette, and camera angle. No text.";
          var asset = await Task.Run(() => imageProvider.Generate(new GeneratedAssetProviderRequest($"{attemptGenerationId}:art-{artAttempt}", cardId, versionId, "STATIC_IMAGE", distinctPrompt, output.VisualPrompt.StyleGuideRef, output.VisualPrompt.SafetyPolicyRef, TimeSpan.FromMinutes(4))), cancellationToken);
          if (!asset.Succeeded) { lastAssetError=asset.ErrorCode; continue; }
          if (generatedImageHashes.Add(asset.ContentHash)) { acceptedAsset = asset; break; }
        }
        if (acceptedAsset is null) return PlayerGenerationExecutionResult.Failure(lastAssetError ?? "GENERATION_DUPLICATE_IMAGE");
        visualAssetRef = acceptedAsset.AssetRef; assetManifestId = acceptedAsset.ContentHash;
      }
      var candidate = Candidate(cardId, versionId, snapshotId, profile.Id, output, now, request.ProviderMode, assetManifestId, visualAssetRef);
      var balanceReport = new GeneratedCardBalanceReport($"balance:{attemptGenerationId}", context.BalancePolicy.PolicyId, 0, output.ComplexityScore, output.RandomnessScore, 0, output.RiskFlags);
      var validationRequest = new GeneratedCardValidationRequest(attemptGenerationId, "player-generation-v1", "player-context-v1", candidate, context, balanceReport);
      var validation = GeneratedCardValidationPipeline.CreateDefault().Validate(validationRequest);
      var balance = new GeneratedCardBalanceEvaluator().Evaluate(new GeneratedCardBalanceEvaluationRequest($"balance-eval:{attemptGenerationId}", candidate, context, balanceReport));
      if (validation.FinalDecision is not GeneratedCardValidationDecision.Accepted || balance.Decision is GeneratedCardBalanceDecision.Rejected)
      {
        Console.Error.WriteLine($"Player generation rejected {attemptGenerationId}: {string.Join(" | ",validation.Diagnostics.Select(item=>$"{item.Code}:{item.Path}:{item.Message}"))}");
        if (cardAttempt < 3) continue;
        return PlayerGenerationExecutionResult.Failure("GENERATION_VALIDATION_REJECTED");
      }
      var publication = new GeneratedCardPublisher().Publish(new GeneratedCardPublicationRequest(validationRequest, validation, versionId, snapshotId, now, capabilities.GetCurrentRegistry()));
      if (!publication.Succeeded || publication.CardVersion is null) return PlayerGenerationExecutionResult.Failure("GENERATION_PUBLICATION_FAILED");
      published.Add(publication.CardVersion);
      cardAccepted = true;
      break;
      }
      if (!cardAccepted) return PlayerGenerationExecutionResult.Failure("GENERATION_VALIDATION_REJECTED");
      request.ReportProgress?.Invoke(15 + (int)Math.Round(50d * (index + 1) / request.Count));
    }

    return PlayerGenerationExecutionResult.Success(new CatalogSnapshot(
      snapshotId,
      profile.Id,
      profile.ResolvedHash,
      now,
      published,
      [profile.SourceProfileId],
      [profile.SourceProfileVersion],
      ProductionScaleCompatible: true));
  }

  private static GenerationProviderConfiguration Provider(string name, string model, string endpoint, string? apiKey) =>
    new(name, model, new Uri(endpoint), apiKey, TimeSpan.FromMinutes(3), 1);

  private static CardDefinition Candidate(string cardId, string versionId, string snapshotId, string profileId, StructuredCardOutput output, DateTimeOffset now, string mode, string assetManifestId, string visualAssetRef) =>
    new(cardId, versionId, "card-dsl-0.2.0", "engine-bootstrap-0.3.0", "primitive-registry-bootstrap-0.2.0", profileId, snapshotId,
      new StaticCardData(output.Name, output.CardType, output.ClassTags, output.Rarity, output.ManaCost, output.Tribes, output.Keywords, output.Collectible, output.FormatTags),
      Entity(output,visualAssetRef), output.Effects, new CardTextGeneration(output.RulesText, null), new BalanceMetadata(output.ComplexityScore, output.RandomnessScore, output.RiskFlags),
      new AssetMetadata(assetManifestId, visualAssetRef, null), new AuthoringMetadata($"player-generation:{mode}", now, "player-generation-v1", []));

  private static EntityDefinition Entity(StructuredCardOutput output,string visualAssetRef) => output.CardType switch
  {
    CardType.MinionCard => new(new MinionDefinition(output.Attack ?? 1, output.Health ?? 1, output.Tribes, [], output.CanAttack), null, null, null, null, null),
    CardType.SpellCard => new(null, new SpellDefinition(null, output.RequiresTarget), null, null, null, null),
    CardType.WeaponCard => new(null, null, new WeaponDefinition(output.Attack ?? 1, output.Health ?? 1, []), null, null, null),
    CardType.LocationCard => new(null, null, null, new LocationDefinition(output.Health ?? 1, "ONCE_PER_TURN", "DURABILITY"), null, null),
    CardType.HeroCard => new(null,null,null,null,new HeroCardDefinition(
      output.HeroArmorGain,
      output.HeroPowerCost.HasValue?new HeroPowerDefinition(output.HeroPowerCost.Value,output.HeroPowerActivationTiming,null,[]):null,
      visualAssetRef),null),
    _ => throw new InvalidOperationException($"Unsupported generated card type {output.CardType}.")
  };
}
