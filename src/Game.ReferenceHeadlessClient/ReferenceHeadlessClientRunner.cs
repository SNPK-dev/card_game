using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game.ReferenceHeadlessClient;

public static class ReferenceHeadlessClientRunner
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
  {
    Converters = { new JsonStringEnumConverter() }
  };

  public static async Task<ReferenceHeadlessClientReport> RunAsync(
    HttpClient http,
    ReferenceHeadlessClientRequest request,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(http);
    ArgumentNullException.ThrowIfNull(request);

    var player1 = await CreateSession(http, "player-1", $"{request.MatchId}-session-p1", cancellationToken);
    var player2 = await CreateSession(http, "player-2", $"{request.MatchId}-session-p2", cancellationToken);
    var spectator = await CreateSession(http, "spectator-1", $"{request.MatchId}-session-s1", cancellationToken);
    var start = await Post<StartMatchRequest, StartMatchResponse>(
      http,
      "matches",
      new StartMatchRequest(
        request.MatchId,
        "reference-headless-game",
        [request.Deck1Id, request.Deck2Id],
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["player-1"] = player1.Session!.SessionToken,
          ["player-2"] = player2.Session!.SessionToken
        },
        "player-1",
        [spectator.Session!.SessionToken]),
      cancellationToken);

    if (!start.Succeeded || start.State is null)
    {
      throw new InvalidOperationException("Match start failed: " + JoinDiagnostics(start.Diagnostics));
    }

    var state = start.State;
    var sessionByPlayer = new Dictionary<string, string>(StringComparer.Ordinal)
    {
      ["player-1"] = player1.Session.SessionToken,
      ["player-2"] = player2.Session.SessionToken
    };

    var submittedCommandTypes = new List<string>();
    var eventBatches = new List<JsonElement>();
    var allEvents = new List<JsonElement>();
    var latestNextEventSeq = state.NextEventSeq;

    for (var step = 0; step < request.MaxSteps && !state.IsTerminal; step++)
    {
      var activePlayer = state.ActivePlayerId ?? throw new InvalidOperationException("Active player is missing.");
      var legal = await GetLegalActions(http, request.MatchId, activePlayer, sessionByPlayer[activePlayer], cancellationToken);
      var command = SelectCommand(legal.LegalActions, step, request.NonConcedeStepLimit);
      var submission = await Post<SubmitCommandRequest, SubmitCommandResponse>(
        http,
        $"matches/{request.MatchId}/commands",
        new SubmitCommandRequest(
          sessionByPlayer[activePlayer],
          command,
          $"reference-command-{step + 1:D4}",
          state.StateHash,
          $"reference-correlation-{step + 1:D4}"),
        cancellationToken);
      if (!submission.Succeeded || submission.State is null)
      {
        throw new InvalidOperationException("Command rejected: " + JoinDiagnostics(submission.Diagnostics));
      }

      submittedCommandTypes.Add(command.CommandType);
      eventBatches.AddRange(submission.Events);
      state = submission.State;

      var events = await Get<EventsResponse>(
        http,
        $"matches/{request.MatchId}/events?afterEventSeq=0",
        sessionByPlayer[activePlayer],
        cancellationToken);
      if (!events.Succeeded)
      {
        throw new InvalidOperationException("Event poll failed: " + JoinDiagnostics(events.Diagnostics));
      }

      allEvents.Clear();
      allEvents.AddRange(events.Events);
      latestNextEventSeq = events.NextEventSeq;
    }

    if (!state.IsTerminal)
    {
      throw new InvalidOperationException(
        $"Reference client did not reach terminal state after {request.MaxSteps} commands.");
    }

    var reconnect = await PostWithSession<ReconnectResponse>(
      http,
      $"matches/{request.MatchId}/reconnect?afterEventSeq=0",
      sessionByPlayer[state.ActivePlayerId ?? "player-1"],
      cancellationToken);
    if (!reconnect.Succeeded || reconnect.State is null)
    {
      throw new InvalidOperationException("Reconnect failed: " + JoinDiagnostics(reconnect.Diagnostics));
    }

    var replay = await PostWithSession<ReplayResponse>(
      http,
      $"matches/{request.MatchId}/replay/verify",
      sessionByPlayer[state.ActivePlayerId ?? "player-1"],
      cancellationToken);
    if (!replay.Succeeded || !replay.Replay.IsVerified)
    {
      throw new InvalidOperationException("Replay verification failed: " + JoinDiagnostics(replay.Diagnostics));
    }

    var eventOrderingVerified = EventsAreStrictlyOrdered(allEvents);
    var artifactRefs = await StoreDryRunArtifacts(
      http,
      request.MatchId,
      allEvents,
      reconnect,
      replay,
      cancellationToken);
    var listedArtifacts = await Get<IReadOnlyList<MatchEvidenceArtifactDto>>(
      http,
      $"matches/{request.MatchId}/evidence-artifacts",
      sessionByPlayer[state.ActivePlayerId ?? "player-1"],
      cancellationToken);

    return new ReferenceHeadlessClientReport(
      request.MatchId,
      state.CommandCount,
      state.IsTerminal,
      state.TerminalReason,
      state.StateHash,
      reconnect.NextEventSeq,
      replay.Replay.IsVerified,
      replay.Replay.FinalStateHash,
      replay.Replay.DerivedEventCount,
      replay.Replay.RngTrace?.TraceRef,
      replay.Replay.DebugTrace?.TraceRef,
      submittedCommandTypes,
      eventBatches.Count,
      allEvents.Count,
      latestNextEventSeq,
      eventOrderingVerified,
      reconnect.State.StateHash == state.StateHash,
      artifactRefs,
      listedArtifacts.Select(artifact => artifact.ArtifactRef).Order(StringComparer.Ordinal).ToArray());
  }

  private static MatchCommandDto SelectCommand(
    IReadOnlyList<MatchCommandDto> legalActions,
    int step,
    int nonConcedeStepLimit)
  {
    if (step >= nonConcedeStepLimit)
    {
      return legalActions.FirstOrDefault(candidate => candidate.CommandType == "CONCEDE")
        ?? legalActions.FirstOrDefault()
        ?? throw new InvalidOperationException("No legal actions are available.");
    }

    return legalActions.FirstOrDefault(candidate => candidate.CommandType != "CONCEDE")
      ?? legalActions.FirstOrDefault()
      ?? throw new InvalidOperationException("No legal actions are available.");
  }

  private static async Task<IReadOnlyList<string>> StoreDryRunArtifacts(
    HttpClient http,
    string matchId,
    IReadOnlyList<JsonElement> events,
    ReconnectResponse reconnect,
    ReplayResponse replay,
    CancellationToken cancellationToken)
  {
    var createdAt = DateTimeOffset.Parse("2026-07-02T00:00:00Z");
    var artifacts = new[]
    {
      CreateArtifact(matchId, "events", events, createdAt),
      CreateArtifact(matchId, "reconnect", reconnect, createdAt),
      CreateArtifact(matchId, "replay", replay.Replay, createdAt)
    };

    foreach (var artifact in artifacts)
    {
      await Post<MatchEvidenceArtifactDto, MatchEvidenceArtifactDto>(
        http,
        "match-evidence-artifacts",
        artifact,
        cancellationToken);
    }

    return artifacts.Select(artifact => artifact.ArtifactRef).ToArray();
  }

  private static MatchEvidenceArtifactDto CreateArtifact<T>(
    string matchId,
    string kind,
    T content,
    DateTimeOffset createdAt)
  {
    var bytes = JsonSerializer.SerializeToUtf8Bytes(content, Json);
    var hashBytes = SHA256.HashData(bytes);
    var hash = "sha256:" + Convert.ToHexString(hashBytes).ToLowerInvariant();
    return new MatchEvidenceArtifactDto(
      $"{matchId}/unity-dry-run/{kind}.json",
      matchId,
      $"UNITY_DRY_RUN_{kind.ToUpperInvariant()}",
      hash,
      createdAt);
  }

  private static bool EventsAreStrictlyOrdered(IReadOnlyList<JsonElement> events)
  {
    long previous = 0;
    foreach (var current in events)
    {
      if (!current.TryGetProperty("eventSeq", out var eventSeqProperty))
      {
        return false;
      }

      var eventSeq = eventSeqProperty.GetInt64();
      if (eventSeq <= previous)
      {
        return false;
      }

      previous = eventSeq;
    }

    return events.Count > 0;
  }

  private static async Task<CreateSessionResponse> CreateSession(
    HttpClient http,
    string playerId,
    string token,
    CancellationToken cancellationToken) =>
    await Post<CreateSessionRequest, CreateSessionResponse>(
      http,
      "sessions",
      new CreateSessionRequest(playerId, token),
      cancellationToken);

  private static async Task<LegalActionsResponse> GetLegalActions(
    HttpClient http,
    string currentMatchId,
    string playerId,
    string sessionToken,
    CancellationToken cancellationToken) =>
    await Get<LegalActionsResponse>(
      http,
      $"matches/{currentMatchId}/players/{playerId}/legal-actions",
      sessionToken,
      cancellationToken);

  private static async Task<TResponse> Get<TResponse>(
    HttpClient http,
    string path,
    string sessionToken,
    CancellationToken cancellationToken)
  {
    using var request = new HttpRequestMessage(HttpMethod.Get, path);
    request.Headers.Add("X-Session-Token", sessionToken);
    using var response = await http.SendAsync(request, cancellationToken);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<TResponse>(Json, cancellationToken)
      ?? throw new InvalidOperationException($"Empty response from {path}.");
  }

  private static async Task<TResponse> Post<TRequest, TResponse>(
    HttpClient http,
    string path,
    TRequest body,
    CancellationToken cancellationToken)
  {
    using var response = await http.PostAsJsonAsync(path, body, Json, cancellationToken);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<TResponse>(Json, cancellationToken)
      ?? throw new InvalidOperationException($"Empty response from {path}.");
  }

  private static async Task<TResponse> PostWithSession<TResponse>(
    HttpClient http,
    string path,
    string sessionToken,
    CancellationToken cancellationToken)
  {
    using var request = new HttpRequestMessage(HttpMethod.Post, path);
    request.Headers.Add("X-Session-Token", sessionToken);
    using var response = await http.SendAsync(request, cancellationToken);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<TResponse>(Json, cancellationToken)
      ?? throw new InvalidOperationException($"Empty response from {path}.");
  }

  private static string JoinDiagnostics(IReadOnlyList<DiagnosticDto> diagnostics) =>
    string.Join("; ", diagnostics.Select(diagnostic => diagnostic.Code));
}

public sealed record ReferenceHeadlessClientRequest(
  string MatchId,
  string Deck1Id,
  string Deck2Id,
  int MaxSteps = 128,
  int NonConcedeStepLimit = 32);

public sealed record ReferenceHeadlessClientReport(
  string MatchId,
  int CommandCount,
  bool IsTerminal,
  string TerminalReason,
  string StateHash,
  long ReconnectNextEventSeq,
  bool ReplayVerified,
  string? ReplayFinalStateHash,
  int ReplayDerivedEventCount,
  string? RngTraceRef,
  string? DebugTraceRef,
  IReadOnlyList<string> SubmittedCommandTypes,
  int SubmissionEventCount,
  int PolledEventCount,
  long PolledNextEventSeq,
  bool EventSequenceVerified,
  bool ReconnectStateHashMatches,
  IReadOnlyList<string> ArtifactRefs,
  IReadOnlyList<string> ListedArtifactRefs);

public sealed record CreateSessionRequest(string PlayerId, string? SessionToken = null);

public sealed record CreateSessionResponse(bool Succeeded, SessionDto? Session, IReadOnlyList<DiagnosticDto> Diagnostics);

public sealed record SessionDto(string SessionToken, string PlayerId, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public sealed record StartMatchRequest(
  string MatchId,
  string GameId,
  IReadOnlyList<string> DeckIds,
  IReadOnlyDictionary<string, string> PlayerSessionTokens,
  string? StartingPlayerId = null,
  IReadOnlyList<string>? SpectatorSessionTokens = null);

public sealed record StartMatchResponse(bool Succeeded, string? MatchId, MatchStateDto? State, IReadOnlyList<DiagnosticDto> Diagnostics);

public sealed record SubmitCommandRequest(
  string SessionToken,
  MatchCommandDto Command,
  string? CommandId = null,
  string? ExpectedPreStateHash = null,
  string? CorrelationId = null);

public sealed record SubmitCommandResponse(
  bool Succeeded,
  MatchStateDto? State,
  IReadOnlyList<JsonElement> Events,
  IReadOnlyList<DiagnosticDto> Diagnostics);

public sealed record LegalActionsResponse(
  bool Succeeded,
  IReadOnlyList<MatchCommandDto> LegalActions,
  IReadOnlyList<JsonElement> LegalDiagnostics,
  IReadOnlyList<DiagnosticDto> Diagnostics);

public sealed record EventsResponse(
  bool Succeeded,
  IReadOnlyList<JsonElement> Events,
  long NextEventSeq,
  IReadOnlyList<DiagnosticDto> Diagnostics);

public sealed record ReconnectResponse(
  bool Succeeded,
  MatchStateDto? State,
  IReadOnlyList<JsonElement> Events,
  long NextEventSeq,
  IReadOnlyList<DiagnosticDto> Diagnostics);

public sealed record ReplayResponse(bool Succeeded, ReplayDto Replay, IReadOnlyList<DiagnosticDto> Diagnostics);

public sealed record ReplayDto(
  bool IsVerified,
  string? FinalStateHash,
  int DerivedEventCount,
  IReadOnlyList<string> RngRollRefs,
  TraceDto? RngTrace,
  IReadOnlyList<JsonElement> Diagnostics,
  TraceDto? DebugTrace = null);

public sealed record TraceDto(string TraceRef, string ContentHash, IReadOnlyList<JsonElement> Entries);

public sealed record MatchStateDto(
  string MatchId,
  int Revision,
  string StateHash,
  string? ActivePlayerId,
  int TurnNumber,
  string Phase,
  bool IsTerminal,
  string TerminalReason,
  JsonElement Players,
  JsonElement PendingChoices,
  long NextEventSeq,
  int CommandCount);

public sealed record MatchCommandDto(
  string CommandType,
  string PlayerId,
  IReadOnlyDictionary<string, int>? Payload = null,
  IReadOnlyDictionary<string, string>? Refs = null);

public sealed record MatchEvidenceArtifactDto(
  string ArtifactRef,
  string MatchId,
  string ArtifactKind,
  string ContentHash,
  DateTimeOffset CreatedAt);

public sealed record DiagnosticDto(string Code, string Path, string Identifier, string Message);
