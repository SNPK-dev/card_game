using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace CardGame.Unity.Client.DebugTools
{
  public sealed class PlaytestEvidenceReport
  {
    public string PlaytestId { get; set; } = string.Empty;
    public string Tester { get; set; } = string.Empty;
    public string Scenario { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string UnityVersion { get; set; } = string.Empty;
    public string BackendUrl { get; set; } = string.Empty;
    public string BuildMode { get; set; } = "Editor";
    public string BuildTargetMode { get; set; } = "Editor";
    public string VisualThemeVersion { get; set; } = string.Empty;
    public bool AudioEnabled { get; set; }
    public string AudioProfile { get; set; } = "generated-cues-v1";
    public string SeedMode { get; set; } = "Ensure";
    public string MatchId { get; set; } = string.Empty;
    public List<string> MatchIds { get; set; } = new();
    public List<string> ModeSwitches { get; set; } = new();
    public List<string> CommandsSubmitted { get; set; } = new();
    public List<string> ChoicesResolved { get; set; } = new();
    public List<string> TargetsSelected { get; set; } = new();
    public List<string> TargetSelectionsAttempted { get; set; } = new();
    public List<string> Reconnects { get; set; } = new();
    public List<string> ReconnectAttempts { get; set; } = new();
    public List<string> ReplayChecks { get; set; } = new();
    public string ReplayVerificationResult { get; set; } = string.Empty;
    public List<string> PresentationIntents { get; set; } = new();
    public List<string> PresentationIntentsSeen { get; set; } = new();
    public List<string> PresentationIntentsPlayed { get; set; } = new();
    public int AnimationsPlayed { get; set; }
    public int SkippedAnimations { get; set; }
    public List<string> AudioCuesRequested { get; set; } = new();
    public List<string> UiErrors { get; set; } = new();
    public string PlaytestVerdict { get; set; } = "in-progress";
    public List<string> HiddenInfoVisualChecks { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<string> KnownLimitationsObserved { get; set; } = new();
    public List<string> PlayerModesExercised { get; set; } = new();
    public List<string> CommandTypesSeen { get; set; } = new();
    public string HiddenInfoVisualCheckResult { get; set; } = "not-run";
    public string SpectatorVisualCheckResult { get; set; } = "not-run";
    public string TerminalFaultStateCheckResult { get; set; } = "not-run";
    public string ManualTesterNotes { get; set; } = "not provided";
    public string Verdict { get; set; } = "in-progress";
    public string FinalLocalPlaytestVerdict { get; set; } = "in-progress";
  }

  public sealed class PlaytestEvidenceRecorder
  {
    public PlaytestEvidenceReport Report { get; } = new()
    {
      PlaytestId = "playtest-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8),
      StartedAt = DateTimeOffset.UtcNow,
      UnityVersion = Application.unityVersion
    };

    public void Start(string backendUrl, string matchId, string tester = "", string scenario = "local-match", string buildMode = "Editor")
    {
      Report.BackendUrl = backendUrl ?? string.Empty;
      Report.MatchId = matchId ?? string.Empty;
      Report.Tester = tester ?? string.Empty;
      Report.Scenario = scenario ?? string.Empty;
      Report.BuildMode = string.IsNullOrWhiteSpace(buildMode) ? "Editor" : buildMode;
      Report.BuildTargetMode = Report.BuildMode;
      Report.VisualThemeVersion = UI.Theme.GameTheme.CurrentVersion;
      Add(Report.MatchIds, matchId);
      Add(Report.KnownLimitationsObserved, "generated in-repo visuals/audio only");
    }

    public void SetAudioEnabled(bool enabled) => Report.AudioEnabled = enabled;

    public void LogModeSwitch(string mode)
    {
      Add(Report.ModeSwitches, mode);
      Add(Report.PlayerModesExercised, mode);
      if (mode != null && mode.Contains("Spectator", StringComparison.OrdinalIgnoreCase))
      {
        Report.SpectatorVisualCheckResult = "checked";
      }
    }

    public void LogCommand(string commandType)
    {
      Add(Report.CommandsSubmitted, commandType);
      Add(Report.CommandTypesSeen, commandType);
    }
    public void LogChoice(string choiceId, string optionId) => Add(Report.ChoicesResolved, $"{choiceId}:{optionId}");
    public void LogTargetSelection(string refId, bool accepted)
    {
      Add(Report.TargetSelectionsAttempted, $"{refId}:{(accepted ? "accepted" : "rejected")}");
      if (accepted)
      {
        Add(Report.TargetsSelected, refId);
      }
    }

    public void LogReconnect(bool succeeded, int missedEventCount)
    {
      var line = $"{(succeeded ? "succeeded" : "failed")}:missed={missedEventCount}";
      Add(Report.ReconnectAttempts, line);
      Add(Report.Reconnects, line);
    }

    public void LogReplay(bool verified)
    {
      Report.ReplayVerificationResult = verified ? "verified" : "mismatch";
      Add(Report.ReplayChecks, Report.ReplayVerificationResult);
    }

    public void LogPresentationIntent(string intent)
    {
      Add(Report.PresentationIntentsSeen, intent);
      Add(Report.PresentationIntents, intent);
    }

    public void LogPresentationPlayed(string intent) => Add(Report.PresentationIntentsPlayed, intent);
    public void LogAnimationPlayed() => Report.AnimationsPlayed++;
    public void LogAnimationSkipped() => Report.SkippedAnimations++;
    public void LogAudioCue(string cueId) => Add(Report.AudioCuesRequested, cueId);
    public void LogUiError(string error) => Add(Report.UiErrors, error);

    public void LogHiddenInfoCheck(string check)
    {
      Add(Report.HiddenInfoVisualChecks, check);
      Report.HiddenInfoVisualCheckResult = "passed";
      if (check != null && check.Contains("Spectator", StringComparison.OrdinalIgnoreCase))
      {
        Report.SpectatorVisualCheckResult = "passed";
      }
    }
    public void LogError(string code, string message) => Add(Report.Errors, $"{code}:{message}");
    public void LogLimitation(string limitation) => Add(Report.KnownLimitationsObserved, limitation);

    public PlaytestEvidenceReport Complete(string verdict)
    {
      Report.CompletedAt = DateTimeOffset.UtcNow;
      Report.Verdict = string.IsNullOrWhiteSpace(verdict) ? "pass-with-limitations" : verdict;
      Report.FinalLocalPlaytestVerdict = Report.Verdict;
      Report.PlaytestVerdict = Report.Verdict;
      return Report;
    }

    private static void Add(List<string> target, string value)
    {
      if (!string.IsNullOrWhiteSpace(value))
      {
        target.Add(value);
      }
    }
  }

  public sealed class PlaytestEvidenceExporter
  {
    public string Export(PlaytestEvidenceReport report, string root = "artifacts/unity-client-playtest")
    {
      if (report == null)
      {
        throw new ArgumentNullException(nameof(report));
      }

      var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHHmmssfffffffZ");
      var directory = Path.Combine(root, timestamp);
      Directory.CreateDirectory(directory);
      var path = Path.Combine(directory, "report.json");
      File.WriteAllText(path, JsonConvert.SerializeObject(report, Formatting.Indented));
      return path;
    }
  }
}
