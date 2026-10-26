using CardGame.Unity.Client.Presentation;

namespace CardGame.Unity.Client.UI.Presentation
{
  public sealed class PresentationPlaybackStatusViewModel
  {
    public PresentationPlaybackStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool Blocking { get; set; }

    public static PresentationPlaybackStatusViewModel From(PresentationIntentQueue queue) =>
      new()
      {
        Status = queue?.Status ?? PresentationPlaybackStatus.Idle,
        Blocking = queue?.Status == PresentationPlaybackStatus.Blocked,
        Message = queue?.Status == PresentationPlaybackStatus.Blocked ? "Presentation playback blocked by terminal/fault event." : "Presentation playback ready."
      };
  }

  public sealed class PresentationPlaybackStatusView
  {
    public PresentationPlaybackStatusViewModel LastBound { get; private set; }

    public void Bind(PresentationPlaybackStatusViewModel model)
    {
      LastBound = model;
    }
  }
}
