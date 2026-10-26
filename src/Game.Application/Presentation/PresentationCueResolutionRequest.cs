using Game.Shared.Assets;
using Game.Shared.Events;

namespace Game.Application.Presentation;

public sealed record PresentationCueResolutionRequest(
  IReadOnlyList<CommittedEventRecord> Events,
  IReadOnlyList<AssetManifest> AssetManifests);
