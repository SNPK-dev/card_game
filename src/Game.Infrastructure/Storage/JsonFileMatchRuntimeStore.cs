using Game.Application.Matches;
using Game.Domain.Commands;
using Game.Domain.State;
using Game.Shared.Catalog;
using Game.Shared.Events;
using Game.Shared.Ruleset;

namespace Game.Infrastructure.Storage;

public sealed class JsonFileMatchRuntimeStore : IMatchRuntimeStore
{
  private readonly object syncRoot = new();
  private readonly string filePath;
  private readonly SortedDictionary<string, MatchRuntimeStorageRecord> matches = new(StringComparer.Ordinal);

  public JsonFileMatchRuntimeStore(string rootDirectory)
  {
    if (string.IsNullOrWhiteSpace(rootDirectory))
    {
      throw new ArgumentException("Storage root directory is required.", nameof(rootDirectory));
    }

    filePath = Path.Combine(rootDirectory, "match-runtime.json");
    foreach (var record in JsonFileStoreIO.Load<List<MatchRuntimeStorageRecord>>(filePath) ?? [])
    {
      matches[record.MatchId] = record;
    }
  }

  public bool TryAdd(MatchRuntimeRecord record)
  {
    ArgumentNullException.ThrowIfNull(record);
    lock (syncRoot)
    {
      if (matches.ContainsKey(record.MatchId))
      {
        return false;
      }

      matches[record.MatchId] = ToStorage(record);
      Save();
      return true;
    }
  }

  public MatchRuntimeRecord? Find(string matchId)
  {
    lock (syncRoot)
    {
      return matches.TryGetValue(matchId, out var record) ? FromStorage(record) : null;
    }
  }

  public void Update(MatchRuntimeRecord record)
  {
    ArgumentNullException.ThrowIfNull(record);
    lock (syncRoot)
    {
      matches[record.MatchId] = ToStorage(record);
      Save();
    }
  }

  private void Save() => JsonFileStoreIO.Save(filePath, matches.Values.ToArray());

  private static MatchRuntimeStorageRecord ToStorage(MatchRuntimeRecord record) =>
    new(
      record.MatchId,
      ToConcreteState(record.InitialState),
      ToConcreteState(record.State),
      record.PlayerSessionTokens,
      record.SpectatorSessionTokens,
      record.CardVersionsByVersionId,
      record.RulesetProfile,
      record.CommandLog,
      record.EventLog,
      record.NextEventSeq);

  private static MatchRuntimeRecord FromStorage(MatchRuntimeStorageRecord record) =>
    new(
      record.MatchId,
      record.InitialState,
      record.State,
      record.PlayerSessionTokens,
      record.SpectatorSessionTokens ?? [],
      record.CardVersionsByVersionId,
      record.RulesetProfile,
      record.CommandLog,
      record.EventLog,
      record.NextEventSeq);

  private static InMemoryGameState ToConcreteState(IGameState state) =>
    state as InMemoryGameState
    ?? new InMemoryGameState(
      state.Revision,
      state.Counters,
      state.Players,
      state.PresentationMetadata,
      state.ActivePlayerId,
      state.TurnNumber,
      state.Phase,
      state.TerminalState,
      state.PendingChoices);

  private sealed record MatchRuntimeStorageRecord(
    string MatchId,
    InMemoryGameState InitialState,
    InMemoryGameState State,
    IReadOnlyDictionary<string, string> PlayerSessionTokens,
    IReadOnlyList<string>? SpectatorSessionTokens,
    IReadOnlyDictionary<string, CardVersion> CardVersionsByVersionId,
    ResolvedRulesetProfile? RulesetProfile,
    IReadOnlyList<DeterministicCommandPayload> CommandLog,
    IReadOnlyList<CommittedEventRecord> EventLog,
    long NextEventSeq);
}
