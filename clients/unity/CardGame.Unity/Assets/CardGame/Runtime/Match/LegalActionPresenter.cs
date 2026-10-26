using System;
using System.Collections.Generic;
using System.Linq;
using Game.UnityClient.Contracts;

namespace CardGame.Unity.Client.Match
{
  public sealed class LegalActionPresenter
  {
    public const string TargetCandidatesUnavailableMessage = "Target selection unavailable: backend did not provide target candidates.";

    private static readonly HashSet<string> TargetedCommandTypes = new(StringComparer.OrdinalIgnoreCase)
    {
      "ATTACK",
      "ACTIVATE_LOCATION",
      "USE_HERO_POWER"
    };

    public IReadOnlyList<LegalActionGroup> Group(ExternalLegalActionsResult result)
    {
      return (result?.LegalActions ?? new List<MatchCommandDto>())
        .GroupBy(action => action.CommandType ?? string.Empty, StringComparer.OrdinalIgnoreCase)
        .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
        .Select(group => new LegalActionGroup(group.Key, group.Select(Create).ToList()))
        .ToList();
    }

    public LegalActionViewModel Create(MatchCommandDto action)
    {
      if (action == null)
      {
        return new LegalActionViewModel(
          null,
          "UNKNOWN",
          false,
          "Missing action template.",
          Array.Empty<string>(),
          false,
          string.Empty,
          Array.Empty<string>());
      }

      var refs = action.Refs ?? new Dictionary<string, string>();
      var commandType = action.CommandType ?? string.Empty;
      var highlights = refs
        .Where(pair => pair.Key.IndexOf("card", StringComparison.OrdinalIgnoreCase) >= 0 ||
          pair.Key.IndexOf("entity", StringComparison.OrdinalIgnoreCase) >= 0 ||
          pair.Key.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0 ||
          pair.Key.IndexOf("choice", StringComparison.OrdinalIgnoreCase) >= 0)
        .Select(pair => pair.Value)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

      if ((RequiresTarget(action) || ExtractCandidateTargetRefs(refs).Any()) && HasTargetRef(refs) == false)
      {
        var candidates = ExtractCandidateTargetRefs(refs).ToList();
        if (candidates.Count > 0)
        {
          return new LegalActionViewModel(
            action,
            commandType,
            true,
            string.Empty,
            highlights,
            true,
            ResolveTargetRefName(refs),
            candidates);
        }

        return new LegalActionViewModel(
          action,
          commandType,
          false,
          TargetCandidatesUnavailableMessage,
          highlights,
          false,
          string.Empty,
          Array.Empty<string>());
      }

      return new LegalActionViewModel(
        action,
        commandType,
        true,
        string.Empty,
        highlights,
        false,
        string.Empty,
        Array.Empty<string>());
    }

    public bool IsHighlighted(IReadOnlyCollection<LegalActionViewModel> actions, string refId)
    {
      return !string.IsNullOrWhiteSpace(refId) &&
        actions != null &&
        actions.Any(action => action.HighlightRefs.Contains(refId, StringComparer.OrdinalIgnoreCase));
    }

    private static bool RequiresTarget(MatchCommandDto action)
    {
      return action != null && TargetedCommandTypes.Contains(action.CommandType ?? string.Empty);
    }

    private static bool HasTargetRef(IDictionary<string, string> refs)
    {
      return refs.Keys.Any(key =>
        key.Equals("targetPlayerId", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("targetEntityInstanceId", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("selectedOptionId", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> ExtractCandidateTargetRefs(IDictionary<string, string> refs)
    {
      foreach (var pair in refs)
      {
        var key = pair.Key ?? string.Empty;
        if (key.IndexOf("candidate", StringComparison.OrdinalIgnoreCase) < 0 &&
          !key.EndsWith("TargetEntityInstanceIds", StringComparison.OrdinalIgnoreCase) &&
          !key.EndsWith("TargetPlayerIds", StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }

        foreach (var value in SplitRefs(pair.Value))
        {
          yield return value;
        }
      }
    }

    private static IEnumerable<string> SplitRefs(string raw)
    {
      if (string.IsNullOrWhiteSpace(raw))
      {
        yield break;
      }

      foreach (var value in raw.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
      {
        var trimmed = value.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
          yield return trimmed;
        }
      }
    }

    private static string ResolveTargetRefName(IDictionary<string, string> refs)
    {
      var candidateKey = refs.Keys.FirstOrDefault(key =>
        key.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0 &&
        (key.IndexOf("candidate", StringComparison.OrdinalIgnoreCase) >= 0 ||
          key.EndsWith("TargetPlayerIds", StringComparison.OrdinalIgnoreCase)));
      return candidateKey == null ? "targetEntityInstanceId" : "targetPlayerId";
    }
  }

  public sealed class LegalActionGroup
  {
    public string CommandType { get; }
    public IReadOnlyList<LegalActionViewModel> Actions { get; }

    public LegalActionGroup(string commandType, IReadOnlyList<LegalActionViewModel> actions)
    {
      CommandType = commandType;
      Actions = actions;
    }
  }

  public sealed class LegalActionViewModel
  {
    public MatchCommandDto Template { get; }
    public string CommandType { get; }
    public bool Enabled { get; }
    public string DisabledReason { get; }
    public IReadOnlyList<string> HighlightRefs { get; }
    public bool RequiresTargetSelection { get; }
    public string TargetRefName { get; }
    public IReadOnlyList<string> CandidateTargetRefs { get; }

    public LegalActionViewModel(
      MatchCommandDto template,
      string commandType,
      bool enabled,
      string disabledReason,
      IReadOnlyList<string> highlightRefs,
      bool requiresTargetSelection,
      string targetRefName,
      IReadOnlyList<string> candidateTargetRefs)
    {
      Template = template;
      CommandType = commandType;
      Enabled = enabled;
      DisabledReason = disabledReason;
      HighlightRefs = highlightRefs ?? Array.Empty<string>();
      RequiresTargetSelection = requiresTargetSelection;
      TargetRefName = targetRefName ?? string.Empty;
      CandidateTargetRefs = candidateTargetRefs ?? Array.Empty<string>();
    }
  }
}
