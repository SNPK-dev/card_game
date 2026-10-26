using Game.Shared.Assets;
using Game.Shared.Events;

namespace Game.Application.Presentation;

public sealed class PresentationCueResolver : IPresentationCueResolver
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

  public PresentationCueResolutionResult Resolve(PresentationCueResolutionRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    var cues = new List<PresentationCue>();
    foreach (var committedEvent in request.Events.OrderBy(@event => @event.EventSeq))
    {
      if (!EventCueMap.TryGetValue(committedEvent.EventType, out var cueType))
      {
        continue;
      }

      var cardId = FindStringPayload(committedEvent, "cardId")
        ?? FindEventRef(committedEvent, "CARD");
      var cardVersionId = FindStringPayload(committedEvent, "cardVersionId")
        ?? FindStringPayload(committedEvent, "cardVersion");

      var matchingManifests = request.AssetManifests
        .Where(manifest => MatchesManifest(manifest, cardId, cardVersionId))
        .OrderBy(manifest => manifest.AssetManifestId, StringComparer.Ordinal)
        .ThenBy(manifest => manifest.AssetManifestVersion, StringComparer.Ordinal);

      foreach (var manifest in matchingManifests)
      {
        if (manifest.AudioManifest is null)
        {
          continue;
        }

        foreach (var cue in manifest.AudioManifest.Cues
          .Where(candidate => candidate.CueType == cueType)
          .OrderBy(candidate => candidate.CueId, StringComparer.Ordinal))
        {
          cues.Add(new PresentationCue(
            BuildCueId(committedEvent.EventSeq, manifest.AssetManifestId, cue.CueId),
            cue.CueType,
            committedEvent.EventSeq,
            manifest.CardId,
            manifest.CardVersionId,
            manifest.AssetManifestId,
            cue.AssetRef,
            cue.PlaybackPolicy,
            committedEvent.Visibility.ToString(),
            manifest.FallbackPolicy));
        }
      }
    }

    return new PresentationCueResolutionResult(cues);
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

    if (!string.IsNullOrWhiteSpace(cardId))
    {
      return StringComparer.Ordinal.Equals(manifest.CardId, cardId);
    }

    return false;
  }

  private static string BuildCueId(long eventSeq, string assetManifestId, string cueId) =>
    $"presentation:{eventSeq}:{assetManifestId}:{cueId}";

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
