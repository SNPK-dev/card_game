#if UNITY_INCLUDE_TESTS
using System;
using System.IO;
using System.Threading.Tasks;
using CardGame.Unity.Client.Config;
using CardGame.Unity.Client.DebugTools;
using CardGame.Unity.Client.Match;
using NUnit.Framework;
using UnityEngine;

namespace CardGame.Unity.Client.Tests.PlayMode
{
  public sealed class UnityClientLiveBackendSmokeTests
  {
    [Test]
    public async Task LiveBackend_FullLocalMatchFlow_RunsWhenEnabled()
    {
      if (!string.Equals(Environment.GetEnvironmentVariable("CARDGAME_UNITY_LIVE_BACKEND_TESTS"), "1", StringComparison.Ordinal))
      {
        Assert.Ignore("Set CARDGAME_UNITY_LIVE_BACKEND_TESTS=1 to run the guarded live-backend Unity smoke.");
      }

      var driver = new LocalMatchFlowDriver(UnityBackendConfig.CreateRuntimeDefault());
      var result = await driver.RunAsync(
        new LocalMatchFlowOptions
        {
          UnityVersion = Application.unityVersion,
          SeedMode = ReadSeedMode(),
          SmokeRunMode = "Unity PlayMode batchmode",
          UnityBatchmodeLogPath = Environment.GetEnvironmentVariable("UNITY_BATCHMODE_LOG_PATH") ?? string.Empty,
          TestResultsPath = Environment.GetEnvironmentVariable("UNITY_TEST_RESULTS_PATH") ?? string.Empty
        },
        default);

      TestContext.Out.WriteLine("Unity live smoke report: " + result.ReportPath);
      if (!result.Succeeded)
      {
        Assert.Fail(result.Error + "\nReport: " + result.ReportPath);
      }

      Assert.That(result.Report.FinalVerdict, Is.EqualTo(LocalMatchFlowVerdicts.Ready));
      Assert.That(result.Report.ReplayVerified, Is.True);
      Assert.That(result.Report.HiddenInfoAssertions, Is.Not.Empty);
      Assert.That(result.Report.HiddenInfoAssertions.TrueForAll(assertion => assertion.Passed), Is.True);
      Assert.That(result.Report.CommandSubmitted, Is.Not.Empty);
      Assert.That(result.Report.CommandCountAfter, Is.GreaterThan(result.Report.CommandCountBefore));
      Assert.That(result.Report.StateHashes.ContainsKey("player1"), Is.True);
      Assert.That(result.Report.StateHashes.ContainsKey("player2"), Is.True);
      Assert.That(result.Report.StateHashes.ContainsKey("spectator"), Is.True);
      Assert.That(result.Report.LegalActionCountBeforeCommand, Is.GreaterThan(0));
      Assert.That(result.Report.RefreshedLegalActionCount, Is.GreaterThanOrEqualTo(0));
      Assert.That(result.Report.SmokeCommandSelectionReason, Is.Not.Empty);
      Assert.That(result.Report.EventCount, Is.GreaterThanOrEqualTo(0));
      Assert.That(result.Report.ReconnectStatus.Succeeded, Is.True);
      Assert.That(result.Report.SeedResults, Is.Not.Empty);
      Assert.That(result.Report.MatchId, Does.StartWith("unity-live-smoke-"));
    }

    [Test]
    public async Task LiveBackend_SeedEnsureRepeatability_RunsWhenEnabled()
    {
      if (!string.Equals(Environment.GetEnvironmentVariable("CARDGAME_UNITY_LIVE_BACKEND_TESTS"), "1", StringComparison.Ordinal))
      {
        Assert.Ignore("Set CARDGAME_UNITY_LIVE_BACKEND_TESTS=1 to run the guarded live-backend Unity smoke repeatability test.");
      }

      var driver = new LocalMatchFlowDriver(UnityBackendConfig.CreateRuntimeDefault());
      var repeatabilityRoot = Path.Combine(RepositoryRoot(), "artifacts", "unity-client-live-smoke-repeatability");
      var first = await driver.RunAsync(
        new LocalMatchFlowOptions
        {
          UnityVersion = Application.unityVersion,
          SeedMode = SeedMode.Ensure,
          SmokeRunMode = "Unity PlayMode repeatability first run",
          ReportRoot = repeatabilityRoot,
          UnityBatchmodeLogPath = Environment.GetEnvironmentVariable("UNITY_BATCHMODE_LOG_PATH") ?? string.Empty,
          TestResultsPath = Environment.GetEnvironmentVariable("UNITY_TEST_RESULTS_PATH") ?? string.Empty
        },
        default);
      var second = await driver.RunAsync(
        new LocalMatchFlowOptions
        {
          UnityVersion = Application.unityVersion,
          SeedMode = SeedMode.Ensure,
          SmokeRunMode = "Unity PlayMode repeatability second run",
          ReportRoot = repeatabilityRoot,
          UnityBatchmodeLogPath = Environment.GetEnvironmentVariable("UNITY_BATCHMODE_LOG_PATH") ?? string.Empty,
          TestResultsPath = Environment.GetEnvironmentVariable("UNITY_TEST_RESULTS_PATH") ?? string.Empty
        },
        default);

      TestContext.Out.WriteLine("Unity repeatability first report: " + first.ReportPath);
      TestContext.Out.WriteLine("Unity repeatability second report: " + second.ReportPath);
      Assert.That(first.Succeeded, Is.True, first.Error);
      Assert.That(second.Succeeded, Is.True, second.Error);
      Assert.That(first.Report.MatchId, Is.Not.EqualTo(second.Report.MatchId));
      Assert.That(second.Report.SeedResults.TrueForAll(result =>
        result.Status is SeedResourceStatus.AlreadyCompatible or SeedResourceStatus.Created or SeedResourceStatus.Skipped), Is.True);
      Assert.That(second.Report.ReplayVerified, Is.True);
      Assert.That(second.Report.ReconnectStatus.Succeeded, Is.True);
    }

    [TestCase("terminal-primary")]
    [TestCase("terminal-repeatability")]
    public async Task LiveBackend_FullMatch_ReachesTerminalAndVerifiesReplay(string scenario)
    {
      if (!string.Equals(Environment.GetEnvironmentVariable("CARDGAME_UNITY_LIVE_BACKEND_TESTS"), "1", StringComparison.Ordinal))
      {
        Assert.Ignore("Set CARDGAME_UNITY_LIVE_BACKEND_TESTS=1 to run complete terminal Unity journeys.");
      }

      var driver = new LocalMatchFlowDriver(UnityBackendConfig.CreateRuntimeDefault());
      var result = await driver.RunAsync(
        new LocalMatchFlowOptions
        {
          UnityVersion = Application.unityVersion,
          SeedMode = SeedMode.Ensure,
          SmokeRunMode = "Unity PlayMode complete match " + scenario,
          RunToTerminal = true,
          RequireTerminal = true,
          MaxCommands = 512,
          ReportRoot = Path.Combine(RepositoryRoot(), "artifacts", "unity-client-full-match", scenario)
        },
        default);

      TestContext.Out.WriteLine("Unity complete match report: " + result.ReportPath);
      Assert.That(result.Succeeded, Is.True, result.Error);
      Assert.That(result.Report.ReachedTerminal, Is.True);
      Assert.That(result.Report.TerminalReason, Is.Not.Empty);
      Assert.That(result.Report.ReplayVerified, Is.True);
      Assert.That(result.Report.CommandsSubmitted, Is.GreaterThan(1));
      Assert.That(result.Report.CommandTypes, Does.Contain("END_TURN"));
      Assert.That(result.Report.CommandTypes, Does.Contain("PLAY_CARD"));
      Assert.That(result.Report.CommandTypes, Does.Contain("ATTACK"));
      Assert.That(result.Report.ObservedEventTypes, Does.Contain("GAME_ENDED"));
      Assert.That(result.Report.ObservedEventTypes, Does.Contain("ENTITY_DAMAGED"));
      Assert.That(result.Report.ObservedEventTypes, Does.Contain("LOCATION_PLAYED"));
      Assert.That(result.Report.ObservedEventTypes, Does.Contain("WEAPON_EQUIPPED"));
      Assert.That(result.Report.ObservedEventTypes, Does.Contain("AURA_REFRESHED"));
      Assert.That(result.Report.ObservedEventTypes, Does.Contain("DEATHRATTLE_TRIGGERED"));
      Assert.That(result.Report.HiddenInfoAssertions.TrueForAll(assertion => assertion.Passed), Is.True);
      Assert.That(result.Report.ReconnectStatus.Succeeded, Is.True);
    }

    private static SeedMode ReadSeedMode()
    {
      var raw = Environment.GetEnvironmentVariable("CARDGAME_UNITY_SEED_MODE");
      return Enum.TryParse<SeedMode>(raw, ignoreCase: true, out var parsed) ? parsed : SeedMode.Ensure;
    }

    private static string RepositoryRoot() =>
      Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", ".."));
  }
}
#endif
