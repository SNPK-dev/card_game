using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CardGame.Unity.Client.Api;
using CardGame.Unity.Client.Config;
using CardGame.Unity.Client.State;
using Game.UnityClient.Contracts;

namespace CardGame.Unity.Client.Events
{
  public sealed class EventPoller
  {
    private readonly BackendApiClient api;
    private readonly UnityBackendConfig config;
    private readonly MatchStateStore stateStore;

    public bool IsPolling { get; private set; }
    public event Action<PublicMatchEventRecord> EventAccepted;
    public event Action<UnityError> PollError;

    public EventPoller(BackendApiClient api, UnityBackendConfig config, MatchStateStore stateStore)
    {
      this.api = api;
      this.config = config;
      this.stateStore = stateStore;
    }

    public async Task<UnityApiResult<MatchEventsPollResult>> PollOnceAsync(string matchId, string sessionToken, CancellationToken cancellationToken)
    {
      var result = await api.GetJsonAsync<MatchEventsPollResult>(
        $"/matches/{Uri.EscapeDataString(matchId)}/events?afterEventSeq={stateStore.LastSeenEventSeq}",
        sessionToken,
        cancellationToken);

      if (!result.Succeeded)
      {
        PollError?.Invoke(result.Error);
        return result;
      }

      foreach (var evt in result.Value.Events)
      {
        if (stateStore.TryAcceptEvent(evt))
        {
          EventAccepted?.Invoke(evt);
        }
      }

      return result;
    }

    public async Task RunAsync(string matchId, string sessionToken, Func<bool> shouldStop, CancellationToken cancellationToken)
    {
      IsPolling = true;
      try
      {
        while (!cancellationToken.IsCancellationRequested && !shouldStop())
        {
          await PollOnceAsync(matchId, sessionToken, cancellationToken);
          await Task.Delay(TimeSpan.FromSeconds(config.PollingIntervalSeconds), cancellationToken);
        }
      }
      catch (OperationCanceledException)
      {
      }
      finally
      {
        IsPolling = false;
      }
    }
  }

  public sealed class ReconnectService
  {
    private readonly BackendApiClient api;
    private readonly MatchStateStore stateStore;

    public ReconnectService(BackendApiClient api, MatchStateStore stateStore)
    {
      this.api = api;
      this.stateStore = stateStore;
    }

    public async Task<UnityApiResult<MatchReconnectResult>> ReconnectAsync(string matchId, string sessionToken, CancellationToken cancellationToken)
    {
      var result = await api.PostJsonAsync<MatchReconnectResult>(
        $"/matches/{Uri.EscapeDataString(matchId)}/reconnect?afterEventSeq={stateStore.LastSeenEventSeq}",
        new { },
        sessionToken,
        cancellationToken);

      if (result.Succeeded && result.Value.State != null)
      {
        stateStore.ReplaceAfterReconnect(result.Value.State, result.Value.NextEventSeq);
      }

      return result;
    }
  }
}
