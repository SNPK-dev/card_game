using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using System.Threading.Tasks;
using CardGame.Unity.Client.UI.Presentation;

namespace CardGame.Unity.Client.UI
{
  public sealed class ZoneTransferAnimationRig:MonoBehaviour
  {
    public Task PlayDeathReaction(Vector3 worldPosition,bool reducedMotion)
    {
      var completion=new TaskCompletionSource<bool>();
      StartCoroutine(AnimateDeathReaction(worldPosition,reducedMotion,completion));
      return completion.Task;
    }

    private IEnumerator AnimateDeathReaction(Vector3 worldPosition,bool reducedMotion,TaskCompletionSource<bool> completion)
    {
      var node=new GameObject("Lethal Death Reaction");node.transform.SetParent(transform,false);
      var rect=node.AddComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=new Vector2(.5f,.5f);rect.sizeDelta=new Vector2(126,126);rect.position=worldPosition;
      var ring=node.AddComponent<Image>();ring.color=new Color(.72f,.12f,.12f,.7f);ring.raycastTarget=false;
      var glyphNode=new GameObject("Death Glyph");glyphNode.transform.SetParent(node.transform,false);var glyphRect=glyphNode.AddComponent<RectTransform>();glyphRect.anchorMin=Vector2.zero;glyphRect.anchorMax=Vector2.one;glyphRect.offsetMin=Vector2.zero;glyphRect.offsetMax=Vector2.zero;
      var glyph=glyphNode.AddComponent<Text>();glyph.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");glyph.text="☠";glyph.fontSize=42;glyph.fontStyle=FontStyle.Bold;glyph.alignment=TextAnchor.MiddleCenter;glyph.color=Color.white;glyph.raycastTarget=false;
      var group=node.AddComponent<CanvasGroup>();var duration=MotionTokens.Duration(MotionTokens.Deliberate,reducedMotion);var elapsed=0f;
      while(elapsed<duration)
      {
        elapsed+=Time.unscaledDeltaTime;var t=Mathf.Clamp01(elapsed/duration);var impact=Mathf.Sin(t*Mathf.PI);
        rect.localScale=Vector3.one*Mathf.LerpUnclamped(reducedMotion?1f:.72f,1.24f,impact);
        rect.localRotation=Quaternion.Euler(0,0,reducedMotion?0f:Mathf.Sin(t*Mathf.PI*3f)*(1f-t)*7f);
        group.alpha=t<.68f?1f:1f-(t-.68f)/.32f;ring.color=new Color(.72f,.08f,.08f,(1f-t)*.7f);yield return null;
      }
      Destroy(node);completion.TrySetResult(true);
    }

    public void Play(Vector3 from,Vector3 to,string glyph,bool reducedMotion)
    {
      Play(from,to,glyph,reducedMotion,null,null);
    }

    public void Play(Vector3 from,Vector3 to,string glyph,bool reducedMotion,Texture art,string title)
    {
      StartCoroutine(Animate(from,to,glyph,reducedMotion,art,title));
    }

    private IEnumerator Animate(Vector3 from,Vector3 to,string glyph,bool reducedMotion,Texture art,string title)
    {
      var glow=new GameObject("Zone Transfer Trail Glow");glow.transform.SetParent(transform,false);var glowRect=glow.AddComponent<RectTransform>();glowRect.sizeDelta=new Vector2(90,118);glowRect.position=from;var glowImage=glow.AddComponent<Image>();glowImage.color=new Color(.72f,.22f,1f,.22f);glowImage.raycastTarget=false;
      var node=new GameObject("Zone Transfer Card");node.transform.SetParent(transform,false);var rect=node.AddComponent<RectTransform>();rect.sizeDelta=new Vector2(76,104);rect.position=from;
      var image=node.AddComponent<Image>();image.color=new Color(.045f,.025f,.075f,.98f);image.raycastTarget=false;var outline=node.AddComponent<Outline>();outline.effectColor=new Color(1f,.42f,.08f,.96f);outline.effectDistance=new Vector2(3,-3);
      if(art!=null){var artNode=new GameObject("Moving Card Art");artNode.transform.SetParent(node.transform,false);var artRect=artNode.AddComponent<RectTransform>();artRect.anchorMin=new Vector2(.08f,.14f);artRect.anchorMax=new Vector2(.92f,.92f);artRect.offsetMin=Vector2.zero;artRect.offsetMax=Vector2.zero;var raw=artNode.AddComponent<RawImage>();raw.texture=art;raw.color=Color.white;raw.raycastTarget=false;}
      var frameNode=new GameObject("Moving Card Frame");frameNode.transform.SetParent(node.transform,false);var frameRect=frameNode.AddComponent<RectTransform>();frameRect.anchorMin=Vector2.zero;frameRect.anchorMax=Vector2.one;frameRect.offsetMin=new Vector2(-3,-3);frameRect.offsetMax=new Vector2(3,3);var frame=frameNode.AddComponent<RawImage>();frame.texture=Resources.Load<Texture2D>("CardFrames/frame-arcane-v1");frame.color=Color.white;frame.raycastTarget=false;
      var textNode=new GameObject("Zone Glyph");textNode.transform.SetParent(node.transform,false);var textRect=textNode.AddComponent<RectTransform>();textRect.anchorMin=new Vector2(.58f,.6f);textRect.anchorMax=new Vector2(.98f,.98f);textRect.offsetMin=Vector2.zero;textRect.offsetMax=Vector2.zero;var text=textNode.AddComponent<Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.text=glyph;text.fontSize=25;text.fontStyle=FontStyle.Bold;text.alignment=TextAnchor.MiddleCenter;text.color=Color.white;text.raycastTarget=false;var textOutline=textNode.AddComponent<Outline>();textOutline.effectColor=new Color(0,0,0,.95f);textOutline.effectDistance=new Vector2(2,-2);
      if(!string.IsNullOrWhiteSpace(title)){var titleNode=new GameObject("Moving Card Name");titleNode.transform.SetParent(node.transform,false);var titleRect=titleNode.AddComponent<RectTransform>();titleRect.anchorMin=new Vector2(.06f,.02f);titleRect.anchorMax=new Vector2(.94f,.19f);titleRect.offsetMin=Vector2.zero;titleRect.offsetMax=Vector2.zero;var titleText=titleNode.AddComponent<Text>();titleText.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");titleText.text=title;titleText.fontSize=9;titleText.fontStyle=FontStyle.Bold;titleText.alignment=TextAnchor.MiddleCenter;titleText.color=Color.white;titleText.resizeTextForBestFit=true;titleText.resizeTextMinSize=6;titleText.raycastTarget=false;}
      var group=node.AddComponent<CanvasGroup>();var glowGroup=glow.AddComponent<CanvasGroup>();
      var duration=reducedMotion?.16f:.72f;var elapsed=0f;while(elapsed<duration){elapsed+=Time.unscaledDeltaTime;var t=Mathf.Clamp01(elapsed/duration);var eased=t*t*(3f-2f*t);var arc=Vector3.up*Mathf.Sin(t*Mathf.PI)*86f;rect.position=Vector3.Lerp(from,to,eased)+arc;glowRect.position=Vector3.Lerp(from,to,Mathf.Clamp01(eased-.05f))+arc*.82f;rect.localRotation=Quaternion.Euler(0,0,Mathf.Lerp(-9f,7f,t)+Mathf.Sin(t*Mathf.PI)*8f);rect.localScale=Vector3.one*Mathf.Lerp(1.08f,.62f,eased);glowRect.localScale=Vector3.one*Mathf.Lerp(1.12f,.65f,eased);group.alpha=t<.9f?1f:(1f-t)/.1f;glowGroup.alpha=(1f-t)*.75f;yield return null;}Destroy(glow);Destroy(node);
    }
  }
}
