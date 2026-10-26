namespace Game.Shared.Assets;

public enum VisualAssetMode
{
  None,
  StaticImage,
  IdleAnimation
}

public enum AudioAssetMode
{
  None,
  SfxOnly,
  VoiceLineOnly,
  SfxAndVoice,
  FullAudioSet
}

public enum AssetModerationStatus
{
  NotRequired,
  Pending,
  Passed,
  Rejected
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

public enum PresentationTimelineEntryKind
{
  Cue,
  Silence
}

public enum PresentationPlaybackStepKind
{
  PlayAsset,
  Silence,
  Skip
}

public sealed record AssetManifest(
  string AssetManifestId,
  string AssetManifestVersion,
  string CardId,
  string CardVersionId,
  VisualAssetMode VisualAssetMode,
  AudioAssetMode AudioAssetMode,
  VisualAssetManifest? VisualManifest,
  AudioAssetManifest? AudioManifest,
  AssetFallbackPolicy FallbackPolicy,
  AssetModerationStatus ModerationStatus,
  DateTimeOffset CreatedAt,
  string ContentHash);

public sealed record VisualAssetManifest(
  string VisualManifestId,
  string VisualManifestVersion,
  string AssetRef,
  string Format,
  AssetModerationStatus ModerationStatus,
  string ContentHash);

public sealed record AudioAssetManifest(
  string AudioManifestId,
  string AudioManifestVersion,
  string CardId,
  string CardVersionId,
  AudioAssetMode AudioAssetMode,
  IReadOnlyList<AudioCueDefinition> Cues,
  IReadOnlyList<string> FallbackCueRefs,
  string LocalizationPolicy,
  string VolumeGroup,
  AssetModerationStatus ModerationStatus,
  string ContentHash);

public sealed record AudioCueDefinition(
  string CueId,
  AudioCueType CueType,
  string AssetRef,
  int DurationMs,
  string? Language,
  string? SubtitleKey,
  string VolumeGroup,
  PlaybackPolicy PlaybackPolicy,
  CooldownPolicy CooldownPolicy,
  string? FallbackCueId,
  string ContentHash);

public sealed record PlaybackPolicy(
  bool PlayOnce,
  string InterruptPolicy,
  string OverlapPolicy,
  int Priority,
  int MaxConcurrentInstances,
  bool ClientMaySkip);

public sealed record CooldownPolicy(
  int CooldownMs,
  string Scope);

public sealed record AssetFallbackPolicy(
  bool AllowSilence,
  IReadOnlyList<string> FallbackAssetRefs);

public sealed record PresentationCue(
  string CueId,
  AudioCueType CueType,
  long SourceEventSeq,
  string? SourceCardId,
  string? SourceCardVersionId,
  string AssetManifestId,
  string AssetRef,
  PlaybackPolicy PlaybackPolicy,
  string VisibilityPolicy,
  AssetFallbackPolicy FallbackPolicy);

public sealed record PresentationTimeline(
  string TimelineId,
  string PresentationCatalogHash,
  long? FirstSourceEventSeq,
  long? LastSourceEventSeq,
  IReadOnlyList<PresentationTimelineEntry> Entries);

public sealed record PresentationTimelineEntry(
  string EntryId,
  PresentationTimelineEntryKind EntryKind,
  long SourceEventSeq,
  AudioCueType CueType,
  string? SourceCardId,
  string? SourceCardVersionId,
  string? AssetManifestId,
  string? AssetRef,
  PlaybackPolicy? PlaybackPolicy,
  string VisibilityPolicy,
  AssetFallbackPolicy FallbackPolicy,
  string Reason);

public sealed record PresentationPlaybackPlan(
  string PlaybackPlanId,
  string TimelineId,
  string PresentationTimelineHash,
  string PresentationCatalogHash,
  long? FirstSourceEventSeq,
  long? LastSourceEventSeq,
  IReadOnlyList<PresentationPlaybackStep> Steps);

public sealed record PresentationPlaybackStep(
  string StepId,
  PresentationPlaybackStepKind StepKind,
  string TimelineEntryId,
  long SourceEventSeq,
  AudioCueType CueType,
  string? SourceCardId,
  string? SourceCardVersionId,
  string? AssetManifestId,
  string? AssetRef,
  PlaybackPolicy PlaybackPolicy,
  AssetFallbackPolicy FallbackPolicy,
  string VisibilityPolicy,
  string Reason);
