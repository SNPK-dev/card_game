using Game.Generator.Generation;
using Game.Shared.Mechanics;
using Game.Shared.Ruleset;
using Xunit;

namespace Game.Generator.Tests;

public sealed class GenerationContextBuilderTests
{
  [Fact]
  public void BuildIncludesOnlyProfileEnabledApprovedMechanics()
  {
    var profile = TestFixtures.CreateProfile(
      enabledMechanics: ["BATTLECRY", "SMART_OFFER", "DEPRECATED_PATTERN", "REJECTED_PATTERN"],
      disabledMechanics: ["SMART_OFFER"]);

    var context = new GenerationContextBuilder().Build(
      profile,
      [
        TestFixtures.Mechanic("BATTLECRY", MechanicStatus.ApprovedForRuleset),
        TestFixtures.Mechanic("SMART_OFFER", MechanicStatus.ApprovedForRuleset),
        TestFixtures.Mechanic("DEPRECATED_PATTERN", MechanicStatus.Deprecated),
        TestFixtures.Mechanic("REJECTED_PATTERN", MechanicStatus.Rejected)
      ]);

    Assert.Contains("BATTLECRY", context.AllowedMechanics);
    Assert.DoesNotContain("SMART_OFFER", context.AllowedMechanics);
    Assert.DoesNotContain("DEPRECATED_PATTERN", context.AllowedMechanics);
    Assert.DoesNotContain("REJECTED_PATTERN", context.AllowedMechanics);
  }

  [Fact]
  public void BuildExcludesFormatDisabledAndLegacyOnlyMechanics()
  {
    var profile = TestFixtures.CreateProfile(
      enabledMechanics: ["BATTLECRY", "OLD_SECRET", "SMART_OFFER"],
      formatMechanicStates:
      [
        new FormatMechanicState("OLD_SECRET", "ruleset_mvp_core_v1", FormatMechanicStateKind.LegacyOnly, null, null, null),
        new FormatMechanicState("SMART_OFFER", "ruleset_mvp_core_v1", FormatMechanicStateKind.Disabled, null, null, null)
      ]);

    var context = new GenerationContextBuilder().Build(
      profile,
      [
        TestFixtures.Mechanic("BATTLECRY", MechanicStatus.ApprovedForRuleset),
        TestFixtures.Mechanic("OLD_SECRET", MechanicStatus.ApprovedForRuleset),
        TestFixtures.Mechanic("SMART_OFFER", MechanicStatus.ApprovedForRuleset)
      ]);

    Assert.Contains("BATTLECRY", context.AllowedMechanics);
    Assert.DoesNotContain("OLD_SECRET", context.AllowedMechanics);
    Assert.DoesNotContain("SMART_OFFER", context.AllowedMechanics);
  }
}
