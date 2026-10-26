using System.Reflection;
using System.Text.Json;
using Game.Shared.Mechanics;
using Game.Shared.Ruleset;
using Xunit;

namespace Game.Shared.Tests;

public sealed class GovernanceContractTests
{
  [Fact]
  public void MechanicStatusSerializesAsDocumentedStringValue()
  {
    var record = new MechanicStatusRecord(
      "BATTLECRY",
      MechanicStatus.ApprovedForRuleset,
      MechanicTier.Tier0,
      "Game Systems Architect",
      [
        new StatusTransition(
          null,
          MechanicStatus.ApprovedForRuleset,
          DateTimeOffset.Parse("2026-06-23T00:00:00Z"),
          "bootstrap",
          "Initial approved MVP mechanic.",
          ["docs/generator/mechanic_status_spec.md"])
      ],
      ["ruleset_mvp_core_v1"],
      null,
      null,
      ["docs/generator/dsl_reference.md"],
      ["tests/Game.Shared.Tests/GovernanceContractTests.cs"],
      ["Ragnaros-like"],
      []);

    var json = JsonSerializer.Serialize(record);

    Assert.Contains("\"CurrentStatus\":\"APPROVED_FOR_RULESET\"", json);
    Assert.Contains("\"Tier\":\"TIER_0\"", json);
  }

  [Fact]
  public void RulesetProfileContractCarriesFormatMechanicStateAndLimits()
  {
    var profile = CreateRulesetProfile();

    Assert.Equal("ruleset_mvp_core_v1", profile.Id);
    Assert.Equal(RulesetProfileStatus.Active, profile.Status);
    Assert.Contains("BATTLECRY", profile.EnabledMechanics);
    Assert.Contains(profile.FormatMechanicStates, state =>
      state.MechanicId == "BATTLECRY" && state.State == FormatMechanicStateKind.Enabled);
    Assert.Equal(30, profile.Limits.MaxComplexityScore);
    Assert.True(profile.LegacyPolicy.ReplayProfilePinningRequired);
  }

  [Fact]
  public void ResolvedRulesetProfileCarriesResolvedHashWithoutCalculatingIt()
  {
    var resolved = new ResolvedRulesetProfile(
      "resolved_mvp_core_v1",
      "ruleset_mvp_core_v1",
      "0.2.0",
      DateTimeOffset.Parse("2026-06-23T00:00:00Z"),
      "profile-resolver",
      "sha256:example",
      "mvp_internal",
      "core_generated_alpha",
      ["BATTLECRY"],
      ["QUESTLINE"],
      [new FormatMechanicState("BATTLECRY", "ruleset_mvp_core_v1", FormatMechanicStateKind.Enabled, null, null, null)],
      ["SIMPLE_PLAY_EFFECT"],
      ["DEAL_DAMAGE"],
      [],
      ["HAND"],
      ["ON_PLAY"],
      [],
      ["MINION_CARD"],
      ["MINION"],
      [],
      [],
      [],
      CreateLimits(),
      CreateValidationPolicy(),
      CreateBalancePolicy(),
      CreateSimulationPolicy(),
      CreatePublicationPolicy(),
      CreateLegacyPolicy());

    Assert.Equal("sha256:example", resolved.ResolvedHash);
    Assert.Equal("ruleset_mvp_core_v1", resolved.SourceProfileId);
    Assert.Contains("DEAL_DAMAGE", resolved.AllowedActions);
  }

  [Fact]
  public void GovernanceContractsDoNotExposeRuntimeBehaviorServices()
  {
    var contractTypes = typeof(MechanicStatus).Assembly
      .GetExportedTypes()
      .Where(type =>
        type.Namespace is "Game.Shared.Mechanics" or "Game.Shared.Ruleset")
      .ToArray();

    Assert.NotEmpty(contractTypes);
    Assert.All(contractTypes, type =>
    {
      Assert.False(type.Name.EndsWith("Service", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Resolver", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Validator", StringComparison.Ordinal));

      var declaredMethods = type
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(method => !method.IsSpecialName)
        .Select(method => method.Name)
        .ToArray();

      Assert.DoesNotContain("Resolve", declaredMethods);
      Assert.DoesNotContain("Validate", declaredMethods);
      Assert.DoesNotContain("BuildGenerationContext", declaredMethods);
    });
  }

  private static RulesetProfile CreateRulesetProfile()
  {
    return new RulesetProfile(
      "ruleset_mvp_core_v1",
      "MVP Core Ruleset",
      "Minimal internal profile for generated card validation.",
      "0.2.0",
      RulesetProfileStatus.Active,
      "mvp_internal",
      "core_generated_alpha",
      null,
      ["BATTLECRY"],
      ["QUESTLINE"],
      [new FormatMechanicState("BATTLECRY", "ruleset_mvp_core_v1", FormatMechanicStateKind.Enabled, null, null, null)],
      ["SIMPLE_PLAY_EFFECT"],
      ["DEAL_DAMAGE"],
      [],
      ["HAND"],
      ["ON_PLAY"],
      [],
      ["MINION_CARD"],
      ["MINION"],
      [],
      [],
      [],
      CreateLimits(),
      CreateValidationPolicy(),
      CreateBalancePolicy(),
      CreateSimulationPolicy(),
      CreatePublicationPolicy(),
      CreateLegacyPolicy(),
      new RulesetProfileMetadata(
        "bootstrap",
        DateTimeOffset.Parse("2026-06-23T00:00:00Z"),
        "bootstrap",
        DateTimeOffset.Parse("2026-06-23T00:00:00Z"),
        ["Contract test fixture."]));
  }

  private static RulesetLimits CreateLimits()
  {
    return new RulesetLimits(
      30,
      25,
      20,
      80,
      5,
      1,
      "LIMITED",
      3,
      2,
      3,
      3,
      3,
      2,
      2,
      3,
      1);
  }

  private static ValidationPolicyRef CreateValidationPolicy()
  {
    return new ValidationPolicyRef(
      "validation_mvp",
      true,
      true,
      true,
      true,
      true,
      true,
      true,
      true,
      true);
  }

  private static BalancePolicyRef CreateBalancePolicy()
  {
    return new BalancePolicyRef(
      "balance_mvp",
      true,
      "MEDIUM",
      [],
      0.05,
      30,
      25,
      5,
      100,
      0.95,
      "baseline_mvp",
      "seed_mvp",
      "flag_outliers");
  }

  private static SimulationPolicyRef CreateSimulationPolicy()
  {
    return new SimulationPolicyRef(
      "simulation_mvp",
      ["smoke"],
      ["empty_deck"],
      10,
      10,
      true,
      30,
      80);
  }

  private static PublicationPolicyRef CreatePublicationPolicy()
  {
    return new PublicationPolicyRef(
      "publication_mvp",
      true,
      true,
      true,
      true,
      true,
      false,
      false,
      ["validated"]);
  }

  private static LegacyPolicy CreateLegacyPolicy()
  {
    return new LegacyPolicy(
      true,
      false,
      true,
      false,
      true);
  }
}
