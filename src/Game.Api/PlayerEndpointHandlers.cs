using Game.Application.Players;
using Game.Application.Catalog;
using Game.Application.Decks;
using Game.Shared.Catalog;
using Game.Shared.Decks;
using System.Security.Cryptography;
using System.Text;

namespace Game.Api;

public static class PlayerEndpointHandlers
{
  public sealed record RegisterRequest(
    string Email,
    string Password,
    string DisplayName,
    string StarterHeroId = StarterHeroProfiles.AstralWardenId);
  public sealed record LoginRequest(string Email, string Password);
  public sealed record RefreshRequest(string RefreshToken);
  public sealed record ProfilePatch(string? DisplayName, string? BoardTheme, float? MusicVolume, float? EffectsVolume, bool? ReducedMotion);
  public sealed record GenerationRequest(string Prompt, string ProviderMode = "local", int Count = 3);
  public sealed record FavoriteRequest(bool Favorite);
  public sealed record SaveDeckRequest(string? DeckId, string Name, IReadOnlyList<PlayerDeckCard> Cards);

  public static IResult Register(RegisterRequest request, PlayerAccountService service, CatalogSnapshotQueryService catalogs, IDeckStore runtimeDecks)
  {
    var result=service.Register(request.Email,request.Password,request.DisplayName,request.StarterHeroId);
    if(result.Succeeded&&result.PlayerId is not null)ProvisionStarterDeck(result.PlayerId,service,catalogs,runtimeDecks);
    return AuthResult(result);
  }
  public static IResult StarterHeroes() => Results.Ok(StarterHeroProfiles.All);
  public static IResult Login(LoginRequest request, PlayerAccountService service, CatalogSnapshotQueryService catalogs, IDeckStore runtimeDecks)
  {
    var result=service.Login(request.Email,request.Password);
    if(result.Succeeded&&result.PlayerId is not null)RepairAllVanillaStarterDeck(result.PlayerId,service,catalogs,runtimeDecks);
    return AuthResult(result);
  }
  public static IResult Refresh(RefreshRequest request, PlayerAccountService service) => AuthResult(service.Refresh(request.RefreshToken));

  public static IResult Logout(HttpRequest request, PlayerAccountService service)
  {
    service.Logout(ReadToken(request));
    return Results.NoContent();
  }

  public static IResult Me(HttpRequest request, PlayerAccountService service)
  {
    var account = service.Authenticate(ReadToken(request));
    return account is null ? Unauthorized() : Results.Ok(ToPlayerView(account));
  }

  public static IResult PatchMe(ProfilePatch patch, HttpRequest request, PlayerAccountService service)
  {
    var account = service.Authenticate(ReadToken(request));
    if (account is null) return Unauthorized();
    if (patch.DisplayName is { Length: > 32 } || patch.MusicVolume is < 0 or > 1 || patch.EffectsVolume is < 0 or > 1)
      return Results.BadRequest(new { code = "PROFILE_VALIDATION_FAILED" });
    var profile = account.Profile with
    {
      DisplayName = string.IsNullOrWhiteSpace(patch.DisplayName) ? account.Profile.DisplayName : patch.DisplayName.Trim(),
      BoardTheme = string.IsNullOrWhiteSpace(patch.BoardTheme) ? account.Profile.BoardTheme : patch.BoardTheme.Trim().ToLowerInvariant(),
      MusicVolume = patch.MusicVolume ?? account.Profile.MusicVolume,
      EffectsVolume = patch.EffectsVolume ?? account.Profile.EffectsVolume,
      ReducedMotion = patch.ReducedMotion ?? account.Profile.ReducedMotion
    };
    account = account with { Profile = profile };
    service.Save(account);
    return Results.Ok(ToPlayerView(account));
  }

  public static IResult Collection(HttpRequest request, PlayerAccountService service, CatalogSnapshotQueryService catalogs, string? search = null, bool? favorite = null)
  {
    var account = service.Authenticate(ReadToken(request));
    if (account is null) return Unauthorized();
    var items = account.Collection.AsEnumerable();
    if (!string.IsNullOrWhiteSpace(search)) items = items.Where(x => x.CardId.Contains(search, StringComparison.OrdinalIgnoreCase));
    if (favorite.HasValue) items = items.Where(x => x.Favorite == favorite.Value);
    var versions = catalogs.GetSnapshots().SelectMany(snapshot=>snapshot.Cards).GroupBy(card=>card.CardVersionId,StringComparer.Ordinal)
      .ToDictionary(group=>group.Key,group=>group.Last(),StringComparer.Ordinal);
    return Results.Ok(items.OrderBy(x => x.CardId).Select(item =>
    {
      versions.TryGetValue(item.CardVersionId, out var version);
      return new
      {
        item.CardId,
        item.CardVersionId,
        item.Count,
        item.Favorite,
        item.AcquiredAtUtc,
        displayName = version?.Definition.StaticData.Name ?? item.CardId,
        cardType = version?.Definition.StaticData.CardType.ToString(),
        rarity = version?.Definition.StaticData.Rarity.ToString(),
        manaCost = version?.Definition.StaticData.ManaCost,
        attack = version?.Definition.Entity.Minion?.Attack ?? version?.Definition.Entity.Weapon?.Attack,
        health = version?.Definition.Entity.Minion?.Health,
        durability = version?.Definition.Entity.Weapon?.Durability ?? version?.Definition.Entity.Location?.Durability,
        armorGain = version?.Definition.Entity.HeroCard?.ArmorGain,
        rulesText = version?.Definition.CardText.RulesText,
        visualAssetRef = version?.Definition.AssetMetadata.VisualAssetRef
      };
    }).ToArray());
  }

  public static IResult FavoriteCard(string cardVersionId, FavoriteRequest body, HttpRequest request, PlayerAccountService service)
  {
    var account = service.Authenticate(ReadToken(request));
    if (account is null) return Unauthorized();
    var index = account.Collection.ToList().FindIndex(x => x.CardVersionId == cardVersionId);
    if (index < 0) return Results.NotFound(new { code = "COLLECTION_CARD_NOT_FOUND" });
    var collection = account.Collection.ToArray();
    collection[index] = collection[index] with { Favorite = body.Favorite };
    service.Save(account with { Collection = collection });
    return Results.Ok(collection[index]);
  }

  public static IResult CardAsset(string cardVersionId, HttpRequest request, PlayerAccountService service, CatalogSnapshotQueryService catalogs)
  {
    var account = service.Authenticate(ReadToken(request));
    if (account is null) return Unauthorized();
    if (!account.Collection.Any(x => string.Equals(x.CardVersionId, cardVersionId, StringComparison.Ordinal)))
      return Results.NotFound(new { code = "COLLECTION_CARD_NOT_FOUND" });
    var version = catalogs.FindCardVersion(cardVersionId);
    var assetRef = version?.Definition.AssetMetadata.VisualAssetRef;
    if (string.IsNullOrWhiteSpace(assetRef) || assetRef.StartsWith("procedural://", StringComparison.Ordinal))
      return Results.NotFound(new { code = "CARD_ASSET_NOT_FOUND" });
    var fullPath = Path.GetFullPath(assetRef);
    if (!File.Exists(fullPath) || !string.Equals(Path.GetExtension(fullPath), ".png", StringComparison.OrdinalIgnoreCase))
      return Results.NotFound(new { code = "CARD_ASSET_NOT_FOUND" });
    return Results.File(fullPath, "image/png", enableRangeProcessing: true);
  }

  public static IResult CreateGeneration(GenerationRequest body, HttpRequest request, PlayerAccountService service, IPlayerGenerationJobQueue queue)
  {
    var account = service.Authenticate(ReadToken(request));
    if (account is null) return Unauthorized();
    if (string.IsNullOrWhiteSpace(body.Prompt) || body.Count is < 1 or > 15 || body.ProviderMode is not ("local" or "openai"))
      return Results.BadRequest(new { code = "GENERATION_VALIDATION_FAILED" });

    var now = DateTimeOffset.UtcNow;
    var job = new PlayerGenerationJob($"job-{Guid.NewGuid():N}", body.Prompt.Trim(), body.ProviderMode, "queued", 0, [], null, now, body.Count);
    service.Save(account with { GenerationJobs = account.GenerationJobs.Append(job).ToArray() });
    queue.Enqueue(account.PlayerId, job.JobId);
    return Results.Accepted($"/player/generation/jobs/{job.JobId}", job);
  }

  public static IResult GenerationJob(string jobId, HttpRequest request, PlayerAccountService service)
  {
    var account = service.Authenticate(ReadToken(request));
    if (account is null) return Unauthorized();
    var job = account.GenerationJobs.FirstOrDefault(x => x.JobId == jobId);
    return job is null ? Results.NotFound(new { code = "GENERATION_JOB_NOT_FOUND" }) : Results.Ok(job);
  }

  public static IResult RetryGeneration(string jobId, HttpRequest request, PlayerAccountService service, IPlayerGenerationJobQueue queue)
  {
    var account = service.Authenticate(ReadToken(request)); if (account is null) return Unauthorized();
    var job = account.GenerationJobs.FirstOrDefault(x => x.JobId == jobId);
    if (job is null) return Results.NotFound(new { code = "GENERATION_JOB_NOT_FOUND" });
    if (job.Status is not ("failed" or "cancelled")) return Results.Conflict(new { code = "GENERATION_JOB_NOT_RETRYABLE" });
    var retried = job with { Status = "queued", ProgressPercent = 0, Results = [], ErrorCode = null, UpdatedAtUtc = DateTimeOffset.UtcNow, Attempt = job.Attempt + 1, CancelRequested = false };
    service.Save(account with { GenerationJobs = account.GenerationJobs.Where(x => x.JobId != jobId).Append(retried).ToArray() });
    queue.Enqueue(account.PlayerId, jobId);
    return Results.Accepted($"/player/generation/jobs/{jobId}", retried);
  }

  public static IResult CancelGeneration(string jobId, HttpRequest request, PlayerAccountService service)
  {
    var account = service.Authenticate(ReadToken(request)); if (account is null) return Unauthorized();
    var job = account.GenerationJobs.FirstOrDefault(x => x.JobId == jobId);
    if (job is null) return Results.NotFound(new { code = "GENERATION_JOB_NOT_FOUND" });
    if (job.Status is "completed" or "cancelled") return Results.Conflict(new { code = "GENERATION_JOB_NOT_CANCELLABLE" });
    var cancelled = job with { Status = "cancelled", ProgressPercent = 0, CancelRequested = true, UpdatedAtUtc = DateTimeOffset.UtcNow };
    service.Save(account with { GenerationJobs = account.GenerationJobs.Where(x => x.JobId != jobId).Append(cancelled).ToArray() });
    return Results.Ok(cancelled);
  }

  public static IResult Decks(HttpRequest request, PlayerAccountService service)
  {
    var account = service.Authenticate(ReadToken(request));
    return account is null ? Unauthorized() : Results.Ok(new { account.ActiveDeckId, Decks = account.Decks });
  }

  public static IResult SaveDeck(SaveDeckRequest body, HttpRequest request, PlayerAccountService service, CatalogSnapshotQueryService catalogs, IDeckStore runtimeDecks)
  {
    var account = service.Authenticate(ReadToken(request));
    if (account is null) return Unauthorized();
    if (string.IsNullOrWhiteSpace(body.Name) || body.Cards.Sum(x => x.Count) != 30 || body.Cards.Any(x => x.Count is < 1 or > 2))
      return Results.BadRequest(new { code = "DECK_VALIDATION_FAILED", requiredCardCount = 30, copyLimit = 2 });
    var owned = account.Collection.ToDictionary(x => x.CardVersionId, x => x.Count, StringComparer.Ordinal);
    if (body.Cards.Any(x => !owned.TryGetValue(x.CardVersionId, out var count) || count < x.Count))
      return Results.BadRequest(new { code = "DECK_CARD_NOT_OWNED" });
    var catalog = catalogs.FindSnapshotContainingVersions(body.Cards.Select(card=>card.CardVersionId));
    if (catalog is null) return Results.Conflict(new { code = "DECK_CATALOG_NOT_READY" });
    var versions = catalog.Cards.ToDictionary(x => x.CardVersionId, StringComparer.Ordinal);
    if (body.Cards.Any(x => !versions.ContainsKey(x.CardVersionId))) return Results.BadRequest(new { code = "DECK_CARD_VERSION_NOT_IN_ACTIVE_CATALOG" });
    var deckId = string.IsNullOrWhiteSpace(body.DeckId) ? $"deck-{Guid.NewGuid():N}" : body.DeckId;
    var runtimeDeckId = $"{deckId}-runtime-{Guid.NewGuid():N}";
    var botRuntimeDeckId = $"{deckId}-bot-runtime-{Guid.NewGuid():N}";
    var cardRefs = body.Cards.Select(x => new DeckCardRef(x.CardId, x.Count, x.CardVersionId)).ToArray();
    var selectedHero=StarterHeroProfiles.Find(account.Profile.StarterHeroId)??StarterHeroProfiles.All[0];
    var stored = runtimeDecks.Store(new DeckDefinition(runtimeDeckId, account.PlayerId, catalog.SnapshotId, catalog.RulesetProfileId, cardRefs,selectedHero.HeroId,selectedHero.DisplayName,selectedHero.PortraitAssetRef,selectedHero.StartingHealth));
    if (!stored.Succeeded) return Results.Conflict(new { code = "RUNTIME_DECK_REGISTRATION_FAILED", message = stored.Error });
    var opponentHero=StarterHeroProfiles.OpponentOf(selectedHero.HeroId);
    var botRefs=StarterDeckCards(catalog,opponentHero).Select(card=>new DeckCardRef(card.CardId,card.Count,card.CardVersionId)).ToArray();
    var botStored = runtimeDecks.Store(new DeckDefinition(botRuntimeDeckId, "player-2", catalog.SnapshotId, catalog.RulesetProfileId, botRefs,opponentHero.HeroId,opponentHero.DisplayName,opponentHero.PortraitAssetRef,opponentHero.StartingHealth));
    if (!botStored.Succeeded) return Results.Conflict(new { code = "BOT_DECK_REGISTRATION_FAILED", message = botStored.Error });
    var decks = account.Decks.Where(x => x.DeckId != deckId).Append(new PlayerDeck(deckId, body.Name.Trim(), body.Cards, DateTimeOffset.UtcNow, runtimeDeckId, botRuntimeDeckId)).ToArray();
    service.Save(account with { Decks = decks });
    return Results.Ok(decks.Single(x => x.DeckId == deckId));
  }

  public static IResult DeleteDeck(string deckId, HttpRequest request, PlayerAccountService service)
  {
    var account = service.Authenticate(ReadToken(request));
    if (account is null) return Unauthorized();
    if (!account.Decks.Any(x => x.DeckId == deckId)) return Results.NotFound(new { code = "DECK_NOT_FOUND" });
    service.Save(account with { Decks = account.Decks.Where(x => x.DeckId != deckId).ToArray(), ActiveDeckId = account.ActiveDeckId == deckId ? null : account.ActiveDeckId });
    return Results.NoContent();
  }

  public static IResult SelectDeck(string deckId, HttpRequest request, PlayerAccountService service)
  {
    var account = service.Authenticate(ReadToken(request));
    if (account is null) return Unauthorized();
    if (!account.Decks.Any(x => x.DeckId == deckId)) return Results.NotFound(new { code = "DECK_NOT_FOUND" });
    service.Save(account with { ActiveDeckId = deckId });
    return Results.Ok(new { activeDeckId = deckId });
  }

  private static object ToPlayerView(PlayerAccount a) => new { a.PlayerId, a.Email, a.Profile, collectionCount = a.Collection.Count, deckCount = a.Decks.Count, a.ActiveDeckId, a.CreatedAtUtc };
  private static void ProvisionStarterDeck(string playerId,PlayerAccountService service,CatalogSnapshotQueryService catalogs,IDeckStore runtimeDecks)
  {
    var account=service.FindById(playerId);var catalog=catalogs.GetCanonicalStarterSnapshot();if(account is null||catalog is null||catalog.Cards.Count<15)return;
    ProvisionStarterDeck(account,catalog,service,runtimeDecks,false);
  }
  private static void RepairAllVanillaStarterDeck(string playerId,PlayerAccountService service,CatalogSnapshotQueryService catalogs,IDeckStore runtimeDecks)
  {
    var account=service.FindById(playerId);var canonical=catalogs.GetCanonicalStarterSnapshot();
    if(account is null||canonical is null||account.Collection.Count<15)return;
    var versions=catalogs.GetSnapshots().SelectMany(snapshot=>snapshot.Cards).GroupBy(card=>card.CardVersionId,StringComparer.Ordinal).ToDictionary(group=>group.Key,group=>group.Last(),StringComparer.Ordinal);
    var known=account.Collection.Select(item=>versions.GetValueOrDefault(item.CardVersionId)).Where(card=>card is not null).ToArray();
    var defective=known.Length==account.Collection.Count&&known.All(card=>card!.Definition.Effects.Count==0&&string.IsNullOrWhiteSpace(card.Definition.CardText.RulesText));
    if(defective)ProvisionStarterDeck(account,canonical,service,runtimeDecks,true);
  }
  private static void ProvisionStarterDeck(PlayerAccount account,CatalogSnapshot catalog,PlayerAccountService service,IDeckStore runtimeDecks,bool repaired)
  {
    var playerId=account.PlayerId;
    var hero=StarterHeroProfiles.Find(account.Profile.StarterHeroId)??StarterHeroProfiles.All[0];
    var cards=StarterDeckCards(catalog,hero);var now=DateTimeOffset.UtcNow;
    var revision=repaired?"-canonical-v1":string.Empty;var runtimeId=$"starter-{playerId}{revision}-runtime";var botRuntimeId=$"starter-{playerId}{revision}-bot-runtime";
    var refs=cards.Select(card=>new DeckCardRef(card.CardId,card.Count,card.CardVersionId)).ToArray();
    if(!runtimeDecks.Store(new DeckDefinition(runtimeId,playerId,catalog.SnapshotId,catalog.RulesetProfileId,refs,hero.HeroId,hero.DisplayName,hero.PortraitAssetRef,hero.StartingHealth)).Succeeded)return;
    var opponentHero=StarterHeroProfiles.OpponentOf(hero.HeroId);var botRefs=StarterDeckCards(catalog,opponentHero).Select(card=>new DeckCardRef(card.CardId,card.Count,card.CardVersionId)).ToArray();
    if(!runtimeDecks.Store(new DeckDefinition(botRuntimeId,"player-2",catalog.SnapshotId,catalog.RulesetProfileId,botRefs,opponentHero.HeroId,opponentHero.DisplayName,opponentHero.PortraitAssetRef,opponentHero.StartingHealth)).Succeeded)return;
    var deck=new PlayerDeck($"starter-{playerId}",$"{hero.DisplayName} Starter",cards,now,runtimeId,botRuntimeId);
    var collection=cards.Select(card=>new PlayerCollectionEntry(card.CardId,card.CardVersionId,2,false,now)).ToArray();
    service.Save(account with{Collection=collection,Decks=[deck],ActiveDeckId=deck.DeckId});
  }
  private static PlayerDeckCard[] StarterDeckCards(CatalogSnapshot catalog,StarterHeroProfile hero)
  {
    var ordered=catalog.Cards.OrderBy(card=>card.CardId,StringComparer.Ordinal).ToArray();
    if(ordered.Length<15)return [];
    var offset=ordered.Length>=30?Math.Clamp(hero.CatalogOffset,0,ordered.Length-15):(StringComparer.Ordinal.Equals(hero.HeroId,StarterHeroProfiles.CinderVanguardId)?ordered.Length/2:0);
    return ordered.Skip(offset).Concat(ordered.Take(offset)).Take(15).Select(card=>new PlayerDeckCard(card.CardId,card.CardVersionId,2)).ToArray();
  }
  private static string ReadToken(HttpRequest request) => request.Headers["Authorization"].ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
    ? request.Headers["Authorization"].ToString()[7..].Trim()
    : !string.IsNullOrWhiteSpace(request.Headers["X-Player-Token"].ToString())
      ? request.Headers["X-Player-Token"].ToString()
      : request.Headers["X-Session-Token"].ToString();
  private static IResult Unauthorized() => Results.Json(new { code = "AUTH_REQUIRED" }, statusCode: StatusCodes.Status401Unauthorized);
  private static IResult AuthResult(PlayerAuthResult result) => result.Succeeded ? Results.Ok(result) : Results.Json(new { code = result.ErrorCode }, statusCode: result.ErrorCode switch { "AUTH_VALIDATION_FAILED" => 400, "AUTH_EMAIL_EXISTS" => 409, _ => 401 });
  private static string Slug(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).Take(20).ToArray());
}
