using System.Globalization;

namespace Game.Generator.Validation;

public sealed class GeneratedCardBalanceEvaluator
{
  public GeneratedCardBalanceEvaluation Evaluate(GeneratedCardBalanceEvaluationRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    var policy = request.Context.BalancePolicy;
    var signals = new List<GeneratedCardBalanceSignal>();
    var riskScores = new List<double>();

    if (policy.RequireBalanceReport && request.BalanceReport is null)
    {
      signals.Add(new GeneratedCardBalanceSignal(
        GeneratedCardBalanceSignalKind.ReportRequired,
        "validation.balanceReport",
        string.Empty,
        "required",
        BlocksPublication: true,
        $"Balance policy '{policy.PolicyId}' requires a balance report."));
      riskScores.Add(100);
    }

    AddThresholdSignal(
      signals,
      riskScores,
      GeneratedCardBalanceSignalKind.Complexity,
      "card.balanceMetadata.complexityScore",
      request.Candidate.BalanceMetadata.ComplexityScore,
      policy.MaxComplexityScore,
      "Candidate complexity score");

    AddThresholdSignal(
      signals,
      riskScores,
      GeneratedCardBalanceSignalKind.Randomness,
      "card.balanceMetadata.randomnessScore",
      request.Candidate.BalanceMetadata.RandomnessScore,
      policy.MaxRandomnessSwingScore,
      "Candidate randomness score");

    AddRiskFlagSignals(
      signals,
      request.Candidate.BalanceMetadata.RiskFlags,
      policy.AutoRejectRiskFlags,
      "card.balanceMetadata.riskFlags");

    if (request.BalanceReport is not null)
    {
      AddRiskFlagSignals(
        signals,
        request.BalanceReport.RiskFlags,
        policy.AutoRejectRiskFlags,
        "validation.balanceReport.riskFlags");
      AddThresholdSignal(
        signals,
        riskScores,
        GeneratedCardBalanceSignalKind.SimulationWinrateDelta,
        "validation.balanceReport.simulationWinrateDelta",
        Math.Abs(request.BalanceReport.SimulationWinrateDelta),
        policy.MaxSimulationWinrateDelta,
        "Simulation winrate delta");
      AddThresholdSignal(
        signals,
        riskScores,
        GeneratedCardBalanceSignalKind.Complexity,
        "validation.balanceReport.complexityScore",
        request.BalanceReport.ComplexityScore,
        policy.MaxComplexityScore,
        "Balance report complexity score");
      AddThresholdSignal(
        signals,
        riskScores,
        GeneratedCardBalanceSignalKind.Randomness,
        "validation.balanceReport.randomnessSwingScore",
        request.BalanceReport.RandomnessSwingScore,
        policy.MaxRandomnessSwingScore,
        "Balance report randomness swing score");
      AddThresholdSignal(
        signals,
        riskScores,
        GeneratedCardBalanceSignalKind.EventCountDelta,
        "validation.balanceReport.eventCountDelta",
        Math.Abs(request.BalanceReport.EventCountDelta),
        policy.MaxEventCountDelta,
        "Simulation event count delta");
    }

    if (signals.Any(signal => signal.Kind is GeneratedCardBalanceSignalKind.RiskFlag && signal.BlocksPublication))
    {
      riskScores.Add(100);
    }

    var riskScore = Math.Round(riskScores.Count == 0 ? 0 : riskScores.Max(), 2, MidpointRounding.AwayFromZero);
    var tier = ToRiskTier(riskScore);
    var blocks = signals.Any(signal => signal.BlocksPublication);
    var requiresReview = !blocks && RequiresHumanReview(tier, policy.RequireHumanReviewAboveRisk);
    if (requiresReview)
    {
      signals.Add(new GeneratedCardBalanceSignal(
        GeneratedCardBalanceSignalKind.HumanReviewThreshold,
        "validation.balanceReview",
        tier.ToString(),
        policy.RequireHumanReviewAboveRisk,
        BlocksPublication: false,
        $"Risk tier '{tier}' requires human balance review by policy '{policy.PolicyId}'."));
    }

    var decision = blocks
      ? GeneratedCardBalanceDecision.Rejected
      : requiresReview
        ? GeneratedCardBalanceDecision.ReviewRequired
        : GeneratedCardBalanceDecision.Accepted;

    return new GeneratedCardBalanceEvaluation(
      request.EvaluationId,
      policy.PolicyId,
      request.Candidate.CardId ?? string.Empty,
      request.Candidate.CardVersion ?? string.Empty,
      decision,
      tier,
      riskScore,
      signals
        .OrderBy(signal => signal.Path, StringComparer.Ordinal)
        .ThenBy(signal => signal.Kind.ToString(), StringComparer.Ordinal)
        .ThenBy(signal => signal.ActualValue, StringComparer.Ordinal)
        .ToArray(),
      BuildRepairHints(signals));
  }

  private static void AddRiskFlagSignals(
    ICollection<GeneratedCardBalanceSignal> signals,
    IEnumerable<string> riskFlags,
    IEnumerable<string> autoRejectFlags,
    string path)
  {
    var rejected = autoRejectFlags.ToHashSet(StringComparer.Ordinal);
    foreach (var flag in riskFlags.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
    {
      var blocks = rejected.Contains(flag);
      signals.Add(new GeneratedCardBalanceSignal(
        GeneratedCardBalanceSignalKind.RiskFlag,
        path,
        flag,
        blocks ? "auto-reject" : "tracked",
        blocks,
        blocks
          ? $"Risk flag '{flag}' is auto-rejected by balance policy."
          : $"Risk flag '{flag}' is tracked by balance policy."));
    }
  }

  private static void AddThresholdSignal(
    ICollection<GeneratedCardBalanceSignal> signals,
    ICollection<double> riskScores,
    GeneratedCardBalanceSignalKind kind,
    string path,
    double actual,
    double limit,
    string label)
  {
    if (limit <= 0)
    {
      return;
    }

    var ratio = Math.Abs(actual) / limit;
    riskScores.Add(Math.Min(100, ratio * 100));
    if (ratio <= 1)
    {
      return;
    }

    signals.Add(new GeneratedCardBalanceSignal(
      kind,
      path,
      Format(actual),
      Format(limit),
      BlocksPublication: true,
      $"{label} '{Format(actual)}' exceeds limit '{Format(limit)}'."));
  }

  private static IReadOnlyList<string> BuildRepairHints(IEnumerable<GeneratedCardBalanceSignal> signals) =>
    signals
      .Where(signal => signal.BlocksPublication || signal.Kind is GeneratedCardBalanceSignalKind.HumanReviewThreshold)
      .OrderBy(signal => signal.Path, StringComparer.Ordinal)
      .ThenBy(signal => signal.Kind.ToString(), StringComparer.Ordinal)
      .Select(signal => signal.Kind switch
      {
        GeneratedCardBalanceSignalKind.Complexity => $"Reduce complexity at {signal.Path} to <= {signal.Limit}.",
        GeneratedCardBalanceSignalKind.Randomness => $"Reduce randomness at {signal.Path} to <= {signal.Limit}.",
        GeneratedCardBalanceSignalKind.SimulationWinrateDelta => $"Rebalance simulated winrate delta at {signal.Path} to <= {signal.Limit}.",
        GeneratedCardBalanceSignalKind.EventCountDelta => $"Reduce event count delta at {signal.Path} to <= {signal.Limit}.",
        GeneratedCardBalanceSignalKind.RiskFlag => $"Remove or redesign auto-rejected risk flag '{signal.ActualValue}' at {signal.Path}.",
        GeneratedCardBalanceSignalKind.ReportRequired => "Attach a deterministic balance report before publication.",
        GeneratedCardBalanceSignalKind.HumanReviewThreshold => $"Route risk tier '{signal.ActualValue}' to human balance review.",
        _ => signal.Message
      })
      .Distinct(StringComparer.Ordinal)
      .ToArray();

  private static GeneratedCardBalanceRiskTier ToRiskTier(double score) =>
    score switch
    {
      >= 100 => GeneratedCardBalanceRiskTier.Critical,
      >= 75 => GeneratedCardBalanceRiskTier.High,
      >= 50 => GeneratedCardBalanceRiskTier.Medium,
      _ => GeneratedCardBalanceRiskTier.Low
    };

  private static bool RequiresHumanReview(
    GeneratedCardBalanceRiskTier tier,
    string threshold)
  {
    if (!Enum.TryParse<GeneratedCardBalanceRiskTier>(threshold, ignoreCase: true, out var parsed))
    {
      return false;
    }

    return tier >= parsed;
  }

  private static string Format(double value) =>
    value.ToString("0.###", CultureInfo.InvariantCulture);
}
