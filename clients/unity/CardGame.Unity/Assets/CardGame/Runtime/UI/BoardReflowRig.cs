using System.Collections;
using CardGame.Unity.Client.UI.Presentation;
using UnityEngine;

namespace CardGame.Unity.Client.UI
{
  public sealed class BoardReflowRig : MonoBehaviour
  {
    private RectTransform rect;
    private CanvasGroup group;
    private PremiumCardVisualRig[] cardMotion;

    public void PlayMove(Vector3 previousWorld,bool reducedMotion)
    {
      rect=transform as RectTransform;if(rect==null)return;
      SuspendCardMotion();
      var target=rect.position;
      if((target-previousWorld).sqrMagnitude<1f){RestoreCardMotion();Destroy(this);return;}
      rect.position=previousWorld;
      StartCoroutine(MoveTo(target,reducedMotion));
    }

    public void PlaySpawn(bool reducedMotion)
    {
      rect=transform as RectTransform;if(rect==null)return;
      SuspendCardMotion();
      group=GetComponent<CanvasGroup>()??gameObject.AddComponent<CanvasGroup>();
      group.alpha=reducedMotion?1f:0f;rect.localScale=Vector3.one*(reducedMotion?1f:.72f);
      StartCoroutine(Spawn(reducedMotion));
    }

    private IEnumerator MoveTo(Vector3 target,bool reducedMotion)
    {
      var from=rect.position;var duration=MotionTokens.Duration(MotionTokens.Normal,reducedMotion);var elapsed=0f;
      while(elapsed<duration&&rect!=null)
      {
        elapsed+=Time.unscaledDeltaTime;var t=MotionTokens.Smooth(elapsed/duration);
        rect.position=Vector3.LerpUnclamped(from,target,t);yield return null;
      }
      if(rect!=null)rect.position=target;
      RestoreCardMotion();
      Destroy(this);
    }

    private IEnumerator Spawn(bool reducedMotion)
    {
      var duration=MotionTokens.Duration(MotionTokens.Fast,reducedMotion);var elapsed=0f;
      while(elapsed<duration&&rect!=null)
      {
        elapsed+=Time.unscaledDeltaTime;var t=MotionTokens.Overshoot(elapsed/duration);
        rect.localScale=Vector3.one*Mathf.LerpUnclamped(reducedMotion?1f:.72f,1f,t);
        if(group!=null)group.alpha=Mathf.Clamp01(elapsed/duration*1.7f);yield return null;
      }
      if(rect!=null)rect.localScale=Vector3.one;if(group!=null)group.alpha=1f;
      RestoreCardMotion();
      Destroy(this);
    }

    private void SuspendCardMotion()
    {
      cardMotion=GetComponentsInChildren<PremiumCardVisualRig>(true);
      foreach(var motion in cardMotion)motion.enabled=false;
    }

    private void RestoreCardMotion()
    {
      foreach(var motion in cardMotion??System.Array.Empty<PremiumCardVisualRig>())if(motion!=null)motion.enabled=true;
    }
  }
}
