using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Game.Api;
using Game.Generator.Acceptance;
using Game.ReferenceHeadlessClient;
using Game.Shared.Catalog;
using Game.Shared.Decks;
using Game.Shared.Dsl;
using Game.Shared.Primitives;
using Game.Shared.Ruleset;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Game.Integration.Tests;

public sealed class ReadinessEvidenceRunTests
{
  private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-07-02T00:00:00Z");
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
  {
    Converters = { new JsonStringEnumConverter() }
  };

  [Fact]
  public async Task PublicApiEvidenceRunsProduceReadinessArtifactReport()
  {
    var root = Path.Combine(Path.GetTempPath(), $"card-game-readiness-evidence-{Guid.NewGuid():N}");
    try
    {
      var security = await RunSecurityAbuseEvidence(root);
      var recovery = await RunRestartRecoveryEvidence(root);
      var load = await RunLoadSmokeEvidence(root);

      var report = new ProductionReadinessEvidenceReportBuilder().Build(new ProductionReadinessEvidenceReportRequest(
        "readiness-evidence-public-api",
        FixedNow,
        [security, recovery, load]));

      Assert.True(report.IsAccepted, string.Join("; ", report.Diagnostics));
      Assert.Contains(security.ArtifactRef, report.ReadinessEvidence.Security.EvidenceRefs);
      Assert.Contains(recovery.ArtifactRef, report.ReadinessEvidence.Recovery.EvidenceRefs);
      Assert.Contains(load.ArtifactRef, report.ReadinessEvidence.Load.EvidenceRefs);
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  private static async Task<ProductionReadinessEvidenceArtifact> RunSecurityAbuseEvidence(string root)
  {
    var storageRoot = Path.Combine(root, "security-storage");
    await using var factory = CreateFactory(storageRoot, writeLimit: 512);
    using var client = factory.CreateClient();
    await PublishFixture(client, "security");

    var player1 = await CreateSession(client, "player-1", "security-session-p1");
    var player2 = await CreateSession(client, "player-2", "security-session-p2");
    var spectator = await CreateSession(client, "spectator-1", "security-session-s1");
    var start = await Post<StartMatchRequest, StartMatchResponse>(
      client,
      "matches",
      CreateStartRequest(
        "match-security-abuse",
        "deck-security-p1",
        "deck-security-p2",
        player1.Session!.SessionToken,
        player2.Session!.SessionToken,
        spectator.Session!.SessionToken));
    Assert.True(start.Succeeded, JoinDiagnostics(start.Diagnostics));

    var legal = await Get<LegalActionsResponse>(
      client,
      "matches/match-security-abuse/players/player-1/legal-actions",
      player1.Session.SessionToken);
    var command = Assert.Single(legal.LegalActions, candidate => candidate.CommandType == "END_TURN");

    using var crossPlayerLegal = await SendWithSession(
      client,
      HttpMethod.Get,
      "matches/match-security-abuse/players/player-1/legal-actions",
      player2.Session.SessionToken);
    using var spectatorLegal = await SendWithSession(
      client,
      HttpMethod.Get,
      "matches/match-security-abuse/players/player-1/legal-actions",
      spectator.Session.SessionToken);
    using var spectatorCommand = await client.PostAsJsonAsync(
      "matches/match-security-abuse/commands",
      new SubmitCommandRequest(
        spectator.Session.SessionToken,
        command,
        "security-spectator-command",
        start.State!.StateHash),
      Json,
      TestContext.Current.CancellationToken);
    using var staleHashCommand = await client.PostAsJsonAsync(
      "matches/match-security-abuse/commands",
      new SubmitCommandRequest(
        player1.Session.SessionToken,
        command,
        "security-stale-hash-command",
        "sha256:stale"),
      Json,
      TestContext.Current.CancellationToken);

    var evidence = new
    {
      scenario = "E2E-009",
      matchId = "match-security-abuse",
      checks = new[]
      {
        new { name = "cross_player_legal_actions", statusCode = (int)crossPlayerLegal.StatusCode },
        new { name = "spectator_legal_actions", statusCode = (int)spectatorLegal.StatusCode },
        new { name = "spectator_command", statusCode = (int)spectatorCommand.StatusCode },
        new { name = "stale_hash_command", statusCode = (int)staleHashCommand.StatusCode }
      }
    };

    Assert.Equal(HttpStatusCode.Forbidden, crossPlayerLegal.StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, spectatorLegal.StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, spectatorCommand.StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, staleHashCommand.StatusCode);

    return await WriteEvidence(root, "L9_SECURITY", "SECURITY_ABUSE_REPORT", "artifacts/security/e2e-009-abuse.json", evidence);
  }

  private static async Task<ProductionReadinessEvidenceArtifact> RunRestartRecoveryEvidence(string root)
  {
    var storageRoot = Path.Combine(root, "recovery-storage");
    string stateHash;
    long nextEventSeq;
    int commandCount;
    string player1Token;

    await using (var factory = CreateFactory(storageRoot, writeLimit: 512))
    {
      using var client = factory.CreateClient();
      await PublishFixture(client, "recovery");
      var player1 = await CreateSession(client, "player-1", "recovery-session-p1");
      var player2 = await CreateSession(client, "player-2", "recovery-session-p2");
      player1Token = player1.Session!.SessionToken;
      var start = await Post<StartMatchRequest, StartMatchResponse>(
        client,
        "matches",
        CreateStartRequest(
          "match-restart-recovery",
          "deck-recovery-p1",
          "deck-recovery-p2",
          player1.Session.SessionToken,
          player2.Session!.SessionToken));
      Assert.True(start.Succeeded, JoinDiagnostics(start.Diagnostics));

      var legal = await Get<LegalActionsResponse>(
        client,
        "matches/match-restart-recovery/players/player-1/legal-actions",
        player1Token);
      var endTurn = Assert.Single(legal.LegalActions, candidate => candidate.CommandType == "END_TURN");
      var submission = await Post<SubmitCommandRequest, SubmitCommandResponse>(
        client,
        "matches/match-restart-recovery/commands",
        new SubmitCommandRequest(player1Token, endTurn, "recovery-command-0001", start.State!.StateHash));
      Assert.True(submission.Succeeded, JoinDiagnostics(submission.Diagnostics));
      stateHash = submission.State!.StateHash;
      nextEventSeq = submission.State.NextEventSeq;
      commandCount = submission.State.CommandCount;
    }

    await using var reloadedFactory = CreateFactory(storageRoot, writeLimit: 512);
    using var reloadedClient = reloadedFactory.CreateClient();
    var reloadedState = await Get<MatchStateDto>(
      reloadedClient,
      "matches/match-restart-recovery/state",
      player1Token);
    var replay = await PostWithSession<ReplayResponse>(
      reloadedClient,
      "matches/match-restart-recovery/replay/verify",
      player1Token);

    Assert.Equal(stateHash, reloadedState.StateHash);
    Assert.Equal(nextEventSeq, reloadedState.NextEventSeq);
    Assert.Equal(commandCount, reloadedState.CommandCount);
    Assert.True(replay.Succeeded, JoinDiagnostics(replay.Diagnostics));
    Assert.True(replay.Replay.IsVerified);

    var evidence = new
    {
      scenario = "E2E-007",
      matchId = "match-restart-recovery",
      stateHash,
      reloadedStateHash = reloadedState.StateHash,
      nextEventSeq,
      reloadedNextEventSeq = reloadedState.NextEventSeq,
      commandCount,
      replayVerified = replay.Replay.IsVerified,
      replayFinalStateHash = replay.Replay.FinalStateHash
    };

    return await WriteEvidence(root, "L10_RECOVERY", "RECOVERY_RESTART_REPORT", "artifacts/recovery/e2e-007-restart.json", evidence);
  }

  private static async Task<ProductionReadinessEvidenceArtifact> RunLoadSmokeEvidence(string root)
  {
    var storageRoot = Path.Combine(root, "load-storage");
    await using var factory = CreateFactory(storageRoot, writeLimit: 1024);
    using var client = factory.CreateClient();
    await PublishFixture(client, "load");

    var report = await ReferenceHeadlessClientRunner.RunAsync(
      client,
      new ReferenceHeadlessClientRequest(
        "match-load-smoke",
        "deck-load-p1",
        "deck-load-p2",
        MaxSteps: 64,
        NonConcedeStepLimit: 8),
      TestContext.Current.CancellationToken);

    var stopwatch = Stopwatch.StartNew();
    var statusCodes = new List<int>();
    for (var index = 0; index < 80; index++)
    {
      using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
      statusCodes.Add((int)response.StatusCode);
    }
    stopwatch.Stop();

    Assert.True(report.IsTerminal);
    Assert.True(report.ReplayVerified);
    Assert.All(statusCodes, statusCode => Assert.Equal(200, statusCode));

    var evidence = new
    {
      scenario = "L10_LOAD_CI_SMOKE",
      requestCount = statusCodes.Count,
      elapsedMilliseconds = stopwatch.ElapsedMilliseconds,
      successCount = statusCodes.Count(statusCode => statusCode == 200),
      unityDryRunTerminal = report.IsTerminal,
      replayVerified = report.ReplayVerified,
      commandCount = report.CommandCount,
      eventCount = report.PolledEventCount
    };

    return await WriteEvidence(root, "L10_LOAD", "LOAD_TEST_REPORT", "artifacts/load/load-ci-smoke.json", evidence);
  }

  private static WebApplicationFactory<Program> CreateFactory(string storageRoot, int writeLimit) =>
    new WebApplicationFactory<Program>()
      .WithWebHostBuilder(builder =>
      {
        builder.UseSetting("Storage:RootPath", storageRoot);
        builder.UseSetting("Api:WriteRateLimit:PermitLimit", writeLimit.ToString());
        builder.UseSetting("Api:WriteRateLimit:WindowSeconds", "60");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
          configuration.AddInMemoryCollection(new Dictionary<string, string?>
          {
            ["Storage:RootPath"] = storageRoot,
            ["Api:WriteRateLimit:PermitLimit"] = writeLimit.ToString(),
            ["Api:WriteRateLimit:WindowSeconds"] = "60"
          });
        });
      });

  private static async Task PublishFixture(HttpClient client, string id)
  {
    var catalog = CreateCatalog(id);
    await Store(client, "/catalog/snapshots", catalog);
    await Store(client, "/rulesets/profiles", CreateProfile(id));
    await Store(client, "/decks", CreateDeck($"deck-{id}-p1", "player-1", catalog.SnapshotId, id));
    await Store(client, "/decks", CreateDeck($"deck-{id}-p2", "player-2", catalog.SnapshotId, id));
  }

  private static StartMatchRequest CreateStartRequest(
    string matchId,
    string deck1Id,
    string deck2Id,
    string player1Token,
    string player2Token,
    string? spectatorToken = null) =>
    new(
      matchId,
      $"{matchId}-game",
      [deck1Id, deck2Id],
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["player-1"] = player1Token,
        ["player-2"] = player2Token
      },
      "player-1",
      spectatorToken is null ? null : [spectatorToken]);

  private static async Task<ProductionReadinessEvidenceArtifact> WriteEvidence<T>(
    string root,
    string gateId,
    string artifactKind,
    string artifactRef,
    T content)
  {
    var path = Path.Combine(root, artifactRef.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var bytes = JsonSerializer.SerializeToUtf8Bytes(content, Json);
    await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
    var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    return new ProductionReadinessEvidenceArtifact(gateId, artifactKind, artifactRef, hash, FixedNow);
  }

  private static async Task Store<T>(HttpClient client, string path, T body)
  {
    using var response = await client.PostAsJsonAsync(path, body, Json, TestContext.Current.CancellationToken);
    response.EnsureSuccessStatusCode();
  }

  private static async Task<CreateSessionResponse> CreateSession(HttpClient client, string playerId, string token) =>
    await Post<CreateSessionRequest, CreateSessionResponse>(
      client,
      "sessions",
      new CreateSessionRequest(playerId, token));

  private static async Task<TResponse> Get<TResponse>(HttpClient client, string path, string sessionToken)
  {
    using var request = new HttpRequestMessage(HttpMethod.Get, path);
    request.Headers.Add("X-Session-Token", sessionToken);
    using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<TResponse>(Json, TestContext.Current.CancellationToken)
      ?? throw new InvalidOperationException($"Empty response from {path}.");
  }

  private static async Task<HttpResponseMessage> SendWithSession(
    HttpClient client,
    HttpMethod method,
    string path,
    string sessionToken)
  {
    using var request = new HttpRequestMessage(method, path);
    request.Headers.Add("X-Session-Token", sessionToken);
    return await client.SendAsync(request, TestContext.Current.CancellationToken);
  }

  private static async Task<TResponse> Post<TRequest, TResponse>(HttpClient client, string path, TRequest body)
  {
    using var response = await client.PostAsJsonAsync(path, body, Json, TestContext.Current.CancellationToken);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<TResponse>(Json, TestContext.Current.CancellationToken)
      ?? throw new InvalidOperationException($"Empty response from {path}.");
  }

  private static async Task<TResponse> PostWithSession<TResponse>(HttpClient client, string path, string sessionToken)
  {
    using var request = new HttpRequestMessage(HttpMethod.Post, path);
    request.Headers.Add("X-Session-Token", sessionToken);
    using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<TResponse>(Json, TestContext.Current.CancellationToken)
      ?? throw new InvalidOperationException($"Empty response from {path}.");
  }

  private static string JoinDiagnostics(IReadOnlyList<DiagnosticDto> diagnostics) =>
    string.Join("; ", diagnostics.Select(diagnostic => diagnostic.Code));

  private static CatalogSnapshot CreateCatalog(string id)
  {
    var card = CreateCard(id);
    return new CatalogSnapshot(
      $"catalog-{id}",
      "ruleset_mvp_core_v1",
      $"sha256:{id}-profile",
      FixedNow,
      [card]);
  }

  private static CardVersion CreateCard(string id) =>
    new(
      $"card_{id}_minion",
      $"card_{id}_minion_v1",
      "card-dsl-0.2.0",
      "engine-bootstrap-0.3.0",
      $"catalog-{id}",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      $"sha256:{id}-profile",
      FixedNow,
      new CardDefinition(
        $"card_{id}_minion",
        $"card_{id}_minion_v1",
        "card-dsl-0.2.0",
        "engine-bootstrap-0.3.0",
        "primitive-registry-bootstrap-0.2.0",
        "ruleset_mvp_core_v1",
        $"catalog-{id}",
        new StaticCardData(
          $"Readiness {id} Minion",
          CardType.MinionCard,
          ["neutral"],
          Rarity.Common,
          1,
          [],
          [],
          Collectible: true,
          ["mvp"]),
        new EntityDefinition(
          new MinionDefinition(1, 1, [], [], CanAttack: true),
          Spell: null,
          Weapon: null,
          Location: null,
          HeroCard: null,
          HeroPower: null),
        Effects: [],
        new CardTextGeneration("A readiness evidence minion.", FlavorText: null),
        new BalanceMetadata(1, 0, []),
        new AssetMetadata(null, null, null),
        new AuthoringMetadata("integration-test", FixedNow, "pipeline-v1", [])));

  private static DeckDefinition CreateDeck(string deckId, string playerId, string catalogId, string id) =>
    new(
      deckId,
      playerId,
      catalogId,
      "ruleset_mvp_core_v1",
      [new DeckCardRef($"card_{id}_minion", 1)]);

  private static ResolvedRulesetProfile CreateProfile(string id) =>
    new(
      $"resolved-{id}",
      "ruleset_mvp_core_v1",
      "0.2.0",
      FixedNow,
      "integration-test",
      $"sha256:{id}-profile",
      "mvp",
      null,
      [],
      [],
      [],
      ["SIMPLE_PLAY_EFFECT"],
      ["PLAY_CARD", "END_TURN", "CONCEDE"],
      [],
      ["HAND", "DECK", "BOARD", "GRAVEYARD", "SET_ASIDE"],
      ["ON_PLAY"],
      [],
      ["MINION_CARD"],
      ["MINION"],
      ["COMMON"],
      ["neutral"],
      [],
      new RulesetLimits(30, 25, 20, 80, 7, 1, "LIMITED", 3, 2, 3, 3, 3, 2, 2, 3, 1, 7, 10),
      new ValidationPolicyRef("validation", true, true, true, true, true, true, true, true, false),
      new BalancePolicyRef("balance", false, "MEDIUM", [], 0.05, 30, 25, 0.1, 0, 0, "baseline", "seed", "none"),
      new SimulationPolicyRef("simulation", [], [], 0, 0, false, 30, 80),
      new PublicationPolicyRef("publication", true, false, false, false, false, false, false, ["INTERNAL"]),
      new LegacyPolicy(false, false, false, false, true));
}
