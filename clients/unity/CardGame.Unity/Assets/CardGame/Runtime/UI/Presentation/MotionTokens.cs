using UnityEngine;

namespace CardGame.Unity.Client.UI.Presentation
{
  /// Presentation-only timing and easing vocabulary. These values never enter gameplay state.
  public static class MotionTokens
  {
    public const float Instant = .06f;
    public const float Micro = .12f;
    public const float Fast = .2f;
    public const float Normal = .34f;
    public const float Deliberate = .58f;
    public const float Dramatic = .82f;

    public static float Duration(float full, bool reducedMotion) => reducedMotion ? Mathf.Min(full, Micro) : full;
    public static float OutCubic(float t) { t = Mathf.Clamp01(t); return 1f - Mathf.Pow(1f - t, 3f); }
    public static float InCubic(float t) { t = Mathf.Clamp01(t); return t * t * t; }
    public static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    public static float Overshoot(float t)
    {
      t = Mathf.Clamp01(t) - 1f;
      const float tension = 1.55f;
      return 1f + (t * t * ((tension + 1f) * t + tension));
    }
  }
}
