using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CardGame.Unity.Client.Api;
using Game.UnityClient.Contracts;

namespace CardGame.Unity.Client.Match
{
  public sealed class CapabilityChecker
  {
    public const string UnityClientContractVersion = "unity-contract-2026-07-07";

    private static readonly string[] RequiredCommandTypes =
    {
      "END_TURN",
      "PLAY_CARD",
      "ATTACK",
      "RESOLVE_CHOICE"
    };

    private static readonly string[] RequiredEventTypes =
    {
      "GAME_STARTED",
      "TURN_STARTED",
      "TURN_ENDED",
      "CARD_PLAYED",
      "CARD_MOVED_ZONE",
      "CARD_DRAWN",
      "MINION_SUMMONED",
      "ENTITY_DAMAGED",
      "ENTITY_DIED",
      "CHOICE_CREATED",
      "CHOICE_RESOLVED",
      "GAME_ENDED"
    };

    private readonly BackendApiClient api;

    public HealthResponse Health { get; private set; }
    public EngineCapabilityRegistry Capabilities { get; private set; }
    public string BootStatus { get; private set; } = "Not checked";
    public CapabilityCheckResult LastResult { get; private set; } = CapabilityCheckResult.Blocked("Not checked");

    public CapabilityChecker(BackendApiClient api)
    {
      this.api = api;
    }

    public async Task<bool> CheckAsync(CancellationToken cancellationToken)
    {
      var health = await api.GetJsonAsync<HealthResponse>("/health", cancellationToken: cancellationToken);
      if (!health.Succeeded)
      {
        BootStatus = health.Error.Message;
        LastResult = CapabilityCheckResult.Blocked("Health check failed");
        return false;
      }

      Health = health.Value;
      var capabilities = await api.GetJsonAsync<EngineCapabilityRegistry>("/engine/capabilities", cancellationToken: cancellationToken);
      if (!capabilities.Succeeded)
      {
        BootStatus = capabilities.Error.Message;
        LastResult = CapabilityCheckResult.Blocked("Capabilities check failed");
        return false;
      }

      Capabilities = capabilities.Value;
      LastResult = Validate(Capabilities);
      BootStatus = LastResult.ToDisplayString(Health?.Status ?? "unknown", Capabilities);
      return LastResult.Supported;
    }

    public static CapabilityCheckResult Validate(EngineCapabilityRegistry capabilities)
    {
      if (capabilities == null)
      {
        return CapabilityCheckResult.Blocked("Capability response was empty");
      }

      var missing = new List<string>();
      RequireValue(capabilities.ApiContractVersion, "apiContractVersion", missing);
      RequireValue(capabilities.StateSchemaVersion, "stateSchemaVersion", missing);
      RequireValue(capabilities.EventSchemaVersion, "eventSchemaVersion", missing);
      RequireValue(capabilities.MinimumUnityClientContractVersion, "minimumUnityClientContractVersion", missing);

      foreach (var commandType in RequiredCommandTypes)
      {
        if (!ContainsToken(capabilities.SupportedCommandTypes, commandType))
        {
          missing.Add("command:" + commandType);
        }
      }

      foreach (var eventType in RequiredEventTypes)
      {
        if (!ContainsToken(capabilities.SupportedEventTypes, eventType))
        {
          missing.Add("event:" + eventType);
        }
      }

      if (!string.IsNullOrWhiteSpace(capabilities.MinimumUnityClientContractVersion) &&
        string.Compare(capabilities.MinimumUnityClientContractVersion, UnityClientContractVersion, StringComparison.Ordinal) > 0)
      {
        missing.Add("minimumUnityClientContractVersion>" + UnityClientContractVersion);
      }

      return missing.Count == 0
        ? CapabilityCheckResult.Pass()
        : CapabilityCheckResult.Blocked(missing);
    }

    private static void RequireValue(string value, string name, List<string> missing)
    {
      if (string.IsNullOrWhiteSpace(value))
      {
        missing.Add(name);
      }
    }

    private static bool ContainsToken(IEnumerable<string> tokens, string token) =>
      tokens != null && tokens.Any(candidate => string.Equals(candidate, token, StringComparison.OrdinalIgnoreCase));
  }

  public sealed class CapabilityCheckResult
  {
    public bool Supported { get; private set; }
    public IReadOnlyList<string> MissingCapabilities { get; private set; }

    private CapabilityCheckResult(bool supported, IReadOnlyList<string> missingCapabilities)
    {
      Supported = supported;
      MissingCapabilities = missingCapabilities;
    }

    public static CapabilityCheckResult Pass() => new(true, Array.Empty<string>());

    public static CapabilityCheckResult Blocked(string missingCapability) =>
      new(false, new[] { missingCapability });

    public static CapabilityCheckResult Blocked(IReadOnlyList<string> missingCapabilities) =>
      new(false, missingCapabilities ?? Array.Empty<string>());

    public string ToDisplayString(string healthStatus, EngineCapabilityRegistry capabilities)
    {
      var status = Supported ? "ready" : "blocked";
      var missing = MissingCapabilities.Count == 0 ? "none" : string.Join(", ", MissingCapabilities);
      return
        $"Backend {healthStatus}; {status}\n" +
        $"Contract {capabilities?.ApiContractVersion ?? "(missing)"} min {capabilities?.MinimumUnityClientContractVersion ?? "(missing)"}\n" +
        $"State schema {capabilities?.StateSchemaVersion ?? "(missing)"} Event schema {capabilities?.EventSchemaVersion ?? "(missing)"}\n" +
        $"Missing: {missing}";
    }
  }

  public sealed class SessionService
  {
    private readonly BackendApiClient api;

    public SessionService(BackendApiClient api)
    {
      this.api = api;
    }

    public Task<UnityApiResult<CreateApiSessionResult>> CreatePlayerSessionAsync(string playerId, CancellationToken cancellationToken) =>
      api.PostJsonAsync<CreateApiSessionResult>("/sessions", new CreateApiSessionRequest { PlayerId = playerId }, cancellationToken: cancellationToken);
  }

  public sealed class MatchService
  {
    private readonly BackendApiClient api;

    public MatchService(BackendApiClient api)
    {
      this.api = api;
    }

    public Task<UnityApiResult<ExternalMatchStartResult>> StartMatchAsync(
      string matchId,
      IReadOnlyList<string> deckIds,
      IDictionary<string, string> playerSessionTokens,
      string startingPlayerId,
      IReadOnlyList<string> spectatorSessionTokens,
      CancellationToken cancellationToken)
    {
      return api.PostJsonAsync<ExternalMatchStartResult>(
        "/matches",
        new ExternalMatchStartRequest
        {
          MatchId = matchId,
          GameId = "unity-debug",
          DeckIds = deckIds.ToList(),
          PlayerSessionTokens = new Dictionary<string, string>(playerSessionTokens),
          StartingPlayerId = startingPlayerId,
          SpectatorSessionTokens = spectatorSessionTokens?.ToList()
        },
        cancellationToken: cancellationToken);
    }

    public Task<UnityApiResult<MatchStateView>> FetchStateAsync(string matchId, string sessionToken, CancellationToken cancellationToken) =>
      api.GetJsonAsync<MatchStateView>($"/matches/{Uri.EscapeDataString(matchId)}/state", sessionToken, cancellationToken);

    public Task<UnityApiResult<MatchReplayVerificationView>> VerifyReplayAsync(string matchId, string sessionToken, CancellationToken cancellationToken) =>
      api.PostJsonAsync<MatchReplayVerificationView>($"/matches/{Uri.EscapeDataString(matchId)}/replay/verify", new { }, sessionToken, cancellationToken);
  }

  public sealed class LegalActionService
  {
    private readonly BackendApiClient api;

    public LegalActionService(BackendApiClient api)
    {
      this.api = api;
    }

    public Task<UnityApiResult<ExternalLegalActionsResult>> FetchLegalActionsAsync(
      string matchId,
      string playerId,
      string sessionToken,
      CancellationToken cancellationToken)
    {
      return api.GetJsonAsync<ExternalLegalActionsResult>(
        $"/matches/{Uri.EscapeDataString(matchId)}/players/{Uri.EscapeDataString(playerId)}/legal-actions",
        sessionToken,
        cancellationToken);
    }
  }
}
