namespace Game.Domain.State;

public sealed class InMemoryGameState : IGameState
{
  private readonly SortedDictionary<string, int> _counters;
  private readonly SortedDictionary<string, PlayerState> _players;
  private readonly SortedDictionary<string, PendingChoice> _pendingChoices;
  private readonly SortedDictionary<string, string> _presentationMetadata;

  public InMemoryGameState(
    int revision = 0,
    IReadOnlyDictionary<string, int>? counters = null,
    IReadOnlyDictionary<string, PlayerState>? players = null,
    IReadOnlyDictionary<string, string>? presentationMetadata = null,
    string? activePlayerId = null,
    int turnNumber = 0,
    string phase = "BOOTSTRAP",
    GameTerminalState? terminalState = null,
    IReadOnlyDictionary<string, PendingChoice>? pendingChoices = null)
  {
    Revision = revision;
    ActivePlayerId = activePlayerId;
    TurnNumber = turnNumber;
    Phase = string.IsNullOrWhiteSpace(phase) ? "BOOTSTRAP" : phase;
    TerminalState = terminalState ?? GameTerminalState.None;
    _counters = new SortedDictionary<string, int>(StringComparer.Ordinal);
    foreach (var pair in counters ?? new Dictionary<string, int>())
    {
      _counters[pair.Key] = pair.Value;
    }

    _players = new SortedDictionary<string, PlayerState>(StringComparer.Ordinal);
    foreach (var pair in players ?? new Dictionary<string, PlayerState>())
    {
      _players[pair.Key] = pair.Value;
    }

    _pendingChoices = new SortedDictionary<string, PendingChoice>(
      StringComparer.Ordinal);
    foreach (var pair in pendingChoices ?? new Dictionary<string, PendingChoice>())
    {
      _pendingChoices[pair.Key] = NormalizeChoice(pair.Value);
    }

    _presentationMetadata = new SortedDictionary<string, string>(
      StringComparer.Ordinal);
    foreach (var pair in presentationMetadata ?? new Dictionary<string, string>())
    {
      _presentationMetadata[pair.Key] = pair.Value;
    }
  }

  public int Revision { get; }

  public IReadOnlyDictionary<string, int> Counters => _counters;

  public IReadOnlyDictionary<string, PlayerState> Players => _players;

  public IReadOnlyDictionary<string, string> PresentationMetadata => _presentationMetadata;

  public IReadOnlyDictionary<string, PendingChoice> PendingChoices => _pendingChoices;

  public string? ActivePlayerId { get; }

  public int TurnNumber { get; }

  public string Phase { get; }

  public GameTerminalState TerminalState { get; }

  public GameStateSnapshot GetGameplaySnapshot() => new(Revision, _counters, _players, _pendingChoices, ActivePlayerId, TurnNumber, Phase, TerminalState);

  public InMemoryGameState WithCounter(string key, int value)
  {
    var next = new SortedDictionary<string, int>(_counters, StringComparer.Ordinal)
    {
      [key] = value
    };

    return new InMemoryGameState(Revision + 1, next, _players, _presentationMetadata, ActivePlayerId, TurnNumber, Phase, TerminalState, _pendingChoices);
  }

  public InMemoryGameState WithPlayer(PlayerState player)
  {
    ArgumentNullException.ThrowIfNull(player);

    var next = new SortedDictionary<string, PlayerState>(_players, StringComparer.Ordinal)
    {
      [player.PlayerId] = player
    };

    return new InMemoryGameState(Revision + 1, _counters, next, _presentationMetadata, ActivePlayerId, TurnNumber, Phase, TerminalState, _pendingChoices);
  }

  public InMemoryGameState WithCardMoved(
    string playerId,
    string cardInstanceId,
    Game.Shared.Primitives.SourceZone fromZone,
    Game.Shared.Primitives.SourceZone toZone)
  {
    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    return WithPlayer(player with
    {
      Zones = player.Zones.MoveCard(cardInstanceId, fromZone, toZone)
    });
  }

  public InMemoryGameState WithCardCreated(
    string playerId,
    CardInstance card,
    Game.Shared.Primitives.SourceZone zone)
  {
    ArgumentNullException.ThrowIfNull(card);
    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    if (_players.Values.Any(candidate => candidate.Zones.Deck
      .Concat(candidate.Zones.Hand)
      .Concat(candidate.Zones.Graveyard)
      .Concat(candidate.Zones.SecretZone)
      .Concat(candidate.Zones.SetAside)
      .Any(existing => StringComparer.Ordinal.Equals(existing.CardInstanceId, card.CardInstanceId))))
    {
      throw new InvalidOperationException($"Card instance '{card.CardInstanceId}' already exists.");
    }

    return WithPlayer(player with
    {
      Zones = player.Zones.AddCard(card, zone)
    });
  }

  public InMemoryGameState WithBoardEntitySummoned(string playerId, EntityInstance entity)
  {
    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    return WithPlayer(player with
    {
      Zones = player.Zones.AddBoardEntity(entity)
    });
  }

  public InMemoryGameState WithWeaponEquipped(string playerId, EntityInstance weapon)
  {
    ArgumentNullException.ThrowIfNull(weapon);
    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    if (player.Weapon is not null)
    {
      throw new InvalidOperationException($"Player '{playerId}' already has an equipped weapon.");
    }

    return WithPlayer(player with
    {
      Weapon = weapon
    });
  }

  public InMemoryGameState WithLocationPlayed(string playerId, EntityInstance location)
  {
    ArgumentNullException.ThrowIfNull(location);
    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    if (player.Location is not null)
    {
      throw new InvalidOperationException($"Player '{playerId}' already has a location.");
    }

    return WithPlayer(player with
    {
      Location = location
    });
  }

  public InMemoryGameState WithEquippedEntityDamageChanged(
    string playerId,
    string entityInstanceId,
    int damageDelta)
  {
    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    if (player.Weapon is not null
      && StringComparer.Ordinal.Equals(player.Weapon.EntityInstanceId, entityInstanceId))
    {
      return WithPlayer(player with
      {
        Weapon = player.Weapon with
        {
          Damage = Math.Max(0, player.Weapon.Damage + damageDelta)
        }
      });
    }

    if (player.Location is not null
      && StringComparer.Ordinal.Equals(player.Location.EntityInstanceId, entityInstanceId))
    {
      return WithPlayer(player with
      {
        Location = player.Location with
        {
          Damage = Math.Max(0, player.Location.Damage + damageDelta)
        }
      });
    }

    throw new InvalidOperationException(
      $"Equipped entity instance '{entityInstanceId}' does not exist for player '{playerId}'.");
  }

  public InMemoryGameState WithEquippedEntityDestroyed(
    string playerId,
    string entityInstanceId)
  {
    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    EntityInstance? entity;
    PlayerState nextPlayer;
    if (player.Weapon is not null
      && StringComparer.Ordinal.Equals(player.Weapon.EntityInstanceId, entityInstanceId))
    {
      entity = player.Weapon;
      nextPlayer = player with { Weapon = null };
    }
    else if (player.Location is not null
      && StringComparer.Ordinal.Equals(player.Location.EntityInstanceId, entityInstanceId))
    {
      entity = player.Location;
      nextPlayer = player with { Location = null };
    }
    else
    {
      throw new InvalidOperationException(
        $"Equipped entity instance '{entityInstanceId}' does not exist for player '{playerId}'.");
    }

    var zones = nextPlayer.Zones;
    if (nextPlayer.Zones.ContainsCardInZone(entity.SourceCardInstanceId, Game.Shared.Primitives.SourceZone.SetAside))
    {
      zones = zones.MoveCard(
        entity.SourceCardInstanceId,
        Game.Shared.Primitives.SourceZone.SetAside,
        Game.Shared.Primitives.SourceZone.Graveyard);
    }

    return WithPlayer(nextPlayer with
    {
      Zones = zones
    });
  }

  public InMemoryGameState WithHeroCardPlayed(
    string playerId,
    int armorGain,
    HeroPowerState? heroPower)
  {
    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    return WithPlayer(player with
    {
      Armor = player.Armor + armorGain,
      HeroPower = heroPower ?? player.HeroPower
    });
  }

  public InMemoryGameState WithHeroPowerReplaced(
    string playerId,
    HeroPowerState heroPower)
  {
    ArgumentNullException.ThrowIfNull(heroPower);
    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    return WithPlayer(player with
    {
      HeroPower = heroPower
    });
  }

  public InMemoryGameState WithEntityDamageChanged(
    string playerId,
    string entityInstanceId,
    int damageDelta)
  {
    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    var entity = player.Zones.Board.SingleOrDefault(candidate =>
      StringComparer.Ordinal.Equals(candidate.EntityInstanceId, entityInstanceId));
    if (entity is not null)
    {
      var nextDamage = Math.Max(0, entity.Damage + damageDelta);
      return WithPlayer(player with
      {
        Zones = player.Zones.ReplaceBoardEntity(entity with
        {
          Damage = nextDamage
        })
      });
    }

    if (player.Hero is not null
      && StringComparer.Ordinal.Equals(player.Hero.EntityInstanceId, entityInstanceId))
    {
      var nextDamage = Math.Max(0, player.Hero.Damage + damageDelta);
      return WithPlayer(player with
      {
        Hero = player.Hero with
        {
          Damage = nextDamage
        }
      });
    }

    throw new InvalidOperationException(
      $"Entity instance '{entityInstanceId}' does not exist for player '{playerId}'.");
  }

  public InMemoryGameState WithEntityAttackChanged(
    string playerId,
    string entityInstanceId,
    int attackDelta)
  {
    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    var entity = player.Zones.Board.SingleOrDefault(candidate =>
      StringComparer.Ordinal.Equals(candidate.EntityInstanceId, entityInstanceId));
    if (entity is null)
    {
      throw new InvalidOperationException(
        $"Entity instance '{entityInstanceId}' does not exist for player '{playerId}'.");
    }

    return WithPlayer(player with
    {
      Zones = player.Zones.ReplaceBoardEntity(entity with
      {
        Attack = Math.Max(0, entity.Attack + attackDelta)
      })
    });
  }

  public InMemoryGameState WithEntityHealthChanged(
    string playerId,
    string entityInstanceId,
    int healthDelta)
  {
    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    var entity = player.Zones.Board.SingleOrDefault(candidate =>
      StringComparer.Ordinal.Equals(candidate.EntityInstanceId, entityInstanceId));
    if (entity is null)
    {
      throw new InvalidOperationException(
        $"Entity instance '{entityInstanceId}' does not exist for player '{playerId}'.");
    }

    return WithPlayer(player with
    {
      Zones = player.Zones.ReplaceBoardEntity(entity with
      {
        Health = Math.Max(1, entity.Health + healthDelta)
      })
    });
  }

  public InMemoryGameState WithEntityKeywordAdded(
    string playerId,
    string entityInstanceId,
    string keyword)
  {
    if (string.IsNullOrWhiteSpace(keyword))
    {
      throw new InvalidOperationException("Keyword must be provided.");
    }

    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    var entity = player.Zones.Board.SingleOrDefault(candidate =>
      StringComparer.Ordinal.Equals(candidate.EntityInstanceId, entityInstanceId));
    if (entity is null)
    {
      throw new InvalidOperationException(
        $"Entity instance '{entityInstanceId}' does not exist for player '{playerId}'.");
    }

    if ((entity.Keywords ?? []).Contains(keyword, StringComparer.Ordinal))
    {
      return this;
    }

    return WithPlayer(player with
    {
      Zones = player.Zones.ReplaceBoardEntity(entity with
      {
        Keywords = (entity.Keywords ?? [])
          .Append(keyword)
          .Distinct(StringComparer.Ordinal)
          .Order(StringComparer.Ordinal)
          .ToArray()
      })
    });
  }

  public InMemoryGameState WithEntityKeywordRemoved(
    string playerId,
    string entityInstanceId,
    string keyword)
  {
    if (string.IsNullOrWhiteSpace(keyword))
    {
      throw new InvalidOperationException("Keyword must be provided.");
    }

    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    var entity = player.Zones.Board.SingleOrDefault(candidate =>
      StringComparer.Ordinal.Equals(candidate.EntityInstanceId, entityInstanceId));
    if (entity is null)
    {
      throw new InvalidOperationException(
        $"Entity instance '{entityInstanceId}' does not exist on board.");
    }

    if (!(entity.Keywords ?? []).Contains(keyword, StringComparer.Ordinal))
    {
      return this;
    }

    var nextKeywords = (entity.Keywords ?? [])
      .Where(existing => !StringComparer.Ordinal.Equals(existing, keyword))
      .Order(StringComparer.Ordinal)
      .ToArray();

    return WithPlayer(player with
    {
      Zones = player.Zones.ReplaceBoardEntity(entity with
      {
        Keywords = nextKeywords.Length == 0 ? null : nextKeywords
      })
    });
  }

  public InMemoryGameState WithBoardEntityDestroyed(
    string playerId,
    string entityInstanceId)
  {
    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    var entity = player.Zones.Board.SingleOrDefault(candidate =>
      StringComparer.Ordinal.Equals(candidate.EntityInstanceId, entityInstanceId));
    if (entity is null)
    {
      throw new InvalidOperationException(
        $"Entity instance '{entityInstanceId}' does not exist for player '{playerId}'.");
    }

    var zones = player.Zones.RemoveBoardEntity(entityInstanceId);
    if (player.Zones.ContainsCardInZone(entity.SourceCardInstanceId, Game.Shared.Primitives.SourceZone.SetAside))
    {
      zones = zones.MoveCard(
        entity.SourceCardInstanceId,
        Game.Shared.Primitives.SourceZone.SetAside,
        Game.Shared.Primitives.SourceZone.Graveyard);
    }

    return WithPlayer(player with
    {
      Zones = zones
    });
  }

  public InMemoryGameState WithBoardEntityReturnedToHand(
    string playerId,
    string entityInstanceId)
  {
    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    var entity = player.Zones.Board.SingleOrDefault(candidate =>
      StringComparer.Ordinal.Equals(candidate.EntityInstanceId, entityInstanceId));
    if (entity is null)
    {
      throw new InvalidOperationException(
        $"Entity instance '{entityInstanceId}' does not exist for player '{playerId}'.");
    }

    if (!player.Zones.ContainsCardInZone(entity.SourceCardInstanceId, Game.Shared.Primitives.SourceZone.SetAside))
    {
      throw new InvalidOperationException(
        $"Source card instance '{entity.SourceCardInstanceId}' for entity '{entityInstanceId}' is not set aside.");
    }

    var zones = player.Zones
      .RemoveBoardEntity(entityInstanceId)
      .MoveCard(
        entity.SourceCardInstanceId,
        Game.Shared.Primitives.SourceZone.SetAside,
        Game.Shared.Primitives.SourceZone.Hand);

    return WithPlayer(player with
    {
      Zones = zones
    });
  }

  public InMemoryGameState WithFatigueChanged(string playerId, int amount)
  {
    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    return WithPlayer(player with
    {
      FatigueCounter = Math.Max(0, player.FatigueCounter + amount)
    });
  }

  public InMemoryGameState WithModifierApplied(string playerId, PlayerModifier modifier)
  {
    ArgumentNullException.ThrowIfNull(modifier);
    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    if (player.Modifiers.Any(candidate =>
      StringComparer.Ordinal.Equals(candidate.ModifierId, modifier.ModifierId)))
    {
      throw new InvalidOperationException(
        $"Modifier '{modifier.ModifierId}' already exists for player '{playerId}'.");
    }

    return WithPlayer(player with
    {
      Modifiers = player.Modifiers
        .Append(modifier)
        .OrderByDescending(candidate => candidate.Priority)
        .ThenBy(candidate => candidate.ModifierId, StringComparer.Ordinal)
        .ToArray()
    });
  }

  public InMemoryGameState WithModifierRemoved(string playerId, string modifierId)
  {
    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    if (!player.Modifiers.Any(candidate =>
      StringComparer.Ordinal.Equals(candidate.ModifierId, modifierId)))
    {
      throw new InvalidOperationException(
        $"Modifier '{modifierId}' does not exist for player '{playerId}'.");
    }

    return WithPlayer(player with
    {
      Modifiers = player.Modifiers
        .Where(candidate => !StringComparer.Ordinal.Equals(candidate.ModifierId, modifierId))
        .ToArray()
    });
  }

  public InMemoryGameState WithQuestProgressChanged(
    string playerId,
    string questId,
    int progressDelta)
  {
    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    var quests = player.Quests ?? [];
    var quest = quests.SingleOrDefault(candidate =>
      StringComparer.Ordinal.Equals(candidate.QuestId, questId));
    if (quest is null)
    {
      throw new InvalidOperationException(
        $"Quest '{questId}' does not exist for player '{playerId}'.");
    }

    if (quest.IsCompleted)
    {
      throw new InvalidOperationException(
        $"Quest '{questId}' for player '{playerId}' is already completed.");
    }

    var nextProgress = Math.Min(quest.RequiredProgress, quest.Progress + progressDelta);
    var nextQuest = quest with
    {
      Progress = nextProgress,
      IsCompleted = nextProgress >= quest.RequiredProgress
    };

    return WithPlayer(player with
    {
      Quests = quests
        .Select(candidate => StringComparer.Ordinal.Equals(candidate.QuestId, questId)
          ? nextQuest
          : candidate)
        .OrderBy(candidate => candidate.QuestId, StringComparer.Ordinal)
        .ToArray()
    });
  }

  public InMemoryGameState WithSecretSet(string playerId, PlayerSecret secret)
  {
    ArgumentNullException.ThrowIfNull(secret);
    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    var secrets = player.Secrets ?? [];
    if (secrets.Any(candidate => StringComparer.Ordinal.Equals(candidate.SecretId, secret.SecretId)))
    {
      throw new InvalidOperationException($"Secret '{secret.SecretId}' already exists for player '{playerId}'.");
    }

    return WithPlayer(player with
    {
      Secrets = secrets
        .Append(secret)
        .OrderBy(candidate => candidate.SecretId, StringComparer.Ordinal)
        .ToArray()
    });
  }

  public InMemoryGameState WithSecretRevealed(string playerId, string secretId)
  {
    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    var secrets = player.Secrets ?? [];
    var secret = secrets.SingleOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.SecretId, secretId));
    if (secret is null)
    {
      throw new InvalidOperationException($"Secret '{secretId}' does not exist for player '{playerId}'.");
    }

    return WithPlayer(player with
    {
      Secrets = secrets
        .Where(candidate => !StringComparer.Ordinal.Equals(candidate.SecretId, secretId))
        .Append(secret with { IsRevealed = true })
        .OrderBy(candidate => candidate.SecretId, StringComparer.Ordinal)
        .ToArray()
    });
  }

  public InMemoryGameState WithAurasRefreshed(string playerId, IReadOnlyList<PlayerAura> auras)
  {
    if (!_players.TryGetValue(playerId, out var player))
    {
      throw new InvalidOperationException($"Player '{playerId}' does not exist.");
    }

    return WithPlayer(player with
    {
      Auras = (auras ?? [])
        .OrderByDescending(candidate => candidate.Priority)
        .ThenBy(candidate => candidate.AuraId, StringComparer.Ordinal)
        .ToArray()
    });
  }

  public InMemoryGameState WithPresentationMetadata(string key, string value)
  {
    var next = new SortedDictionary<string, string>(_presentationMetadata, StringComparer.Ordinal)
    {
      [key] = value
    };

    return new InMemoryGameState(Revision, _counters, _players, next, ActivePlayerId, TurnNumber, Phase, TerminalState, _pendingChoices);
  }

  public InMemoryGameState WithTerminalState(GameTerminalState terminalState)
  {
    ArgumentNullException.ThrowIfNull(terminalState);

    return new InMemoryGameState(
      Revision + 1,
      _counters,
      _players,
      _presentationMetadata,
      ActivePlayerId,
      TurnNumber,
      Phase,
      terminalState,
      _pendingChoices);
  }

  public InMemoryGameState WithChoiceCreated(PendingChoice choice)
  {
    ArgumentNullException.ThrowIfNull(choice);
    if (!_players.ContainsKey(choice.PlayerId))
    {
      throw new InvalidOperationException($"Player '{choice.PlayerId}' does not exist.");
    }

    if (_pendingChoices.ContainsKey(choice.ChoiceId))
    {
      throw new InvalidOperationException($"Choice '{choice.ChoiceId}' already exists.");
    }

    var next = new SortedDictionary<string, PendingChoice>(_pendingChoices, StringComparer.Ordinal)
    {
      [choice.ChoiceId] = NormalizeChoice(choice)
    };

    return new InMemoryGameState(
      Revision + 1,
      _counters,
      _players,
      _presentationMetadata,
      ActivePlayerId,
      TurnNumber,
      Phase,
      TerminalState,
      next);
  }

  public InMemoryGameState WithChoiceResolved(
    string choiceId,
    string selectedOptionId)
  {
    if (!_pendingChoices.TryGetValue(choiceId, out var choice))
    {
      throw new InvalidOperationException($"Choice '{choiceId}' does not exist.");
    }

    if (!choice.Options.Any(option => StringComparer.Ordinal.Equals(option.OptionId, selectedOptionId)))
    {
      throw new InvalidOperationException(
        $"Choice '{choiceId}' does not contain option '{selectedOptionId}'.");
    }

    var next = new SortedDictionary<string, PendingChoice>(_pendingChoices, StringComparer.Ordinal);
    next.Remove(choiceId);

    return new InMemoryGameState(
      Revision + 1,
      _counters,
      _players,
      _presentationMetadata,
      ActivePlayerId,
      TurnNumber,
      Phase,
      TerminalState,
      next);
  }

  public InMemoryGameState WithTurnAdvanced()
  {
    if (string.IsNullOrWhiteSpace(ActivePlayerId))
    {
      throw new InvalidOperationException("Cannot advance turn without an active player.");
    }

    if (_players.Count == 0)
    {
      throw new InvalidOperationException("Cannot advance turn without players.");
    }

    var orderedPlayerIds = _players.Keys.Order(StringComparer.Ordinal).ToArray();
    var currentIndex = Array.BinarySearch(orderedPlayerIds, ActivePlayerId, StringComparer.Ordinal);
    if (currentIndex < 0)
    {
      throw new InvalidOperationException($"Active player '{ActivePlayerId}' does not exist.");
    }

    string? consumedExtraTurnKey = null;
    string nextPlayerId;
    int nextTurnNumber;
    if (ExtraTurnSchedule.TryGetNext(_counters, out var extraTurnPlayerId, out var extraTurnQueueKey))
    {
      if (!_players.ContainsKey(extraTurnPlayerId))
      {
        throw new InvalidOperationException($"Extra-turn player '{extraTurnPlayerId}' does not exist.");
      }

      consumedExtraTurnKey = extraTurnQueueKey;
      nextPlayerId = extraTurnPlayerId;
      var nextPlayerIndex = Array.BinarySearch(orderedPlayerIds, nextPlayerId, StringComparer.Ordinal);
      nextTurnNumber = nextPlayerIndex <= currentIndex ? TurnNumber + 1 : TurnNumber;
    }
    else
    {
      var nextIndex = (currentIndex + 1) % orderedPlayerIds.Length;
      nextTurnNumber = nextIndex == 0 ? TurnNumber + 1 : TurnNumber;
      nextPlayerId = orderedPlayerIds[nextIndex];
    }

    var players = new SortedDictionary<string, PlayerState>(_players, StringComparer.Ordinal);
    var nextPlayer = players[nextPlayerId];
    var nextMaxMana = Math.Min(10, nextPlayer.MaxMana + 1);
    players[nextPlayerId] = nextPlayer with
    {
      MaxMana = nextMaxMana,
      Mana = nextMaxMana
    };
    var counters = new SortedDictionary<string, int>(_counters, StringComparer.Ordinal);
    if (consumedExtraTurnKey is not null)
    {
      counters[consumedExtraTurnKey] = 0;
    }

    return new InMemoryGameState(
      Revision + 1,
      counters,
      players,
      _presentationMetadata,
      nextPlayerId,
      nextTurnNumber,
      "TURN_START",
      TerminalState,
      _pendingChoices);
  }

  private static PendingChoice NormalizeChoice(PendingChoice choice)
  {
    return choice with
    {
      Options = choice.Options
        .OrderBy(option => option.OptionId, StringComparer.Ordinal)
        .ToArray()
    };
  }
}
