using Game.Shared.Primitives;

namespace Game.Domain.State;

public sealed record PlayerZones
{
  public static PlayerZones Empty { get; } = new();

  public PlayerZones(
    IReadOnlyList<CardInstance>? deck = null,
    IReadOnlyList<CardInstance>? hand = null,
    IReadOnlyList<CardInstance>? graveyard = null,
    IReadOnlyList<CardInstance>? secretZone = null,
    IReadOnlyList<CardInstance>? setAside = null,
    IReadOnlyList<EntityInstance>? board = null)
  {
    Deck = Copy(deck);
    Hand = Copy(hand);
    Graveyard = Copy(graveyard);
    SecretZone = Copy(secretZone);
    SetAside = Copy(setAside);
    Board = Copy(board);
    EnsureUniqueCardMembership();
    EnsureUniqueEntityMembership();
  }

  public IReadOnlyList<CardInstance> Deck { get; }

  public IReadOnlyList<CardInstance> Hand { get; }

  public IReadOnlyList<CardInstance> Graveyard { get; }

  public IReadOnlyList<CardInstance> SecretZone { get; }

  public IReadOnlyList<CardInstance> SetAside { get; }

  public IReadOnlyList<EntityInstance> Board { get; }

  public bool ContainsCardInZone(string cardInstanceId, SourceZone zone)
  {
    return CardZone(zone).Any(card => StringComparer.Ordinal.Equals(card.CardInstanceId, cardInstanceId));
  }

  public bool ContainsEntityOnBoard(string entityInstanceId)
  {
    return Board.Any(entity =>
      StringComparer.Ordinal.Equals(entity.EntityInstanceId, entityInstanceId));
  }

  public PlayerZones MoveCard(string cardInstanceId, SourceZone fromZone, SourceZone toZone)
  {
    var source = CardZone(fromZone);
    var card = source.SingleOrDefault(card =>
      StringComparer.Ordinal.Equals(card.CardInstanceId, cardInstanceId));

    if (card is null)
    {
      throw new InvalidOperationException(
        $"Card instance '{cardInstanceId}' does not exist in zone '{fromZone}'.");
    }

    var deck = Deck.ToList();
    var hand = Hand.ToList();
    var graveyard = Graveyard.ToList();
    var secretZone = SecretZone.ToList();
    var setAside = SetAside.ToList();

    RemoveCard(deck, cardInstanceId);
    RemoveCard(hand, cardInstanceId);
    RemoveCard(graveyard, cardInstanceId);
    RemoveCard(secretZone, cardInstanceId);
    RemoveCard(setAside, cardInstanceId);
    Destination(toZone, deck, hand, graveyard, secretZone, setAside).Add(card);

    return new PlayerZones(deck, hand, graveyard, secretZone, setAside, Board);
  }

  public PlayerZones AddCard(CardInstance card, SourceZone zone)
  {
    ArgumentNullException.ThrowIfNull(card);
    if (Deck
      .Concat(Hand)
      .Concat(Graveyard)
      .Concat(SetAside)
      .Any(existing => StringComparer.Ordinal.Equals(existing.CardInstanceId, card.CardInstanceId)))
    {
      throw new InvalidOperationException(
        $"Card instance '{card.CardInstanceId}' already exists in a card zone.");
    }

    var deck = Deck.ToList();
    var hand = Hand.ToList();
    var graveyard = Graveyard.ToList();
    var secretZone = SecretZone.ToList();
    var setAside = SetAside.ToList();
    Destination(zone, deck, hand, graveyard, secretZone, setAside).Add(card);

    return new PlayerZones(deck, hand, graveyard, secretZone, setAside, Board);
  }

  public PlayerZones AddBoardEntity(EntityInstance entity)
  {
    ArgumentNullException.ThrowIfNull(entity);

    if (ContainsEntityOnBoard(entity.EntityInstanceId))
    {
      throw new InvalidOperationException(
        $"Entity instance '{entity.EntityInstanceId}' already exists on board.");
    }

    return new PlayerZones(Deck, Hand, Graveyard, SecretZone, SetAside, Board.Concat([entity]).ToArray());
  }

  public PlayerZones ReplaceBoardEntity(EntityInstance entity)
  {
    ArgumentNullException.ThrowIfNull(entity);

    var board = Board.ToList();
    var index = board.FindIndex(candidate =>
      StringComparer.Ordinal.Equals(candidate.EntityInstanceId, entity.EntityInstanceId));
    if (index < 0)
    {
      throw new InvalidOperationException(
        $"Entity instance '{entity.EntityInstanceId}' does not exist on board.");
    }

    board[index] = entity;
    return new PlayerZones(Deck, Hand, Graveyard, SecretZone, SetAside, board);
  }

  public PlayerZones RemoveBoardEntity(string entityInstanceId)
  {
    var board = Board.ToList();
    var removed = board.RemoveAll(entity =>
      StringComparer.Ordinal.Equals(entity.EntityInstanceId, entityInstanceId));
    if (removed == 0)
    {
      throw new InvalidOperationException(
        $"Entity instance '{entityInstanceId}' does not exist on board.");
    }

    return new PlayerZones(Deck, Hand, Graveyard, SecretZone, SetAside, board);
  }

  private IReadOnlyList<CardInstance> CardZone(SourceZone zone)
  {
    return zone switch
    {
      SourceZone.Deck => Deck,
      SourceZone.Hand => Hand,
      SourceZone.Graveyard => Graveyard,
      SourceZone.SecretZone => SecretZone,
      SourceZone.SetAside => SetAside,
      _ => throw new InvalidOperationException($"Source zone '{zone}' is not a card instance zone.")
    };
  }

  private static List<CardInstance> Destination(
    SourceZone zone,
    List<CardInstance> deck,
    List<CardInstance> hand,
    List<CardInstance> graveyard,
    List<CardInstance> secretZone,
    List<CardInstance> setAside)
  {
    return zone switch
    {
      SourceZone.Deck => deck,
      SourceZone.Hand => hand,
      SourceZone.Graveyard => graveyard,
      SourceZone.SecretZone => secretZone,
      SourceZone.SetAside => setAside,
      _ => throw new InvalidOperationException($"Source zone '{zone}' is not a card instance zone.")
    };
  }

  private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T>? values)
  {
    return values?.ToArray() ?? [];
  }

  private static void RemoveCard(List<CardInstance> zone, string cardInstanceId)
  {
    zone.RemoveAll(card => StringComparer.Ordinal.Equals(card.CardInstanceId, cardInstanceId));
  }

  private void EnsureUniqueCardMembership()
  {
    var duplicate = Deck
      .Concat(Hand)
      .Concat(Graveyard)
      .Concat(SecretZone)
      .Concat(SetAside)
      .GroupBy(card => card.CardInstanceId, StringComparer.Ordinal)
      .FirstOrDefault(group => group.Count() > 1);

    if (duplicate is not null)
    {
      throw new InvalidOperationException(
        $"Card instance '{duplicate.Key}' appears in more than one card zone.");
    }
  }

  private void EnsureUniqueEntityMembership()
  {
    var duplicate = Board
      .GroupBy(entity => entity.EntityInstanceId, StringComparer.Ordinal)
      .FirstOrDefault(group => group.Count() > 1);

    if (duplicate is not null)
    {
      throw new InvalidOperationException(
        $"Entity instance '{duplicate.Key}' appears in more than one entity zone.");
    }
  }
}
