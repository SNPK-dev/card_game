using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game.UnityClient.Contracts;

public sealed class HealthResponse
{
  public string Status { get; set; } = string.Empty;
}

public sealed class UnityError
{
  public string Code { get; set; } = string.Empty;
  public string Message { get; set; } = string.Empty;
  public bool Retryable { get; set; }
  public string SuggestedUnityBehavior { get; set; } = string.Empty;
  public List<Diagnostic> Diagnostics { get; set; } = new();
}

public sealed class Diagnostic
{
  public string Code { get; set; } = string.Empty;
  public string Path { get; set; } = string.Empty;
  public string Identifier { get; set; } = string.Empty;
  public string Message { get; set; } = string.Empty;
}

public sealed class StorageResult
{
  public bool Succeeded { get; set; }
  public string? Diagnostic { get; set; }
}

public sealed class EngineCapabilityRegistry
{
  public string EngineVersion { get; set; } = string.Empty;
  public string ApiContractVersion { get; set; } = string.Empty;
  public string StateSchemaVersion { get; set; } = string.Empty;
  public string EventSchemaVersion { get; set; } = string.Empty;
  public string AssetManifestSchemaVersion { get; set; } = string.Empty;
  public string PlaybackSchemaVersion { get; set; } = string.Empty;
  public string MinimumUnityClientContractVersion { get; set; } = string.Empty;
  public List<string> SupportedCommandTypes { get; set; } = new();
  public List<string> SupportedEventTypes { get; set; } = new();
  public List<string> SupportedAssetManifestVersions { get; set; } = new();
  public string DefaultProductionRulesetId { get; set; } = string.Empty;
  public string DefaultProductionRulesetVersion { get; set; } = string.Empty;
  public bool ProductionDeckRulesAvailable { get; set; }
  public List<string> FeatureFlags { get; set; } = new();
}

public sealed class RulesetDefinition
{
  public string RulesetId { get; set; } = string.Empty;
  public string RulesetVersion { get; set; } = string.Empty;
  public string DisplayName { get; set; } = string.Empty;
  public string Environment { get; set; } = string.Empty;
  public bool ProductionScale { get; set; }
  public DeckRules DeckRules { get; set; } = new();
  public CardPoolPolicy CardPoolPolicy { get; set; } = new();
  public List<string>? CompatibleCatalogIds { get; set; }
  public List<string>? CompatibleCatalogVersions { get; set; }
}

public sealed class DeckRules
{
  public int MinimumDeckSize { get; set; }
  public int MaximumDeckSize { get; set; }
  public int DefaultDeckSize { get; set; }
  public int MaximumCopiesPerCard { get; set; }
}

public sealed class CardPoolPolicy
{
  public bool CatalogBound { get; set; }
  public bool RequirePublishedCardVersion { get; set; }
  public bool AllowUnknownCards { get; set; }
}

public sealed class CreateApiSessionRequest
{
  public string PlayerId { get; set; } = string.Empty;
  public string? SessionToken { get; set; }
  public string? Ttl { get; set; }
}

public sealed class CreateApiSessionResult
{
  public bool Succeeded { get; set; }
  public ApiSession? Session { get; set; }
  public List<Diagnostic> Diagnostics { get; set; } = new();
}

public sealed class ApiSession
{
  public string SessionToken { get; set; } = string.Empty;
  public string PlayerId { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class ExternalMatchStartRequest
{
  public string MatchId { get; set; } = string.Empty;
  public string GameId { get; set; } = string.Empty;
  public List<string> DeckIds { get; set; } = new();
  public Dictionary<string, string> PlayerSessionTokens { get; set; } = new();
  public string? StartingPlayerId { get; set; }
  public List<string>? SpectatorSessionTokens { get; set; }
}

public sealed class ExternalMatchStartResult
{
  public bool Succeeded { get; set; }
  public string? MatchId { get; set; }
  public MatchStateView? State { get; set; }
  public List<Diagnostic> Diagnostics { get; set; } = new();
}

public sealed class MatchStateView
{
  public string MatchId { get; set; } = string.Empty;
  public string ViewKind { get; set; } = string.Empty;
  public string? ViewerPlayerId { get; set; }
  public int Revision { get; set; }
  public string StateHash { get; set; } = string.Empty;
  public string? ActivePlayerId { get; set; }
  public int TurnNumber { get; set; }
  public string Phase { get; set; } = string.Empty;
  public bool IsTerminal { get; set; }
  public string TerminalReason { get; set; } = string.Empty;
  public Dictionary<string, PublicPlayerStateView> Players { get; set; } = new();
  public Dictionary<string, PublicChoiceView> PendingChoices { get; set; } = new();
  public long NextEventSeq { get; set; }
  public int CommandCount { get; set; }
}

public sealed class PublicPlayerStateView
{
  public string PlayerId { get; set; } = string.Empty;
  public string Relationship { get; set; } = string.Empty;
  public PublicPlayerZonesView Zones { get; set; } = new();
  public PublicEntityView? Hero { get; set; }
  public int Mana { get; set; }
  public int MaxMana { get; set; }
  public int Armor { get; set; }
  public int FatigueCounter { get; set; }
  public List<PublicPlayerModifierView> Modifiers { get; set; } = new();
  public PublicEntityView? Weapon { get; set; }
  public PublicEntityView? Location { get; set; }
  public PublicHeroPowerView? HeroPower { get; set; }
  public List<PublicQuestView>? Quests { get; set; }
  public List<PublicSecretView>? Secrets { get; set; }
  public List<PublicAuraView>? Auras { get; set; }
}

public sealed class PublicPlayerZonesView
{
  public PublicZoneView Deck { get; set; } = new();
  public PublicZoneView Hand { get; set; } = new();
  public PublicZoneView Graveyard { get; set; } = new();
  public PublicZoneView SecretZone { get; set; } = new();
  public PublicZoneView SetAside { get; set; } = new();
  public List<PublicEntityView> Board { get; set; } = new();
}

public sealed class PublicZoneView
{
  public string Zone { get; set; } = string.Empty;
  public int Count { get; set; }
  public List<PublicCardInstanceView> Cards { get; set; } = new();
}

public sealed class PublicCardInstanceView
{
  public string? CardInstanceId { get; set; }
  public string? CardId { get; set; }
  public string? CardVersion { get; set; }
  public string OwnerPlayerId { get; set; } = string.Empty;
  public string CardType { get; set; } = string.Empty;
  public string Visibility { get; set; } = string.Empty;
  public int? BaseManaCost { get; set; }
  public int? EffectiveManaCost { get; set; }
}

public sealed class PublicEntityView
{
  public string EntityInstanceId { get; set; } = string.Empty;
  public string SourceCardInstanceId { get; set; } = string.Empty;
  public string ControllerPlayerId { get; set; } = string.Empty;
  public string EntityType { get; set; } = string.Empty;
  public int Attack { get; set; }
  public int Health { get; set; }
  public int Damage { get; set; }
  public List<string>? Keywords { get; set; }
}

public sealed class PublicHeroPowerView
{
  public string SourceCardInstanceId { get; set; } = string.Empty;
  public int Cost { get; set; }
  public string ActivationTiming { get; set; } = string.Empty;
}

public sealed class PublicPlayerModifierView
{
  public string ModifierId { get; set; } = string.Empty;
  public string ModifierType { get; set; } = string.Empty;
  public int Amount { get; set; }
  public string Duration { get; set; } = string.Empty;
  public int Priority { get; set; }
  public string SourceRef { get; set; } = string.Empty;
}

public sealed class PublicQuestView
{
  public string QuestId { get; set; } = string.Empty;
  public string SourceCardInstanceId { get; set; } = string.Empty;
  public string ControllerPlayerId { get; set; } = string.Empty;
  public string ProgressEventType { get; set; } = string.Empty;
  public int Progress { get; set; }
  public int RequiredProgress { get; set; }
  public bool IsCompleted { get; set; }
  public string? RewardEffectId { get; set; }
  public string? SourceCardVersion { get; set; }
}

public sealed class PublicSecretView
{
  public string SecretId { get; set; } = string.Empty;
  public string ControllerPlayerId { get; set; } = string.Empty;
  public bool IsRevealed { get; set; }
  public string Visibility { get; set; } = string.Empty;
  public string? SourceCardInstanceId { get; set; }
  public string? SourceCardVersion { get; set; }
  public string? RevealEventType { get; set; }
  public string? EffectId { get; set; }
}

public sealed class PublicAuraView
{
  public string AuraId { get; set; } = string.Empty;
  public string SourceEntityInstanceId { get; set; } = string.Empty;
  public string SourceCardInstanceId { get; set; } = string.Empty;
  public string SourceCardVersion { get; set; } = string.Empty;
  public string ControllerPlayerId { get; set; } = string.Empty;
  public string EffectId { get; set; } = string.Empty;
  public int Priority { get; set; }
}

public sealed class PublicChoiceView
{
  public string ChoiceId { get; set; } = string.Empty;
  public string PlayerId { get; set; } = string.Empty;
  public string ChoiceType { get; set; } = string.Empty;
  public string VisibilityPolicy { get; set; } = string.Empty;
  public string ResultZone { get; set; } = string.Empty;
  public string SourceRef { get; set; } = string.Empty;
  public List<PublicChoiceOptionView> Options { get; set; } = new();
}

public sealed class PublicChoiceOptionView
{
  public string OptionId { get; set; } = string.Empty;
  public string ItemKind { get; set; } = string.Empty;
  public string? ItemId { get; set; }
  public string Visibility { get; set; } = string.Empty;
}

public sealed class MatchCommandDto
{
  public string CommandType { get; set; } = string.Empty;
  public string PlayerId { get; set; } = string.Empty;
  public Dictionary<string, int>? Payload { get; set; }
  public Dictionary<string, string>? Refs { get; set; }
}

public sealed class ExternalLegalActionsResult
{
  public bool Succeeded { get; set; }
  public List<MatchCommandDto> LegalActions { get; set; } = new();
  public List<LegalActionDiagnostic> LegalDiagnostics { get; set; } = new();
  public List<Diagnostic> Diagnostics { get; set; } = new();
}

public sealed class LegalActionDiagnostic
{
  public string Code { get; set; } = string.Empty;
  public string PlayerId { get; set; } = string.Empty;
  public string CommandType { get; set; } = string.Empty;
  public string Message { get; set; } = string.Empty;
  public string? SourceRef { get; set; }
  public string? TargetRef { get; set; }
}

public sealed class ExternalCommandSubmissionRequest
{
  public string SessionToken { get; set; } = string.Empty;
  public MatchCommandDto Command { get; set; } = new();
  public string? CommandId { get; set; }
  public string? ExpectedPreStateHash { get; set; }
  public string? CorrelationId { get; set; }
}

public sealed class ExternalCommandSubmissionResult
{
  public bool Succeeded { get; set; }
  public MatchStateView? State { get; set; }
  public List<PublicMatchEventRecord> Events { get; set; } = new();
  public List<Diagnostic> Diagnostics { get; set; } = new();
}

public sealed class MatchEventsPollResult
{
  public bool Succeeded { get; set; }
  public List<PublicMatchEventRecord> Events { get; set; } = new();
  public long NextEventSeq { get; set; }
  public List<Diagnostic> Diagnostics { get; set; } = new();
}

public sealed class MatchReconnectResult
{
  public bool Succeeded { get; set; }
  public MatchStateView? State { get; set; }
  public List<PublicMatchEventRecord> Events { get; set; } = new();
  public long NextEventSeq { get; set; }
  public List<Diagnostic> Diagnostics { get; set; } = new();
}

public sealed class PublicMatchEventRecord
{
  public long EventSeq { get; set; }
  public long? ParentEventSeq { get; set; }
  public long? CommandSeq { get; set; }
  public string EventType { get; set; } = string.Empty;
  public string Category { get; set; } = string.Empty;
  public string Phase { get; set; } = string.Empty;
  public List<PublicEventRef> SourceRefs { get; set; } = new();
  public List<PublicEventRef> TargetRefs { get; set; } = new();
  public List<PublicEventPayloadEntry> Payload { get; set; } = new();
  public string? MutationBatchRef { get; set; }
  public List<string> MutationRefs { get; set; } = new();
  public List<string> RngRollRefs { get; set; } = new();
  public string PreStateHash { get; set; } = string.Empty;
  public string PostStateHash { get; set; } = string.Empty;
  public string Visibility { get; set; } = string.Empty;
}

public sealed class PublicEventRef
{
  public string RefType { get; set; } = string.Empty;
  public string RefId { get; set; } = string.Empty;
  public string? Zone { get; set; }
  public string? ControllerPlayerId { get; set; }
}

public sealed class PublicEventPayloadEntry
{
  public string Key { get; set; } = string.Empty;
  public PublicEventPayloadValue Value { get; set; } = new();
}

public sealed class PublicEventPayloadValue
{
  public string ValueKind { get; set; } = string.Empty;
  public string? StringValue { get; set; }
  public int? IntValue { get; set; }
  public bool? BoolValue { get; set; }
  public List<string> StringValues { get; set; } = new();
}

public sealed class MatchReplayVerificationView
{
  public bool Succeeded { get; set; }
  public ReplayVerificationResult Replay { get; set; } = new();
  public List<Diagnostic> Diagnostics { get; set; } = new();
}

public sealed class ReplayVerificationResult
{
  public bool IsVerified { get; set; }
  public string? FinalStateHash { get; set; }
  public int DerivedEventCount { get; set; }
  public List<string> RngRollRefs { get; set; } = new();
  public ReplayTrace? RngTrace { get; set; }
  public List<Diagnostic> Diagnostics { get; set; } = new();
  public ReplayTrace? DebugTrace { get; set; }
}

public sealed class ReplayTrace
{
  public string TraceRef { get; set; } = string.Empty;
  public string ContentHash { get; set; } = string.Empty;
  public List<ReplayTraceEntry> Entries { get; set; } = new();
}

public sealed class ReplayTraceEntry
{
  public int Seq { get; set; }
  public string Kind { get; set; } = string.Empty;
  public string Value { get; set; } = string.Empty;
}

public sealed class CatalogSnapshot
{
  public string SnapshotId { get; set; } = string.Empty;
  public string RulesetId { get; set; } = string.Empty;
  public string ProfileHash { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
  public List<CardVersionSummary> Cards { get; set; } = new();
  public List<string>? CompatibleRulesetIds { get; set; }
  public List<string>? CompatibleRulesetVersions { get; set; }
  public bool ProductionScaleCompatible { get; set; }
  [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class CardVersionSummary
{
  public string CardId { get; set; } = string.Empty;
  public string CardVersionId { get; set; } = string.Empty;
  public bool Published { get; set; }
  public string? DisplayName { get; set; }
  [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class DeckDefinition
{
  public string DeckId { get; set; } = string.Empty;
  public string OwnerPlayerId { get; set; } = string.Empty;
  public string PlayerId { get; set; } = string.Empty;
  public string CatalogSnapshotId { get; set; } = string.Empty;
  public string RulesetId { get; set; } = string.Empty;
  public string RulesetProfileId { get; set; } = string.Empty;
  public List<DeckCardRef> Cards { get; set; } = new();
}

public sealed class DeckCardRef
{
  public string CardId { get; set; } = string.Empty;
  public string? CardVersionId { get; set; }
  public int Count { get; set; }
}

public sealed class ResolvedRulesetProfile
{
  public string ProfileId { get; set; } = string.Empty;
  public string RulesetId { get; set; } = string.Empty;
  public string ProfileVersion { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
  public string ProfileHash { get; set; } = string.Empty;
  public List<string> AllowedCommands { get; set; } = new();
  [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class CompatibilityResult
{
  public bool Succeeded { get; set; }
  public List<Diagnostic> Diagnostics { get; set; } = new();
  [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class AssetManifest
{
  public string AssetManifestId { get; set; } = string.Empty;
  public string AssetManifestVersion { get; set; } = string.Empty;
  public string CardId { get; set; } = string.Empty;
  public string CardVersionId { get; set; } = string.Empty;
  public string VisualAssetMode { get; set; } = string.Empty;
  public string AudioAssetMode { get; set; } = string.Empty;
  public VisualAssetManifest? VisualManifest { get; set; }
  public AudioAssetManifest? AudioManifest { get; set; }
  public AssetFallbackPolicy FallbackPolicy { get; set; } = new();
  public string ModerationStatus { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
  public string ContentHash { get; set; } = string.Empty;
}

public sealed class VisualAssetManifest
{
  public string VisualManifestId { get; set; } = string.Empty;
  public string VisualManifestVersion { get; set; } = string.Empty;
  public string AssetRef { get; set; } = string.Empty;
  public string Format { get; set; } = string.Empty;
  public string ModerationStatus { get; set; } = string.Empty;
  public string ContentHash { get; set; } = string.Empty;
}

public sealed class AudioAssetManifest
{
  public string AudioManifestId { get; set; } = string.Empty;
  public string AudioManifestVersion { get; set; } = string.Empty;
  public string CardId { get; set; } = string.Empty;
  public string CardVersionId { get; set; } = string.Empty;
  public string AudioAssetMode { get; set; } = string.Empty;
  public List<AudioCueDefinition> Cues { get; set; } = new();
  public List<string> FallbackCueRefs { get; set; } = new();
  public string LocalizationPolicy { get; set; } = string.Empty;
  public string VolumeGroup { get; set; } = string.Empty;
  public string ModerationStatus { get; set; } = string.Empty;
  public string ContentHash { get; set; } = string.Empty;
}

public sealed class AudioCueDefinition
{
  public string CueId { get; set; } = string.Empty;
  public string CueType { get; set; } = string.Empty;
  public string AssetRef { get; set; } = string.Empty;
  public int DurationMs { get; set; }
  public string? Language { get; set; }
  public string? SubtitleKey { get; set; }
  public string VolumeGroup { get; set; } = string.Empty;
  public PlaybackPolicy PlaybackPolicy { get; set; } = new();
  public CooldownPolicy CooldownPolicy { get; set; } = new();
  public string? FallbackCueId { get; set; }
  public string ContentHash { get; set; } = string.Empty;
}

public enum AudioCueType
{
  OnPlay,
  OnSummon,
  OnAttack,
  OnDeath,
  OnTrigger,
  OnSecretReveal,
  OnQuestComplete,
  OnHover,
  OnInspect,
  OnCrafted
}

public sealed class PlaybackPolicy
{
  public bool PlayOnce { get; set; }
  public string InterruptPolicy { get; set; } = string.Empty;
  public string OverlapPolicy { get; set; } = string.Empty;
  public int Priority { get; set; }
  public int MaxConcurrentInstances { get; set; }
  public bool ClientMaySkip { get; set; }
}

public sealed class CooldownPolicy
{
  public int CooldownMs { get; set; }
  public string Scope { get; set; } = string.Empty;
}

public sealed class AssetFallbackPolicy
{
  public bool AllowSilence { get; set; }
  public List<string> FallbackAssetRefs { get; set; } = new();
}

public sealed class PresentationHashResponse
{
  public string AssetManifestId { get; set; } = string.Empty;
  public string PresentationCatalogHash { get; set; } = string.Empty;
}

public sealed class PresentationTimelineBuildRequest
{
  public string TimelineId { get; set; } = string.Empty;
  public List<PublicMatchEventRecord> Events { get; set; } = new();
  public List<AssetManifest> AssetManifests { get; set; } = new();
}

public sealed class PresentationTimelineBuildResult
{
  public PresentationTimeline Timeline { get; set; } = new();
  public string PresentationTimelineHash { get; set; } = string.Empty;
}

public sealed class PresentationTimeline
{
  public string TimelineId { get; set; } = string.Empty;
  public string PresentationCatalogHash { get; set; } = string.Empty;
  public long? FirstSourceEventSeq { get; set; }
  public long? LastSourceEventSeq { get; set; }
  public List<PresentationTimelineEntry> Entries { get; set; } = new();
}

public sealed class PresentationTimelineEntry
{
  public string EntryId { get; set; } = string.Empty;
  public string EntryKind { get; set; } = string.Empty;
  public long SourceEventSeq { get; set; }
  public string CueType { get; set; } = string.Empty;
  public string? SourceCardId { get; set; }
  public string? SourceCardVersionId { get; set; }
  public string? AssetManifestId { get; set; }
  public string? AssetRef { get; set; }
  public PlaybackPolicy? PlaybackPolicy { get; set; }
  public string VisibilityPolicy { get; set; } = string.Empty;
  public AssetFallbackPolicy FallbackPolicy { get; set; } = new();
  public string Reason { get; set; } = string.Empty;
}

public sealed class PresentationPlaybackPlanBuildRequest
{
  public string PlaybackPlanId { get; set; } = string.Empty;
  public PresentationTimeline Timeline { get; set; } = new();
  public string PresentationTimelineHash { get; set; } = string.Empty;
}

public sealed class PresentationPlaybackPlanBuildResult
{
  public PresentationPlaybackPlan PlaybackPlan { get; set; } = new();
}

public sealed class PresentationPlaybackPlan
{
  public string PlaybackPlanId { get; set; } = string.Empty;
  public string TimelineId { get; set; } = string.Empty;
  public string PresentationTimelineHash { get; set; } = string.Empty;
  public string PresentationCatalogHash { get; set; } = string.Empty;
  public long? FirstSourceEventSeq { get; set; }
  public long? LastSourceEventSeq { get; set; }
  public List<PresentationPlaybackStep> Steps { get; set; } = new();
}

public sealed class PresentationPlaybackStep
{
  public string StepId { get; set; } = string.Empty;
  public string StepKind { get; set; } = string.Empty;
  public string TimelineEntryId { get; set; } = string.Empty;
  public long SourceEventSeq { get; set; }
  public string CueType { get; set; } = string.Empty;
  public string? SourceCardId { get; set; }
  public string? SourceCardVersionId { get; set; }
  public string? AssetManifestId { get; set; }
  public string? AssetRef { get; set; }
  public PlaybackPolicy PlaybackPolicy { get; set; } = new();
  public AssetFallbackPolicy FallbackPolicy { get; set; } = new();
  public string VisibilityPolicy { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
}

public sealed class PresentationPlaybackRuntimeRequest
{
  public PresentationPlaybackPlan PlaybackPlan { get; set; } = new();
  public PresentationPlaybackRuntimeState ClientState { get; set; } = new();
  public int NowMs { get; set; }
}

public sealed class PresentationPlaybackRuntimeState
{
  public List<string> PlayedStepIds { get; set; } = new();
  public Dictionary<string, int> ActiveInstanceCounts { get; set; } = new();
}

public sealed class PresentationPlaybackRuntimeResult
{
  public List<PresentationPlaybackInstruction> Instructions { get; set; } = new();
  public PresentationPlaybackRuntimeState NextState { get; set; } = new();
}

public sealed class PresentationPlaybackInstruction
{
  public string StepId { get; set; } = string.Empty;
  public string InstructionKind { get; set; } = string.Empty;
  public string TimelineEntryId { get; set; } = string.Empty;
  public long SourceEventSeq { get; set; }
  public string CueType { get; set; } = string.Empty;
  public string? AssetRef { get; set; }
  public int Priority { get; set; }
  public bool ClientMaySkip { get; set; }
  public string Reason { get; set; } = string.Empty;
}

public sealed class MatchEvidenceArtifact
{
  public string ArtifactRef { get; set; } = string.Empty;
  public string MatchId { get; set; } = string.Empty;
  public string ArtifactKind { get; set; } = string.Empty;
  public string ContentHash { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class UnityBotMatchSmokeReport
{
  public string MatchId { get; set; } = string.Empty;
  public List<string> DecksUsed { get; set; } = new();
  public int CommandsSubmitted { get; set; }
  public List<string> CommandsUsed { get; set; } = new();
  public int EventCount { get; set; }
  public string FinalStateHash { get; set; } = string.Empty;
  public string TerminalReason { get; set; } = string.Empty;
  public bool ReplayVerified { get; set; }
  public bool EventSequenceMonotonic { get; set; }
  public bool ReconnectStateHashMatches { get; set; }
  public List<string> EffectFamiliesObserved { get; set; } = new();
  public List<string> HiddenInfoAssertions { get; set; } = new();
  public List<string> EvidenceArtifactRefs { get; set; } = new();
  public DateTimeOffset StartedAt { get; set; }
  public DateTimeOffset CompletedAt { get; set; }
  public double DurationMs { get; set; }
  public string ReportKind { get; set; } = string.Empty;
}
