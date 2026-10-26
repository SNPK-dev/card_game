using System;
using System.Collections.Generic;
using System.Linq;
using CardGame.Unity.Client.UI.Shell;

namespace CardGame.Unity.Client.UI.Status
{
  public sealed class StatusPanelViewModel
  {
    public ModeStatusViewModel Mode { get; set; } = new();
    public CommandStatusViewModel Command { get; set; } = new();
    public ConnectionStatusViewModel Connection { get; set; } = new();
    public ReplayVerificationViewModel Replay { get; set; } = new();
    public ErrorStatusViewModel Error { get; set; } = new();
  }

  public sealed class StatusPanelBinder : PanelBinderBase<StatusPanelViewModel>
  {
    public string Summary { get; private set; } = string.Empty;

    protected override void OnBind(StatusPanelViewModel model)
    {
      model ??= new StatusPanelViewModel();
      Summary =
        $"Mode {model.Mode.Mode} {model.Mode.ProjectionPlayerId}\n" +
        $"Commands {(model.Command.Enabled ? "enabled" : "disabled")} {string.Join("; ", model.Command.Reasons ?? Array.Empty<string>())}\n" +
        $"Connection {(model.Connection.IsReconnecting ? "reconnecting" : model.Connection.Message)}\n" +
        $"Replay {model.Replay.Message}\n" +
        $"Error {(model.Error.HasError ? model.Error.Code + ": " + model.Error.Message : "none")}";
      EnsureText(transform, "StatusPanelText", 13).text = Summary;
    }
  }

  public sealed class ModeStatusBinder : PanelBinderBase<ModeStatusViewModel>
  {
    protected override void OnBind(ModeStatusViewModel model)
    {
      EnsureText(transform, "ModeStatusText", 13).text =
        $"Mode {model?.Mode} Projection {model?.ProjectionPlayerId} {model?.Warning}";
    }
  }

  public sealed class CommandStatusBinder : PanelBinderBase<CommandStatusViewModel>
  {
    public bool LastEnabled { get; private set; }

    protected override void OnBind(CommandStatusViewModel model)
    {
      LastEnabled = model?.Enabled == true;
      EnsureText(transform, "CommandStatusText", 13).text =
        $"Commands {(LastEnabled ? "enabled" : "disabled")} hash {model?.ExpectedPreStateHash ?? string.Empty}\n" +
        string.Join("\n", model?.Reasons ?? Array.Empty<string>());
    }
  }

  public sealed class ConnectionStatusBinder : PanelBinderBase<ConnectionStatusViewModel>
  {
    protected override void OnBind(ConnectionStatusViewModel model)
    {
      EnsureText(transform, "ConnectionStatusText", 13).text =
        $"{model?.BackendUrl ?? string.Empty} {(model?.BackendHealthy == true ? "healthy" : "unavailable")} {model?.Message ?? string.Empty}";
    }
  }

  public sealed class ReplayStatusBinder : PanelBinderBase<ReplayVerificationViewModel>
  {
    protected override void OnBind(ReplayVerificationViewModel model)
    {
      EnsureText(transform, "ReplayStatusText", 13).text =
        $"{model?.Message ?? "Replay not verified"} {model?.FinalStateHash ?? string.Empty}";
    }
  }

  public sealed class ErrorStatusBinder : PanelBinderBase<ErrorStatusViewModel>
  {
    protected override void OnBind(ErrorStatusViewModel model)
    {
      EnsureText(transform, "ErrorStatusText", 13).text =
        model?.HasError == true ? $"{model.Code}: {model.Message} {(model.Blocking ? "blocking" : "recoverable")}" : "No errors.";
    }
  }

  public sealed class ReconnectDiagnosticsViewModel
  {
    public string BeforeStateHash { get; set; } = string.Empty;
    public string AfterStateHash { get; set; } = string.Empty;
    public long EventCursor { get; set; }
    public int MissedEventCount { get; set; }
    public bool Succeeded { get; set; }

    public static ReconnectDiagnosticsViewModel From(string beforeStateHash, ReconnectViewModel reconnect) =>
      new()
      {
        BeforeStateHash = beforeStateHash ?? string.Empty,
        AfterStateHash = reconnect?.StateHash ?? string.Empty,
        EventCursor = reconnect?.EventCursor ?? 0,
        MissedEventCount = reconnect?.MissedEventCount ?? 0,
        Succeeded = reconnect?.Succeeded == true
      };
  }
}
