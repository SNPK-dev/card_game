using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CardGame.Unity.Client.UI
{
  public sealed class CardLongPressPreview : MonoBehaviour,IPointerDownHandler,IPointerUpHandler,IPointerExitHandler
  {
    private Action show;
    private Action hide;
    private Coroutine pending;
    private bool opened;
    private bool pointerHeld;
    private bool suppressClick;

    public bool IsOpen => opened;
    public void Configure(Action showPreview,Action hidePreview){show=showPreview;hide=hidePreview;}
    public void OnPointerDown(PointerEventData eventData){if(eventData.button!=PointerEventData.InputButton.Left)return;pointerHeld=true;CancelPending();pending=StartCoroutine(OpenAfterDelay());}
    public void OnPointerUp(PointerEventData eventData){if(eventData.button!=PointerEventData.InputButton.Left)return;pointerHeld=false;suppressClick=opened;Close();}
    public bool ConsumeClick(){if(!suppressClick)return false;suppressClick=false;return true;}
    public void OnPointerExit(PointerEventData eventData){if(!opened){pointerHeld=false;CancelPending();}}
    public void CancelForDrag(){pointerHeld=false;Close();}
    private IEnumerator OpenAfterDelay(){yield return new WaitForSecondsRealtime(.42f);pending=null;if(!pointerHeld)yield break;opened=true;show?.Invoke();}
    private void CancelPending(){if(pending==null)return;StopCoroutine(pending);pending=null;}
    private void Close(){CancelPending();if(!opened)return;opened=false;hide?.Invoke();}
    private void OnDisable(){pointerHeld=false;Close();}
  }
}
