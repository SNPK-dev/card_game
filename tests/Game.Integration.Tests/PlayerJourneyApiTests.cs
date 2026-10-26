using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Xunit;
using Game.Shared.Catalog;
using Game.Shared.Dsl;
using Game.Shared.Primitives;
using Game.Application.Players;
using Game.Application.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable xUnit1051

namespace Game.Integration.Tests;

public sealed class PlayerJourneyApiTests
{
  [Fact]
  public async Task RegistrationListsAndPersistsOneOfTwoStarterHeroes()
  {
    await using var factory=new WebApplicationFactory<Program>();using var client=factory.CreateClient();
    await SeedCatalog(client);
    var heroes=await (await client.GetAsync("/auth/starter-heroes")).Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal(2,heroes.GetArrayLength());
    var response=await client.PostAsJsonAsync("/auth/register",new{email="hero-choice@example.test",password="ReleaseReady!123",displayName="Hero Player",starterHeroId="cinder-vanguard"});
    Assert.Equal(HttpStatusCode.OK,response.StatusCode);var auth=await response.Content.ReadFromJsonAsync<AuthBody>();
    client.DefaultRequestHeaders.Add("X-Player-Token",auth!.AccessToken);var me=await (await client.GetAsync("/me")).Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal("cinder-vanguard",me.GetProperty("profile").GetProperty("starterHeroId").GetString());
    var vargDecks=await (await client.GetAsync("/player/decks")).Content.ReadFromJsonAsync<JsonElement>();
    Assert.Single(vargDecks.GetProperty("decks").EnumerateArray());
    client.DefaultRequestHeaders.Remove("X-Player-Token");
    var astraResponse=await client.PostAsJsonAsync("/auth/register",new{email="astra-choice@example.test",password="ReleaseReady!123",displayName="Astra Player",starterHeroId="astral-warden"});
    Assert.Equal(HttpStatusCode.OK,astraResponse.StatusCode);var astraAuth=await astraResponse.Content.ReadFromJsonAsync<AuthBody>();
    client.DefaultRequestHeaders.Add("X-Player-Token",astraAuth!.AccessToken);var astraDecks=await (await client.GetAsync("/player/decks")).Content.ReadFromJsonAsync<JsonElement>();
    Assert.Single(astraDecks.GetProperty("decks").EnumerateArray());
    var vargDeck=vargDecks.GetProperty("decks").EnumerateArray().Single();var astraDeck=astraDecks.GetProperty("decks").EnumerateArray().Single();
    Assert.NotEqual(vargDeck.GetProperty("name").GetString(),astraDeck.GetProperty("name").GetString());
    Assert.NotEqual(
      string.Join(",",vargDeck.GetProperty("cards").EnumerateArray().Select(card=>card.GetProperty("cardVersionId").GetString())),
      string.Join(",",astraDeck.GetProperty("cards").EnumerateArray().Select(card=>card.GetProperty("cardVersionId").GetString())));
    client.DefaultRequestHeaders.Remove("X-Player-Token");
    var invalid=await client.PostAsJsonAsync("/auth/register",new{email="unknown-hero@example.test",password="ReleaseReady!123",displayName="Bad",starterHeroId="unknown"});
    Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
  }

  [Fact]
  public async Task AuthenticationFailuresReturnStableActionableCodes()
  {
    await using var factory=new WebApplicationFactory<Program>();using var client=factory.CreateClient();
    var invalid=await client.PostAsJsonAsync("/auth/register",new{email="bad",password="short",displayName=""});
    Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);Assert.Equal("AUTH_VALIDATION_FAILED",(await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    var login=await client.PostAsJsonAsync("/auth/login",new{email="missing@example.test",password="ReleaseReady!123"});
    Assert.Equal(HttpStatusCode.Unauthorized,login.StatusCode);Assert.Equal("AUTH_INVALID_CREDENTIALS",(await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
  }

  [Fact]
  public async Task LoginRepairsPersistedAllVanillaStarterCollectionFromCanonicalCatalog()
  {
    await using var factory=new WebApplicationFactory<Program>();using var client=factory.CreateClient();
    await SeedCatalog(client,"legacy-all-vanilla",string.Empty);
    var registered=await Register(client,"repair-vanilla@example.test");
    client.DefaultRequestHeaders.Add("X-Player-Token",registered.AccessToken);
    var before=await (await client.GetAsync("/player/collection")).Content.ReadFromJsonAsync<JsonElement>();
    Assert.All(before.EnumerateArray(),card=>Assert.True(card.GetProperty("rulesText").ValueKind is JsonValueKind.String&&string.IsNullOrEmpty(card.GetProperty("rulesText").GetString())));
    client.DefaultRequestHeaders.Remove("X-Player-Token");

    await SeedCatalog(client,CatalogSnapshotQueryService.CanonicalStarterSnapshotId,"Battlecry: Deal 1 damage.");
    var login=await client.PostAsJsonAsync("/auth/login",new{email="repair-vanilla@example.test",password="ReleaseReady!123"});
    var repaired=await login.Content.ReadFromJsonAsync<AuthBody>();client.DefaultRequestHeaders.Add("X-Player-Token",repaired!.AccessToken);
    var after=await (await client.GetAsync("/player/collection")).Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal(15,after.GetArrayLength());
    Assert.All(after.EnumerateArray(),card=>Assert.Contains("Battlecry",card.GetProperty("rulesText").GetString(),StringComparison.Ordinal));
  }

  [Fact]
  public async Task RegisterProfileRefreshAndLogoutUseOwnedAuthenticatedBoundary()
  {
    await using var factory = new WebApplicationFactory<Program>();
    using var client = factory.CreateClient();
    var auth = await Register(client, "owner@example.test");

    client.DefaultRequestHeaders.Add("X-Player-Token", auth.AccessToken);
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/me")).StatusCode);
    var patch = await client.PatchAsJsonAsync("/me", new { displayName = "Owner Prime", boardTheme = "cyberpunk", reducedMotion = true });
    Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
    var body = await patch.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal("Owner Prime", body.GetProperty("profile").GetProperty("displayName").GetString());

    client.DefaultRequestHeaders.Remove("X-Player-Token");
    var refresh = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = auth.RefreshToken });
    Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    var refreshed = await refresh.Content.ReadFromJsonAsync<AuthBody>();
    client.DefaultRequestHeaders.Add("X-Player-Token", refreshed!.AccessToken);
    Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/auth/logout", new { })).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/me")).StatusCode);
  }

  [Fact]
  public async Task CollectionGenerationDeckOwnershipAndRestartArePersisted()
  {
    var root = Path.Combine(Path.GetTempPath(), $"cardgame-player-{Guid.NewGuid():N}");
    try
    {
      AuthBody auth;
      string deckId;
      await using (var factory = Factory(root))
      using (var client = factory.CreateClient())
      {
        await SeedCatalog(client);
        auth = await Register(client, "persist@example.test");
        client.DefaultRequestHeaders.Add("X-Player-Token", auth.AccessToken);
        var starterCollection=await (await client.GetAsync("/player/collection")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(15,starterCollection.GetArrayLength());
        var starterDecks=await (await client.GetAsync("/player/decks")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(starterDecks.GetProperty("decks").EnumerateArray());
        deckId=starterDecks.GetProperty("activeDeckId").GetString()!;
      }

      await using (var factory = Factory(root))
      using (var client = factory.CreateClient())
      {
        var login = await client.PostAsJsonAsync("/auth/login", new { email = "persist@example.test", password = "ReleaseReady!123" });
        var restored = await login.Content.ReadFromJsonAsync<AuthBody>();
        client.DefaultRequestHeaders.Add("X-Player-Token", restored!.AccessToken);
        var decks = await (await client.GetAsync("/player/decks")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(deckId, decks.GetProperty("activeDeckId").GetString());
        var collection = await (await client.GetAsync("/player/collection")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(15, collection.GetArrayLength());
        var firstCard = collection.EnumerateArray().First();
        Assert.StartsWith("Journey Card ", firstCard.GetProperty("displayName").GetString(), StringComparison.Ordinal);
        Assert.Equal("MinionCard", firstCard.GetProperty("cardType").GetString());
        Assert.Equal("Common", firstCard.GetProperty("rarity").GetString());
        Assert.Equal(1, firstCard.GetProperty("manaCost").GetInt32());
        Assert.Equal(1, firstCard.GetProperty("attack").GetInt32());
        Assert.Equal(1, firstCard.GetProperty("health").GetInt32());
        Assert.Equal(JsonValueKind.Null, firstCard.GetProperty("durability").ValueKind);
        Assert.Equal("Test minion.", firstCard.GetProperty("rulesText").GetString());
      }
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
  }

  [Fact]
  public async Task GeneratedCatalogKeepsExistingCardsAvailableForDeckUpdates()
  {
    var root = Path.Combine(Path.GetTempPath(), $"cardgame-cumulative-catalog-{Guid.NewGuid():N}");
    try
    {
      await using var factory = Factory(root).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
      {
        services.RemoveAll<IPlayerGenerationExecutor>();
        services.AddSingleton<IPlayerGenerationExecutor, SuccessfulGenerationExecutor>();
      }));
      using var client = factory.CreateClient();
      await SeedCatalog(client);
      var auth = await Register(client, "cumulative-catalog@example.test");
      client.DefaultRequestHeaders.Add("X-Player-Token", auth.AccessToken);

      var created = await client.PostAsJsonAsync("/player/generation/jobs", new { prompt = "new card", providerMode = "local", count = 1 });
      var jobId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString()!;
      await WaitForJob(client, jobId, "completed");

      var deckList = await (await client.GetAsync("/player/decks")).Content.ReadFromJsonAsync<JsonElement>();
      var deck = deckList.GetProperty("decks").EnumerateArray().Single();
      var updated = await client.PostAsJsonAsync("/player/decks", new
      {
        deckId = deck.GetProperty("deckId").GetString(),
        name = "Starter Renamed After Generation",
        cards = deck.GetProperty("cards")
      });
      Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
      Assert.Equal(16, (await (await client.GetAsync("/player/collection")).Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
  }

  [Fact]
  public async Task PlayerCannotReadAnotherPlayersResourcesAndTokensAreNotStoredInPlaintext()
  {
    var root = Path.Combine(Path.GetTempPath(), $"cardgame-security-{Guid.NewGuid():N}");
    try
    {
      await using var factory = Factory(root);
      using var client = factory.CreateClient();
      var first = await Register(client, "first@example.test");
      var second = await Register(client, "second@example.test");
      client.DefaultRequestHeaders.Add("X-Player-Token", second.AccessToken);
      Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/player/generation/jobs/job-owned-by-first")).StatusCode);
      var persisted = await File.ReadAllTextAsync(Path.Combine(root, "players", "sessions.json"));
      Assert.DoesNotContain(first.AccessToken, persisted, StringComparison.Ordinal);
      Assert.DoesNotContain(first.RefreshToken, persisted, StringComparison.Ordinal);
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
  }

  [Fact]
  public async Task GenerationFailureCanBeRetriedAndJobsRemainOwnerScoped()
  {
    await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
    {
      services.RemoveAll<IPlayerGenerationExecutor>();
      services.AddSingleton<IPlayerGenerationExecutor, FailingGenerationExecutor>();
    }));
    using var owner = factory.CreateClient(); using var other = factory.CreateClient();
    await SeedProfile(owner);
    var first = await Register(owner, "job-owner@example.test"); var second = await Register(other, "job-other@example.test");
    owner.DefaultRequestHeaders.Add("X-Player-Token", first.AccessToken); other.DefaultRequestHeaders.Add("X-Player-Token", second.AccessToken);
    var created = await owner.PostAsJsonAsync("/player/generation/jobs", new { prompt = "provider failure", providerMode = "openai", count = 1 });
    var accepted = await created.Content.ReadFromJsonAsync<JsonElement>(); var jobId = accepted.GetProperty("jobId").GetString()!;
    var failed = await WaitForJob(owner, jobId, "failed");
    Assert.Equal("TRANSIENT_PROVIDER_REQUEST", failed.GetProperty("errorCode").GetString());
    Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/player/generation/jobs/{jobId}")).StatusCode);
    var retry = await owner.PostAsJsonAsync($"/player/generation/jobs/{jobId}/retry", new { });
    Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
    Assert.Equal(2, (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("attempt").GetInt32());
  }

  [Fact]
  public async Task RunningGenerationCanBeCancelledWithoutAcquiringCards()
  {
    await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
    {
      services.RemoveAll<IPlayerGenerationExecutor>(); services.AddSingleton<IPlayerGenerationExecutor, BlockingGenerationExecutor>();
    }));
    using var client = factory.CreateClient(); var auth = await Register(client, "cancel-job@example.test");
    client.DefaultRequestHeaders.Add("X-Player-Token", auth.AccessToken);
    var created = await client.PostAsJsonAsync("/player/generation/jobs", new { prompt = "cancel me", providerMode = "openai", count = 1 });
    var jobId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString()!;
    await WaitForJob(client, jobId, "running");
    var cancelled = await client.PostAsJsonAsync($"/player/generation/jobs/{jobId}/cancel", new { });
    Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
    Assert.Equal("cancelled", (await cancelled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    Assert.Equal(0, (await (await client.GetAsync("/player/collection")).Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
  }

  private static async Task<AuthBody> Register(HttpClient client, string email)
  {
    var response = await client.PostAsJsonAsync("/auth/register", new { email, password = "ReleaseReady!123", displayName = "Player" });
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    return (await response.Content.ReadFromJsonAsync<AuthBody>())!;
  }

  private static async Task<JsonElement> WaitForJob(HttpClient client, string jobId, string status)
  {
    JsonElement last = default;
    for (var attempt = 0; attempt < 400; attempt++)
    {
      var response = await client.GetAsync($"/player/generation/jobs/{jobId}");
      var job = await response.Content.ReadFromJsonAsync<JsonElement>();
      last = job;
      if (job.GetProperty("status").GetString() == status) return job;
      await Task.Delay(25);
    }
    throw new TimeoutException($"Generation job {jobId} did not reach {status}; last state was {last}.");
  }

  private static async Task SeedCatalog(HttpClient client,string snapshotId="player-journey-catalog-v1",string rulesText="Test minion.")
  {
    await SeedProfile(client);
    var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    var cards = Enumerable.Range(1, 15).Select(i =>
    {
      var cardId = $"journey_card_{i:00}"; var versionId = $"{cardId}_v1";
      return new CardVersion(cardId, versionId, "card-dsl-0.2.0", "engine-bootstrap-0.3.0", snapshotId, "pipeline-v1", "ruleset_mvp_core_v1", "sha256:player-journey", now,
        new CardDefinition(cardId, versionId, "card-dsl-0.2.0", "engine-bootstrap-0.3.0", "primitive-registry-bootstrap-0.2.0", "ruleset_mvp_core_v1", snapshotId,
          new StaticCardData($"Journey Card {i}", CardType.MinionCard, ["neutral"], Rarity.Common, 1, [], [], true, ["journey"]),
          new EntityDefinition(new MinionDefinition(1, 1, [], [], true), null, null, null, null, null), [],
          new CardTextGeneration(rulesText, null), new BalanceMetadata(1, 0, []), new AssetMetadata($"asset-{i}", $"asset://journey/{i}", null),
          new AuthoringMetadata("integration-test", now, "pipeline-v1", [])));
    }).ToArray();
    var response = await client.PostAsJsonAsync("/catalog/snapshots", new CatalogSnapshot(snapshotId, "ruleset_mvp_core_v1", "sha256:player-journey", now, cards));
    Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict);
  }

  private static async Task SeedProfile(HttpClient client)
  {
    var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "clients", "unity", "CardGame.Unity", "Assets", "StreamingAssets", "Seed", "resolved-ruleset-profile.json"));
    var response = await client.PostAsync("/rulesets/profiles", new StringContent(await File.ReadAllTextAsync(path), System.Text.Encoding.UTF8, "application/json"));
    Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict);
  }

  private static WebApplicationFactory<Program> Factory(string root) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
  {
    builder.UseSetting("Storage:RootPath", root);
    builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:RootPath"] = root }));
  });
  private sealed record AuthBody(string AccessToken, string RefreshToken, string PlayerId);
  private sealed class BlockingGenerationExecutor : IPlayerGenerationExecutor
  {
    public async Task<PlayerGenerationExecutionResult> ExecuteAsync(PlayerGenerationExecutionRequest request, CancellationToken cancellationToken)
    {
      await Task.Delay(500, cancellationToken); return PlayerGenerationExecutionResult.Success();
    }
  }
  private sealed class FailingGenerationExecutor : IPlayerGenerationExecutor
  {
    public Task<PlayerGenerationExecutionResult> ExecuteAsync(PlayerGenerationExecutionRequest request, CancellationToken cancellationToken) =>
      Task.FromResult(PlayerGenerationExecutionResult.Failure("TRANSIENT_PROVIDER_REQUEST"));
  }
  private sealed class SuccessfulGenerationExecutor : IPlayerGenerationExecutor
  {
    public Task<PlayerGenerationExecutionResult> ExecuteAsync(PlayerGenerationExecutionRequest request, CancellationToken cancellationToken)
    {
      var now = DateTimeOffset.Parse("2026-01-02T00:00:00Z");
      const string snapshotId = "generated-cumulative-catalog-v1";
      const string cardId = "generated_cumulative_card";
      const string versionId = "generated_cumulative_card_v1";
      var definition = new CardDefinition(cardId, versionId, "card-dsl-0.2.0", "engine-bootstrap-0.3.0", "primitive-registry-bootstrap-0.2.0", "ruleset_mvp_core_v1", snapshotId,
        new StaticCardData("Generated Cumulative Card", CardType.MinionCard, ["neutral"], Rarity.Common, 1, [], [], true, ["journey"]),
        new EntityDefinition(new MinionDefinition(1, 1, [], [], true), null, null, null, null, null), [], new CardTextGeneration("Generated minion.", null),
        new BalanceMetadata(1, 0, []), new AssetMetadata("asset-generated", "asset://generated/cumulative", null), new AuthoringMetadata("integration-test", now, "pipeline-v1", []));
      var card = new CardVersion(cardId, versionId, "card-dsl-0.2.0", "engine-bootstrap-0.3.0", snapshotId, "pipeline-v1", "ruleset_mvp_core_v1", "sha256:player-journey", now, definition, true);
      return Task.FromResult(PlayerGenerationExecutionResult.Success(new CatalogSnapshot(snapshotId, "ruleset_mvp_core_v1", "sha256:player-journey", now, [card])));
    }
  }
}
