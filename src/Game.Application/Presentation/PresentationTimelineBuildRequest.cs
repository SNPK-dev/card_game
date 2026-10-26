using Game.Shared.Assets;
using Game.Shared.Events;

namespace Game.Application.Presentation;

public sealed record PresentationTimelineBuildRequest(
  string TimelineId,
  IReadOnlyList<CommittedEventRecord> Events,
  IReadOnlyList<AssetManifest> AssetManifests);
