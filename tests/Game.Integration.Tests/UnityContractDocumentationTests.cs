using System.Text.Json;
using System.Text.Json.Serialization;
using Game.Application.Matches;
using Game.Application.Presentation;
using Game.Shared.Assets;
using Game.Shared.Events;
using Xunit;
using UnityContracts = Game.UnityClient.Contracts;

namespace Game.Integration.Tests;

public sealed class UnityContractDocumentationTests
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
  {
    Converters = { new JsonStringEnumConverter() }
  };

  [Fact]
  public void OpenApiContractExistsAndDocumentsUnityEndpointSet()
  {
    var openApi = File.ReadAllText(RepoPath("docs/api/openapi.yaml"));

    Assert.Contains("openapi: 3.1.0", openApi, StringComparison.Ordinal);
    foreach (var path in new[]
    {
      "/health",
      "/engine/capabilities",
      "/sessions",
      "/matches",
      "/matches/{matchId}/state",
      "/matches/{matchId}/players/{playerId}/legal-actions",
      "/matches/{matchId}/commands",
      "/matches/{matchId}/events",
      "/matches/{matchId}/reconnect",
      "/matches/{matchId}/replay/verify",
      "/catalog/snapshots/current",
      "/catalog/snapshots/{snapshotId}",
      "/catalog/snapshots/{snapshotId}/compatibility",
      "/rulesets/profiles/current",
      "/rulesets/profiles/{profileId}",
      "/decks/current",
      "/decks/{deckId}",
      "/assets/manifests/current",
      "/assets/manifests/{assetManifestId}",
      "/assets/manifests/{assetManifestId}/compatibility",
      "/assets/manifests/{assetManifestId}/presentation-hash",
      "/presentation/timelines",
      "/presentation/playback-plans",
      "/presentation/playback-instructions"
    })
    {
      Assert.Contains(path + ":", openApi, StringComparison.Ordinal);
    }

    Assert.Contains("SessionToken:", openApi, StringComparison.Ordinal);
    Assert.Contains("MatchStateView:", openApi, StringComparison.Ordinal);
    Assert.Contains("PublicMatchEventRecord:", openApi, StringComparison.Ordinal);
    Assert.Contains("PresentationPlaybackRuntimeResult:", openApi, StringComparison.Ordinal);
    Assert.Contains("PLAY_CARD", openApi, StringComparison.Ordinal);
    Assert.Contains("RESOLVE_CHOICE", openApi, StringComparison.Ordinal);
    Assert.DoesNotContain("events:\n          type: array\n          items:\n            type: object", openApi, StringComparison.Ordinal);
    Assert.DoesNotContain("AssetManifest:\n      type: object\n      description", openApi, StringComparison.Ordinal);
  }

  [Fact]
  public void OpenApiContractParsesWithRealParserAndHasNoBrokenReferences()
  {
    var result = OperatingSystem.IsWindows()
      ? RunProcess("cmd", "/c npx --yes @redocly/cli@1.25.15 lint docs/api/openapi.yaml")
      : RunProcess("npx", "--yes @redocly/cli@1.25.15 lint docs/api/openapi.yaml");

    Assert.True(result.ExitCode == 0, result.Output);
    Assert.Contains("Your API description is valid", result.Output, StringComparison.Ordinal);
  }

  [Fact]
  public void UnityDocumentationPackageExistsAndCoversPublicEvents()
  {
    foreach (var relativePath in new[]
    {
      "docs/unity/README.md",
      "docs/unity/integration-guide.md",
      "docs/unity/api.md",
      "docs/unity/state.md",
      "docs/unity/commands.md",
      "docs/unity/events.md",
      "docs/unity/event-animation-map.md",
      "docs/unity/effect-coverage.md",
      "docs/unity/assets.md",
      "docs/unity/presentation.md",
      "docs/unity/errors.md",
      "docs/unity/versioning.md",
      "docs/unity/realtime.md",
      "docs/unity/webgl.md",
      "docs/unity/local-smoke.md",
      "docs/unity/architecture.md",
      "docs/unity/generated-contracts.md",
      "scripts/unity/run-local-smoke.sh",
      "scripts/unity/run-bot-match-smoke.sh",
      "scripts/unity/run-webgl-cors-smoke.sh"
    })
    {
      Assert.True(File.Exists(RepoPath(relativePath)), $"Missing Unity integration artifact: {relativePath}");
    }

    var eventsDoc = File.ReadAllText(RepoPath("docs/unity/events.md"));
    var openApi = File.ReadAllText(RepoPath("docs/api/openapi.yaml"));
    foreach (var eventType in Enum.GetValues<CommittedEventType>().Select(ToContractName))
    {
      Assert.Contains(eventType, eventsDoc, StringComparison.Ordinal);
      Assert.Contains(eventType, openApi, StringComparison.Ordinal);
    }

    var coverage = File.ReadAllText(RepoPath("docs/unity/effect-coverage.md"));
    Assert.Contains("targeted damage", coverage, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("secret reveal", coverage, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("engine fault terminal handling", coverage, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void UnityEventAnimationMapDocumentsEveryPublicEvent()
  {
    var animationMap = File.ReadAllText(RepoPath("docs/unity/event-animation-map.md"));
    var openApi = File.ReadAllText(RepoPath("docs/api/openapi.yaml"));
    foreach (var eventType in Enum.GetValues<CommittedEventType>().Select(ToContractName))
    {
      Assert.Contains($"`{eventType}`", animationMap, StringComparison.Ordinal);
      Assert.Contains(eventType, openApi, StringComparison.Ordinal);
    }
  }

  [Fact]
  public void UnityJsonExamplesDeserializeIntoPublicContracts()
  {
    AssertExample<CreateApiSessionRequest>("create-session.request.json");
    AssertExample<CreateApiSessionResult>("create-session.response.json");
    AssertExample<ExternalMatchStartRequest>("start-match.request.json");
    AssertExample<ExternalMatchStartResult>("start-match.response.json");
    AssertExample<MatchStateView>("state.response.player.json");
    AssertExample<MatchStateView>("state.response.spectator.json");
    AssertExample<ExternalLegalActionsResult>("legal-actions.response.json");
    AssertExample<ExternalCommandSubmissionRequest>("play-card.command.request.json");
    AssertExample<ExternalCommandSubmissionResult>("command.response.json");
    AssertExample<MatchEventsPollResult>("events.response.json");
    AssertExample<MatchReconnectResult>("reconnect.response.json");
    AssertExample<AssetManifest>("asset-manifest.response.json");
    AssertExample<AssetManifest>("asset-manifest.missing-fallback.response.json");
    AssertExample<PresentationTimelineBuildResult>("presentation-timeline.response.json");
    AssertExample<PresentationPlaybackPlanBuildResult>("playback-plan.response.json");
    AssertExample<PresentationPlaybackRuntimeResult>("playback-instructions.response.json");
    AssertEventExamples();

    var playerState = ReadExample<MatchStateView>("state.response.player.json");
    Assert.Equal("PLAYER", playerState.ViewKind);
    Assert.Equal("player-1", playerState.ViewerPlayerId);
    Assert.Empty(playerState.Players["player-2"].Zones.Hand.Cards);
    Assert.Empty(playerState.Players["player-2"].Zones.Deck.Cards);
    Assert.All(playerState.Players["player-2"].Secrets ?? [], secret =>
    {
      if (!secret.IsRevealed)
      {
        Assert.Null(secret.SourceCardInstanceId);
        Assert.Null(secret.SourceCardVersion);
      }
    });

    var spectatorState = ReadExample<MatchStateView>("state.response.spectator.json");
    Assert.Equal("SPECTATOR", spectatorState.ViewKind);
    Assert.Null(spectatorState.ViewerPlayerId);
    Assert.All(spectatorState.Players.Values, player =>
    {
      Assert.Empty(player.Zones.Hand.Cards);
      Assert.Empty(player.Zones.Deck.Cards);
    });
  }

  [Fact]
  public void UnityJsonExamplesDeserializeIntoUnityClientContracts()
  {
    AssertUnityExample<UnityContracts.CreateApiSessionRequest>("create-session.request.json");
    AssertUnityExample<UnityContracts.CreateApiSessionResult>("create-session.response.json");
    AssertUnityExample<UnityContracts.ExternalMatchStartRequest>("start-match.request.json");
    AssertUnityExample<UnityContracts.ExternalMatchStartResult>("start-match.response.json");
    AssertUnityExample<UnityContracts.MatchStateView>("state.response.player.json");
    AssertUnityExample<UnityContracts.MatchStateView>("state.response.spectator.json");
    AssertUnityExample<UnityContracts.ExternalLegalActionsResult>("legal-actions.response.json");
    AssertUnityExample<UnityContracts.ExternalCommandSubmissionRequest>("play-card.command.request.json");
    AssertUnityExample<UnityContracts.ExternalCommandSubmissionResult>("command.response.json");
    AssertUnityExample<UnityContracts.MatchEventsPollResult>("events.response.json");
    AssertUnityExample<UnityContracts.MatchReconnectResult>("reconnect.response.json");
    AssertUnityExample<UnityContracts.AssetManifest>("asset-manifest.response.json");
    AssertUnityExample<UnityContracts.AssetManifest>("asset-manifest.missing-fallback.response.json");
    AssertUnityExample<UnityContracts.PresentationTimelineBuildResult>("presentation-timeline.response.json");
    AssertUnityExample<UnityContracts.PresentationPlaybackPlanBuildResult>("playback-plan.response.json");
    AssertUnityExample<UnityContracts.PresentationPlaybackRuntimeResult>("playback-instructions.response.json");
    AssertUnityExample<UnityContracts.UnityBotMatchSmokeReport>("smoke-report.example.json");

    var eventDirectory = Path.GetDirectoryName(RepoPath("docs/unity/examples/events/card-played.json"))!;
    foreach (var file in Directory.EnumerateFiles(eventDirectory, "*.json"))
    {
      Assert.NotNull(JsonSerializer.Deserialize<UnityContracts.PublicMatchEventRecord>(File.ReadAllText(file), Json));
    }
  }

  [Theory]
  [InlineData("\n")]
  [InlineData("\r\n")]
  public void UnityClientContractsHaveNoBackendDependenciesAndCoverOpenApiSchemas(string lineEnding)
  {
    var contractAssembly = typeof(UnityContracts.MatchStateView).Assembly;
    Assert.DoesNotContain(contractAssembly.GetReferencedAssemblies(), reference =>
      reference.Name is "Game.Domain" or "Game.Application" or "Game.Api" or "Game.Infrastructure" or "Game.Generator"
        or "Microsoft.AspNetCore" or "EntityFramework");

    var openApi = File.ReadAllText(RepoPath("docs/api/openapi.yaml")).ReplaceLineEndings(lineEnding);
    var contractTypeNames = contractAssembly.GetExportedTypes()
      .Select(type => type.Name)
      .ToHashSet(StringComparer.Ordinal);
    foreach (var schemaName in GetOpenApiSchemaNames(openApi))
    {
      Assert.Contains(schemaName, contractTypeNames);
    }
  }

  [Fact]
  public void OpenApiUnitySchemasDoNotUseUndocumentedGenericObjects()
  {
    var lines = File.ReadAllLines(RepoPath("docs/api/openapi.yaml"));
    for (var i = 0; i < lines.Length; i++)
    {
      if (!lines[i].Contains("type: object", StringComparison.Ordinal))
      {
        continue;
      }

      var localBlock = string.Join('\n', lines.Skip(i).Take(10));
      var isSpecific = localBlock.Contains("properties:", StringComparison.Ordinal)
        || localBlock.Contains("additionalProperties:", StringComparison.Ordinal)
        || localBlock.Contains("opaque", StringComparison.OrdinalIgnoreCase);
      Assert.True(isSpecific, $"OpenAPI object schema near line {i + 1} is generic without properties, additionalProperties, or opaque description.");
    }
  }

  [Fact]
  public void EveryUnityExampleHasAnExplicitContractAssertion()
  {
    var expected = new HashSet<string>(StringComparer.Ordinal)
    {
      "asset-manifest.missing-fallback.response.json",
      "asset-manifest.response.json",
      "client-polished-playtest-report.example.json",
      "client-production-readiness-audit.example.json",
      "client-playtest-report.example.json",
      "client-rc-playtest-report.example.json",
      "unity-client-rc-validation-summary.example.json",
      "unity-build-summary.example.json",
      "unity-dev-build-summary.example.json",
      "command.response.json",
      "create-session.request.json",
      "create-session.response.json",
      "error.forbidden.json",
      "error.validation.json",
      "events.response.json",
      "legal-actions.response.json",
      "mcp-inspection-report.example.json",
      "play-card.command.request.json",
      "playback-instructions.response.json",
      "playback-plan.response.json",
      "presentation-timeline.response.json",
      "reconnect.response.json",
      "smoke-report.example.json",
      "start-match.request.json",
      "start-match.response.json",
      "state.response.player.json",
      "state.response.spectator.json",
      "unity-live-smoke-report.example.json",
      "unity-live-smoke-repeatability-report.example.json"
    };
    var actual = Directory.EnumerateFiles(RepoPath("docs/unity/examples"), "*.json", SearchOption.TopDirectoryOnly)
      .Select(Path.GetFileName)
      .ToHashSet(StringComparer.Ordinal);
    Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
  }

  [Fact]
  public void UnityPublicResponseDtosDoNotExposeDomainOrCommittedEventInternals()
  {
    foreach (var type in new[]
    {
      typeof(MatchStateView),
      typeof(PublicPlayerStateView),
      typeof(PublicPlayerZonesView),
      typeof(PublicZoneView),
      typeof(PublicCardInstanceView),
      typeof(PublicEntityView),
      typeof(PublicChoiceView),
      typeof(PublicMatchEventRecord),
      typeof(ExternalCommandSubmissionResult),
      typeof(MatchEventsPollResult),
      typeof(MatchReconnectResult)
    })
    {
      foreach (var property in type.GetProperties())
      {
        var propertyTypeName = property.PropertyType.FullName ?? property.PropertyType.Name;
        Assert.DoesNotContain("Game.Domain.", propertyTypeName, StringComparison.Ordinal);
        Assert.DoesNotContain("CommittedEventRecord", propertyTypeName, StringComparison.Ordinal);
      }
    }
  }

  [Fact]
  public void UnityErrorExamplesExposeStableClientErrorShape()
  {
    var validation = ReadJsonDocument("error.validation.json").RootElement;
    var forbidden = ReadJsonDocument("error.forbidden.json").RootElement;

    Assert.Equal("VALIDATION_ERROR", validation.GetProperty("code").GetString());
    Assert.False(validation.GetProperty("retryable").GetBoolean());
    Assert.Equal("MATCH_SESSION_FORBIDDEN", forbidden.GetProperty("code").GetString());
    Assert.True(forbidden.TryGetProperty("suggestedUnityBehavior", out _));
  }

  private static void AssertExample<T>(string fileName) =>
    Assert.NotNull(ReadExample<T>(fileName));

  private static void AssertUnityExample<T>(string fileName) =>
    Assert.NotNull(JsonSerializer.Deserialize<T>(
      File.ReadAllText(RepoPath(Path.Combine("docs/unity/examples", fileName))),
      Json));

  private static T ReadExample<T>(string fileName)
  {
    var json = File.ReadAllText(RepoPath(Path.Combine("docs/unity/examples", fileName)));
    return JsonSerializer.Deserialize<T>(json, Json)
      ?? throw new InvalidOperationException($"Example '{fileName}' did not deserialize as {typeof(T).Name}.");
  }

  private static JsonDocument ReadJsonDocument(string fileName) =>
    JsonDocument.Parse(File.ReadAllText(RepoPath(Path.Combine("docs/unity/examples", fileName))));

  private static void AssertEventExamples()
  {
    var directory = Path.GetDirectoryName(RepoPath("docs/unity/examples/events/card-played.json"))
      ?? throw new InvalidOperationException("Could not resolve Unity event example directory.");
    foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
    {
      var json = File.ReadAllText(file);
      Assert.NotNull(JsonSerializer.Deserialize<PublicMatchEventRecord>(json, Json));
    }
  }

  private static string ToContractName<TEnum>(TEnum value)
    where TEnum : struct, Enum
  {
    var name = value.ToString();
    return string.Concat(name.Select((character, index) =>
      index > 0 && char.IsUpper(character)
        ? "_" + character
      : character.ToString())).ToUpperInvariant();
  }

  private static IReadOnlyList<string> GetOpenApiSchemaNames(string openApi)
  {
    var schemaNames = new List<string>();
    var inSchemas = false;
    foreach (var line in openApi.Split('\n'))
    {
      if (line.TrimEnd('\r') == "  schemas:")
      {
        inSchemas = true;
        continue;
      }

      if (!inSchemas)
      {
        continue;
      }

      var match = System.Text.RegularExpressions.Regex.Match(line, "^    (?<name>[A-Za-z][A-Za-z0-9]*):\\s*$");
      if (match.Success)
      {
        schemaNames.Add(match.Groups["name"].Value);
      }
    }

    Assert.NotEmpty(schemaNames);
    return schemaNames;
  }

  private static (int ExitCode, string Output) RunProcess(string fileName, string arguments)
  {
    var startInfo = new System.Diagnostics.ProcessStartInfo(fileName, arguments)
    {
      WorkingDirectory = RepoRoot(),
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false
    };
    using var process = System.Diagnostics.Process.Start(startInfo)
      ?? throw new InvalidOperationException($"Could not start {fileName} {arguments}");
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    process.WaitForExit(120_000);
    if (!process.HasExited)
    {
      process.Kill(entireProcessTree: true);
      throw new TimeoutException($"{fileName} {arguments} timed out.");
    }

    var output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
    return (process.ExitCode, output);
  }

  private static string RepoRoot()
  {
    var openApi = RepoPath("docs/api/openapi.yaml");
    return Directory.GetParent(openApi)?.Parent?.Parent?.FullName
      ?? throw new InvalidOperationException("Could not resolve repository root.");
  }

  private static string RepoPath(string relativePath)
  {
    var current = AppContext.BaseDirectory;
    while (!string.IsNullOrWhiteSpace(current))
    {
      var candidate = Path.Combine(current, relativePath);
      if (File.Exists(candidate) || Directory.Exists(candidate))
      {
        return candidate;
      }

      var parent = Directory.GetParent(current);
      if (parent is null)
      {
        break;
      }

      current = parent.FullName;
    }

    throw new FileNotFoundException($"Could not find repository file '{relativePath}'.", relativePath);
  }
}
