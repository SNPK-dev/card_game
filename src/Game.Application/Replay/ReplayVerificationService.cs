using Game.Application.Capabilities;
using Game.Domain.Hashing;
using Game.Domain.Replay;
using Game.Domain.Serialization;
using Game.Domain.State;
using Game.Shared.Events;
using System.Globalization;
using System.Security.Cryptography;

namespace Game.Application.Replay;

public sealed class ReplayVerificationService(
  CommandLogReplayer replayer,
  IStateHasher stateHasher,
  IEngineCapabilityAuditService? capabilityAuditService = null)
{
  private readonly IEngineCapabilityAuditService _capabilityAuditService =
    capabilityAuditService ?? new EngineCapabilityAuditService();

  public ReplayVerificationResult Verify(ReplayVerificationRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.InitialState);
    ArgumentNullException.ThrowIfNull(request.CommandLog);

    try
    {
      var initialStateHash = stateHasher.HashGameplayState(request.InitialState);
      if (!string.IsNullOrWhiteSpace(request.ExpectedInitialStateHash)
        && !string.Equals(request.ExpectedInitialStateHash, initialStateHash, StringComparison.Ordinal))
      {
        return ReplayVerificationResult.Rejected(
          null,
          0,
          [
            new ReplayVerificationDiagnostic(
              "INITIAL_STATE_HASH_MISMATCH",
              "replay.initialStateHash",
              initialStateHash,
              "Replay initial state hash does not match the expected hash.")
          ]);
      }

      var preflightDiagnostics = RunCompatibilityPreflight(request);
      if (preflightDiagnostics.Count > 0)
      {
        return ReplayVerificationResult.Rejected(null, 0, preflightDiagnostics);
      }

      var replay = replayer.Replay(request.InitialState, request.CommandLog);
      var finalStateHash = stateHasher.HashGameplayState(replay.State);
      var diagnostics = new List<ReplayVerificationDiagnostic>();
      var rngRollRefs = replay.DerivedEvents
        .SelectMany(@event => @event.RngRollRefs)
        .ToArray();
      var rngTrace = CreateRngTraceArtifact(replay.DerivedEvents);
      var debugTrace = CreateDebugTraceArtifact(replay.DerivedEvents);

      if (string.IsNullOrWhiteSpace(request.ExpectedFinalStateHash))
      {
        diagnostics.Add(new ReplayVerificationDiagnostic(
          "EXPECTED_FINAL_STATE_HASH_REQUIRED",
          "replay.expectedFinalStateHash",
          string.Empty,
          "Replay verification requires an expected final state hash."));
      }
      else if (!string.Equals(request.ExpectedFinalStateHash, finalStateHash, StringComparison.Ordinal))
      {
        diagnostics.Add(new ReplayVerificationDiagnostic(
          "FINAL_STATE_HASH_MISMATCH",
          "replay.finalStateHash",
          finalStateHash,
          "Replay final state hash does not match the expected hash."));
      }

      if (request.ExpectedRngRollRefs is not null
        && !request.ExpectedRngRollRefs.SequenceEqual(rngRollRefs, StringComparer.Ordinal))
      {
        diagnostics.Add(new ReplayVerificationDiagnostic(
          "RNG_ROLL_REFS_MISMATCH",
          "replay.rngRollRefs",
          string.Join(",", rngRollRefs),
          "Replay RNG roll refs do not match the expected deterministic RNG trace."));
      }

      if (request.ExpectedRngTrace is not null
        && !IsSameRngTrace(request.ExpectedRngTrace, rngTrace))
      {
        diagnostics.Add(new ReplayVerificationDiagnostic(
          "RNG_TRACE_MISMATCH",
          "replay.rngTrace",
          rngTrace.TraceHash,
          "Replay RNG trace artifact does not match the expected deterministic RNG trace."));
      }

      return diagnostics.Count == 0
        ? ReplayVerificationResult.Verified(finalStateHash, replay.DerivedEvents.Count, rngRollRefs, rngTrace, debugTrace)
        : ReplayVerificationResult.Rejected(finalStateHash, replay.DerivedEvents.Count, diagnostics, rngRollRefs, rngTrace, debugTrace);
    }
    catch (InvalidOperationException exception)
    {
      return ReplayVerificationResult.Rejected(
        null,
        0,
        [
          new ReplayVerificationDiagnostic(
            "REPLAY_EXECUTION_FAILED",
            "replay.commandLog",
            string.Empty,
            exception.Message)
        ]);
    }
    catch (ArgumentException exception)
    {
      return ReplayVerificationResult.Rejected(
        null,
        0,
        [
          new ReplayVerificationDiagnostic(
            "REPLAY_COMMAND_LOG_INVALID",
            "replay.commandLog",
            string.Empty,
            exception.Message)
        ]);
    }
  }

  public ReplayVerificationResult Verify(
    IGameState initialState,
    CanonicalCommandLog commandLog,
    string? expectedFinalStateHash) =>
    Verify(new ReplayVerificationRequest(initialState, commandLog, expectedFinalStateHash));

  private IReadOnlyList<ReplayVerificationDiagnostic> RunCompatibilityPreflight(
    ReplayVerificationRequest request)
  {
    if (request.EngineCapabilityRegistry is null)
    {
      return [];
    }

    var report = _capabilityAuditService.Audit(new EngineCapabilityAuditRequest(
      request.EngineCapabilityRegistry,
      request.RulesetProfile,
      request.CatalogSnapshot,
      request.ExpectedEngineCapabilityRegistryHash));
    if (report.IsCompatible)
    {
      return [];
    }

    return report.Diagnostics
      .Select(diagnostic => new ReplayVerificationDiagnostic(
        diagnostic.Code,
        $"replay.preflight.{diagnostic.Path}",
        diagnostic.Identifier,
        diagnostic.Message))
      .ToArray();
  }

  private static ReplayRngTraceArtifact CreateRngTraceArtifact(
    IReadOnlyList<CommittedEventRecord> derivedEvents)
  {
    var rolls = derivedEvents
      .Where(@event => @event.EventType == CommittedEventType.RngRolled)
      .OrderBy(@event => @event.EventSeq)
      .Select(CreateRngTraceEntry)
      .ToArray();

    return new ReplayRngTraceArtifact(
      "rng-trace-v1",
      ComputeRngTraceHash("rng-trace-v1", rolls),
      rolls);
  }

  private static ReplayDebugTraceArtifact CreateDebugTraceArtifact(
    IReadOnlyList<CommittedEventRecord> derivedEvents)
  {
    var entries = derivedEvents
      .OrderBy(@event => @event.EventSeq)
      .Select(@event => new ReplayDebugTraceEntry(
        @event.EventSeq,
        @event.ParentEventSeq,
        @event.CommandSeq,
        @event.EventType.ToString(),
        @event.Phase.ToString(),
        @event.SourceRefs
          .OrderBy(reference => reference.RefType, StringComparer.Ordinal)
          .ThenBy(reference => reference.ControllerPlayerId ?? string.Empty, StringComparer.Ordinal)
          .ThenBy(reference => reference.Zone ?? string.Empty, StringComparer.Ordinal)
          .ThenBy(reference => reference.RefId, StringComparer.Ordinal)
          .ToArray(),
        @event.TargetRefs
          .OrderBy(reference => reference.RefType, StringComparer.Ordinal)
          .ThenBy(reference => reference.ControllerPlayerId ?? string.Empty, StringComparer.Ordinal)
          .ThenBy(reference => reference.Zone ?? string.Empty, StringComparer.Ordinal)
          .ThenBy(reference => reference.RefId, StringComparer.Ordinal)
          .ToArray(),
        @event.MutationRefs.Order(StringComparer.Ordinal).ToArray(),
        @event.RngRollRefs.Order(StringComparer.Ordinal).ToArray()))
      .ToArray();

    return new ReplayDebugTraceArtifact(
      "replay-debug-trace-v1",
      ComputeDebugTraceHash("replay-debug-trace-v1", entries),
      entries);
  }

  private static ReplayRngTraceEntry CreateRngTraceEntry(CommittedEventRecord rngEvent)
  {
    var commandSeq = rngEvent.CommandSeq
      ?? throw new InvalidOperationException(
        $"RNG event '{rngEvent.EventSeq}' is missing command sequence.");

    return new ReplayRngTraceEntry(
      RequiredString(rngEvent, "rngRollId"),
      RequiredString(rngEvent, "stream"),
      RequiredString(rngEvent, "preRngState"),
      RequiredString(rngEvent, "postRngState"),
      RequiredInt(rngEvent, "candidateCount"),
      RequiredInt(rngEvent, "selectedIndex"),
      RequiredString(rngEvent, "reason"),
      commandSeq,
      rngEvent.EventSeq,
      rngEvent.TargetRefs
        .OrderBy(target => target.RefType, StringComparer.Ordinal)
        .ThenBy(target => target.ControllerPlayerId ?? string.Empty, StringComparer.Ordinal)
        .ThenBy(target => target.Zone ?? string.Empty, StringComparer.Ordinal)
        .ThenBy(target => target.RefId, StringComparer.Ordinal)
        .ToArray());
  }

  private static string RequiredString(CommittedEventRecord rngEvent, string key) =>
    rngEvent.Payload
      .Where(entry => StringComparer.Ordinal.Equals(entry.Key, key))
      .Select(entry => entry.Value.StringValue)
      .FirstOrDefault(value => value is not null)
    ?? throw new InvalidOperationException(
      $"RNG event '{rngEvent.EventSeq}' is missing string payload '{key}'.");

  private static int RequiredInt(CommittedEventRecord rngEvent, string key)
  {
    var intValue = rngEvent.Payload
      .Where(entry => StringComparer.Ordinal.Equals(entry.Key, key))
      .Select(entry => entry.Value.IntValue)
      .FirstOrDefault(value => value.HasValue);
    if (intValue.HasValue)
    {
      return intValue.Value;
    }

    var stringValue = rngEvent.Payload
      .Where(entry => StringComparer.Ordinal.Equals(entry.Key, key))
      .Select(entry => entry.Value.StringValue)
      .FirstOrDefault(value => value is not null);
    if (stringValue is not null
      && int.TryParse(
        stringValue,
        NumberStyles.None,
        CultureInfo.InvariantCulture,
        out var parsed))
    {
      return parsed;
    }

    throw new InvalidOperationException(
      $"RNG event '{rngEvent.EventSeq}' is missing integer payload '{key}'.");
  }

  private static bool IsSameRngTrace(
    ReplayRngTraceArtifact expected,
    ReplayRngTraceArtifact actual)
  {
    var expectedContentHash = ComputeRngTraceHash(expected.TraceVersion, expected.Rolls);

    return string.Equals(expected.TraceVersion, actual.TraceVersion, StringComparison.Ordinal)
      && string.Equals(expected.TraceHash, actual.TraceHash, StringComparison.Ordinal)
      && string.Equals(expectedContentHash, actual.TraceHash, StringComparison.Ordinal);
  }

  private static string ComputeRngTraceHash(
    string traceVersion,
    IReadOnlyList<ReplayRngTraceEntry> rolls)
  {
    var hashInput = new ReplayRngTraceHashInput(traceVersion, rolls);
    var canonicalBytes = new CanonicalJsonSerializer().SerializeToCanonicalBytes(hashInput);
    var hash = Convert.ToHexString(SHA256.HashData(canonicalBytes)).ToLowerInvariant();

    return $"sha256:{hash}";
  }

  private static string ComputeDebugTraceHash(
    string traceVersion,
    IReadOnlyList<ReplayDebugTraceEntry> entries)
  {
    var hashInput = new ReplayDebugTraceHashInput(traceVersion, entries);
    var canonicalBytes = new CanonicalJsonSerializer().SerializeToCanonicalBytes(hashInput);
    var hash = Convert.ToHexString(SHA256.HashData(canonicalBytes)).ToLowerInvariant();

    return $"sha256:{hash}";
  }

  private sealed record ReplayRngTraceHashInput(
    string TraceVersion,
    IReadOnlyList<ReplayRngTraceEntry> Rolls);

  private sealed record ReplayDebugTraceHashInput(
    string TraceVersion,
    IReadOnlyList<ReplayDebugTraceEntry> Entries);
}
