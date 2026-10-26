using System.Security.Cryptography;
using System.Text;
using Game.Shared.Assets;
using Game.Shared.Events;

namespace Game.Application.Presentation;

public sealed class PresentationTimelineBuilder : IPresentationTimelineBuilder
{
  private static readonly IReadOnlyDictionary<CommittedEventType, AudioCueType> EventCueMap =
    new Dictionary<CommittedEventType, AudioCueType>
    {
      [CommittedEventType.CardPlayed] = AudioCueType.OnPlay,
      [CommittedEventType.MinionSummoned] = AudioCueType.OnSummon,
      [CommittedEventType.EntityDamaged] = AudioCueType.OnTrigger,
      [CommittedEventType.EntityDied] = AudioCueType.OnDeath,
      [CommittedEventType.DeathrattleTriggered] = AudioCueType.OnTrigger,
      [CommittedEventType.SecretRevealed] = AudioCueType.OnSecretReveal,
      [CommittedEventType.QuestProgressChanged] = AudioCueType.OnQuestComplete
    };

  private static readonly PlaybackPolicy SilencePlaybackPolicy =
    new(false, "NONE", "SKIP_IF_BUSY", 0, 0, true);

  private readonly IPresentationCueResolver _cueResolver;
  private readonly IPresentationCatalogHasher _catalogHasher;
  private readonly IPresentationTimelineHasher _timelineHasher;

  public PresentationTimelineBuilder(
    IPresentationCueResolver cueResolver,
    IPresentationCatalogHasher catalogHasher,
    IPresentationTimelineHasher timelineHasher)
  {
    _cueResolver = cueResolver;
    _catalogHasher = catalogHasher;
    _timelineHasher = timelineHasher;
  }

  public PresentationTimelineBuildResult Build(PresentationTimelineBuildRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    var resolvedCues = _cueResolver.Resolve(new PresentationCueResolutionRequest(
      request.Events,
      request.AssetManifests)).Cues;
    var entries = resolvedCues
      .Select(CreateCueEntry)
      .Concat(CreateSilenceEntries(request, resolvedCues))
      .OrderBy(entry => entry.SourceEventSeq)
      .ThenBy(entry => entry.AssetManifestId ?? string.Empty, StringComparer.Ordinal)
      .ThenBy(entry => entry.EntryKind.ToString(), StringComparer.Ordinal)
      .ThenBy(entry => entry.EntryId, StringComparer.Ordinal)
      .ToArray();

    var sourceEventSeqs = request.Events
      .Select(@event => @event.EventSeq)
      .Order()
      .ToArray();
    var timeline = new PresentationTimeline(
      request.TimelineId,
      HashPresentationCatalog(request.AssetManifests),
      sourceEventSeqs.FirstOrDefaultNullable(),
      sourceEventSeqs.LastOrDefaultNullable(),
      entries);

    return new PresentationTimelineBuildResult(
      timeline,
      _timelineHasher.HashTimeline(timeline));
  }

  private IEnumerable<PresentationTimelineEntry> CreateSilenceEntries(
    PresentationTimelineBuildRequest request,
    IReadOnlyList<PresentationCue> resolvedCues)
  {
    var cueKeys = resolvedCues
      .Select(cue => (cue.SourceEventSeq, cue.AssetManifestId, cue.CueType))
      .ToHashSet();

    foreach (var committedEvent in request.Events.OrderBy(@event => @event.EventSeq))
    {
      if (!EventCueMap.TryGetValue(committedEvent.EventType, out var cueType))
      {
        continue;
      }

      var cardId = FindStringPayload(committedEvent, "cardId") ?? FindEventRef(committedEvent, "CARD");
      var cardVersionId = FindStringPayload(committedEvent, "cardVersionId") ?? FindStringPayload(committedEvent, "cardVersion");
      var matchingManifests = request.AssetManifests
        .Where(manifest => MatchesManifest(manifest, cardId, cardVersionId))
        .OrderBy(manifest => manifest.AssetManifestId, StringComparer.Ordinal)
        .ThenBy(manifest => manifest.AssetManifestVersion, StringComparer.Ordinal);

      foreach (var manifest in matchingManifests)
      {
        if (!manifest.FallbackPolicy.AllowSilence ||
            cueKeys.Contains((committedEvent.EventSeq, manifest.AssetManifestId, cueType)))
        {
          continue;
        }

        yield return new PresentationTimelineEntry(
          $"presentation:{committedEvent.EventSeq}:{manifest.AssetManifestId}:silence:{cueType}",
          PresentationTimelineEntryKind.Silence,
          committedEvent.EventSeq,
          cueType,
          manifest.CardId,
          manifest.CardVersionId,
          manifest.AssetManifestId,
          null,
          SilencePlaybackPolicy,
          committedEvent.Visibility.ToString(),
          manifest.FallbackPolicy,
          "MISSING_CUE_FALLBACK_SILENCE");
      }
    }
  }

  private static PresentationTimelineEntry CreateCueEntry(PresentationCue cue) =>
    new(
      cue.CueId,
      PresentationTimelineEntryKind.Cue,
      cue.SourceEventSeq,
      cue.CueType,
      cue.SourceCardId,
      cue.SourceCardVersionId,
      cue.AssetManifestId,
      cue.AssetRef,
      cue.PlaybackPolicy,
      cue.VisibilityPolicy,
      cue.FallbackPolicy,
      "RESOLVED_CUE");

  private string HashPresentationCatalog(IReadOnlyList<AssetManifest> manifests)
  {
    var manifestHashes = manifests
      .OrderBy(manifest => manifest.AssetManifestId, StringComparer.Ordinal)
      .ThenBy(manifest => manifest.AssetManifestVersion, StringComparer.Ordinal)
      .Select(_catalogHasher.HashManifest);

    var joinedHashes = string.Join('|', manifestHashes);
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joinedHashes)));
  }

  private static bool MatchesManifest(
    AssetManifest manifest,
    string? cardId,
    string? cardVersionId)
  {
    if (!string.IsNullOrWhiteSpace(cardVersionId))
    {
      return StringComparer.Ordinal.Equals(manifest.CardVersionId, cardVersionId);
    }

    return !string.IsNullOrWhiteSpace(cardId) &&
      StringComparer.Ordinal.Equals(manifest.CardId, cardId);
  }

  private static string? FindEventRef(CommittedEventRecord committedEvent, string refType) =>
    committedEvent.SourceRefs
      .Concat(committedEvent.TargetRefs)
      .Where(eventRef => StringComparer.Ordinal.Equals(eventRef.RefType, refType))
      .OrderBy(eventRef => eventRef.RefId, StringComparer.Ordinal)
      .Select(eventRef => eventRef.RefId)
      .FirstOrDefault();

  private static string? FindStringPayload(CommittedEventRecord committedEvent, string key) =>
    committedEvent.Payload
      .Where(entry => StringComparer.Ordinal.Equals(entry.Key, key))
      .Select(entry => entry.Value.StringValue)
      .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

file static class PresentationTimelineEnumerableExtensions
{
  public static long? FirstOrDefaultNullable(this IReadOnlyList<long> values) =>
    values.Count == 0 ? null : values[0];

  public static long? LastOrDefaultNullable(this IReadOnlyList<long> values) =>
    values.Count == 0 ? null : values[^1];
}
