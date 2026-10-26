using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CardGame.Unity.Client.Api;
using CardGame.Unity.Client.Commands;
using CardGame.Unity.Client.Config;
using CardGame.Unity.Client.Errors;
using CardGame.Unity.Client.Events;
using CardGame.Unity.Client.Match;
using CardGame.Unity.Client.Presentation;
using CardGame.Unity.Client.State;
using CardGame.Unity.Client.UI;
using CardGame.Unity.Client.UI.Presentation;
using CardGame.Unity.Client.UI.Shell;
using CardGame.Unity.Client.Audio;
using Game.UnityClient.Contracts;
using UnityEngine;

namespace CardGame.Unity.Client.DebugTools
{
  public sealed class DebugMatchBootstrapper : MonoBehaviour
  {
    public static event Action ProductionMatchCompleted;
    public static event Action GameplayMainMenuRequested;
    public static event Action GameplaySettingsRequested;
    public static DebugMatchBootstrapper Instance { get; private set; }
    public static string LastCompletedMatchId { get; private set; } = string.Empty;
    public static string LastTerminalReason { get; private set; } = string.Empty;
    public static int LastCompletedCommandCount { get; private set; }
    public static bool LastReplayVerified { get; private set; }
    private UnityBackendConfig config;
    private BackendApiClient api;
    private CapabilityChecker capabilityChecker;
    private SessionService sessionService;
    private MatchService matchService;
    private LegalActionService legalActionService;
    private CommandQueue commandQueue;
    private CommandBuilder commandBuilder;
    private MatchStateStore stateStore;
    private EventPoller eventPoller;
    private ReconnectService reconnectService;
    private DebugDemoSeeder seeder;
    private EventVisualDispatcher visualDispatcher;
    private EventVisualQueue visualQueue;
    private PresentationPlaybackRunner presentationRunner;
    private EventPresentationDirector eventPresentationDirector;
    private GameplayScreenStateMachine screenStateMachine;
    private PlaytestEvidenceRecorder playtestRecorder;
    private PlaytestEvidenceExporter playtestExporter;
    private TargetSelectionController targetSelectionController;
    private ClientModeState modeState;
    private CardGameScreen screen;
    private PresentationEffectsHost effectsHost;
    private AudioEventRouter audioEventRouter;
    private AudioPlaybackController audioPlayback;

    private string matchId = string.Empty;
    private string playerOneToken = string.Empty;
    private string playerTwoToken = string.Empty;
    private string spectatorToken = string.Empty;
    private string lastError = string.Empty;
    public static string LastStartupError => Instance?.lastError ?? string.Empty;
    private string lastVisual = string.Empty;
    private string replayStatus = string.Empty;
    private bool reconnecting;
    private bool botTurnInProgress;
    private string lastFirstPlayerId=DebugDemoSeeder.PlayerOneId;
    private string lastFirstDeckId=DebugDemoSeeder.PlayerOneDeckId;
    private string lastSecondDeckId=DebugDemoSeeder.PlayerTwoDeckId;
    private ClientUiState uiState = ClientUiState.ReadyToSeed;

    private string ActiveViewerPlayerId => modeState?.ProjectionKey ?? DebugDemoSeeder.PlayerOneId;
    private bool CommandsEnabled => modeState?.CommandsEnabled == true && !commandQueue.CommandInFlight && !reconnecting && !botTurnInProgress;

    private string ActiveSessionToken => ActiveViewerPlayerId switch
    {
      DebugDemoSeeder.PlayerTwoId => playerTwoToken,
      DebugDemoSeeder.SpectatorId => spectatorToken,
      _ => playerOneToken
    };

    private ExternalLegalActionsResult legalActions;
    private readonly List<string> eventLines = new();
    private readonly List<string> productionCommandTypes = new();
    private int productionMaxBoardEntities;
    private CancellationTokenSource lifecycleCts;
    private CancellationTokenSource pollingCts;

    private void Awake()
    {
      Application.runInBackground = true;
      Instance = this;
      lifecycleCts = new CancellationTokenSource();
      config = UnityBackendConfig.CreateRuntimeDefault();
      api = new BackendApiClient(config);
      capabilityChecker = new CapabilityChecker(api);
      sessionService = new SessionService(api);
      matchService = new MatchService(api);
      legalActionService = new LegalActionService(api);
      commandBuilder = new CommandBuilder();
      commandQueue = new CommandQueue(api, commandBuilder);
      stateStore = new MatchStateStore();
      eventPoller = new EventPoller(api, config, stateStore);
      reconnectService = new ReconnectService(api, stateStore);
      seeder = new DebugDemoSeeder(api);
      visualDispatcher = new EventVisualDispatcher();
      visualQueue = new EventVisualQueue(visualDispatcher);
      presentationRunner = new PresentationPlaybackRunner();
      eventPresentationDirector = new EventPresentationDirector();
      screenStateMachine = new GameplayScreenStateMachine();
      playtestRecorder = new PlaytestEvidenceRecorder();
      playtestExporter = new PlaytestEvidenceExporter();
      targetSelectionController = new TargetSelectionController();
      modeState = new ClientModeState();

      screen = new CardGameScreen();
      screen.Build(transform, Debug.isDebugBuild);
      effectsHost = PresentationEffectsHost.Attach(screen.PresentationEffectsParent);
      effectsHost.SetAnchorResolver(screen.FindPresentationWorldAnchor);
      audioEventRouter = new AudioEventRouter();
      var volume = new AudioVolumeSettings { MasterVolume = PlayerPrefs.GetFloat("cardgame.effects-volume", 0.8f) };
      audioPlayback = new AudioPlaybackController(new AudioCueRegistry(), volume);
      screen.SetVisible(false);
      screen.BootRequested += () => _ = CheckBackendAsync();
      screen.SeedRequested += () => _ = SeedDemoDataAsync();
      screen.StartRequested += () => _ = StartDebugMatchAsync();
      screen.ModeCycleRequested += CycleMode;
      screen.SwitchViewerRequested += SwitchViewer;
      screen.RefreshStateRequested += () => _ = RefreshStateAsync();
      screen.RefreshLegalActionsRequested += () => _ = RefreshLegalActionsAsync();
      screen.SubmitCommandRequested += template => _ = SubmitCommandAsync(template);
      screen.TargetSelectionRequested += BeginTargetSelection;
      screen.TargetSelectedRequested += targetRef => _ = SubmitSelectedTargetAsync(targetRef);
      screen.TargetSelectionCancelledRequested += CancelTargetSelection;
      screen.ResolveChoiceRequested += (choice, option, template) => _ = ResolveChoiceAsync(choice, option, template);
      screen.ReconnectRequested += () => _ = ReconnectAsync();
      screen.ReplayRequested += () => _ = VerifyReplayAsync();
      screen.RestartRequested += () => _ = StartMatchAsync(lastFirstPlayerId,lastFirstDeckId,lastSecondDeckId);
      screen.MainMenuRequested += () => GameplayMainMenuRequested?.Invoke();
      screen.SettingsRequested += () => GameplaySettingsRequested?.Invoke();
      screen.TogglePollingRequested += TogglePolling;
      screen.ExportPlaytestReportRequested += ExportPlaytestReport;

      eventPoller.EventAccepted += evt => _ = HandleAcceptedEventAsync(evt);
      eventPoller.PollError += error => _ = HandlePollErrorAsync(error);
      RefreshUi();
    }

    private void OnDestroy()
    {
      if (Instance == this) Instance = null;
      pollingCts?.Cancel();
      pollingCts?.Dispose();
      lifecycleCts?.Cancel();
      lifecycleCts?.Dispose();
    }

    public static void SetGameplayVisible(bool visible) => Instance?.screen?.SetVisible(visible);

    public static void SetProductionCardCatalog(IEnumerable<GameplayCardPresentation> cards) =>
      Instance?.screen?.SetCardCatalog(cards);

    public static async Task<RecordedInteractiveMatchEvidence> AutoPlayHumanToTerminalAsync()
    {
      if (Instance == null) return new RecordedInteractiveMatchEvidence { Succeeded = false, Error = "Gameplay bootstrap is unavailable." };
      return await Instance.AutoPlayHumanAsync();
    }

    public static async Task<bool> StartProductionMatchAsync()
    {
      if (Instance == null) return false;
      if (!await PrepareProductionContentAsync()) return false;
      await Instance.StartDebugMatchAsync();
      return Instance.uiState == ClientUiState.MatchActive;
    }

    public static async Task<bool> StartProductionMatchAsync(string playerId, string runtimeDeckId, string opponentRuntimeDeckId = null)
    {
      if (Instance == null || string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(runtimeDeckId)) return false;
      // Player decks are registered with their compatible bot mirror when they
      // are saved. Production match startup must not depend on developer seed
      // resources, whose persisted revision may legitimately come from an
      // older release.
      if (!await PrepareBackendAsync()) return false;
      await Instance.StartMatchAsync(playerId, runtimeDeckId, opponentRuntimeDeckId);
      return Instance.uiState == ClientUiState.MatchActive;
    }

    private static async Task<bool> PrepareBackendAsync()
    {
      if (Instance == null) return false;
      await Instance.CheckBackendAsync();
      return Instance != null && Instance.uiState != ClientUiState.Faulted;
    }

    public static async Task<bool> PrepareProductionContentAsync()
    {
      if (Instance == null) return false;
      await Instance.CheckBackendAsync();
      if (Instance.uiState == ClientUiState.Faulted) return false;
      await Instance.SeedDemoDataAsync();
      return Instance.uiState != ClientUiState.Faulted;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
      if (hasFocus && !string.IsNullOrWhiteSpace(matchId) && !string.IsNullOrWhiteSpace(ActiveSessionToken))
      {
        _ = ReconnectAsync();
      }
    }

    private async Task CheckBackendAsync()
    {
      lastError = string.Empty;
      uiState = ClientUiState.Booting;
      RefreshUi();
      var ok = await capabilityChecker.CheckAsync(lifecycleCts.Token);
      if (!ok)
      {
        SetError(new UnityError { Code = "BOOT_BLOCKED", Message = capabilityChecker.BootStatus, Retryable = false });
      }

      uiState = ok ? ClientUiState.ReadyToSeed : ClientUiState.Faulted;
      RefreshUi();
    }

    private async Task SeedDemoDataAsync()
    {
      lastVisual = "Seeding demo data through public endpoints.";
      uiState = ClientUiState.ReadyToSeed;
      RefreshUi();
      var seed = await seeder.SeedAsync(SeedMode.Ensure, lifecycleCts.Token);
      lastVisual = seed.Succeeded
        ? "Demo data ready: " + seed.Summary
        : "Demo seeding failed: " + seed.Summary;
      uiState = seed.Succeeded ? ClientUiState.ReadyToStart : ClientUiState.Faulted;
      RefreshUi();
    }

    private Task StartDebugMatchAsync() => StartMatchAsync(DebugDemoSeeder.PlayerOneId, DebugDemoSeeder.PlayerOneDeckId, DebugDemoSeeder.PlayerTwoDeckId);

    private async Task StartMatchAsync(string firstPlayerId, string firstDeckId, string secondDeckId)
    {
      lastFirstPlayerId=firstPlayerId;lastFirstDeckId=firstDeckId;lastSecondDeckId=string.IsNullOrWhiteSpace(secondDeckId)?DebugDemoSeeder.PlayerTwoDeckId:secondDeckId;
      lastError = string.Empty;
      uiState = ClientUiState.ReadyToStart;
      modeState.ConfigurePlayers(firstPlayerId, DebugDemoSeeder.PlayerTwoId);
      if (!capabilityChecker.LastResult.Supported)
      {
        var ok = await capabilityChecker.CheckAsync(lifecycleCts.Token);
        if (!ok)
        {
          SetError(new UnityError { Code = "BOOT_BLOCKED", Message = capabilityChecker.BootStatus, Retryable = false });
          return;
        }
      }

      var p1 = await sessionService.CreatePlayerSessionAsync(firstPlayerId, lifecycleCts.Token);
      if (!p1.Succeeded || p1.Value?.Session == null)
      {
        SetError(p1.Error);
        return;
      }

      var p2 = await sessionService.CreatePlayerSessionAsync(DebugDemoSeeder.PlayerTwoId, lifecycleCts.Token);
      if (!p2.Succeeded || p2.Value?.Session == null)
      {
        SetError(p2.Error);
        return;
      }

      playerOneToken = p1.Value.Session.SessionToken;
      playerTwoToken = p2.Value.Session.SessionToken;
      var spectator = await sessionService.CreatePlayerSessionAsync(DebugDemoSeeder.SpectatorId, lifecycleCts.Token);
      if (!spectator.Succeeded || spectator.Value?.Session == null)
      {
        SetError(spectator.Error);
        return;
      }

      spectatorToken = spectator.Value.Session.SessionToken;
      matchId = $"unity-board-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}".Substring(0, 38);
      productionCommandTypes.Clear();
      productionMaxBoardEntities = 0;
      playtestRecorder.Start(config.BackendBaseUrl, matchId);

      var start = await matchService.StartMatchAsync(
        matchId,
        new[] { firstDeckId, string.IsNullOrWhiteSpace(secondDeckId) ? DebugDemoSeeder.PlayerTwoDeckId : secondDeckId },
        new Dictionary<string, string>
        {
          [firstPlayerId] = playerOneToken,
          [DebugDemoSeeder.PlayerTwoId] = playerTwoToken
        },
        firstPlayerId,
        new[] { spectatorToken },
        lifecycleCts.Token);

      if (!start.Succeeded)
      {
        SetError(start.Error);
        return;
      }

      if (start.Value?.State != null)
      {
        stateStore.ReplaceFromServer(start.Value.State);
      }

      await RefreshLegalActionsAsync();
      await PollEventsOnceAsync();
      uiState = ClientUiState.MatchActive;
      playtestRecorder.LogHiddenInfoCheck("state projections loaded from backend");
      RefreshUi();
    }

    private async Task RefreshStateAsync()
    {
      if (string.IsNullOrWhiteSpace(matchId) || string.IsNullOrWhiteSpace(ActiveSessionToken))
      {
        SetError(new UnityError { Code = "MATCH_NOT_STARTED", Message = "Start a debug match first." });
        return;
      }

      var state = await matchService.FetchStateAsync(matchId, ActiveSessionToken, lifecycleCts.Token);
      if (state.Succeeded)
      {
        stateStore.ReplaceFromServer(state.Value);
      }
      else
      {
        SetError(state.Error);
      }

      RefreshUi();
    }

    private async Task RefreshLegalActionsAsync()
    {
      if (string.IsNullOrWhiteSpace(matchId) || modeState.IsSpectator)
      {
        legalActions = modeState.IsSpectator ? null : legalActions;
        RefreshUi();
        return;
      }

      var result = await legalActionService.FetchLegalActionsAsync(matchId, ActiveViewerPlayerId, ActiveSessionToken, lifecycleCts.Token);
      if (result.Succeeded)
      {
        legalActions = result.Value;
      }
      else
      {
        SetError(result.Error);
      }

      RefreshUi();
    }

    private async Task SubmitCommandAsync(MatchCommandDto template)
    {
      if (!CommandsEnabled)
      {
        SetError(new UnityError { Code = "COMMANDS_DISABLED", Message = "Commands are disabled for spectator mode, reconnecting, or an in-flight command." });
        return;
      }

      if (template == null)
      {
        SetError(new UnityError { Code = "LEGAL_ACTION_MISSING", Message = "No server-provided legal action template is selected." });
        return;
      }

      uiState = ClientUiState.SubmittingCommand;
      playtestRecorder.LogCommand(template.CommandType);
      productionCommandTypes.Add(template.CommandType);
      RefreshUi();
      var result = await commandQueue.SubmitAsync(matchId, template, ActiveSessionToken, stateStore.StateHash, lifecycleCts.Token);
      await HandleCommandResultAsync(result);
    }

    private void BeginTargetSelection(LegalActionViewModel action)
    {
      if (!CommandsEnabled)
      {
        SetError(new UnityError { Code = "COMMANDS_DISABLED", Message = "Target selection is disabled while commands are unavailable." });
        return;
      }

      if (!targetSelectionController.Begin(action))
      {
        SetError(new UnityError { Code = "TARGET_SELECTION_UNAVAILABLE", Message = LegalActionPresenter.TargetCandidatesUnavailableMessage });
        return;
      }

      lastVisual = "Select a highlighted server-provided target.";
      uiState = ClientUiState.SelectingTarget;
      RefreshUi();
    }

    private async Task SubmitSelectedTargetAsync(string targetRef)
    {
      var command = targetSelectionController.Select(targetRef);
      if (command == null)
      {
        playtestRecorder.LogTargetSelection(targetRef, false);
        SetError(new UnityError { Code = "TARGET_NOT_SELECTABLE", Message = "Selected target was not included in the server-provided target candidates." });
        return;
      }

      playtestRecorder.LogTargetSelection(targetRef, true);
      await SubmitCommandAsync(command);
    }

    private void CancelTargetSelection()
    {
      targetSelectionController.Cancel();
      lastVisual = "Target selection cancelled.";
      uiState = ClientUiState.MatchActive;
      RefreshUi();
    }

    private async Task ResolveChoiceAsync(PublicChoiceView choice, PublicChoiceOptionView option, MatchCommandDto template)
    {
      if (!CommandsEnabled)
      {
        SetError(new UnityError { Code = "COMMANDS_DISABLED", Message = "Choice resolution is disabled for spectator mode, reconnecting, or an in-flight command." });
        return;
      }

      if (choice == null || option == null || template == null)
      {
        SetError(new UnityError { Code = "CHOICE_TEMPLATE_MISSING", Message = "Choice resolution needs a visible choice, option, and server template." });
        return;
      }

      ExternalCommandSubmissionRequest request;
      try
      {
        request = commandBuilder.BuildResolveChoice(template, ActiveSessionToken, stateStore.StateHash, choice.ChoiceId, option.OptionId);
      }
      catch (Exception ex)
      {
        SetError(new UnityError { Code = "CHOICE_BUILD_FAILED", Message = ex.Message });
        return;
      }

      uiState = ClientUiState.ResolvingChoice;
      playtestRecorder.LogChoice(choice.ChoiceId, option.OptionId);
      RefreshUi();
      var result = await api.PostJsonAsync<ExternalCommandSubmissionResult>(
        $"/matches/{Uri.EscapeDataString(matchId)}/commands",
        request,
        ActiveSessionToken,
        lifecycleCts.Token);
      await HandleCommandResultAsync(result);
    }

    private async Task HandleCommandResultAsync(UnityApiResult<ExternalCommandSubmissionResult> result)
    {
      if (!result.Succeeded)
      {
        SetError(result.Error);
        if (result.Error.Code == "COMMAND_PRE_STATE_HASH_MISMATCH")
        {
          await ReconnectAsync();
        }

        return;
      }

      await ApplyAcceptedCommandResultAsync(result.Value, useReturnedState: true);
      var terminal = stateStore.Current?.IsTerminal == true;
      uiState = terminal ? ClientUiState.Terminal : ClientUiState.MatchActive;
      RefreshUi();
      if (terminal)
      {
        await PublishCompletionAsync();
        ProductionMatchCompleted?.Invoke();
        return;
      }

      if (StringComparer.Ordinal.Equals(stateStore.Current?.ActivePlayerId, DebugDemoSeeder.PlayerTwoId))
      {
        await RunBotTurnAsync();
        return;
      }

      await RefreshLegalActionsAsync();
    }

    private async Task ApplyAcceptedCommandResultAsync(ExternalCommandSubmissionResult result, bool useReturnedState)
    {
      uiState = ClientUiState.SubmittingCommand;
      if (useReturnedState && result?.State != null)
      {
        stateStore.ReplaceFromServer(result.State);
      }

      foreach (var evt in result?.Events ?? new List<PublicMatchEventRecord>())
      {
        if (stateStore.TryAcceptEvent(evt))
        {
          await HandleAcceptedEventAsync(evt);
        }
      }

      // Always return to the human player's projection after a bot command so
      // hidden opponent cards never leak through the bot session response.
      await PollEventsOnceAsync();
      productionMaxBoardEntities = Math.Max(
        productionMaxBoardEntities,
        stateStore.Current?.Players?.Values.Sum(player => player.Zones?.Board?.Count ?? 0) ?? 0);
    }

    private async Task RunBotTurnAsync()
    {
      if (botTurnInProgress || stateStore.Current?.IsTerminal == true)
      {
        return;
      }

      botTurnInProgress = true;
      legalActions = null;
      uiState = ClientUiState.SubmittingCommand;
      lastVisual = localizationBotThinking();
      RefreshUi();
      try
      {
        var commandsThisTurn = 0;
        while (!lifecycleCts.IsCancellationRequested
          && stateStore.Current?.IsTerminal != true
          && StringComparer.Ordinal.Equals(stateStore.Current?.ActivePlayerId, DebugDemoSeeder.PlayerTwoId))
        {
          if (++commandsThisTurn > 64)
          {
            SetError(new UnityError { Code = "BOT_TURN_COMMAND_LIMIT", Message = "The bot exceeded the per-turn command safety limit.", Retryable = false });
            return;
          }

          var nextLegal = await legalActionService.FetchLegalActionsAsync(
            matchId,
            DebugDemoSeeder.PlayerTwoId,
            playerTwoToken,
            lifecycleCts.Token);
          if (!nextLegal.Succeeded || nextLegal.Value == null)
          {
            SetError(nextLegal.Error ?? new UnityError { Code = "BOT_LEGAL_ACTIONS_FAILED", Message = "The bot could not fetch legal actions." });
            return;
          }

          var command = LocalMatchFlowDriver.SelectFullMatchCommand(nextLegal.Value);
          if (command == null)
          {
            SetError(new UnityError { Code = "BOT_NO_LEGAL_COMMAND", Message = "The bot turn has no complete server-provided legal command." });
            return;
          }

          lastVisual = $"{localizationBotAction()} {command.CommandType}";
          playtestRecorder.LogCommand("BOT:" + command.CommandType);
          productionCommandTypes.Add("BOT:" + command.CommandType);
          RefreshUi();
          await Task.Delay(700, lifecycleCts.Token);
          var submitted = await commandQueue.SubmitAsync(
            matchId,
            command,
            playerTwoToken,
            stateStore.StateHash,
            lifecycleCts.Token);
          if (!submitted.Succeeded || submitted.Value?.State == null)
          {
            SetError(submitted.Error ?? new UnityError { Code = "BOT_COMMAND_REJECTED", Message = "The authoritative server rejected the bot command." });
            return;
          }

          await ApplyAcceptedCommandResultAsync(submitted.Value, useReturnedState: false);
        }

        if (stateStore.Current?.IsTerminal == true)
        {
          uiState = ClientUiState.Terminal;
          RefreshUi();
          await PublishCompletionAsync();
          ProductionMatchCompleted?.Invoke();
          return;
        }

        uiState = ClientUiState.MatchActive;
        await RefreshLegalActionsAsync();
      }
      catch (OperationCanceledException) when (lifecycleCts.IsCancellationRequested)
      {
      }
      finally
      {
        botTurnInProgress = false;
        RefreshUi();
      }
    }

    private static string localizationBotThinking() => "Opponent is thinking...";
    private static string localizationBotAction() => "Opponent action:";

    private async Task<RecordedInteractiveMatchEvidence> AutoPlayHumanAsync()
    {
      try
      {
        while (!lifecycleCts.IsCancellationRequested && stateStore.Current?.IsTerminal != true)
        {
          if (!StringComparer.Ordinal.Equals(stateStore.Current?.ActivePlayerId, ActiveViewerPlayerId))
          {
            await Task.Delay(100, lifecycleCts.Token);
            continue;
          }

          await RefreshLegalActionsAsync();
          var command = LocalMatchFlowDriver.SelectFullMatchCommand(legalActions);
          if (command == null)
          {
            return Evidence(false, "The evidence player found no complete server-provided legal command.");
          }

          lastVisual = "Evidence player action: " + command.CommandType;
          RefreshUi();
          await Task.Delay(700, lifecycleCts.Token);
          var view = new LegalActionPresenter().Create(command);
          if (view.RequiresTargetSelection && view.CandidateTargetRefs.Count > 0)
          {
            BeginTargetSelection(view);
            await Task.Delay(900, lifecycleCts.Token);
            await SubmitSelectedTargetAsync(view.CandidateTargetRefs[0]);
          }
          else
          {
            await SubmitCommandAsync(command);
          }
        }

        await VerifyReplayAsync();
        var hasPlay = productionCommandTypes.Any(type => type.EndsWith("PLAY_CARD", StringComparison.OrdinalIgnoreCase));
        var hasAttack = productionCommandTypes.Any(type => type.EndsWith("ATTACK", StringComparison.OrdinalIgnoreCase));
        var verified = replayStatus.StartsWith("Replay verified", StringComparison.OrdinalIgnoreCase);
        return Evidence(verified && hasPlay && hasAttack && productionMaxBoardEntities > 0, verified ? string.Empty : replayStatus);
      }
      catch (Exception ex)
      {
        return Evidence(false, ex.Message);
      }
    }

    private RecordedInteractiveMatchEvidence Evidence(bool succeeded, string error) => new()
    {
      Succeeded = succeeded,
      MatchId = matchId,
      CommandCount = stateStore.Current?.CommandCount ?? 0,
      CommandTypes = productionCommandTypes.ToArray(),
      MaxBoardEntityCount = productionMaxBoardEntities,
      ReplayVerified = replayStatus.StartsWith("Replay verified", StringComparison.OrdinalIgnoreCase),
      FinalStateHash = stateStore.StateHash,
      Error = error ?? string.Empty
    };

    private async Task PollEventsOnceAsync()
    {
      if (string.IsNullOrWhiteSpace(matchId))
      {
        return;
      }

      var result = await eventPoller.PollOnceAsync(matchId, ActiveSessionToken, lifecycleCts.Token);
      if (!result.Succeeded)
      {
        return;
      }

      await RefreshStateAsync();
    }

    private async Task HandlePollErrorAsync(UnityError error)
    {
      SetError(error);
      if (!string.IsNullOrWhiteSpace(matchId))
      {
        await ReconnectAsync();
      }
    }

    private async Task ReconnectAsync()
    {
      if (string.IsNullOrWhiteSpace(matchId) || string.IsNullOrWhiteSpace(ActiveSessionToken))
      {
        return;
      }

      reconnecting = true;
      uiState = ClientUiState.Reconnecting;
      RefreshUi();
      var result = await reconnectService.ReconnectAsync(matchId, ActiveSessionToken, lifecycleCts.Token);
      reconnecting = false;
      if (!result.Succeeded)
      {
        SetError(result.Error);
        return;
      }

      foreach (var evt in result.Value.Events ?? new List<PublicMatchEventRecord>())
      {
        await HandleAcceptedEventAsync(evt, false);
      }

      playtestRecorder.LogReconnect(true, result.Value.Events?.Count ?? 0);
      await RefreshLegalActionsAsync();
      uiState = stateStore.Current?.IsTerminal == true ? ClientUiState.Terminal : ClientUiState.MatchActive;
      RefreshUi();
    }

    private async Task VerifyReplayAsync()
    {
      if (string.IsNullOrWhiteSpace(matchId))
      {
        return;
      }

      var result = await matchService.VerifyReplayAsync(matchId, ActiveSessionToken, lifecycleCts.Token);
      if (!result.Succeeded)
      {
        SetError(result.Error);
        return;
      }

      var diagnostics = result.Value.Replay.Diagnostics.Count == 0
        ? "none"
        : string.Join("; ", result.Value.Replay.Diagnostics.Select(diagnostic => diagnostic.Code + ":" + diagnostic.Message));
      replayStatus = result.Value.Replay.IsVerified
        ? $"Replay verified. Final hash {result.Value.Replay.FinalStateHash ?? "(none)"}"
        : $"Replay mismatch. Final hash {result.Value.Replay.FinalStateHash ?? "(none)"} Diagnostics {diagnostics}";
      lastVisual = replayStatus;
      playtestRecorder.LogReplay(result.Value.Replay.IsVerified);
      if (!result.Value.Replay.IsVerified)
      {
        uiState = ClientUiState.Faulted;
        SetError(new UnityError { Code = "REPLAY_MISMATCH", Message = replayStatus, Retryable = false });
      }

      RefreshUi();
    }

    private async Task PublishCompletionAsync()
    {
      await VerifyReplayAsync();
      LastCompletedMatchId=stateStore.Current?.MatchId??matchId??string.Empty;
      LastTerminalReason=stateStore.Current?.TerminalReason??string.Empty;
      LastCompletedCommandCount=stateStore.Current?.CommandCount??0;
      LastReplayVerified=replayStatus.StartsWith("Replay verified",StringComparison.OrdinalIgnoreCase);
    }

    private void TogglePolling()
    {
      if (eventPoller.IsPolling)
      {
        pollingCts?.Cancel();
        RefreshUi();
        return;
      }

      if (string.IsNullOrWhiteSpace(matchId))
      {
        SetError(new UnityError { Code = "MATCH_NOT_STARTED", Message = "Start a debug match first." });
        return;
      }

      pollingCts = CancellationTokenSource.CreateLinkedTokenSource(lifecycleCts.Token);
      _ = eventPoller.RunAsync(matchId, ActiveSessionToken, () => stateStore.Current?.IsTerminal == true, pollingCts.Token);
      RefreshUi();
    }

    private void SwitchViewer()
    {
      modeState.SwitchDebugProjection();
      playtestRecorder.LogModeSwitch(modeState.Mode + ":" + modeState.ProjectionKey);
      targetSelectionController.Cancel();
      _ = RefreshStateAsync();
      _ = RefreshLegalActionsAsync();
    }

    private void CycleMode()
    {
      modeState.CycleMode();
      playtestRecorder.LogModeSwitch(modeState.Mode + ":" + modeState.ProjectionKey);
      targetSelectionController.Cancel();
      legalActions = null;
      _ = RefreshStateAsync();
      _ = RefreshLegalActionsAsync();
      RefreshUi();
    }

    private async Task HandleAcceptedEventAsync(PublicMatchEventRecord evt, bool dedupeAlreadyHandled = true)
    {
      if (!dedupeAlreadyHandled && !stateStore.TryAcceptEvent(evt))
      {
        return;
      }

      visualQueue.Enqueue(evt);
      presentationRunner.EnqueueEvents(new[] { evt });
      presentationRunner.Tick(1f);
      var visual = visualQueue.Advance() ?? visualDispatcher.Dispatch(evt);
      var descriptor = eventPresentationDirector.Resolve(evt, PlayerPrefs.GetString(LocalizationService.LocaleKey, "en") == "ru");
      lastVisual = string.IsNullOrWhiteSpace(descriptor.Title)
        ? visual.Message
        : descriptor.Title + (string.IsNullOrWhiteSpace(descriptor.Subtitle) ? string.Empty : " — " + descriptor.Subtitle);
      if (presentationRunner.CurrentIntent != null)
      {
        playtestRecorder.LogPresentationIntent($"{presentationRunner.CurrentIntent.EventSeq}:{presentationRunner.CurrentIntent.EventType}:{presentationRunner.CurrentIntent.IntentType}");
        var cueId = audioEventRouter.CueForIntent(presentationRunner.CurrentIntent);
        var audioRequest = audioPlayback.Request(cueId);
        effectsHost.Play(presentationRunner.CurrentIntent, audioRequest, PlayerPrefs.GetInt("cardgame.reduced-motion", 0) == 1, descriptor);
        playtestRecorder.LogAnimationPlayed();
        if (!audioRequest.Suppressed) playtestRecorder.LogAudioCue(audioRequest.CueId);
      }

      eventLines.Add(visual.ToLogLine(evt.EventSeq));
      if (visual.Blocking && evt.EventType == "ENGINE_FAULTED")
      {
        SetError(new UnityError { Code = "ENGINE_FAULTED", Message = lastVisual, Retryable = false });
      }

      if(string.Equals(evt.EventType,"ENTITY_DIED",StringComparison.OrdinalIgnoreCase))
        await screen.AnimatePreRemovalDeathAsync(evt,PlayerPrefs.GetInt("cardgame.reduced-motion",0)==1);

      if (visual.RefreshState && !string.IsNullOrWhiteSpace(matchId))
      {
        await RefreshStateAsync();
      }
      else
      {
        RefreshUi();
      }

      screen.AnimateCommittedZoneEvent(evt,PlayerPrefs.GetInt("cardgame.reduced-motion",0)==1);
      screen.AnimateTurnEvent(evt,descriptor.Title,descriptor.Subtitle,
        string.Equals(stateStore.Current?.ActivePlayerId,ActiveViewerPlayerId,StringComparison.Ordinal),
        PlayerPrefs.GetInt("cardgame.reduced-motion",0)==1);

      // Damage numbers must begin after the new health values have rendered. Starting
      // them before RefreshStateAsync made short event batches consume the visible hold.
      screen.AnimateCommittedCombatEvent(evt,PlayerPrefs.GetInt("cardgame.reduced-motion",0)==1);
    }

    private void RefreshUi()
    {
      var screenState = screenStateMachine.Evaluate(
        MapGameplayState(uiState),
        modeState.Mode,
        stateStore.StateHash,
        backendAvailable: string.IsNullOrWhiteSpace(lastError) || lastError.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) < 0,
        replayMismatch: uiState == ClientUiState.Faulted && replayStatus.IndexOf("mismatch", StringComparison.OrdinalIgnoreCase) >= 0);
      if (screenState.ResetsTargetSelection && targetSelectionController.Current.IsActive)
      {
        targetSelectionController.Cancel();
      }

      screen.Render(
        config.BackendBaseUrl,
        capabilityChecker,
        stateStore.Current,
        ActiveViewerPlayerId,
        modeState.Mode,
        targetSelectionController.Current,
        uiState,
        legalActions,
        eventLines,
        string.IsNullOrWhiteSpace(replayStatus) ? lastError : lastError + "\n" + replayStatus,
        lastVisual + PresentationSummary(),
        eventPoller.IsPolling,
        CommandsEnabled && screenState.CommandsEnabled,
        reconnecting);
    }

    private void SetError(UnityError error)
    {
      lastError = UnityErrorMapper.ToDebugMessage(error);
      playtestRecorder.LogError(error?.Code, error?.Message);
      RefreshUi();
    }

    private string PresentationSummary()
    {
      var panel = PresentationPanelViewModel.From(presentationRunner);
      return "\nPresentation: " + panel.Status + " | " + panel.CurrentIntent;
    }

    private void ExportPlaytestReport()
    {
      try
      {
        var path = playtestExporter.Export(playtestRecorder.Complete(
          uiState == ClientUiState.Faulted ? "blocked" : "pass-with-limitations"));
        lastVisual = "Playtest report exported: " + path;
      }
      catch (Exception ex)
      {
        SetError(new UnityError { Code = "PLAYTEST_REPORT_EXPORT_FAILED", Message = ex.Message, Retryable = false });
        return;
      }

      RefreshUi();
    }

    private static GameplayScreenState MapGameplayState(ClientUiState state) =>
      state switch
      {
        ClientUiState.Booting => GameplayScreenState.Booting,
        ClientUiState.ReadyToSeed => GameplayScreenState.ReadyToSeed,
        ClientUiState.ReadyToStart => GameplayScreenState.ReadyToStart,
        ClientUiState.MatchActive => GameplayScreenState.MatchActive,
        ClientUiState.SelectingTarget => GameplayScreenState.SelectingTarget,
        ClientUiState.ResolvingChoice => GameplayScreenState.ResolvingChoice,
        ClientUiState.SubmittingCommand => GameplayScreenState.SubmittingCommand,
        ClientUiState.Reconnecting => GameplayScreenState.Reconnecting,
        ClientUiState.Terminal => GameplayScreenState.Terminal,
        ClientUiState.Faulted => GameplayScreenState.Faulted,
        _ => GameplayScreenState.MatchActive
      };
  }

  public sealed class GameplayCardPresentation
  {
    public GameplayCardPresentation(string cardId, string cardVersionId, string displayName, int manaCost, string cardType, string rulesText, Texture2D art, int attack=0, int health=0, int durability=0, int armorGain=0)
    {
      CardId = cardId ?? string.Empty;
      CardVersionId = cardVersionId ?? string.Empty;
      DisplayName = string.IsNullOrWhiteSpace(displayName) ? CardId : displayName;
      ManaCost = manaCost;
      CardType = cardType ?? string.Empty;
      RulesText = rulesText ?? string.Empty;
      Art = art;
      Attack=attack;Health=health;Durability=durability;ArmorGain=armorGain;
    }

    public string CardId { get; }
    public string CardVersionId { get; }
    public string DisplayName { get; }
    public int ManaCost { get; }
    public string CardType { get; }
    public string RulesText { get; }
    public Texture2D Art { get; }
    public int Attack { get; }
    public int Health { get; }
    public int Durability { get; }
    public int ArmorGain { get; }
  }

  public sealed class RecordedInteractiveMatchEvidence
  {
    public bool Succeeded { get; set; }
    public string MatchId { get; set; } = string.Empty;
    public int CommandCount { get; set; }
    public string[] CommandTypes { get; set; } = Array.Empty<string>();
    public int MaxBoardEntityCount { get; set; }
    public bool ReplayVerified { get; set; }
    public string FinalStateHash { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
  }
}
