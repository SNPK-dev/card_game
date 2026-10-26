using System;
using System.Collections.Generic;
using System.Linq;
using CardGame.Unity.Client.Presentation;
using CardGame.Unity.Client.UI.Shell;
using Game.UnityClient.Contracts;

namespace CardGame.Unity.Client.UI.Presentation
{
  public sealed class PresentationTimelineEntry
  {
    public long EventSeq { get; set; }
    public string EventType { get; set; } = string.Empty;
    public PresentationIntentType IntentType { get; set; }
    public string Diagnostic { get; set; } = string.Empty;
  }

  public sealed class PresentationEventFeed
  {
    private readonly int capacity;
    private readonly Queue<PresentationTimelineEntry> entries = new();

    public PresentationEventFeed(int capacity = 20)
    {
      this.capacity = Math.Max(1, capacity);
    }

    public IReadOnlyList<PresentationTimelineEntry> Entries => entries.ToList();

    public void Add(PresentationIntent intent)
    {
      if (intent == null)
      {
        return;
      }

      entries.Enqueue(new PresentationTimelineEntry
      {
        EventSeq = intent.EventSeq,
        EventType = intent.EventType,
        IntentType = intent.IntentType,
        Diagnostic = intent.Diagnostic
      });

      while (entries.Count > capacity)
      {
        entries.Dequeue();
      }
    }
  }

  public sealed class PresentationPlaybackRunner
  {
    private readonly PresentationIntentQueue queue;
    private readonly PresentationEventRouter router;
    private readonly PresentationEventFeed feed;
    private readonly PresentationPlaybackClock clock;
    private readonly PresentationAnimationRegistry animations;
    private readonly PresentationPlaybackReport report = new();
    private PresentationIntent current;
    private float currentStartedAt;
    private float remainingSeconds;

    public PresentationPlaybackRunner(PresentationIntentQueue queue = null, PresentationEventRouter router = null, PresentationEventFeed feed = null, PresentationPlaybackClock clock = null, PresentationAnimationRegistry animations = null)
    {
      this.queue = queue ?? new PresentationIntentQueue();
      this.router = router ?? new PresentationEventRouter();
      this.feed = feed ?? new PresentationEventFeed();
      this.clock = clock ?? new PresentationPlaybackClock();
      this.animations = animations ?? new PresentationAnimationRegistry();
    }

    public PresentationPlaybackStatus Status => queue.Status;
    public PresentationIntent CurrentIntent => current;
    public PresentationEventFeed Feed => feed;
    public PresentationPlaybackReport Report => report;

    public int EnqueueEvents(IEnumerable<PublicMatchEventRecord> events)
    {
      var accepted = 0;
      foreach (var evt in (events ?? Array.Empty<PublicMatchEventRecord>()).OrderBy(evt => evt.EventSeq))
      {
        if (queue.Enqueue(router.Route(evt)))
        {
          accepted++;
          report.Enqueued++;
        }
        else
        {
          report.DuplicatesIgnored++;
        }
      }

      return accepted;
    }

    public int EnqueueReconnectEvents(IEnumerable<PublicMatchEventRecord> events)
    {
      var accepted = queue.EnqueueReconnectMissedEvents(events, router);
      report.Enqueued += accepted;
      return accepted;
    }

    public bool EnqueueStatus(string eventType, PresentationIntentType intentType, string diagnostic)
    {
      var accepted = queue.Enqueue(router.RouteStatus(eventType, intentType, diagnostic));
      if (accepted)
      {
        report.Enqueued++;
      }

      return accepted;
    }

    public bool Tick(float deltaSeconds, float defaultDurationSeconds = 0.35f)
    {
      clock.Advance(deltaSeconds);
      if (current != null)
      {
        remainingSeconds -= Math.Max(0f, deltaSeconds);
        if (remainingSeconds > 0f || current.BlocksQueue)
        {
          report.Blocked = current.BlocksQueue;
          return false;
        }

        current = null;
      }

      if (!queue.TryDequeue(out current))
      {
        return false;
      }

      feed.Add(current);
      currentStartedAt = clock.NowSeconds;
      var binding = animations.Resolve(current);
      remainingSeconds = Math.Max(0.01f, binding.DurationSeconds > 0f ? binding.DurationSeconds : current.DurationSeconds > 0f ? current.DurationSeconds : defaultDurationSeconds);
      report.Played++;
      if (binding.IntentType == PresentationIntentType.Generic || string.IsNullOrWhiteSpace(binding.AnimatorName))
      {
        report.SkippedAnimations++;
      }
      else
      {
        report.AnimationsPlayed++;
      }

      report.Blocked = current.BlocksQueue;
      report.Lines.Add($"{current.EventSeq}:{current.EventType}:{animations.Animate(current)}:{remainingSeconds:0.00}s");
      return true;
    }
  }

  public sealed class PresentationPanelViewModel
  {
    public PresentationPlaybackStatus Status { get; set; }
    public string CurrentIntent { get; set; } = string.Empty;
    public bool Blocking { get; set; }
    public IReadOnlyList<string> FeedLines { get; set; } = Array.Empty<string>();

    public static PresentationPanelViewModel From(PresentationPlaybackRunner runner) =>
      new()
      {
        Status = runner?.Status ?? PresentationPlaybackStatus.Idle,
        CurrentIntent = runner?.CurrentIntent == null
          ? "No current presentation intent."
          : $"{runner.CurrentIntent.EventSeq}:{runner.CurrentIntent.EventType} {runner.CurrentIntent.IntentType} {runner.CurrentIntent.ChoreographyLabel}",
        Blocking = runner?.CurrentIntent?.BlocksQueue == true || runner?.Status == PresentationPlaybackStatus.Blocked,
        FeedLines = runner?.Feed?.Entries
          .Select(entry => $"{entry.EventSeq}:{entry.EventType} {entry.IntentType} {entry.Diagnostic}")
          .ToList() ?? new List<string>()
      };
  }

  public sealed class PresentationPanelBinder : PanelBinderBase<PresentationPanelViewModel>
  {
    public string LastText { get; private set; } = string.Empty;

    protected override void OnBind(PresentationPanelViewModel model)
    {
      LastText =
        $"Presentation {model?.Status}\n{model?.CurrentIntent ?? string.Empty}\n" +
        string.Join("\n", model?.FeedLines ?? Array.Empty<string>());
      EnsureText(transform, "PresentationPanelText", 11).text = LastText;
    }
  }

  public sealed class PresentationPlaybackBinder : PanelBinderBase<PresentationPlaybackStatusViewModel>
  {
    protected override void OnBind(PresentationPlaybackStatusViewModel model)
    {
      EnsureText(transform, "PresentationPlaybackText", 11).text =
        $"{model?.Status} {(model?.Blocking == true ? "blocking" : "ready")} {model?.Message ?? string.Empty}";
    }
  }

  public sealed class EventFeedBinder : PanelBinderBase<IEnumerable<PresentationTimelineEntry>>
  {
    public IReadOnlyList<string> Lines { get; private set; } = Array.Empty<string>();

    protected override void OnBind(IEnumerable<PresentationTimelineEntry> model)
    {
      Lines = (model ?? Array.Empty<PresentationTimelineEntry>())
        .Select(entry => $"{entry.EventSeq}:{entry.EventType} {entry.IntentType}")
        .ToList();
      EnsureText(transform, "EventFeedText", 11).text = string.Join("\n", Lines);
    }
  }
}
