using System.Collections;
using CardGame.Unity.Client.UI.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace CardGame.Unity.Client.UI
{
  public sealed class TurnTransitionRig : MonoBehaviour
  {
    private RectTransform ribbon;
    private CanvasGroup group;
    private Image surface;
    private Text title;
    private Text subtitle;
    private Coroutine active;

    public void Initialize(Transform parent)
    {
      var node=new GameObject("Compact Turn Transition");node.transform.SetParent(parent,false);
      ribbon=node.AddComponent<RectTransform>();ribbon.anchorMin=ribbon.anchorMax=new Vector2(.5f,1f);ribbon.pivot=new Vector2(.5f,1f);ribbon.sizeDelta=new Vector2(360,62);ribbon.anchoredPosition=new Vector2(0,-42);
      surface=node.AddComponent<Image>();surface.color=new Color(.035f,.16f,.28f,.94f);surface.raycastTarget=false;
      var outline=node.AddComponent<Outline>();outline.effectColor=new Color(.26f,.78f,1f,.78f);outline.effectDistance=new Vector2(2,-2);
      group=node.AddComponent<CanvasGroup>();group.interactable=false;group.blocksRaycasts=false;group.alpha=0;
      title=CreateText(node.transform,"Turn Title",17,FontStyle.Bold,new Vector2(.04f,.42f),new Vector2(.96f,.94f));
      subtitle=CreateText(node.transform,"Turn Subtitle",10,FontStyle.Normal,new Vector2(.05f,.08f),new Vector2(.95f,.45f));
    }

    public void Play(string heading,string detail,bool localTurn,bool reducedMotion)
    {
      if(active!=null)StopCoroutine(active);
      title.text=heading??string.Empty;subtitle.text=detail??string.Empty;
      surface.color=localTurn?new Color(.025f,.24f,.38f,.95f):new Color(.16f,.12f,.2f,.94f);
      active=StartCoroutine(Animate(reducedMotion));
    }

    private IEnumerator Animate(bool reducedMotion)
    {
      var enter=MotionTokens.Duration(MotionTokens.Fast,reducedMotion);var hold=reducedMotion?.18f:.42f;var exit=MotionTokens.Duration(MotionTokens.Fast,reducedMotion);var elapsed=0f;
      while(elapsed<enter){elapsed+=Time.unscaledDeltaTime;var t=MotionTokens.OutCubic(elapsed/enter);group.alpha=t;ribbon.anchoredPosition=new Vector2(0,Mathf.Lerp(-24,-42,t));yield return null;}
      elapsed=0f;while(elapsed<hold){elapsed+=Time.unscaledDeltaTime;group.alpha=1;yield return null;}
      elapsed=0f;while(elapsed<exit){elapsed+=Time.unscaledDeltaTime;var t=MotionTokens.InCubic(elapsed/exit);group.alpha=1-t;ribbon.anchoredPosition=new Vector2(0,Mathf.Lerp(-42,-54,t));yield return null;}
      group.alpha=0;active=null;
    }

    private static Text CreateText(Transform parent,string name,int size,FontStyle style,Vector2 min,Vector2 max)
    {
      var node=new GameObject(name);node.transform.SetParent(parent,false);var rect=node.AddComponent<RectTransform>();rect.anchorMin=min;rect.anchorMax=max;rect.offsetMin=Vector2.zero;rect.offsetMax=Vector2.zero;
      var text=node.AddComponent<Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=size;text.fontStyle=style;text.alignment=TextAnchor.MiddleCenter;text.color=Color.white;text.raycastTarget=false;return text;
    }
  }
}
