using Game.Application.Catalog;
using Game.Application.Capabilities;
using Game.Application.Decks;
using Game.Application.Matchmaking;
using Game.Application.Matches;
using Game.Application.Presentation;
using Game.Application.Players;
using Game.Application.Rulesets;
using Game.Api;
using Game.Generator.Acceptance;
using Game.Infrastructure.Storage;
using Game.Infrastructure.Generation;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

DotEnvLoader.LoadNearest();
var builder = WebApplication.CreateBuilder(args);
var storageRootPath = builder.Configuration["Storage:RootPath"];
var maxRequestBodyBytes = builder.Configuration.GetValue<long?>("Api:MaxRequestBodyBytes") ?? 1_048_576;
var writeRateLimitPolicy = "api-write";
var writeRateLimitPermitLimit = builder.Configuration.GetValue<int?>("Api:WriteRateLimit:PermitLimit") ?? 60;
var writeRateLimitWindowSeconds = builder.Configuration.GetValue<int?>("Api:WriteRateLimit:WindowSeconds") ?? 60;
var corsEnabled = builder.Configuration.GetValue<bool?>("Api:Cors:Enabled") ?? false;
var corsPolicy = "unity-webgl";
var corsAllowedOrigins = ReadCsvConfiguration("Api:Cors:AllowedOrigins", string.Empty);
var corsAllowedMethods = ReadCsvConfiguration("Api:Cors:AllowedMethods", "GET,POST,OPTIONS");
var corsAllowedHeaders = ReadCsvConfiguration("Api:Cors:AllowedHeaders", "Content-Type,X-Session-Token");
builder.Services.ConfigureHttpJsonOptions(options =>
{
  options.SerializerOptions.AllowOutOfOrderMetadataProperties = true;
  options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

if (string.IsNullOrWhiteSpace(storageRootPath))
{
  builder.Services.AddSingleton<ICatalogSnapshotStore, InMemoryCatalogSnapshotStore>();
  builder.Services.AddSingleton<IDeckStore, InMemoryDeckStore>();
  builder.Services.AddSingleton<IResolvedRulesetProfileStore, InMemoryResolvedRulesetProfileStore>();
  builder.Services.AddSingleton<IAssetManifestStore, InMemoryAssetManifestStore>();
  builder.Services.AddSingleton<IProductionAcceptanceReportStore, InMemoryProductionAcceptanceReportStore>();
  builder.Services.AddSingleton<IMatchEvidenceArtifactStore, InMemoryMatchEvidenceArtifactStore>();
  builder.Services.AddSingleton<IApiSessionStore, InMemoryApiSessionStore>();
  builder.Services.AddSingleton<IMatchRuntimeStore, InMemoryMatchRuntimeStore>();
  builder.Services.AddSingleton<IPlayerAccountStore, InMemoryPlayerAccountStore>();
}
else
{
  builder.Services.AddSingleton<ICatalogSnapshotStore>(_ => new JsonFileCatalogSnapshotStore(storageRootPath));
  builder.Services.AddSingleton<IDeckStore>(_ => new JsonFileDeckStore(storageRootPath));
  builder.Services.AddSingleton<IResolvedRulesetProfileStore>(_ => new JsonFileResolvedRulesetProfileStore(storageRootPath));
  builder.Services.AddSingleton<IAssetManifestStore>(_ => new JsonFileAssetManifestStore(storageRootPath));
  builder.Services.AddSingleton<IProductionAcceptanceReportStore>(_ => new JsonFileProductionAcceptanceReportStore(storageRootPath));
  builder.Services.AddSingleton<IMatchEvidenceArtifactStore>(_ => new JsonFileMatchEvidenceArtifactStore(storageRootPath));
  builder.Services.AddSingleton<IApiSessionStore>(_ => new JsonFileApiSessionStore(storageRootPath));
  builder.Services.AddSingleton<IMatchRuntimeStore>(_ => new JsonFileMatchRuntimeStore(storageRootPath));
  builder.Services.AddSingleton<IPlayerAccountStore>(_ => new JsonFilePlayerAccountStore(storageRootPath));
}

builder.Services.AddSingleton<CatalogSnapshotQueryService>();
builder.Services.AddSingleton<ICatalogCompatibilityValidator, CatalogCompatibilityValidator>();
builder.Services.AddSingleton<DeckQueryService>();
builder.Services.AddSingleton<IDeckValidator, DeckValidator>();
builder.Services.AddSingleton<ResolvedRulesetProfileQueryService>();
builder.Services.AddSingleton<IRulesetDefinitionRegistry, DefaultRulesetDefinitionRegistry>();
builder.Services.AddSingleton<IMatchReadinessService, MatchReadinessService>();
builder.Services.AddSingleton<IMatchStartService, MatchStartService>();
builder.Services.AddSingleton<AssetManifestQueryService>();
builder.Services.AddSingleton<IPresentationAssetCompatibilityValidator, PresentationAssetCompatibilityValidator>();
builder.Services.AddSingleton<IPresentationCatalogHasher, PresentationCatalogHasher>();
builder.Services.AddSingleton<IPresentationCueResolver, PresentationCueResolver>();
builder.Services.AddSingleton<IPresentationTimelineHasher, PresentationTimelineHasher>();
builder.Services.AddSingleton<IPresentationTimelineBuilder, PresentationTimelineBuilder>();
builder.Services.AddSingleton<IPresentationPlaybackPlanner, PresentationPlaybackPlanner>();
builder.Services.AddSingleton<IPresentationPlaybackRuntime, PresentationPlaybackRuntime>();
builder.Services.AddSingleton<IEngineCapabilityRegistryProvider, DefaultEngineCapabilityRegistryProvider>();
builder.Services.AddSingleton<IEngineCapabilityCompatibilityValidator, EngineCapabilityCompatibilityValidator>();
builder.Services.AddSingleton<IEngineCapabilityAuditService, EngineCapabilityAuditService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MatchRuntimeService>();
builder.Services.AddSingleton<PlayerAccountService>();
builder.Services.AddSingleton<IPlayerGenerationExecutor>(_ => new PlayerGenerationExecutor(
  new HttpClient { Timeout = TimeSpan.FromMinutes(4) },
  builder.Configuration["Generation:OpenAIStructuredEndpoint"] ?? "https://api.openai.com/v1/responses",
  builder.Configuration["Generation:OpenAIStructuredModel"] ?? "gpt-5.6-terra",
  builder.Configuration["Generation:OpenAIImageEndpoint"] ?? "https://api.openai.com/v1/images/generations",
  builder.Configuration["Generation:OpenAIImageModel"] ?? "gpt-image-2",
  builder.Configuration["OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
  builder.Configuration["Generation:LocalStructuredEndpoint"] ?? "http://127.0.0.1:8011/v1/structured-card",
  builder.Configuration["Generation:LocalStructuredModel"] ?? "qwen3.5:latest",
  builder.Configuration["Generation:LocalImageEndpoint"] ?? "http://127.0.0.1:8012/v1/assets",
  builder.Configuration["Generation:LocalImageModel"] ?? "stabilityai/sd-turbo",
  storageRootPath,
  _.GetRequiredService<IResolvedRulesetProfileStore>(),
  _.GetRequiredService<IEngineCapabilityRegistryProvider>()));
builder.Services.AddSingleton<PlayerGenerationJobWorker>();
builder.Services.AddSingleton<IPlayerGenerationJobQueue>(services => services.GetRequiredService<PlayerGenerationJobWorker>());
builder.Services.AddHostedService(services => services.GetRequiredService<PlayerGenerationJobWorker>());
builder.Services.AddSingleton<Game.Api.ApiTelemetry>();
if (corsEnabled)
{
  builder.Services.AddCors(options =>
  {
    options.AddPolicy(corsPolicy, policy =>
    {
      if (corsAllowedOrigins.Length > 0)
      {
        policy.WithOrigins(corsAllowedOrigins)
          .WithMethods(corsAllowedMethods)
          .WithHeaders(corsAllowedHeaders);
      }
    });
  });
}

builder.Services.AddRateLimiter(options =>
{
  options.OnRejected = async (context, cancellationToken) =>
  {
    context.HttpContext.RequestServices.GetRequiredService<Game.Api.ApiTelemetry>()
      .RecordRequestRejected("rate_limit_exceeded", context.HttpContext.Request.Path, StatusCodes.Status429TooManyRequests);
    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
    await context.HttpContext.Response.WriteAsJsonAsync(new
    {
      code = "RATE_LIMIT_EXCEEDED",
      policy = writeRateLimitPolicy,
      permitLimit = writeRateLimitPermitLimit,
      windowSeconds = writeRateLimitWindowSeconds
    }, cancellationToken);
  };

  options.AddPolicy(writeRateLimitPolicy, context =>
    RateLimitPartition.GetFixedWindowLimiter(
      context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
      _ => new FixedWindowRateLimiterOptions
      {
        PermitLimit = writeRateLimitPermitLimit,
        Window = TimeSpan.FromSeconds(writeRateLimitWindowSeconds),
        QueueLimit = 0,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        AutoReplenishment = true
      }));
});

var app = builder.Build();

if (corsEnabled)
{
  app.Use(async (context, next) =>
  {
    var request = context.Request;
    if (HttpMethods.IsOptions(request.Method)
      && request.Headers.TryGetValue("Origin", out var origin)
      && request.Headers.ContainsKey("Access-Control-Request-Method"))
    {
      var requestedOrigin = origin.ToString();
      if (corsAllowedOrigins.Contains(requestedOrigin, StringComparer.OrdinalIgnoreCase))
      {
        context.Response.Headers.AccessControlAllowOrigin = requestedOrigin;
        context.Response.Headers.Vary = "Origin";
        context.Response.Headers.AccessControlAllowMethods = string.Join(",", corsAllowedMethods);
        context.Response.Headers.AccessControlAllowHeaders = string.Join(",", corsAllowedHeaders);
      }

      context.Response.StatusCode = StatusCodes.Status204NoContent;
      return;
    }

    await next();
  });

  app.UseCors(corsPolicy);
}

app.Use(async (context, next) =>
{
  if (context.Request.ContentLength is > 0
    && context.Request.ContentLength > maxRequestBodyBytes)
  {
    context.RequestServices.GetRequiredService<Game.Api.ApiTelemetry>()
      .RecordRequestRejected("request_body_too_large", context.Request.Path, StatusCodes.Status413PayloadTooLarge);
    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
    await context.Response.WriteAsJsonAsync(new
    {
      code = "REQUEST_BODY_TOO_LARGE",
      maxRequestBodyBytes
    });
    return;
  }

  await next(context);
});

app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/auth/starter-heroes", PlayerEndpointHandlers.StarterHeroes);
app.MapPost("/auth/register", PlayerEndpointHandlers.Register).RequireRateLimiting(writeRateLimitPolicy);
app.MapPost("/auth/login", PlayerEndpointHandlers.Login).RequireRateLimiting(writeRateLimitPolicy);
app.MapPost("/auth/refresh", PlayerEndpointHandlers.Refresh).RequireRateLimiting(writeRateLimitPolicy);
app.MapPost("/auth/logout", PlayerEndpointHandlers.Logout).RequireRateLimiting(writeRateLimitPolicy);
app.MapGet("/me", PlayerEndpointHandlers.Me);
app.MapPatch("/me", PlayerEndpointHandlers.PatchMe).RequireRateLimiting(writeRateLimitPolicy);
app.MapGet("/player/collection", PlayerEndpointHandlers.Collection);
app.MapGet("/player/card-assets/{cardVersionId}", PlayerEndpointHandlers.CardAsset);
app.MapPatch("/player/collection/{cardVersionId}/favorite", PlayerEndpointHandlers.FavoriteCard).RequireRateLimiting(writeRateLimitPolicy);
app.MapPost("/player/generation/jobs", PlayerEndpointHandlers.CreateGeneration).RequireRateLimiting(writeRateLimitPolicy);
app.MapGet("/player/generation/jobs/{jobId}", PlayerEndpointHandlers.GenerationJob);
app.MapPost("/player/generation/jobs/{jobId}/retry", PlayerEndpointHandlers.RetryGeneration).RequireRateLimiting(writeRateLimitPolicy);
app.MapPost("/player/generation/jobs/{jobId}/cancel", PlayerEndpointHandlers.CancelGeneration).RequireRateLimiting(writeRateLimitPolicy);
app.MapGet("/player/decks", PlayerEndpointHandlers.Decks);
app.MapPost("/player/decks", PlayerEndpointHandlers.SaveDeck).RequireRateLimiting(writeRateLimitPolicy);
app.MapDelete("/player/decks/{deckId}", PlayerEndpointHandlers.DeleteDeck).RequireRateLimiting(writeRateLimitPolicy);
app.MapPost("/player/decks/{deckId}/select", PlayerEndpointHandlers.SelectDeck).RequireRateLimiting(writeRateLimitPolicy);
app.MapGet(
  "/engine/capabilities",
  (IEngineCapabilityRegistryProvider provider, IRulesetDefinitionRegistry rulesets) =>
  {
    var registry = provider.GetCurrentRegistry();
    var productionRuleset = rulesets.DefaultProductionRuleset;
    return Results.Ok(new
    {
      registry.EngineVersion,
      apiContractVersion = "unity-contract-2026-07-07",
      stateSchemaVersion = "match-state-v1",
      eventSchemaVersion = "match-events-v1",
      assetManifestSchemaVersion = registry.SupportedAssetManifestVersions.FirstOrDefault() ?? string.Empty,
      playbackSchemaVersion = "presentation-playback-v1",
      minimumUnityClientContractVersion = "unity-contract-2026-07-07",
      supportedCommandTypes = registry.SupportedActionTypes.Where(IsUnityCommandType).Order(StringComparer.Ordinal).ToArray(),
      supportedEventTypes = GetSupportedUnityEventTypes(),
      registry.SupportedSchemaVersions,
      registry.SupportedPrimitiveRegistryVersions,
      registry.SupportedRulesetProfileVersions,
      registry.SupportedActionTypes,
      registry.SupportedEffectKinds,
      registry.SupportedMechanics,
      registry.SupportedPhases,
      registry.SupportedSourceZones,
      registry.SupportedResolutionPolicies,
      registry.SupportedFizzlePolicies,
      registry.SupportedAssetManifestVersions,
      defaultProductionRulesetId = productionRuleset.RulesetId,
      defaultProductionRulesetVersion = productionRuleset.RulesetVersion,
      productionDeckRulesAvailable = true,
      featureFlags = Array.Empty<string>(),
      registry.Metadata
    });
  });
app.MapGet(
  "/rulesets",
  (IRulesetDefinitionRegistry service) => Results.Ok(service.GetAll()));
app.MapGet(
  "/rulesets/{rulesetId}",
  (string rulesetId, IRulesetDefinitionRegistry service) =>
  {
    var ruleset = service.Find(rulesetId);
    return ruleset is null ? Results.NotFound() : Results.Ok(ruleset);
  });
app.MapGet(
  "/rulesets/{rulesetId}/versions/{version}",
  (string rulesetId, string version, IRulesetDefinitionRegistry service) =>
  {
    var ruleset = service.FindVersion(rulesetId, version);
    return ruleset is null ? Results.NotFound() : Results.Ok(ruleset);
  });
app.MapGet(
  "/catalog/snapshots/current",
  (CatalogSnapshotQueryService service) =>
  {
    var snapshot = service.GetLatestSnapshot();
    return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
  });
app.MapGet(
  "/catalog/snapshots/{snapshotId}",
  (string snapshotId, CatalogSnapshotQueryService service) =>
  {
    var snapshot = service.GetSnapshot(snapshotId);
    return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
  });
app.MapPost("/catalog/snapshots", StorageEndpointHandlers.StoreCatalogSnapshot)
  .RequireRateLimiting(writeRateLimitPolicy);
app.MapGet(
  "/catalog/snapshots/{snapshotId}/compatibility",
  (
    string snapshotId,
    CatalogSnapshotQueryService catalogService,
    IEngineCapabilityRegistryProvider capabilityProvider,
    ICatalogCompatibilityValidator validator) =>
  {
    var snapshot = catalogService.GetSnapshot(snapshotId);
    if (snapshot is null)
    {
      return Results.NotFound();
    }

    var result = validator.Validate(new CatalogCompatibilityRequest(
      snapshot,
      capabilityProvider.GetCurrentRegistry()));
    return Results.Ok(result);
  });
app.MapGet(
  "/decks/current",
  (DeckQueryService service) =>
  {
    var deck = service.GetLatestDeck();
    return deck is null ? Results.NotFound() : Results.Ok(deck);
  });
app.MapGet(
  "/decks/{deckId}",
  (string deckId, DeckQueryService service) =>
  {
    var deck = service.GetDeck(deckId);
    return deck is null ? Results.NotFound() : Results.Ok(deck);
  });
app.MapPost("/decks", StorageEndpointHandlers.StoreDeck)
  .RequireRateLimiting(writeRateLimitPolicy);
app.MapGet(
  "/rulesets/profiles/current",
  (ResolvedRulesetProfileQueryService service) =>
  {
    var profile = service.GetLatestProfile();
    return profile is null ? Results.NotFound() : Results.Ok(profile);
  });
app.MapGet(
  "/rulesets/profiles/{profileId}",
  (string profileId, ResolvedRulesetProfileQueryService service) =>
  {
    var profile = service.GetProfile(profileId);
    return profile is null ? Results.NotFound() : Results.Ok(profile);
  });
app.MapPost("/rulesets/profiles", StorageEndpointHandlers.StoreResolvedRulesetProfile)
  .RequireRateLimiting(writeRateLimitPolicy);
app.MapGet(
  "/matches/readiness/{deckId}",
  (string deckId, IMatchReadinessService service) =>
    Results.Ok(service.Check(new MatchReadinessRequest(deckId))));
app.MapPost(
  "/matches/start",
  (MatchStartRequest request, IMatchStartService service) =>
  {
    var result = service.Start(request);
    return result.Succeeded ? Results.Ok(result) : Results.BadRequest(result);
  })
  .RequireRateLimiting(writeRateLimitPolicy);
app.MapGet(
  "/assets/manifests/current",
  (AssetManifestQueryService service) =>
  {
    var manifest = service.GetLatestManifest();
    return manifest is null ? Results.NotFound() : Results.Ok(manifest);
  });
app.MapGet(
  "/assets/manifests/{assetManifestId}",
  (string assetManifestId, AssetManifestQueryService service) =>
  {
    var manifest = service.GetManifest(assetManifestId);
    return manifest is null ? Results.NotFound() : Results.Ok(manifest);
  });
app.MapPost("/assets/manifests", StorageEndpointHandlers.StoreAssetManifest)
  .RequireRateLimiting(writeRateLimitPolicy);
app.MapGet(
  "/assets/manifests/{assetManifestId}/compatibility",
  (
    string assetManifestId,
    AssetManifestQueryService assetService,
    IEngineCapabilityRegistryProvider capabilityProvider,
    IPresentationAssetCompatibilityValidator validator) =>
  {
    var manifest = assetService.GetManifest(assetManifestId);
    if (manifest is null)
    {
      return Results.NotFound();
    }

    var result = validator.Validate(manifest, capabilityProvider.GetCurrentRegistry());
    return Results.Ok(result);
  });
app.MapGet(
  "/assets/manifests/{assetManifestId}/presentation-hash",
  (
    string assetManifestId,
    AssetManifestQueryService assetService,
    IPresentationCatalogHasher hasher) =>
  {
    var manifest = assetService.GetManifest(assetManifestId);
    if (manifest is null)
    {
      return Results.NotFound();
    }

    return Results.Ok(new
    {
      assetManifestId = manifest.AssetManifestId,
      presentationCatalogHash = hasher.HashManifest(manifest)
    });
  });
app.MapPost(
  "/presentation/timelines",
  (PresentationTimelineBuildRequest request, IPresentationTimelineBuilder builder) =>
    Results.Ok(builder.Build(request)))
  .RequireRateLimiting(writeRateLimitPolicy);
app.MapPost(
  "/presentation/playback-plans",
  (PresentationPlaybackPlanBuildRequest request, IPresentationPlaybackPlanner planner) =>
    Results.Ok(planner.Build(request)))
  .RequireRateLimiting(writeRateLimitPolicy);
app.MapPost(
  "/presentation/playback-instructions",
  (PresentationPlaybackRuntimeRequest request, IPresentationPlaybackRuntime runtime) =>
    Results.Ok(runtime.BuildInstructions(request)))
  .RequireRateLimiting(writeRateLimitPolicy);
app.MapGet(
  "/acceptance/reports/current",
  (IProductionAcceptanceReportStore store) =>
  {
    var report = store.Latest;
    return report is null ? Results.NotFound() : Results.Ok(report);
  });
app.MapGet(
  "/acceptance/reports/{reportId}",
  (string reportId, IProductionAcceptanceReportStore store) =>
  {
    var report = store.FindById(reportId);
    return report is null ? Results.NotFound() : Results.Ok(report);
  });
app.MapGet("/acceptance/reports/{reportId}/match", StorageEndpointHandlers.GetProductionAcceptanceReportMatchEvidence);
app.MapPost("/acceptance/reports", StorageEndpointHandlers.StoreProductionAcceptanceReport)
  .RequireRateLimiting(writeRateLimitPolicy);
app.MapGet("/matches/{matchId}/evidence-artifacts", StorageEndpointHandlers.GetMatchEvidenceArtifacts);
app.MapGet("/match-evidence-artifacts/{**artifactRef}", StorageEndpointHandlers.GetMatchEvidenceArtifact);
app.MapPost("/match-evidence-artifacts", StorageEndpointHandlers.StoreMatchEvidenceArtifact)
  .RequireRateLimiting(writeRateLimitPolicy);
app.MapPost("/sessions", Game.Api.MatchEndpointHandlers.CreateSession)
  .RequireRateLimiting(writeRateLimitPolicy);
app.MapPost("/matches", Game.Api.MatchEndpointHandlers.StartMatch)
  .RequireRateLimiting(writeRateLimitPolicy);
app.MapGet("/matches/{matchId}/state", Game.Api.MatchEndpointHandlers.GetState);
app.MapGet("/matches/{matchId}/players/{playerId}/legal-actions", Game.Api.MatchEndpointHandlers.GetLegalActions);
app.MapPost("/matches/{matchId}/commands", Game.Api.MatchEndpointHandlers.SubmitCommand)
  .RequireRateLimiting(writeRateLimitPolicy);
app.MapGet("/matches/{matchId}/events", Game.Api.MatchEndpointHandlers.PollEvents);
app.MapPost("/matches/{matchId}/reconnect", Game.Api.MatchEndpointHandlers.Reconnect)
  .RequireRateLimiting(writeRateLimitPolicy);
app.MapPost("/matches/{matchId}/replay/verify", Game.Api.MatchEndpointHandlers.VerifyReplay)
  .RequireRateLimiting(writeRateLimitPolicy);

if (corsEnabled)
{
  foreach (var path in new[]
  {
    "/sessions",
    "/matches",
    "/matches/{matchId}/state",
    "/matches/{matchId}/players/{playerId}/legal-actions",
    "/matches/{matchId}/commands",
    "/matches/{matchId}/events",
    "/matches/{matchId}/reconnect",
    "/matches/{matchId}/replay/verify",
    "/catalog/snapshots/current",
    "/catalog/snapshots/{snapshotId}",
    "/catalog/snapshots/{snapshotId}/compatibility",
    "/decks/current",
    "/decks/{deckId}",
    "/rulesets",
    "/rulesets/{rulesetId}",
    "/rulesets/{rulesetId}/versions/{version}",
    "/rulesets/profiles/current",
    "/rulesets/profiles/{profileId}",
    "/assets/manifests/current",
    "/assets/manifests/{assetManifestId}",
    "/assets/manifests/{assetManifestId}/compatibility",
    "/assets/manifests/{assetManifestId}/presentation-hash",
    "/presentation/timelines",
    "/presentation/playback-plans",
    "/presentation/playback-instructions"
  })
  {
    app.MapMethods(path, ["OPTIONS"], () => Results.NoContent());
  }

  app.MapMethods("/{**path}", ["OPTIONS"], () => Results.NoContent());
}

app.Run();

string[] ReadCsvConfiguration(string key, string fallback)
{
  var value = builder.Configuration[key];
  if (string.IsNullOrWhiteSpace(value))
  {
    value = fallback;
  }

  return (value ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Where(entry => !string.IsNullOrWhiteSpace(entry))
    .Distinct(StringComparer.Ordinal)
    .ToArray();
}

bool IsUnityCommandType(string actionType) =>
  actionType is "ACTIVATE_LOCATION" or "ATTACK" or "CONCEDE" or "END_TURN" or "PLAY_CARD" or "RESOLVE_CHOICE" or "USE_HERO_POWER";

string[] GetSupportedUnityEventTypes() =>
[
  "GAME_STARTED",
  "TURN_STARTED",
  "EXTRA_TURN_SCHEDULED",
  "TURN_ENDED",
  "CARD_PLAYED",
  "CARD_MOVED_ZONE",
  "CARD_DRAWN",
  "CARD_DISCARDED",
  "CARD_RETURNED_TO_HAND",
  "MINION_SUMMONED",
  "ENTITY_DAMAGED",
  "DAMAGE_WOULD_BE_DEALT",
  "REPLACEMENT_RESOLVED",
  "ENTITY_HEALED",
  "MANA_CHANGED",
  "ENTITY_DESTROYED",
  "ENTITY_DIED",
  "DEATHRATTLE_TRIGGERED",
  "SECRET_REVEALED",
  "QUEST_PROGRESS_CHANGED",
  "MODIFIER_APPLIED",
  "MODIFIER_EXPIRED",
  "AURA_REFRESHED",
  "WEAPON_EQUIPPED",
  "LOCATION_PLAYED",
  "LOCATION_ACTIVATED",
  "HERO_CARD_PLAYED",
  "HERO_POWER_USED",
  "HERO_POWER_REPLACED",
  "CHOICE_CREATED",
  "CHOICE_RESOLVED",
  "RNG_ROLLED",
  "GAME_ENDED",
  "ENGINE_FAULTED"
];

public partial class Program;
