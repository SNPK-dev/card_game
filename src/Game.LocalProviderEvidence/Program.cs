using System.Text.Json;
using System.Text.Json.Serialization;
using Game.Application.Capabilities;
using Game.Generator.Generation;
using Game.Generator.Providers;
using Game.Generator.Publication;
using Game.Generator.Skeletons;
using Game.Generator.Validation;
using Game.Infrastructure.Generation;
using Game.Shared.Assets;
using Game.Shared.Catalog;
using Game.Shared.Dsl;
using Game.Shared.Mechanics;
using Game.Shared.Primitives;
using Game.Shared.Ruleset;

var options = LocalProviderEvidenceOptions.Parse(args);
Directory.CreateDirectory(options.OutputRoot);

var structuredConfiguration = new GenerationProviderConfiguration(
  "local-ollama-structured",
  options.StructuredModel,
  new Uri(options.StructuredEndpoint),
  ApiKey: null,
  TimeSpan.FromSeconds(120),
  MaxRetryAttempts: 0);
var imageConfiguration = new GenerationProviderConfiguration(
  "local-stable-diffusion-image",
  options.ImageModel,
  new Uri(options.ImageEndpoint),
  ApiKey: null,
  TimeSpan.FromSeconds(180),
  MaxRetryAttempts: 0);

var structuredProvider = new HttpStructuredCardGenerationProvider(new HttpClient(), structuredConfiguration);
var imageProvider = new HttpGeneratedAssetProvider(new HttpClient(), imageConfiguration);
var profile = CreateProfile();
var context = new GenerationContextBuilder().Build(
  profile,
  [CreateMechanic("BATTLECRY", MechanicStatus.ApprovedForRuleset)]);
var validator = GeneratedCardValidationPipeline.CreateDefault();
var publisher = new GeneratedCardPublisher();
var imageManifestBuilder = new GeneratedAssetManifestBuilder();
var capabilityRegistry = new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry();
var generated = new List<CardRunReport>();
var cards = new List<CardVersion>();
var assetManifests = new List<AssetManifest>();
var createdAt = DateTimeOffset.UtcNow;

for (var index = 1; index <= options.Count; index++)
{
  var generationId = $"local-provider-card-{index:000}";
  var cardId = $"local_provider_card_{index:000}";
  var cardVersionId = $"{cardId}_v1";
  var prompt = $"Generate balanced local provider evidence card #{index}. Keep it simple, deterministic, playable, and visually distinct.";
  var diagnostics = new List<string>();
  CardDefinition? candidate = null;
  GeneratedCardValidationPipelineReport? validation = null;
  GeneratedCardBalanceEvaluation? balance = null;
  CardVersion? published = null;
  AssetManifest? imageManifest = null;

  var structured = GenerateStructuredWithRetries(
    structuredProvider,
    generationId,
    options.StructuredModel,
    prompt,
    context,
    options.CatalogSnapshotId,
    maxAttempts: 3);

  if (!structured.Succeeded || structured.Output is null)
  {
    diagnostics.Add($"structured:{structured.ErrorCode}:{structured.ErrorMessage}");
    generated.Add(CreateReport(index, generationId, cardId, null, false, false, false, diagnostics, null, null, null));
    continue;
  }

  try
  {
    candidate = BuildCandidate(
      generationId,
      cardId,
      cardVersionId,
      options.CatalogSnapshotId,
      options.StructuredModel,
      createdAt,
      context,
      structured.Output);
  }
  catch (Exception exception)
  {
    diagnostics.Add($"candidate:{exception.Message}");
    generated.Add(CreateReport(index, generationId, cardId, structured.Output.Name, false, false, false, diagnostics, null, null, null));
    continue;
  }

  validation = validator.Validate(new GeneratedCardValidationRequest(
    generationId,
    "local-provider-evidence-pipeline-v1",
    "local-provider-evidence-context-v1",
    candidate,
    context));
  balance = new GeneratedCardBalanceEvaluator().Evaluate(new GeneratedCardBalanceEvaluationRequest(
    $"balance:{generationId}",
    candidate,
    context));

  if (validation.FinalDecision is GeneratedCardValidationDecision.Accepted)
  {
    var publication = publisher.Publish(new GeneratedCardPublicationRequest(
      new GeneratedCardValidationRequest(
        generationId,
        "local-provider-evidence-pipeline-v1",
        "local-provider-evidence-context-v1",
        candidate,
        context),
      validation,
      cardVersionId,
      options.CatalogSnapshotId,
      createdAt,
      capabilityRegistry));
    if (publication.Succeeded && publication.CardVersion is not null)
    {
      published = publication.CardVersion;
      cards.Add(published);
    }
    else
    {
      diagnostics.AddRange(publication.Diagnostics.Select(diagnostic =>
        $"publication:{diagnostic.Code}:{diagnostic.Path}:{diagnostic.Message}"));
    }
  }

  var image = imageProvider.Generate(new GeneratedAssetProviderRequest(
    generationId,
    candidate.CardId ?? cardId,
    cardVersionId,
    "STATIC_IMAGE",
    structured.Output.VisualPrompt.Prompt,
    structured.Output.VisualPrompt.StyleGuideRef,
    structured.Output.VisualPrompt.SafetyPolicyRef,
    TimeSpan.FromSeconds(180)));
  if (image.Succeeded)
  {
    imageManifest = imageManifestBuilder.BuildVisualManifest(
      generationId,
      candidate.CardId ?? cardId,
      cardVersionId,
      VisualAssetMode.StaticImage,
      image,
      createdAt);
    assetManifests.Add(imageManifest);
  }
  else
  {
    diagnostics.Add($"image:{image.ErrorCode}:{image.ErrorMessage}");
  }

  diagnostics.AddRange(validation.Diagnostics.Select(diagnostic =>
    $"validation:{diagnostic.Code}:{diagnostic.Path}:{diagnostic.Message}"));
  diagnostics.AddRange(balance.Signals.Select(signal =>
    $"balance:{signal.Kind}:{signal.Path}:{signal.Message}"));

  generated.Add(CreateReport(
    index,
    generationId,
    candidate.CardId ?? cardId,
    candidate.StaticData.Name,
    validation.FinalDecision is GeneratedCardValidationDecision.Accepted,
    published is not null,
    imageManifest is not null,
    diagnostics,
    validation.FinalDecision.ToString(),
    balance.Decision.ToString(),
    balance.RiskTier.ToString()));
}

var report = new LocalProviderEvidenceReport(
  $"local-provider-10-card-evidence-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}",
  DateTimeOffset.UtcNow,
  options.StructuredEndpoint,
  options.StructuredModel,
  options.ImageEndpoint,
  options.ImageModel,
  options.Count,
  generated.Count(card => card.ValidationAccepted),
  generated.Count(card => card.Published),
  generated.Count(card => card.ImageGenerated),
  generated,
  cards.Select(card => card.CardVersionId).Order(StringComparer.Ordinal).ToArray(),
  assetManifests.Select(asset => asset.AssetManifestId).Order(StringComparer.Ordinal).ToArray());

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
  WriteIndented = true,
  Converters = { new JsonStringEnumConverter() }
};
var reportPath = Path.Combine(options.OutputRoot, "local-provider-10-card-evidence.json");
await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, jsonOptions));
Console.WriteLine($"Local provider evidence report: {reportPath}");
Console.WriteLine($"validated={report.ValidatedCount}/{report.RequestedCount}; published={report.PublishedCount}/{report.RequestedCount}; images={report.ImageGeneratedCount}/{report.RequestedCount}");

return report.ValidatedCount == options.Count && report.PublishedCount == options.Count && report.ImageGeneratedCount == options.Count
  ? 0
  : 2;

static CardRunReport CreateReport(
  int index,
  string generationId,
  string cardId,
  string? name,
  bool validationAccepted,
  bool published,
  bool imageGenerated,
  IReadOnlyList<string> diagnostics,
  string? validationDecision,
  string? balanceDecision,
  string? balanceRiskTier) =>
  new(index, generationId, cardId, name, validationAccepted, published, imageGenerated, validationDecision, balanceDecision, balanceRiskTier, diagnostics);

static StructuredCardGenerationResult GenerateStructuredWithRetries(
  IStructuredCardGenerationProvider provider,
  string generationId,
  string modelId,
  string prompt,
  GenerationContext context,
  string catalogSnapshotId,
  int maxAttempts)
{
  StructuredCardGenerationResult? last = null;
  for (var attempt = 1; attempt <= maxAttempts; attempt++)
  {
    last = provider.Generate(new StructuredCardGenerationRequest(
      generationId,
      "local-provider-evidence-v1",
      modelId,
      $"{prompt}\nAttempt {attempt}: return strict JSON only.",
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["rulesetProfileId"] = context.RulesetProfileId,
        ["rulesetProfileHash"] = context.RulesetProfileHash,
        ["catalogSnapshotId"] = catalogSnapshotId
      },
      TimeSpan.FromSeconds(120)));
    if (last.Succeeded && last.Output is not null)
    {
      return last;
    }
  }

  return last ?? StructuredCardGenerationResult.Failure(
    "local-ollama-structured",
    modelId,
    $"raw:{generationId}:missing",
    "NO_ATTEMPT",
    "Structured generation was not attempted.");
}

static CardDefinition BuildCandidate(
  string generationId,
  string cardId,
  string cardVersionId,
  string catalogSnapshotId,
  string modelId,
  DateTimeOffset createdAt,
  GenerationContext context,
  StructuredCardOutput output)
{
  var skeletonResult = new GeneratedCardSkeletonBuilder().Build(new SkeletonGenerationRequest(
    $"skeleton:{generationId}",
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
    "local-ollama-structured-provider",
    createdAt,
    context));

  if (!skeletonResult.Succeeded || skeletonResult.Skeleton is null)
  {
    throw new InvalidOperationException(string.Join("; ", skeletonResult.Diagnostics.Select(diagnostic => $"{diagnostic.Path}:{diagnostic.Message}")));
  }

  var compiled = new GeneratedCardSkeletonCompiler().Compile(skeletonResult.Skeleton);
  return compiled with
  {
    CardId = cardId,
    CardVersion = cardVersionId,
    CatalogSnapshotId = catalogSnapshotId,
    Effects = output.Effects,
    CardText = new CardTextGeneration(output.RulesText, FlavorText: null),
    BalanceMetadata = new BalanceMetadata(output.ComplexityScore, output.RandomnessScore, output.RiskFlags),
    AuthoringMetadata = compiled.AuthoringMetadata with
    {
      GenerationPipelineVersion = "local-provider-evidence-pipeline-v1",
      SourceRefs =
      [
        ..compiled.AuthoringMetadata.SourceRefs,
        $"provider:structured:{modelId}",
        "prompt:local-provider-evidence-v1"
      ]
    }
  };
}

static ResolvedRulesetProfile CreateProfile() =>
  new(
    "ruleset_mvp_core_v1",
    "ruleset_mvp_core_v1",
    "1.0.0",
    DateTimeOffset.Parse("2026-07-02T00:00:00Z"),
    "local-provider-evidence",
    "resolved-local-provider-evidence",
    "mvp_internal",
    "core_generated_alpha",
    ["BATTLECRY"],
    [],
    [],
    ["SIMPLE_PLAY_EFFECT"],
    ["PLAY_CARD", "END_TURN", "CONCEDE"],
    ["BATTLECRY"],
    ["HAND", "BOARD", "DECK", "GRAVEYARD", "SET_ASIDE"],
    ["ON_PLAY"],
    [],
    ["MINION_CARD"],
    ["MINION"],
    ["COMMON"],
    ["mage"],
    ["APPRENTICE"],
    new RulesetLimits(30, 25, 20, 80, 7, 1, "LIMITED", 3, 2, 3, 3, 3, 2, 2, 3, 1, 7, 10),
    new ValidationPolicyRef("validation_local_provider", true, true, true, true, true, true, true, true, false),
    new BalancePolicyRef("balance_local_provider", false, "HIGH", [], 0.05, 30, 25, 0.1, 0, 0, "baseline", "seed", "none"),
    new SimulationPolicyRef("simulation_local_provider", [], [], 0, 0, false, 30, 80),
    new PublicationPolicyRef("publication_local_provider", true, false, false, false, false, false, false, ["INTERNAL"]),
    new LegacyPolicy(false, false, false, false, true));

static MechanicStatusRecord CreateMechanic(string mechanicId, MechanicStatus status) =>
  new(
    mechanicId,
    status,
    MechanicTier.Tier0,
    "local-provider-evidence",
    [],
    status is MechanicStatus.ApprovedForRuleset ? ["ruleset_mvp_core_v1"] : [],
    null,
    null,
    ["docs/generator/mechanic_status_spec.md"],
    [],
    [],
    []);

public sealed record LocalProviderEvidenceOptions(
  string StructuredEndpoint,
  string StructuredModel,
  string ImageEndpoint,
  string ImageModel,
  int Count,
  string OutputRoot,
  string CatalogSnapshotId)
{
  public static LocalProviderEvidenceOptions Parse(string[] args)
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
    }

    return new LocalProviderEvidenceOptions(
      values.GetValueOrDefault("structured-endpoint") ?? Environment.GetEnvironmentVariable("CCG_LOCAL_STRUCTURED_ENDPOINT") ?? "http://127.0.0.1:8011/v1/structured-card",
      values.GetValueOrDefault("structured-model") ?? Environment.GetEnvironmentVariable("CCG_LOCAL_STRUCTURED_MODEL") ?? "qwen3.5:latest",
      values.GetValueOrDefault("image-endpoint") ?? Environment.GetEnvironmentVariable("CCG_LOCAL_IMAGE_ENDPOINT") ?? "http://127.0.0.1:8012/v1/assets",
      values.GetValueOrDefault("image-model") ?? Environment.GetEnvironmentVariable("CCG_LOCAL_IMAGE_MODEL") ?? "stable-diffusion-local",
      int.TryParse(values.GetValueOrDefault("count"), out var count) ? count : 10,
      values.GetValueOrDefault("output-root") ?? Path.Combine("artifacts", "local-provider-evidence", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss")),
      values.GetValueOrDefault("catalog-snapshot-id") ?? "catalog-local-provider-evidence");
  }
}

public sealed record LocalProviderEvidenceReport(
  string ReportId,
  DateTimeOffset CreatedAt,
  string StructuredEndpoint,
  string StructuredModel,
  string ImageEndpoint,
  string ImageModel,
  int RequestedCount,
  int ValidatedCount,
  int PublishedCount,
  int ImageGeneratedCount,
  IReadOnlyList<CardRunReport> Cards,
  IReadOnlyList<string> PublishedCardVersionIds,
  IReadOnlyList<string> AssetManifestIds);

public sealed record CardRunReport(
  int Index,
  string GenerationId,
  string CardId,
  string? Name,
  bool ValidationAccepted,
  bool Published,
  bool ImageGenerated,
  string? ValidationDecision,
  string? BalanceDecision,
  string? BalanceRiskTier,
  IReadOnlyList<string> Diagnostics);
