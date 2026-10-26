using Game.Application.Legality;
using Game.Application.Replay;
using Game.Domain.Commands;
using Game.Domain.State;
using Game.Shared.Events;

namespace Game.Application.Matches;

public sealed record MatchRuntimeDiagnostic(
  string Code,
  string Path,
  string Identifier,
  string Message);

public sealed record ExternalMatchStartRequest(
  string MatchId,
  string GameId,
  IReadOnlyList<string> DeckIds,
  IReadOnlyDictionary<string, string> PlayerSessionTokens,
  string? StartingPlayerId = null,
  IReadOnlyList<string>? SpectatorSessionTokens = null);

public sealed record ExternalMatchStartResult(
  bool Succeeded,
  string? MatchId,
  MatchStateView? State,
  IReadOnlyList<MatchRuntimeDiagnostic> Diagnostics)
{
  public static ExternalMatchStartResult Success(string matchId, MatchStateView state) =>
    new(true, matchId, state, []);

  public static ExternalMatchStartResult Rejected(IReadOnlyList<MatchRuntimeDiagnostic> diagnostics) =>
    new(false, null, null, diagnostics);
}

public sealed record MatchStateView(
  string MatchId,
  string ViewKind,
  string? ViewerPlayerId,
  int Revision,
  string StateHash,
  string? ActivePlayerId,
  int TurnNumber,
  string Phase,
  bool IsTerminal,
  string TerminalReason,
  IReadOnlyDictionary<string, PublicPlayerStateView> Players,
  IReadOnlyDictionary<string, PublicChoiceView> PendingChoices,
  long NextEventSeq,
  int CommandCount);

public sealed record PublicPlayerStateView(
  string PlayerId,
  string Relationship,
  PublicPlayerZonesView Zones,
  PublicEntityView? Hero,
  int Mana,
  int MaxMana,
  int Armor,
  int FatigueCounter,
  IReadOnlyList<PublicPlayerModifierView> Modifiers,
  PublicEntityView? Weapon = null,
  PublicEntityView? Location = null,
  PublicHeroPowerView? HeroPower = null,
  IReadOnlyList<PublicQuestView>? Quests = null,
  IReadOnlyList<PublicSecretView>? Secrets = null,
  IReadOnlyList<PublicAuraView>? Auras = null);

public sealed record PublicPlayerZonesView(
  PublicZoneView Deck,
  PublicZoneView Hand,
  PublicZoneView Graveyard,
  PublicZoneView SecretZone,
  PublicZoneView SetAside,
  IReadOnlyList<PublicEntityView> Board);

public sealed record PublicZoneView(
  string Zone,
  int Count,
  IReadOnlyList<PublicCardInstanceView> Cards);

public sealed record PublicCardInstanceView(
  string? CardInstanceId,
  string? CardId,
  string? CardVersion,
  string OwnerPlayerId,
  string CardType,
  string Visibility,
  int? BaseManaCost = null,
  int? EffectiveManaCost = null);

public sealed record PublicEntityView(
  string EntityInstanceId,
  string SourceCardInstanceId,
  string ControllerPlayerId,
  string EntityType,
  int Attack,
  int Health,
  int Damage,
  IReadOnlyList<string>? Keywords = null);

public sealed record PublicHeroPowerView(
  string SourceCardInstanceId,
  int Cost,
  string ActivationTiming);

public sealed record PublicPlayerModifierView(
  string ModifierId,
  string ModifierType,
  int Amount,
  string Duration,
  int Priority,
  string SourceRef);

public sealed record PublicQuestView(
  string QuestId,
  string SourceCardInstanceId,
  string ControllerPlayerId,
  string ProgressEventType,
  int Progress,
  int RequiredProgress,
  bool IsCompleted,
  string? RewardEffectId = null,
  string? SourceCardVersion = null);

public sealed record PublicSecretView(
  string SecretId,
  string ControllerPlayerId,
  bool IsRevealed,
  string Visibility,
  string? SourceCardInstanceId = null,
  string? SourceCardVersion = null,
  string? RevealEventType = null,
  string? EffectId = null);

public sealed record PublicAuraView(
  string AuraId,
  string SourceEntityInstanceId,
  string SourceCardInstanceId,
  string SourceCardVersion,
  string ControllerPlayerId,
  string EffectId,
  int Priority);

public sealed record PublicChoiceView(
  string ChoiceId,
  string PlayerId,
  string ChoiceType,
  string VisibilityPolicy,
  string ResultZone,
  string SourceRef,
  IReadOnlyList<PublicChoiceOptionView> Options);

public sealed record PublicChoiceOptionView(
  string OptionId,
  string ItemKind,
  string? ItemId,
  string Visibility);

public sealed record MatchCommandDto(
  string CommandType,
  string PlayerId,
  IReadOnlyDictionary<string, int>? Payload = null,
  IReadOnlyDictionary<string, string>? Refs = null);

public sealed record ExternalCommandSubmissionRequest(
  string SessionToken,
  MatchCommandDto Command,
  string? CommandId = null,
  string? ExpectedPreStateHash = null,
  string? CorrelationId = null);

public sealed record ExternalCommandSubmissionResult(
  bool Succeeded,
  MatchStateView? State,
  IReadOnlyList<PublicMatchEventRecord> Events,
  IReadOnlyList<MatchRuntimeDiagnostic> Diagnostics)
{
  public static ExternalCommandSubmissionResult Success(MatchStateView state, IReadOnlyList<PublicMatchEventRecord> events) =>
    new(true, state, events, []);

  public static ExternalCommandSubmissionResult Rejected(IReadOnlyList<MatchRuntimeDiagnostic> diagnostics) =>
    new(false, null, [], diagnostics);
}

public sealed record ExternalLegalActionsResult(
  bool Succeeded,
  IReadOnlyList<MatchCommandDto> LegalActions,
  IReadOnlyList<LegalActionDiagnostic> LegalDiagnostics,
  IReadOnlyList<MatchRuntimeDiagnostic> Diagnostics)
{
  public static ExternalLegalActionsResult Success(
    IReadOnlyList<MatchCommandDto> legalActions,
    IReadOnlyList<LegalActionDiagnostic> legalDiagnostics) =>
    new(true, legalActions, legalDiagnostics, []);

  public static ExternalLegalActionsResult Rejected(IReadOnlyList<MatchRuntimeDiagnostic> diagnostics) =>
    new(false, [], [], diagnostics);
}

public sealed record MatchEventsPollResult(
  bool Succeeded,
  IReadOnlyList<PublicMatchEventRecord> Events,
  long NextEventSeq,
  IReadOnlyList<MatchRuntimeDiagnostic> Diagnostics)
{
  public static MatchEventsPollResult Success(IReadOnlyList<PublicMatchEventRecord> events, long nextEventSeq) =>
    new(true, events, nextEventSeq, []);

  public static MatchEventsPollResult Rejected(IReadOnlyList<MatchRuntimeDiagnostic> diagnostics) =>
    new(false, [], 0, diagnostics);
}

public sealed record MatchReconnectResult(
  bool Succeeded,
  MatchStateView? State,
  IReadOnlyList<PublicMatchEventRecord> Events,
  long NextEventSeq,
  IReadOnlyList<MatchRuntimeDiagnostic> Diagnostics)
{
  public static MatchReconnectResult Success(
    MatchStateView state,
    IReadOnlyList<PublicMatchEventRecord> events,
    long nextEventSeq) =>
    new(true, state, events, nextEventSeq, []);

  public static MatchReconnectResult Rejected(IReadOnlyList<MatchRuntimeDiagnostic> diagnostics) =>
    new(false, null, [], 0, diagnostics);
}

public sealed record PublicMatchEventRecord(
  long EventSeq,
  long? ParentEventSeq,
  long? CommandSeq,
  string EventType,
  string Category,
  string Phase,
  IReadOnlyList<PublicEventRef> SourceRefs,
  IReadOnlyList<PublicEventRef> TargetRefs,
  IReadOnlyList<PublicEventPayloadEntry> Payload,
  string? MutationBatchRef,
  IReadOnlyList<string> MutationRefs,
  IReadOnlyList<string> RngRollRefs,
  string PreStateHash,
  string PostStateHash,
  string Visibility);

public sealed record PublicEventRef(
  string RefType,
  string RefId,
  string? Zone,
  string? ControllerPlayerId);

public sealed record PublicEventPayloadEntry(
  string Key,
  PublicEventPayloadValue Value);

public sealed record PublicEventPayloadValue(
  string ValueKind,
  string? StringValue,
  int? IntValue,
  bool? BoolValue,
  IReadOnlyList<string> StringValues);

public sealed record MatchReplayVerificationView(
  bool Succeeded,
  ReplayVerificationResult Replay,
  IReadOnlyList<MatchRuntimeDiagnostic> Diagnostics)
{
  public static MatchReplayVerificationView Success(ReplayVerificationResult replay) =>
    new(true, replay, []);

  public static MatchReplayVerificationView Rejected(IReadOnlyList<MatchRuntimeDiagnostic> diagnostics) =>
    new(false, ReplayVerificationResult.Rejected(null, 0, []), diagnostics);
}

public interface IMatchRuntimeStore
{
  bool TryAdd(MatchRuntimeRecord record);

  MatchRuntimeRecord? Find(string matchId);

  void Update(MatchRuntimeRecord record);
}

public sealed record MatchRuntimeRecord(
  string MatchId,
  IGameState InitialState,
  IGameState State,
  IReadOnlyDictionary<string, string> PlayerSessionTokens,
  IReadOnlyList<string> SpectatorSessionTokens,
  IReadOnlyDictionary<string, Game.Shared.Catalog.CardVersion> CardVersionsByVersionId,
  Game.Shared.Ruleset.ResolvedRulesetProfile? RulesetProfile,
  IReadOnlyList<DeterministicCommandPayload> CommandLog,
  IReadOnlyList<CommittedEventRecord> EventLog,
  long NextEventSeq);
