using System.Net.Http.Json;
using System.Text.Json;
using Game.Api;
using Game.Application.Matches;
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

public sealed class ReferenceHeadlessClientDryRunTests
{
  private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-07-02T00:00:00Z");

  [Fact]
  public async Task ReferenceHeadlessClientCompletesUnityContractDryRunThroughPublicApi()
  {
    var report = await RunReferenceSmoke("unity-dry-run-match", "unity-dry-run", writeReport: false);

    Assert.True(report.IsTerminal);
    Assert.True(report.ReplayVerified);
    Assert.Equal(report.StateHash, report.ReplayFinalStateHash);
    Assert.True(report.EventSequenceVerified);
    Assert.True(report.ReconnectStateHashMatches);
    Assert.Contains("END_TURN", report.SubmittedCommandTypes);
    Assert.True(report.PolledEventCount >= report.SubmissionEventCount);
    Assert.Equal(3, report.ArtifactRefs.Count);
    Assert.All(report.ArtifactRefs, artifactRef =>
      Assert.Contains(artifactRef, report.ListedArtifactRefs));
    Assert.Contains("unity-dry-run-match/unity-dry-run/events.json", report.ArtifactRefs);
    Assert.Contains("unity-dry-run-match/unity-dry-run/reconnect.json", report.ArtifactRefs);
    Assert.Contains("unity-dry-run-match/unity-dry-run/replay.json", report.ArtifactRefs);
  }

  [Fact]
  public async Task UnityTwoBotPublicApiFullMatchSmokeCompletesAndWritesReport()
  {
    var report = await RunReferenceSmoke("unity-bot-smoke-match", "unity-bot-smoke", writeReport: true);

    Assert.True(report.IsTerminal);
    Assert.True(report.ReplayVerified);
    Assert.Equal(report.StateHash, report.ReplayFinalStateHash);
    Assert.True(report.EventSequenceVerified);
    Assert.True(report.ReconnectStateHashMatches);
    Assert.Contains("CONCEDE", report.SubmittedCommandTypes);
    Assert.True(report.CommandCount > 0);
    Assert.True(report.PolledEventCount > 0);
  }

  private static async Task<ReferenceHeadlessClientReport> RunReferenceSmoke(
    string matchId,
    string reportKind,
    bool writeReport)
  {
    var root = Path.Combine(Path.GetTempPath(), $"card-game-unity-dry-run-{Guid.NewGuid():N}");
    var startedAt = DateTimeOffset.UtcNow;
    try
    {
      await using var factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
          builder.UseSetting("Storage:RootPath", root);
          builder.UseSetting("Api:WriteRateLimit:PermitLimit", "512");
          builder.UseSetting("Api:WriteRateLimit:WindowSeconds", "60");
          builder.ConfigureAppConfiguration((_, configuration) =>
          {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
              ["Storage:RootPath"] = root,
              ["Api:WriteRateLimit:PermitLimit"] = "512",
              ["Api:WriteRateLimit:WindowSeconds"] = "60"
            });
          });
        });
      using var client = factory.CreateClient();
      var catalog = CreateCatalog();

      await Store(client, "/catalog/snapshots", catalog);
      await Store(client, "/rulesets/profiles", CreateProfile());
      await Store(client, "/decks", CreateDeck("deck-p1", "player-1", catalog.SnapshotId));
      await Store(client, "/decks", CreateDeck("deck-p2", "player-2", catalog.SnapshotId));

      var report = await ReferenceHeadlessClientRunner.RunAsync(
        client,
        new ReferenceHeadlessClientRequest(
          matchId,
          "deck-p1",
          "deck-p2",
          MaxSteps: 64,
          NonConcedeStepLimit: 8),
        TestContext.Current.CancellationToken);

      using var stateRequest = new HttpRequestMessage(HttpMethod.Get, $"matches/{matchId}/state");
      stateRequest.Headers.Add("X-Session-Token", $"{matchId}-session-s1");
      using var stateResponse = await client.SendAsync(stateRequest, TestContext.Current.CancellationToken);
      stateResponse.EnsureSuccessStatusCode();
      var spectatorState = await stateResponse.Content.ReadFromJsonAsync<MatchStateView>(cancellationToken: TestContext.Current.CancellationToken);
      Assert.NotNull(spectatorState);
      Assert.All(spectatorState.Players.Values, player =>
      {
        Assert.Empty(player.Zones.Hand.Cards);
        Assert.Empty(player.Zones.Deck.Cards);
        Assert.Empty(player.Zones.SecretZone.Cards);
        Assert.Empty(player.Zones.SetAside.Cards);
      });

      if (writeReport)
      {
        WriteUnitySmokeReport(report, reportKind, startedAt, DateTimeOffset.UtcNow);
      }

      return report;
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  private static void WriteUnitySmokeReport(
    ReferenceHeadlessClientReport report,
    string reportKind,
    DateTimeOffset startedAt,
    DateTimeOffset completedAt)
  {
    var artifactRoot = Environment.GetEnvironmentVariable("UNITY_SMOKE_ARTIFACT_ROOT");
    if (string.IsNullOrWhiteSpace(artifactRoot))
    {
      artifactRoot = Path.Combine(Path.GetTempPath(), "card-game-unity-integration-smoke");
    }

    artifactRoot = NormalizeArtifactRoot(artifactRoot);
    var timestamp = completedAt.UtcDateTime.ToString("yyyyMMddTHHmmssZ", System.Globalization.CultureInfo.InvariantCulture);
    var outputDirectory = Path.Combine(artifactRoot, timestamp);
    Directory.CreateDirectory(outputDirectory);
    var smokeReport = new UnityBotMatchSmokeReport(
      report.MatchId,
      ["deck-p1", "deck-p2"],
      report.CommandCount,
      report.SubmittedCommandTypes,
      report.PolledEventCount,
      report.StateHash,
      report.TerminalReason,
      report.ReplayVerified,
      report.EventSequenceVerified,
      report.ReconnectStateHashMatches,
      ["turn", "concede", "event-poll", "reconnect", "replay"],
      ["spectator-hidden-zones-redacted"],
      report.ArtifactRefs,
      startedAt,
      completedAt,
      (completedAt - startedAt).TotalMilliseconds,
      reportKind);

    File.WriteAllText(
      Path.Combine(outputDirectory, "report.json"),
      JsonSerializer.Serialize(smokeReport, new JsonSerializerOptions(JsonSerializerDefaults.Web)
      {
        WriteIndented = true
      }));
  }

  private static string NormalizeArtifactRoot(string artifactRoot)
  {
    if (OperatingSystem.IsWindows()
      && artifactRoot.Length > "/mnt/c/".Length
      && artifactRoot.StartsWith("/mnt/", StringComparison.Ordinal)
      && artifactRoot[6] == '/')
    {
      var drive = char.ToUpperInvariant(artifactRoot[5]);
      var tail = artifactRoot[7..].Replace('/', Path.DirectorySeparatorChar);
      return $"{drive}:{Path.DirectorySeparatorChar}{tail}";
    }

    return artifactRoot;
  }

  private static async Task Store<T>(HttpClient client, string path, T body)
  {
    using var response = await client.PostAsJsonAsync(path, body, TestContext.Current.CancellationToken);
    response.EnsureSuccessStatusCode();
  }

  private static CatalogSnapshot CreateCatalog()
  {
    var card = CreateCard();
    return new CatalogSnapshot(
      "catalog-unity-dry-run",
      "ruleset_mvp_core_v1",
      "sha256:unity-dry-run-profile",
      FixedNow,
      [card]);
  }

  private static CardVersion CreateCard() =>
    new(
      "card_unity_dry_run_minion",
      "card_unity_dry_run_minion_v1",
      "card-dsl-0.2.0",
      "engine-bootstrap-0.3.0",
      "catalog-unity-dry-run",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "sha256:unity-dry-run-profile",
      FixedNow,
      new CardDefinition(
        "card_unity_dry_run_minion",
        "card_unity_dry_run_minion_v1",
        "card-dsl-0.2.0",
        "engine-bootstrap-0.3.0",
        "primitive-registry-bootstrap-0.2.0",
        "ruleset_mvp_core_v1",
        "catalog-unity-dry-run",
        new StaticCardData(
          "Unity Dry Run Minion",
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
        new CardTextGeneration("A public API dry-run minion.", FlavorText: null),
        new BalanceMetadata(1, 0, []),
        new AssetMetadata(null, null, null),
        new AuthoringMetadata("integration-test", FixedNow, "pipeline-v1", [])));

  private static DeckDefinition CreateDeck(string deckId, string playerId, string catalogId) =>
    new(
      deckId,
      playerId,
      catalogId,
      "ruleset_mvp_core_v1",
      [new DeckCardRef("card_unity_dry_run_minion", 1)]);

  private static ResolvedRulesetProfile CreateProfile() =>
    new(
      "resolved-unity-dry-run",
      "ruleset_mvp_core_v1",
      "0.2.0",
      FixedNow,
      "integration-test",
      "sha256:unity-dry-run-profile",
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

  private sealed record UnityBotMatchSmokeReport(
    string MatchId,
    IReadOnlyList<string> DecksUsed,
    int CommandsSubmitted,
    IReadOnlyList<string> CommandsUsed,
    int EventCount,
    string FinalStateHash,
    string TerminalReason,
    bool ReplayVerified,
    bool EventSequenceMonotonic,
    bool ReconnectStateHashMatches,
    IReadOnlyList<string> EffectFamiliesObserved,
    IReadOnlyList<string> HiddenInfoAssertions,
    IReadOnlyList<string> EvidenceArtifactRefs,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    double DurationMs,
    string ReportKind);
}
