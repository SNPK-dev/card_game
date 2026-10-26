using Game.Application.Catalog;
using Game.Application.Decks;
using Game.Application.Players;
using Game.Domain.State;
using Game.Shared.Catalog;
using Game.Shared.Decks;
using Game.Shared.Primitives;

namespace Game.Application.Matchmaking;

public sealed class MatchStartService(
  DeckQueryService deckQuery,
  CatalogSnapshotQueryService catalogQuery,
  IMatchReadinessService readinessService) : IMatchStartService
{
  public MatchStartResult Start(MatchStartRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.DeckIds);

    var diagnostics = new List<MatchStartDiagnostic>();
    AddRequired(diagnostics, request.GameId, "gameId", "MATCH_START_GAME_ID_REQUIRED");
    if (request.DeckIds.Count < 2)
    {
      diagnostics.Add(new MatchStartDiagnostic(
        "MATCH_START_REQUIRES_TWO_DECKS",
        "deckIds",
        request.DeckIds.Count.ToString(),
        "Match start requires at least two decks."));
    }

    var decks = new List<DeckDefinition>();
    var catalogs = new Dictionary<string, CatalogSnapshot>(StringComparer.Ordinal);
    for (var deckIndex = 0; deckIndex < request.DeckIds.Count; deckIndex++)
    {
      var deckId = request.DeckIds[deckIndex];
      var deckPath = $"deckIds[{deckIndex}]";
      AddRequired(diagnostics, deckId, deckPath, "MATCH_START_DECK_ID_REQUIRED");
      if (string.IsNullOrWhiteSpace(deckId))
      {
        continue;
      }

      var readiness = readinessService.Check(new MatchReadinessRequest(deckId));
      if (!readiness.IsReady)
      {
        foreach (var diagnostic in readiness.Diagnostics)
        {
          diagnostics.Add(new MatchStartDiagnostic(
            diagnostic.Code,
            $"{deckPath}.readiness.{diagnostic.Path}",
            diagnostic.Identifier,
            diagnostic.Message));
        }

        continue;
      }

      var deck = deckQuery.GetDeck(deckId);
      if (deck is null)
      {
        diagnostics.Add(new MatchStartDiagnostic(
          "MATCH_START_DECK_NOT_FOUND",
          deckPath,
          deckId,
          $"Deck '{deckId}' was not found after readiness passed."));
        continue;
      }

      var catalog = catalogQuery.GetSnapshot(deck.CatalogSnapshotId);
      if (catalog is null)
      {
        diagnostics.Add(new MatchStartDiagnostic(
          "MATCH_START_CATALOG_SNAPSHOT_NOT_FOUND",
          $"{deckPath}.catalogSnapshotId",
          deck.CatalogSnapshotId,
          $"Catalog snapshot '{deck.CatalogSnapshotId}' was not found after readiness passed."));
        continue;
      }

      if (StarterHeroProfiles.Find(deck.StarterHeroId) is null)
      {
        diagnostics.Add(new MatchStartDiagnostic(
          "MATCH_START_STARTER_HERO_NOT_FOUND",
          $"{deckPath}.starterHeroId",
          deck.StarterHeroId,
          $"Starter hero '{deck.StarterHeroId}' is not a trusted starter profile."));
        continue;
      }

      decks.Add(deck);
      catalogs[deck.DeckId] = catalog;
    }

    var duplicatePlayer = decks
      .GroupBy(deck => deck.PlayerId, StringComparer.Ordinal)
      .FirstOrDefault(group => group.Count() > 1);
    if (duplicatePlayer is not null)
    {
      diagnostics.Add(new MatchStartDiagnostic(
        "MATCH_START_DUPLICATE_PLAYER",
        "deckIds",
        duplicatePlayer.Key,
        $"Player '{duplicatePlayer.Key}' is assigned to more than one starting deck."));
    }

    var deckRulesets = decks
      .Select(deck => deck.RulesetProfileId)
      .Distinct(StringComparer.Ordinal)
      .ToArray();
    if (deckRulesets.Length > 1)
    {
      diagnostics.Add(new MatchStartDiagnostic(
        "MATCH_START_RULESET_MISMATCH",
        "deckIds",
        string.Join(",", deckRulesets),
        "All decks in a match must use the same backend-registered ruleset."));
    }

    var catalogRulesets = catalogs.Values
      .Select(catalog => catalog.RulesetProfileId)
      .Distinct(StringComparer.Ordinal)
      .ToArray();
    if (catalogRulesets.Length > 1)
    {
      diagnostics.Add(new MatchStartDiagnostic(
        "MATCH_START_CATALOG_RULESET_MISMATCH",
        "deckIds.catalogSnapshotId",
        string.Join(",", catalogRulesets),
        "All catalogs in a match must use the same ruleset."));
    }

    var playerIds = decks
      .Select(deck => deck.PlayerId)
      .Order(StringComparer.Ordinal)
      .ToArray();
    if (!string.IsNullOrWhiteSpace(request.StartingPlayerId)
      && !playerIds.Contains(request.StartingPlayerId, StringComparer.Ordinal))
    {
      diagnostics.Add(new MatchStartDiagnostic(
        "MATCH_START_PLAYER_NOT_FOUND",
        "startingPlayerId",
        request.StartingPlayerId,
        $"Starting player '{request.StartingPlayerId}' is not part of this match."));
    }

    if (diagnostics.Count > 0)
    {
      return MatchStartResult.Rejected(diagnostics);
    }

    var players = decks
      .OrderBy(deck => deck.PlayerId, StringComparer.Ordinal)
      .ToDictionary(
        deck => deck.PlayerId,
        deck => CreatePlayer(deck, catalogs[deck.DeckId]),
        StringComparer.Ordinal);
    var activePlayerId = string.IsNullOrWhiteSpace(request.StartingPlayerId)
      ? playerIds[0]
      : request.StartingPlayerId;

    players = players.ToDictionary(
      pair => pair.Key,
      pair => PrepareOpeningPlayer(pair.Value, StringComparer.Ordinal.Equals(pair.Key, activePlayerId)),
      StringComparer.Ordinal);

    return MatchStartResult.Success(
      request.GameId,
      new InMemoryGameState(
        players: players,
        activePlayerId: activePlayerId,
        turnNumber: 1,
        phase: "TURN_START"));
  }

  private static PlayerState CreatePlayer(DeckDefinition deck, CatalogSnapshot catalog)
  {
    var cardVersions = catalog.Cards.ToDictionary(card => card.CardId, StringComparer.Ordinal);
    var cardInstances = new List<CardInstance>();

    foreach (var card in deck.Cards)
    {
      var version = cardVersions[card.CardId];
      for (var copyIndex = 1; copyIndex <= card.Count; copyIndex++)
      {
        cardInstances.Add(new CardInstance(
          $"{deck.PlayerId}:{card.CardId}:{copyIndex:000}",
          card.CardId,
          version.CardVersionId,
          deck.PlayerId,
          version.Definition.StaticData.CardType));
      }
    }

    var starterHero = StarterHeroProfiles.Find(deck.StarterHeroId)
      ?? throw new InvalidOperationException($"Validated starter hero '{deck.StarterHeroId}' is unavailable.");
    var heroPowerVersion = StarterHeroPowerCatalog.Create(starterHero, catalog);
    var heroPowerSource = new CardInstance(
      StarterHeroPowerCatalog.SourceCardInstanceId(deck.PlayerId, starterHero),
      heroPowerVersion.CardId,
      heroPowerVersion.CardVersionId,
      deck.PlayerId,
      CardType.HeroPowerCard);

    return PlayerState.Empty(deck.PlayerId) with
    {
      Hero = new EntityInstance(
        $"hero:{deck.PlayerId}",
        $"hero-card:{starterHero.HeroId}",
        deck.PlayerId,
        EntityType.Hero,
        Attack: 0,
        Health: starterHero.StartingHealth,
        Damage: 0),
      HeroPower = new HeroPowerState(
        heroPowerSource.CardInstanceId,
        starterHero.HeroPowerCost,
        "MAIN_PHASE"),
      Zones = new PlayerZones(deck: cardInstances, setAside: [heroPowerSource])
    };
  }

  private static PlayerState PrepareOpeningPlayer(PlayerState player, bool starts)
  {
    var openingHandSize = starts ? 3 : 4;
    var openingHand = player.Zones.Deck.Take(openingHandSize).ToArray();
    var remainingDeck = player.Zones.Deck.Skip(openingHand.Length).ToArray();
    return player with
    {
      Mana = starts ? 1 : 0,
      MaxMana = starts ? 1 : 0,
      Zones = new PlayerZones(
        deck: remainingDeck,
        hand: openingHand,
        graveyard: player.Zones.Graveyard,
        secretZone: player.Zones.SecretZone,
        setAside: player.Zones.SetAside,
        board: player.Zones.Board)
    };
  }

  private static void AddRequired(
    ICollection<MatchStartDiagnostic> diagnostics,
    string? value,
    string path,
    string code)
  {
    if (!string.IsNullOrWhiteSpace(value))
    {
      return;
    }

    diagnostics.Add(new MatchStartDiagnostic(
      code,
      path,
      string.Empty,
      $"Required field '{path}' is missing."));
  }
}
