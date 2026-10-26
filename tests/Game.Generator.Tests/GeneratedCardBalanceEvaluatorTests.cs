using Game.Generator.Generation;
using Game.Generator.Validation;
using Game.Shared.Mechanics;
using Xunit;

namespace Game.Generator.Tests;

public sealed class GeneratedCardBalanceEvaluatorTests
{
  [Fact]
  public void EvaluateAcceptsLowRiskCandidateWithDeterministicScore()
  {
    var request = CreateRequest(
      TestFixtures.CreateBattlecryCandidate(complexityScore: 6, randomnessScore: 2),
      TestFixtures.CreateBalanceReport(
        simulationWinrateDelta: 0.01,
        complexityScore: 6,
        randomnessSwingScore: 2,
        eventCountDelta: 0.01));

    var result = new GeneratedCardBalanceEvaluator().Evaluate(request);

    Assert.Equal(GeneratedCardBalanceDecision.Accepted, result.Decision);
    Assert.Equal(GeneratedCardBalanceRiskTier.Low, result.RiskTier);
    Assert.Equal(20, result.RiskScore);
    Assert.Empty(result.RepairHints);
    Assert.Equal("balance_mvp", result.PolicyId);
    Assert.Equal("card_arcane_apprentice", result.CardId);
  }

  [Fact]
  public void EvaluateRejectsBlockingRiskSignalsAndProducesRepairHints()
  {
    var profile = TestFixtures.CreateProfile(
      allowedActions: ["DEAL_DAMAGE", "DRAW_CARD"],
      autoRejectRiskFlags: ["UNBOUNDED_LOOP"]);
    var candidate = TestFixtures.CreateBattlecryCandidate(
      complexityScore: 31,
      randomnessScore: 26,
      riskFlags: ["UNBOUNDED_LOOP"]);
    var request = CreateRequest(
      candidate,
      TestFixtures.CreateBalanceReport(
        simulationWinrateDelta: 0.2,
        complexityScore: 31,
        randomnessSwingScore: 26,
        eventCountDelta: 0.2,
        riskFlags: ["UNBOUNDED_LOOP"]),
      profile);

    var result = new GeneratedCardBalanceEvaluator().Evaluate(request);

    Assert.Equal(GeneratedCardBalanceDecision.Rejected, result.Decision);
    Assert.Equal(GeneratedCardBalanceRiskTier.Critical, result.RiskTier);
    Assert.Contains(result.Signals, signal =>
      signal.Kind == GeneratedCardBalanceSignalKind.RiskFlag
      && signal.Path == "card.balanceMetadata.riskFlags"
      && signal.BlocksPublication);
    Assert.Contains(result.Signals, signal =>
      signal.Kind == GeneratedCardBalanceSignalKind.SimulationWinrateDelta
      && signal.Path == "validation.balanceReport.simulationWinrateDelta"
      && signal.BlocksPublication);
    Assert.Contains("Remove or redesign auto-rejected risk flag 'UNBOUNDED_LOOP' at card.balanceMetadata.riskFlags.", result.RepairHints);
    Assert.Contains("Rebalance simulated winrate delta at validation.balanceReport.simulationWinrateDelta to <= 0.05.", result.RepairHints);
  }

  [Fact]
  public void EvaluateRoutesThresholdRiskToHumanReviewWithoutBlockingPublication()
  {
    var profile = TestFixtures.CreateProfile(
      allowedActions: ["DEAL_DAMAGE", "DRAW_CARD"]);
    var strictProfile = profile with
    {
      BalancePolicy = profile.BalancePolicy with { RequireHumanReviewAboveRisk = "MEDIUM" }
    };
    var request = CreateRequest(
      TestFixtures.CreateBattlecryCandidate(complexityScore: 18, randomnessScore: 2),
      TestFixtures.CreateBalanceReport(
        simulationWinrateDelta: 0.01,
        complexityScore: 18,
        randomnessSwingScore: 2,
        eventCountDelta: 0.01),
      strictProfile);

    var result = new GeneratedCardBalanceEvaluator().Evaluate(request);

    Assert.Equal(GeneratedCardBalanceDecision.ReviewRequired, result.Decision);
    Assert.Equal(GeneratedCardBalanceRiskTier.Medium, result.RiskTier);
    Assert.Contains(result.Signals, signal =>
      signal.Kind == GeneratedCardBalanceSignalKind.HumanReviewThreshold
      && !signal.BlocksPublication);
    Assert.Contains("Route risk tier 'Medium' to human balance review.", result.RepairHints);
  }

  private static GeneratedCardBalanceEvaluationRequest CreateRequest(
    Game.Shared.Dsl.CardDefinition candidate,
    GeneratedCardBalanceReport balanceReport,
    Game.Shared.Ruleset.ResolvedRulesetProfile? profile = null)
  {
    profile ??= TestFixtures.CreateProfile(allowedActions: ["DEAL_DAMAGE", "DRAW_CARD"]);
    var context = new GenerationContextBuilder().Build(
      profile,
      [TestFixtures.Mechanic("BATTLECRY", MechanicStatus.ApprovedForRuleset)]);

    return new GeneratedCardBalanceEvaluationRequest(
      "balance-eval-001",
      candidate,
      context,
      balanceReport);
  }
}

