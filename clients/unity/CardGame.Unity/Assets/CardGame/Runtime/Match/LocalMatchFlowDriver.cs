using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CardGame.Unity.Client.Api;
using CardGame.Unity.Client.Commands;
using CardGame.Unity.Client.Config;
using CardGame.Unity.Client.DebugTools;
using CardGame.Unity.Client.Events;
using CardGame.Unity.Client.State;
using Game.UnityClient.Contracts;
using Newtonsoft.Json;
using UnityEngine;

namespace CardGame.Unity.Client.Match
{
  public sealed class LocalMatchFlowDriver
  {
    private static readonly HashSet<string> TargetedCommandTypes = new(StringComparer.OrdinalIgnoreCase)
    {
      "ATTACK",
      "ACTIVATE_LOCATION",
      "USE_HERO_POWER"
    };

    private readonly UnityBackendConfig config;
    private readonly BackendApiClient api;
    private readonly CapabilityChecker capabilityChecker;
    private readonly DebugDemoSeeder seeder;
    private readonly SessionService sessionService;
    private readonly MatchService matchService;
    private readonly LegalActionService legalActionService;
    private readonly CommandQueue commandQueue;
    private readonly MatchStateStore stateStore;
    private readonly EventPoller eventPoller;
    private readonly ReconnectService reconnectService;
    private readonly EventVisualQueue visualQueue;

    public LocalMatchFlowDriver(UnityBackendConfig config)
      : this(
        config,
        new BackendApiClient(config),
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null)
    {
    }

    public LocalMatchFlowDriver(
      UnityBackendConfig config,
      BackendApiClient api,
      CapabilityChecker capabilityChecker,
      DebugDemoSeeder seeder,
      SessionService sessionService,
      MatchService matchService,
      LegalActionService legalActionService,
      CommandQueue commandQueue,
      MatchStateStore stateStore,
      EventPoller eventPoller,
      ReconnectService reconnectService)
    {
      this.config = config ?? UnityBackendConfig.CreateRuntimeDefault();
      this.api = api ?? new BackendApiClient(this.config);
      this.capabilityChecker = capabilityChecker ?? new CapabilityChecker(this.api);
      this.seeder = seeder ?? new DebugDemoSeeder(this.api);
      this.sessionService = sessionService ?? new SessionService(this.api);
      this.matchService = matchService ?? new MatchService(this.api);
      this.legalActionService = legalActionService ?? new LegalActionService(this.api);
      this.commandQueue = commandQueue ?? new CommandQueue(this.api, new CommandBuilder());
      this.stateStore = stateStore ?? new MatchStateStore();
      this.eventPoller = eventPoller ?? new EventPoller(this.api, this.config, this.stateStore);
      this.reconnectService = reconnectService ?? new ReconnectService(this.api, this.stateStore);
      visualQueue = new EventVisualQueue(new EventVisualDispatcher());
    }

    public async Task<LocalMatchFlowResult> RunAsync(LocalMatchFlowOptions options, CancellationToken cancellationToken)
    {
      options ??= new LocalMatchFlowOptions();
      var playerOneId = string.IsNullOrWhiteSpace(options.PlayerOneId) ? DebugDemoSeeder.PlayerOneId : options.PlayerOneId;
      var playerOneDeckId = string.IsNullOrWhiteSpace(options.PlayerOneDeckId) ? DebugDemoSeeder.PlayerOneDeckId : options.PlayerOneDeckId;
      var report = LocalMatchFlowReport.Create(config.BackendBaseUrl, options.UnityVersion);
      report.SmokeRunId = string.IsNullOrWhiteSpace(options.SmokeRunId)
        ? GenerateSmokeRunId()
        : options.SmokeRunId;
      report.SeedMode = options.SeedMode.ToString();
      report.SmokeRunMode = options.SmokeRunMode;
      report.UnityBatchmodeLogPath = options.UnityBatchmodeLogPath;
      report.TestResultsPath = options.TestResultsPath;
      report.BuildValidationSummary = options.BuildValidationSummary;
      report.Environment["unityVersion"] = options.UnityVersion;
      report.Environment["backendUrl"] = config.BackendBaseUrl;
      report.Environment["seedMode"] = options.SeedMode.ToString();
      report.MatchId = string.IsNullOrWhiteSpace(options.MatchId)
        ? GenerateSmokeMatchId()
        : options.MatchId;
      report.MatchIdStrategy = string.IsNullOrWhiteSpace(options.MatchId)
        ? "unique-default:unity-live-smoke-<timestamp>-<short-random>"
        : "custom-option";
      report.PlayerIds.Add(playerOneId);
      report.PlayerIds.Add(DebugDemoSeeder.PlayerTwoId);
      report.SpectatorId = DebugDemoSeeder.SpectatorId;

      try
      {
        await Step(report, "boot", async () =>
        {
          report.BootSucceeded = await capabilityChecker.CheckAsync(cancellationToken);
          report.CapabilityStatus = capabilityChecker.BootStatus;
          report.BackendPrecheck = report.BootSucceeded ? "health-and-capabilities-ok" : "health-or-capabilities-failed";
          if (!report.BootSucceeded)
          {
            throw new InvalidOperationException(capabilityChecker.BootStatus);
          }
        });

        await Step(report, "seed", async () =>
        {
          if (!options.SeedDemoData)
          {
            report.SeedStatus = "skipped";
            return;
          }

          var seed = await seeder.SeedAsync(options.SeedMode, cancellationToken);
          report.SeedSucceeded = seed.Succeeded;
          report.SeedStatus = seed.Succeeded ? seed.Summary : "failed:" + seed.Summary;
          report.SeedResults = seed.Results;
          report.ResourceCompatibilityChecks = seed.CompatibilityChecks;
          if (!report.SeedSucceeded)
          {
            throw new InvalidOperationException("Deterministic demo data seed failed.");
          }
        });

        CreateApiSessionResult p1Session = null;
        CreateApiSessionResult p2Session = null;
        CreateApiSessionResult spectatorSession = null;
        await Step(report, "sessions", async () =>
        {
          p1Session = await RequireValue(
            sessionService.CreatePlayerSessionAsync(playerOneId, cancellationToken),
            "player 1 session");
          p2Session = await RequireValue(
            sessionService.CreatePlayerSessionAsync(DebugDemoSeeder.PlayerTwoId, cancellationToken),
            "player 2 session");
          spectatorSession = await RequireValue(
            sessionService.CreatePlayerSessionAsync(DebugDemoSeeder.SpectatorId, cancellationToken),
            "spectator session");
          report.PlayerOneTokenPresent = !string.IsNullOrWhiteSpace(p1Session.Session?.SessionToken);
          report.PlayerTwoTokenPresent = !string.IsNullOrWhiteSpace(p2Session.Session?.SessionToken);
          report.SpectatorTokenPresent = !string.IsNullOrWhiteSpace(spectatorSession.Session?.SessionToken);
        });

        var p1Token = p1Session.Session.SessionToken;
        var p2Token = p2Session.Session.SessionToken;
        var spectatorToken = spectatorSession.Session.SessionToken;

        await Step(report, "start-match", async () =>
        {
          var start = await RequireValue(
            matchService.StartMatchAsync(
              report.MatchId,
              new[] { playerOneDeckId, string.IsNullOrWhiteSpace(options.PlayerTwoDeckId) ? DebugDemoSeeder.PlayerTwoDeckId : options.PlayerTwoDeckId },
              new Dictionary<string, string>
              {
                [playerOneId] = p1Token,
                [DebugDemoSeeder.PlayerTwoId] = p2Token
              },
              playerOneId,
              new[] { spectatorToken },
              cancellationToken),
            "match start");
          if (!start.Succeeded || start.State == null)
          {
            throw new InvalidOperationException("Match start did not return state.");
          }

          report.StateHashes["start"] = start.State.StateHash;
        });

        MatchStateView p1State = null;
        MatchStateView p2State = null;
        MatchStateView spectatorState = null;
        await Step(report, "fetch-states", async () =>
        {
          p1State = await RequireValue(matchService.FetchStateAsync(report.MatchId, p1Token, cancellationToken), "player 1 state");
          p2State = await RequireValue(matchService.FetchStateAsync(report.MatchId, p2Token, cancellationToken), "player 2 state");
          spectatorState = await RequireValue(matchService.FetchStateAsync(report.MatchId, spectatorToken, cancellationToken), "spectator state");
          report.StateHashes["player1"] = p1State.StateHash;
          report.StateHashes["player2"] = p2State.StateHash;
          report.StateHashes["spectator"] = spectatorState.StateHash;
        });

        await Step(report, "hidden-information", () =>
        {
          report.HiddenInfoAssertions.AddRange(HiddenInformationAssertions.CheckAll(p1State, p2State, spectatorState, playerOneId, DebugDemoSeeder.PlayerTwoId));
          foreach (var assertion in report.HiddenInfoAssertions.Where(assertion => !assertion.Passed))
          {
            throw new InvalidOperationException(assertion.Name + ": " + assertion.Message);
          }

          var switchStore = new MatchStateStore();
          switchStore.ReplaceFromServer(p1State);
          switchStore.ReplaceFromServer(p2State);
          var switchCheck = HiddenInformationAssertions.CheckProjectionSwitch(switchStore.Current, DebugDemoSeeder.PlayerTwoId);
          report.HiddenInfoAssertions.Add(switchCheck);
          if (!switchCheck.Passed)
          {
            throw new InvalidOperationException(switchCheck.Name + ": " + switchCheck.Message);
          }

          return Task.CompletedTask;
        });

        ExternalLegalActionsResult legalActions = null;
        string activePlayerId = null;
        string activeToken = null;
        await Step(report, "legal-actions", async () =>
        {
          activePlayerId = p1State.ActivePlayerId ?? playerOneId;
          activeToken = string.Equals(activePlayerId, DebugDemoSeeder.PlayerTwoId, StringComparison.OrdinalIgnoreCase)
            ? p2Token
            : p1Token;
          legalActions = await RequireValue(
            legalActionService.FetchLegalActionsAsync(report.MatchId, activePlayerId, activeToken, cancellationToken),
            "legal actions");
          if (!legalActions.Succeeded || legalActions.LegalActions.Count == 0)
          {
            throw new InvalidOperationException("No legal actions returned for active player.");
          }

          report.LegalActionCountBeforeCommand = legalActions.LegalActions.Count;
        });

        MatchCommandDto command = null;
        MatchStateView beforeCommandState = null;
        ExternalCommandSubmissionResult commandResult = null;
        await Step(report, "submit-command", async () =>
        {
          beforeCommandState = await RequireValue(matchService.FetchStateAsync(report.MatchId, activeToken, cancellationToken), "pre-command state");
          var selection = SelectSmokeCommandWithReason(legalActions);
          command = selection.Command;
          report.SmokeCommandSelectionReason = selection.Reason;
          if (command == null)
          {
            throw new InvalidOperationException("No complete non-targeted server-provided legal action was available.");
          }

          report.CommandSubmitted = command.CommandType;
          report.CommandTypes.Add(command.CommandType);
          report.CommandCountBefore = beforeCommandState.CommandCount;
          commandResult = await RequireValue(
            commandQueue.SubmitAsync(report.MatchId, command, activeToken, beforeCommandState.StateHash, cancellationToken),
            "command submission");
          if (!commandResult.Succeeded || commandResult.State == null)
          {
            throw new InvalidOperationException("Command did not return an accepted state.");
          }

          report.CommandCountAfter = commandResult.State.CommandCount;
          report.StateHashes["afterCommand"] = commandResult.State.StateHash;
          if (commandResult.State.CommandCount <= beforeCommandState.CommandCount)
          {
            throw new InvalidOperationException("Command did not advance command count.");
          }

          foreach (var evt in commandResult.Events)
          {
            visualQueue.Enqueue(evt);
            report.ObservedEventTypes.Add(evt.EventType);
          }

          report.CommandsSubmitted = 1;
          report.MaxBoardEntityCount = Math.Max(
            report.MaxBoardEntityCount,
            commandResult.State.Players.Values.Sum(player => player.Zones?.Board?.Count ?? 0));
          if (options.RunToTerminal)
          {
            var currentState = commandResult.State;
            while (!currentState.IsTerminal && report.CommandsSubmitted < options.MaxCommands)
            {
              var nextPlayerId = currentState.ActivePlayerId ?? playerOneId;
              var nextToken = string.Equals(nextPlayerId, DebugDemoSeeder.PlayerTwoId, StringComparison.OrdinalIgnoreCase)
                ? p2Token
                : p1Token;
              var nextLegal = await RequireValue(
                legalActionService.FetchLegalActionsAsync(report.MatchId, nextPlayerId, nextToken, cancellationToken),
                "full-match legal actions");
              var nextCommand = SelectFullMatchCommand(nextLegal);
              if (nextCommand == null)
              {
                throw new InvalidOperationException("Full match reached a non-terminal state without a complete server-provided legal command.");
              }

              if (options.InterCommandDelayMs > 0) await Task.Delay(options.InterCommandDelayMs, cancellationToken);

              var nextResult = await RequireValue(
                commandQueue.SubmitAsync(report.MatchId, nextCommand, nextToken, currentState.StateHash, cancellationToken),
                "full-match command submission");
              if (!nextResult.Succeeded || nextResult.State == null)
              {
                throw new InvalidOperationException("Full-match command was rejected.");
              }

              if (nextResult.State.CommandCount <= currentState.CommandCount)
              {
                throw new InvalidOperationException("Full-match command did not advance command count.");
              }

              report.CommandsSubmitted++;
              report.CommandTypes.Add(nextCommand.CommandType);
              report.MaxBoardEntityCount = Math.Max(
                report.MaxBoardEntityCount,
                nextResult.State.Players.Values.Sum(player => player.Zones?.Board?.Count ?? 0));
              foreach (var evt in nextResult.Events)
              {
                visualQueue.Enqueue(evt);
                report.ObservedEventTypes.Add(evt.EventType);
              }

              currentState = nextResult.State;
              commandResult = nextResult;
            }

            report.ReachedTerminal = currentState.IsTerminal;
            report.TerminalReason = currentState.TerminalReason;
            report.CommandCountAfter = currentState.CommandCount;
            report.StateHashes["terminal"] = currentState.StateHash;
            if (options.RequireTerminal && !currentState.IsTerminal)
            {
              throw new InvalidOperationException($"Full match did not reach terminal state within {options.MaxCommands} commands.");
            }
          }
        });

        MatchEventsPollResult events = null;
        await Step(report, "poll-events", async () =>
        {
          stateStore.ReplaceFromServer(commandResult.State);
          events = await RequireValue(eventPoller.PollOnceAsync(report.MatchId, activeToken, cancellationToken), "event poll");
          report.EventCount = commandResult.Events.Count + events.Events.Count;
          report.LastEventSeq = Math.Max(
            commandResult.Events.Count == 0 ? 0 : commandResult.Events.Max(evt => evt.EventSeq),
            events.Events.Count == 0 ? 0 : events.Events.Max(evt => evt.EventSeq));
          AssertMonotonic(commandResult.Events.Concat(events.Events));
          foreach (var evt in events.Events)
          {
            visualQueue.Enqueue(evt);
            report.ObservedEventTypes.Add(evt.EventType);
          }
        });

        MatchReconnectResult reconnect = null;
        await Step(report, "reconnect", async () =>
        {
          report.ReconnectStatus.BeforeStateHash = stateStore.StateHash;
          report.ReconnectStatus.BeforeEventCursor = stateStore.LastSeenEventSeq;
          reconnect = await RequireValue(reconnectService.ReconnectAsync(report.MatchId, activeToken, cancellationToken), "reconnect");
          if (!reconnect.Succeeded || reconnect.State == null)
          {
            throw new InvalidOperationException("Reconnect failed to return state.");
          }

          report.ReconnectStatus.Succeeded = true;
          report.ReconnectStatus.AfterStateHash = reconnect.State.StateHash;
          report.ReconnectStatus.AfterEventCursor = reconnect.NextEventSeq - 1;
          report.ReconnectStatus.MissedEventCount = reconnect.Events.Count;
          report.StateHashes["reconnect"] = reconnect.State.StateHash;
          foreach (var evt in reconnect.Events)
          {
            visualQueue.Enqueue(evt);
          }
        });

        await Step(report, "refresh-legal-actions", async () =>
        {
          var refreshed = await RequireValue(
            legalActionService.FetchLegalActionsAsync(report.MatchId, activePlayerId, activeToken, cancellationToken),
            "post-reconnect legal actions");
          report.RefreshedLegalActionCount = refreshed.LegalActions.Count;
        });

        await Step(report, "replay-verify", async () =>
        {
          var replay = await RequireValue(matchService.VerifyReplayAsync(report.MatchId, activeToken, cancellationToken), "replay verify");
          report.ReplayVerified = replay.Replay?.IsVerified == true;
          report.ReplayFinalStateHash = replay.Replay?.FinalStateHash;
          if (!report.ReplayVerified)
          {
            throw new InvalidOperationException("Replay verification failed.");
          }
        });

        report.CompletedAt = DateTimeOffset.UtcNow.ToString("O");
        report.FinalVerdict = LocalMatchFlowVerdicts.Ready;
        var path = WriteReport(report, options);
        return LocalMatchFlowResult.Success(report, path);
      }
      catch (Exception ex)
      {
        report.CompletedAt = DateTimeOffset.UtcNow.ToString("O");
        report.FinalVerdict = LocalMatchFlowVerdicts.Failed;
        report.Errors.Add(ex.Message);
        var path = WriteReport(report, options);
        return LocalMatchFlowResult.Failure(report, path, ex.Message);
      }
    }

    public static string GenerateSmokeRunId() =>
      "unity-smoke-run-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + ShortRandom();

    public static string GenerateSmokeMatchId() =>
      "unity-live-smoke-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + ShortRandom();

    private static async Task Step(LocalMatchFlowReport report, string name, Func<Task> action)
    {
      var step = new LocalMatchFlowStepResult
      {
        Name = name,
        StartedAt = DateTimeOffset.UtcNow.ToString("O")
      };
      report.Steps.Add(step);
      try
      {
        await action();
        step.Succeeded = true;
      }
      catch (Exception ex)
      {
        step.Succeeded = false;
        step.Error = ex.Message;
        throw;
      }
      finally
      {
        step.CompletedAt = DateTimeOffset.UtcNow.ToString("O");
      }
    }

    private static async Task<T> RequireValue<T>(Task<UnityApiResult<T>> task, string label)
    {
      var result = await task;
      if (!result.Succeeded)
      {
        throw new InvalidOperationException(label + " failed: " + result.Error?.Code + " " + result.Error?.Message);
      }

      if (result.Value == null)
      {
        throw new InvalidOperationException(label + " returned no value.");
      }

      return result.Value;
    }

    public static MatchCommandDto SelectSmokeCommand(ExternalLegalActionsResult legalActions)
    {
      return SelectSmokeCommandWithReason(legalActions).Command;
    }

    public static SmokeCommandSelection SelectSmokeCommandWithReason(ExternalLegalActionsResult legalActions)
    {
      var actions = legalActions?.LegalActions ?? new List<MatchCommandDto>();
      var endTurn = actions.FirstOrDefault(action => string.Equals(action.CommandType, "END_TURN", StringComparison.OrdinalIgnoreCase));
      if (endTurn != null)
      {
        return new SmokeCommandSelection(endTurn, "preferred END_TURN legal action");
      }

      var fallback = actions.FirstOrDefault(IsCompleteNonTargeted);
      return fallback != null
        ? new SmokeCommandSelection(fallback, "first complete non-targeted server-provided legal action")
        : new SmokeCommandSelection(null, "no complete non-targeted server-provided legal action");
    }

    public static MatchCommandDto SelectFullMatchCommand(ExternalLegalActionsResult legalActions)
    {
      var priority = new[]
      {
        "RESOLVE_CHOICE", "ATTACK", "PLAY_CARD", "USE_HERO_POWER", "ACTIVATE_LOCATION", "END_TURN", "CONCEDE"
      };
      var actions = legalActions?.LegalActions ?? new List<MatchCommandDto>();
      foreach (var commandType in priority)
      {
        var command = actions.FirstOrDefault(action =>
          action != null && string.Equals(action.CommandType, commandType, StringComparison.OrdinalIgnoreCase));
        if (command != null)
        {
          return command;
        }
      }

      return actions.FirstOrDefault(action => action != null && !string.IsNullOrWhiteSpace(action.CommandType));
    }

    private static bool IsCompleteNonTargeted(MatchCommandDto action) =>
      action != null &&
      !string.IsNullOrWhiteSpace(action.CommandType) &&
      !TargetedCommandTypes.Contains(action.CommandType) &&
      !HasCandidateRefs(action.Refs);

    private static bool HasCandidateRefs(Dictionary<string, string> refs) =>
      refs != null &&
      refs.Keys.Any(key => key.IndexOf("candidate", StringComparison.OrdinalIgnoreCase) >= 0);

    private static void AssertMonotonic(IEnumerable<PublicMatchEventRecord> events)
    {
      long last = 0;
      foreach (var evt in events)
      {
        if (evt.EventSeq <= last)
        {
          throw new InvalidOperationException("Event sequence was not monotonic at " + evt.EventSeq);
        }

        last = evt.EventSeq;
      }
    }

    private static string WriteReport(LocalMatchFlowReport report, LocalMatchFlowOptions options)
    {
      if (!options.WriteReport)
      {
        return string.Empty;
      }

      var root = string.IsNullOrWhiteSpace(options.ReportRoot)
        ? DefaultReportRoot()
        : options.ReportRoot;
      var directory = Path.Combine(root, SafeTimestamp(report.StartedAt));
      Directory.CreateDirectory(directory);
      var path = Path.Combine(directory, "report.json");
      File.WriteAllText(path, JsonConvert.SerializeObject(report, Formatting.Indented));
      return path;
    }

    private static string DefaultReportRoot()
    {
      var dataPath = Application.dataPath;
      var repositoryRoot = Path.GetFullPath(Path.Combine(dataPath, "..", "..", "..", ".."));
      return Path.Combine(repositoryRoot, "artifacts", "unity-client-live-smoke");
    }

    private static string SafeTimestamp(string timestamp) =>
      timestamp.Replace(":", string.Empty).Replace(".", string.Empty).Replace("+", "Z");

    private static string ShortRandom() =>
      Guid.NewGuid().ToString("N").Substring(0, 8);
  }

  public sealed class LocalMatchFlowOptions
  {
    public bool SeedDemoData { get; set; } = true;
    public SeedMode SeedMode { get; set; } = SeedMode.Ensure;
    public bool WriteReport { get; set; } = true;
    public string ReportRoot { get; set; } = string.Empty;
    public string MatchId { get; set; } = string.Empty;
    public string SmokeRunId { get; set; } = string.Empty;
    public string UnityVersion { get; set; } = string.Empty;
    public string SmokeRunMode { get; set; } = "PlayMode";
    public string UnityBatchmodeLogPath { get; set; } = string.Empty;
    public string TestResultsPath { get; set; } = string.Empty;
    public string BuildValidationSummary { get; set; } = string.Empty;
    public bool RunToTerminal { get; set; }
    public bool RequireTerminal { get; set; }
    public int MaxCommands { get; set; } = 512;
    public int InterCommandDelayMs { get; set; }
    public string PlayerOneId { get; set; } = string.Empty;
    public string PlayerOneDeckId { get; set; } = string.Empty;
    public string PlayerTwoDeckId { get; set; } = string.Empty;
  }

  public sealed class LocalMatchFlowResult
  {
    public bool Succeeded { get; private set; }
    public LocalMatchFlowReport Report { get; private set; }
    public string ReportPath { get; private set; }
    public string Error { get; private set; }

    public static LocalMatchFlowResult Success(LocalMatchFlowReport report, string reportPath) =>
      new() { Succeeded = true, Report = report, ReportPath = reportPath };

    public static LocalMatchFlowResult Failure(LocalMatchFlowReport report, string reportPath, string error) =>
      new() { Succeeded = false, Report = report, ReportPath = reportPath, Error = error };
  }

  public sealed class LocalMatchFlowStepResult
  {
    public string Name { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public string StartedAt { get; set; } = string.Empty;
    public string CompletedAt { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
  }

  public sealed class LocalMatchFlowReport
  {
    public string StartedAt { get; set; } = string.Empty;
    public string CompletedAt { get; set; } = string.Empty;
    public string SmokeRunId { get; set; } = string.Empty;
    public string UnityVersion { get; set; } = string.Empty;
    public string BackendUrl { get; set; } = string.Empty;
    public string MatchId { get; set; } = string.Empty;
    public string MatchIdStrategy { get; set; } = string.Empty;
    public List<string> PlayerIds { get; set; } = new();
    public string SpectatorId { get; set; } = string.Empty;
    public bool PlayerOneTokenPresent { get; set; }
    public bool PlayerTwoTokenPresent { get; set; }
    public bool SpectatorTokenPresent { get; set; }
    public bool BootSucceeded { get; set; }
    public string CapabilityStatus { get; set; } = string.Empty;
    public string BackendPrecheck { get; set; } = string.Empty;
    public bool SeedSucceeded { get; set; }
    public string SeedMode { get; set; } = string.Empty;
    public string SeedStatus { get; set; } = string.Empty;
    public List<SeedResourceResult> SeedResults { get; set; } = new();
    public List<string> ResourceCompatibilityChecks { get; set; } = new();
    public Dictionary<string, string> StateHashes { get; set; } = new();
    public string CommandSubmitted { get; set; } = string.Empty;
    public int CommandsSubmitted { get; set; }
    public List<string> CommandTypes { get; set; } = new();
    public int MaxBoardEntityCount { get; set; }
    public HashSet<string> ObservedEventTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool ReachedTerminal { get; set; }
    public string TerminalReason { get; set; } = string.Empty;
    public int CommandCountBefore { get; set; }
    public int CommandCountAfter { get; set; }
    public int LegalActionCountBeforeCommand { get; set; }
    public string SmokeCommandSelectionReason { get; set; } = string.Empty;
    public string SmokeRunMode { get; set; } = string.Empty;
    public string UnityBatchmodeLogPath { get; set; } = string.Empty;
    public string TestResultsPath { get; set; } = string.Empty;
    public string BuildValidationSummary { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public Dictionary<string, string> Environment { get; set; } = new();
    public int EventCount { get; set; }
    public long LastEventSeq { get; set; }
    public int RefreshedLegalActionCount { get; set; }
    public LocalMatchReconnectReport ReconnectStatus { get; set; } = new();
    public bool ReplayVerified { get; set; }
    public string ReplayFinalStateHash { get; set; } = string.Empty;
    public List<HiddenInformationAssertionResult> HiddenInfoAssertions { get; set; } = new();
    public List<LocalMatchFlowStepResult> Steps { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public string FinalVerdict { get; set; } = string.Empty;

    public static LocalMatchFlowReport Create(string backendUrl, string unityVersion) =>
      new()
      {
        StartedAt = DateTimeOffset.UtcNow.ToString("O"),
        BackendUrl = backendUrl,
        UnityVersion = unityVersion
      };
  }

  public sealed class LocalMatchReconnectReport
  {
    public bool Succeeded { get; set; }
    public string BeforeStateHash { get; set; } = string.Empty;
    public string AfterStateHash { get; set; } = string.Empty;
    public long BeforeEventCursor { get; set; }
    public long AfterEventCursor { get; set; }
    public int MissedEventCount { get; set; }
  }

  public sealed class SmokeCommandSelection
  {
    public SmokeCommandSelection(MatchCommandDto command, string reason)
    {
      Command = command;
      Reason = reason ?? string.Empty;
    }

    public MatchCommandDto Command { get; }
    public string Reason { get; }
  }

  public static class LocalMatchFlowVerdicts
  {
    public const string Ready = "unity-client-full-live-match-smoke-ready";
    public const string Failed = "unity-client-full-live-match-smoke-failed";
  }
}
