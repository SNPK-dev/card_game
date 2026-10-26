using System;
using System.Collections.Generic;
using System.Linq;
using Game.UnityClient.Contracts;

namespace CardGame.Unity.Client.Match
{
  public sealed class TargetSelectionController
  {
    public TargetSelectionState Current { get; private set; } = TargetSelectionState.Inactive;

    public bool Begin(LegalActionViewModel action)
    {
      if (action == null || !action.RequiresTargetSelection || action.CandidateTargetRefs.Count == 0)
      {
        Current = TargetSelectionState.Inactive;
        return false;
      }

      Current = new TargetSelectionState(
        true,
        action.Template,
        action.TargetRefName,
        action.CandidateTargetRefs);
      return true;
    }

    public MatchCommandDto Select(string targetRef)
    {
      if (!Current.IsActive || string.IsNullOrWhiteSpace(targetRef))
      {
        return null;
      }

      if (!Current.CandidateTargetRefs.Contains(targetRef, StringComparer.OrdinalIgnoreCase))
      {
        return null;
      }

      var refs = Current.Template.Refs == null
        ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        : new Dictionary<string, string>(Current.Template.Refs, StringComparer.OrdinalIgnoreCase);
      foreach (var key in refs.Keys.Where(IsCandidateKey).ToList())
      {
        refs.Remove(key);
      }

      refs[Current.TargetRefName] = targetRef;

      var selected = new MatchCommandDto
      {
        CommandType = Current.Template.CommandType,
        PlayerId = Current.Template.PlayerId,
        Payload = Current.Template.Payload,
        Refs = refs
      };
      Current = TargetSelectionState.Inactive;
      return selected;
    }

    private static bool IsCandidateKey(string key) =>
      !string.IsNullOrWhiteSpace(key) &&
      (key.IndexOf("candidate", StringComparison.OrdinalIgnoreCase) >= 0 ||
        key.EndsWith("TargetEntityInstanceIds", StringComparison.OrdinalIgnoreCase) ||
        key.EndsWith("TargetPlayerIds", StringComparison.OrdinalIgnoreCase));

    public void Cancel()
    {
      Current = TargetSelectionState.Inactive;
    }
  }

  public sealed class TargetSelectionState
  {
    public static TargetSelectionState Inactive { get; } =
      new(false, null, string.Empty, Array.Empty<string>());

    public bool IsActive { get; }
    public MatchCommandDto Template { get; }
    public string TargetRefName { get; }
    public IReadOnlyList<string> CandidateTargetRefs { get; }

    public TargetSelectionState(
      bool isActive,
      MatchCommandDto template,
      string targetRefName,
      IReadOnlyList<string> candidateTargetRefs)
    {
      IsActive = isActive;
      Template = template;
      TargetRefName = targetRefName;
      CandidateTargetRefs = candidateTargetRefs ?? Array.Empty<string>();
    }
  }

  public sealed class TargetHighlightView
  {
    private readonly TargetSelectionState state;

    public TargetHighlightView(TargetSelectionState state)
    {
      this.state = state ?? TargetSelectionState.Inactive;
    }

    public bool IsHighlighted(string refId) =>
      state.IsActive &&
      !string.IsNullOrWhiteSpace(refId) &&
      state.CandidateTargetRefs.Contains(refId, StringComparer.OrdinalIgnoreCase);

    public bool IsSource(string refId) => state.IsActive && state.Template?.Refs != null &&
      state.Template.Refs.Any(pair =>
        pair.Key.IndexOf("candidate", StringComparison.OrdinalIgnoreCase) < 0 &&
        !pair.Key.StartsWith("target", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(pair.Value, refId, StringComparison.OrdinalIgnoreCase));
  }
}
