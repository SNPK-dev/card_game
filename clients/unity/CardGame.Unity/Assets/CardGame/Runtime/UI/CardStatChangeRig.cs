using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace CardGame.Unity.Client.UI
{
  public sealed class CardStatChangeRig : MonoBehaviour
  {
    public void Configure(int delta)
    {
      if(delta!=0)StartCoroutine(Pulse(delta));
    }

    private IEnumerator Pulse(int delta)
    {
      var image=GetComponent<Image>();
      var original=image==null?Color.white:image.color;
      var accent=delta>0?new Color(.24f,1f,.48f,1f):new Color(1f,.2f,.28f,1f);
      const float duration=.42f;
      var elapsed=0f;
      while(elapsed<duration)
      {
        elapsed+=Time.unscaledDeltaTime;
        var t=Mathf.Clamp01(elapsed/duration);
        var pulse=Mathf.Sin(t*Mathf.PI);
        transform.localScale=Vector3.one*(1f+pulse*.34f);
        if(image!=null)image.color=Color.Lerp(original,accent,pulse*.72f);
        yield return null;
      }
      transform.localScale=Vector3.one;
      if(image!=null)image.color=original;
    }
  }
}
