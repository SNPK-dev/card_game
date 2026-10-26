using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using CardGame.Unity.Client.UI.Presentation;
using System.Threading.Tasks;

namespace CardGame.Unity.Client.UI
{
  public sealed class CombatLungeRig:MonoBehaviour
  {
    public Task PlayAsync(RectTransform source,RectTransform target,bool reducedMotion=false)
    {
      if(source==null||target==null)return Task.CompletedTask;
      var completion=new TaskCompletionSource<bool>();
      var proxy=Instantiate(source.gameObject,transform,true);proxy.name="Attacker Card Lunge";
      foreach(var selectable in proxy.GetComponentsInChildren<Selectable>(true))selectable.interactable=false;
      foreach(var motion in proxy.GetComponentsInChildren<PremiumCardVisualRig>(true))motion.enabled=false;
      var proxyGroup=proxy.GetComponent<CanvasGroup>();if(proxyGroup==null)proxyGroup=proxy.AddComponent<CanvasGroup>();proxyGroup.blocksRaycasts=false;proxyGroup.interactable=false;proxyGroup.alpha=1;
      var originalGroup=source.GetComponent<CanvasGroup>();if(originalGroup==null)originalGroup=source.gameObject.AddComponent<CanvasGroup>();var previousAlpha=originalGroup.alpha;originalGroup.alpha=0;
      var rect=proxy.GetComponent<RectTransform>();rect.SetAsLastSibling();
      StartCoroutine(Animate(rect,source,target,originalGroup,previousAlpha,reducedMotion,completion));
      return completion.Task;
    }
    private static IEnumerator Animate(RectTransform rect,RectTransform source,RectTransform target,CanvasGroup original,float previousAlpha,bool reducedMotion,TaskCompletionSource<bool> completion)
    {
      var from=source.TransformPoint(source.rect.center);var to=target.TransformPoint(target.rect.center);var direction=(to-from).normalized;
      var anticipation=MotionTokens.Duration(MotionTokens.Micro,reducedMotion);var elapsed=0f;
      while(elapsed<anticipation&&rect!=null){elapsed+=Time.unscaledDeltaTime;var t=MotionTokens.OutCubic(elapsed/anticipation);rect.position=Vector3.Lerp(from,from-direction*(reducedMotion?0f:18f),t);rect.localScale=Vector3.one*Mathf.Lerp(1f,1.04f,t);yield return null;}
      var travel=MotionTokens.Duration(MotionTokens.Fast,reducedMotion);elapsed=0f;var windup=rect.position;
      while(elapsed<travel&&rect!=null){elapsed+=Time.unscaledDeltaTime;var t=MotionTokens.InCubic(elapsed/travel);rect.position=Vector3.Lerp(windup,to,t);rect.localRotation=Quaternion.Euler(0,0,reducedMotion?0f:Mathf.Sin(t*Mathf.PI)*-8f);yield return null;}
      if(!reducedMotion&&target!=null)target.localScale=Vector3.one*.94f;
      var hitStop=MotionTokens.Duration(MotionTokens.Instant,reducedMotion);elapsed=0f;while(elapsed<hitStop){elapsed+=Time.unscaledDeltaTime;yield return null;}
      var recovery=MotionTokens.Duration(MotionTokens.Fast,reducedMotion);elapsed=0f;
      while(elapsed<recovery&&rect!=null){elapsed+=Time.unscaledDeltaTime;var t=MotionTokens.Smooth(elapsed/recovery);rect.position=Vector3.Lerp(to,from,t);rect.localScale=Vector3.Lerp(Vector3.one*1.04f,Vector3.one,t);if(target!=null)target.localScale=Vector3.Lerp(target.localScale,Vector3.one,t);yield return null;}
      if(target!=null)target.localScale=Vector3.one;
      if(original!=null)original.alpha=previousAlpha;
      if(rect!=null)Destroy(rect.gameObject);
      completion.TrySetResult(true);
    }
  }
}
