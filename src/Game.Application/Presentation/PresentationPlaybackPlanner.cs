using Game.Shared.Assets;

namespace Game.Application.Presentation;

public sealed class PresentationPlaybackPlanner : IPresentationPlaybackPlanner
{
  private static readonly PlaybackPolicy SkipPlaybackPolicy =
    new(false, "NONE", "SKIP_IF_BUSY", 0, 0, true);

  public PresentationPlaybackPlanBuildResult Build(PresentationPlaybackPlanBuildRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.Timeline);

    var steps = request.Timeline.Entries
      .OrderBy(entry => entry.SourceEventSeq)
      .ThenBy(entry => entry.AssetManifestId ?? string.Empty, StringComparer.Ordinal)
      .ThenBy(entry => entry.EntryKind.ToString(), StringComparer.Ordinal)
      .ThenBy(entry => entry.EntryId, StringComparer.Ordinal)
      .Select(CreateStep)
      .ToArray();

    return new PresentationPlaybackPlanBuildResult(new PresentationPlaybackPlan(
      request.PlaybackPlanId,
      request.Timeline.TimelineId,
      request.PresentationTimelineHash,
      request.Timeline.PresentationCatalogHash,
      request.Timeline.FirstSourceEventSeq,
      request.Timeline.LastSourceEventSeq,
      steps));
  }

  private static PresentationPlaybackStep CreateStep(PresentationTimelineEntry entry)
  {
    if (!StringComparer.Ordinal.Equals(entry.VisibilityPolicy, "Public"))
    {
      return BuildSkipStep(entry, "HIDDEN_BY_VISIBILITY_POLICY");
    }

    return entry.EntryKind switch
    {
      PresentationTimelineEntryKind.Cue when !string.IsNullOrWhiteSpace(entry.AssetRef) =>
        BuildStep(entry, PresentationPlaybackStepKind.PlayAsset, entry.PlaybackPolicy ?? SkipPlaybackPolicy, entry.Reason),
      PresentationTimelineEntryKind.Silence when entry.FallbackPolicy.AllowSilence =>
        BuildStep(entry, PresentationPlaybackStepKind.Silence, entry.PlaybackPolicy ?? SkipPlaybackPolicy, entry.Reason),
      _ => BuildSkipStep(entry, "NO_PLAYABLE_PRESENTATION_OUTPUT")
    };
  }

  private static PresentationPlaybackStep BuildSkipStep(
    PresentationTimelineEntry entry,
    string reason) =>
    BuildStep(entry, PresentationPlaybackStepKind.Skip, SkipPlaybackPolicy, reason);

  private static PresentationPlaybackStep BuildStep(
    PresentationTimelineEntry entry,
    PresentationPlaybackStepKind stepKind,
    PlaybackPolicy playbackPolicy,
    string reason) =>
    new(
      $"playback:{entry.EntryId}",
      stepKind,
      entry.EntryId,
      entry.SourceEventSeq,
      entry.CueType,
      entry.SourceCardId,
      entry.SourceCardVersionId,
      entry.AssetManifestId,
      stepKind is PresentationPlaybackStepKind.PlayAsset ? entry.AssetRef : null,
      playbackPolicy,
      entry.FallbackPolicy,
      entry.VisibilityPolicy,
      reason);
}

