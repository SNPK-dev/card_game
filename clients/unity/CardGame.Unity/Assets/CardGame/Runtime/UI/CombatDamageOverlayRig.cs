using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using CardGame.Unity.Client.UI.Presentation;

namespace CardGame.Unity.Client.UI
{
  public sealed class CombatDamageOverlayRig : MonoBehaviour
  {
    public void Play(Vector3 worldPosition,int delta,bool reducedMotion)
    {
      if(delta==0)return;
      StartCoroutine(Animate(worldPosition,delta,reducedMotion));
    }

    private IEnumerator Animate(Vector3 worldPosition,int delta,bool reducedMotion)
    {
      var node=new GameObject(delta<0?"Committed Damage Number":"Committed Healing Number");
      node.transform.SetParent(transform,false);
      var rect=node.AddComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=new Vector2(.5f,.5f);rect.sizeDelta=new Vector2(68,52);rect.position=worldPosition;
      var group=node.AddComponent<CanvasGroup>();group.alpha=0;
      var textNode=new GameObject("Amount");textNode.transform.SetParent(node.transform,false);var textRect=textNode.AddComponent<RectTransform>();textRect.anchorMin=Vector2.zero;textRect.anchorMax=Vector2.one;textRect.offsetMin=Vector2.zero;textRect.offsetMax=Vector2.zero;
      var text=textNode.AddComponent<Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.text=delta<0?$"−{Mathf.Abs(delta)}":$"+{delta}";text.fontSize=34;text.fontStyle=FontStyle.Bold;text.alignment=TextAnchor.MiddleCenter;text.color=delta<0?new Color(1f,.2f,.1f,1f):new Color(.25f,1f,.48f,1f);text.raycastTarget=false;
      var textOutline=textNode.AddComponent<Outline>();textOutline.effectColor=new Color(0,0,0,1);textOutline.effectDistance=new Vector2(2,-2);

      var popDuration=MotionTokens.Duration(MotionTokens.Fast,reducedMotion);var holdDuration=reducedMotion?.16f:.34f;var fadeDuration=MotionTokens.Duration(MotionTokens.Fast,reducedMotion);
      var elapsed=0f;
      while(elapsed<popDuration)
      {
        elapsed+=Time.unscaledDeltaTime;var t=Mathf.Clamp01(elapsed/popDuration);var eased=MotionTokens.Overshoot(t);group.alpha=Mathf.Clamp01(t*2f);rect.localScale=Vector3.one*Mathf.Lerp(.45f,1.12f,eased);yield return null;
      }
      rect.localScale=Vector3.one;
      elapsed=0f;var origin=worldPosition;
      while(elapsed<holdDuration)
      {
        elapsed+=Time.unscaledDeltaTime;var t=Mathf.Clamp01(elapsed/holdDuration);rect.position=origin+Vector3.up*Mathf.Sin(t*Mathf.PI)*10f;group.alpha=1f;yield return null;
      }
      elapsed=0f;var fadeOrigin=rect.position;
      while(elapsed<fadeDuration)
      {
        elapsed+=Time.unscaledDeltaTime;var t=Mathf.Clamp01(elapsed/fadeDuration);group.alpha=1f-t;rect.position=fadeOrigin+Vector3.up*(t*20f);rect.localScale=Vector3.one*(1f+t*.12f);yield return null;
      }
      Destroy(node);
    }
  }
}
