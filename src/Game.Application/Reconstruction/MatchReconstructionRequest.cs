using Game.Domain.Replay;
using Game.Domain.State;
using Game.Shared.Assets;
using Game.Shared.Events;

namespace Game.Application.Reconstruction;

public sealed record MatchReconstructionRequest(
  string MatchId,
  CanonicalCommandLog CommandLog,
  IReadOnlyList<CommittedEventRecord> RegeneratedEvents,
  IGameState FinalState,
  string FinalStateHash,
  IReadOnlyList<AssetManifest>? AssetManifests = null);
