using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CardGame.Unity.Client.UI.Presentation;

namespace CardGame.Unity.Client.UI
{
  public sealed class CardDragToBoard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
  {
    private RectTransform card;
    private RectTransform board;
    private Canvas canvas;
    private CanvasGroup group;
    private Transform originalParent;
    private int originalSiblingIndex;
    private Vector2 originalPosition;
    private Action play;
    private Action<bool> dragStateChanged;
    private Action<bool> dragEnded;
    private Action rejectFeedbackEnded;
    private bool insideDropLane;
    private Vector2 lastPointerPosition;
    private PremiumCardVisualRig visualRig;
    private bool ending;

    public void Configure(RectTransform dropBoard, Action playCard, Action<bool> onDragStateChanged = null, Action<bool> onDragEnded = null, Action onRejectFeedbackEnded = null)
    {
      board=dropBoard; play=playCard; card=transform as RectTransform;
      canvas=GetComponentInParent<Canvas>();group=GetComponent<CanvasGroup>();
      if(group==null)group=gameObject.AddComponent<CanvasGroup>();
      dragStateChanged=onDragStateChanged;dragEnded=onDragEnded;
      rejectFeedbackEnded=onRejectFeedbackEnded;
      visualRig=GetComponent<PremiumCardVisualRig>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
      if(play==null||card==null||ending)return;
      GetComponent<CardLongPressPreview>()?.CancelForDrag();
      originalParent=card.parent;originalSiblingIndex=card.GetSiblingIndex();originalPosition=card.anchoredPosition;
      lastPointerPosition=eventData.position;insideDropLane=false;
      visualRig?.SetDragging(true);
      card.SetParent(canvas.transform,true);card.localScale=Vector3.one*1.08f;card.localRotation=Quaternion.Euler(0,0,-2f);group.blocksRaycasts=false;group.alpha=.98f;
      dragStateChanged?.Invoke(false);card.SetAsLastSibling();
    }

    public void OnDrag(PointerEventData eventData)
    {
      if(originalParent==null)return;
      var delta=eventData.position-lastPointerPosition;lastPointerPosition=eventData.position;
      card.position=Vector3.Lerp(card.position,eventData.position+new Vector2(0,26),.72f);
      card.localRotation=Quaternion.Euler(0,0,Mathf.Clamp(-delta.x*.09f,-8f,8f));
      var nextInside=board!=null&&RectTransformUtility.RectangleContainsScreenPoint(board,eventData.position,eventData.pressEventCamera);
      if(nextInside!=insideDropLane){insideDropLane=nextInside;dragStateChanged?.Invoke(insideDropLane);card.SetAsLastSibling();}
    }

    public void OnEndDrag(PointerEventData eventData)
    {
      if(originalParent==null)return;
      var accepted=board!=null&&RectTransformUtility.RectangleContainsScreenPoint(board,eventData.position,eventData.pressEventCamera);
      var parent=originalParent;var sibling=originalSiblingIndex;originalParent=null;ending=true;
      group.blocksRaycasts=true;dragEnded?.Invoke(accepted);
      if(accepted)
      {
        StartCoroutine(Commit(parent,sibling));
        return;
      }

      card.SetParent(parent,false);card.SetSiblingIndex(sibling);visualRig?.SetDragging(false);
      Vector2 rejectedPosition;
      var parentRect=parent as RectTransform;
      if(parentRect==null||!RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect,eventData.position,eventData.pressEventCamera,out rejectedPosition))rejectedPosition=originalPosition;
      card.anchoredPosition=rejectedPosition;StartCoroutine(SnapBack());
    }

    private IEnumerator Commit(Transform parent,int sibling)
    {
      var startScale=card.localScale;var startRotation=card.localRotation;var elapsed=0f;
      while(elapsed<MotionTokens.Micro&&card!=null)
      {
        elapsed+=Time.unscaledDeltaTime;var t=MotionTokens.OutCubic(elapsed/MotionTokens.Micro);
        card.localScale=Vector3.LerpUnclamped(startScale,Vector3.one*.9f,t);
        card.localRotation=Quaternion.Slerp(startRotation,Quaternion.identity,t);group.alpha=Mathf.Lerp(1f,.68f,t);yield return null;
      }
      if(card!=null){card.SetParent(parent,false);card.SetSiblingIndex(sibling);card.anchoredPosition=originalPosition;visualRig?.SetDragging(false);}
      ending=false;play?.Invoke();
    }

    private IEnumerator SnapBack()
    {
      var start=card.anchoredPosition;var elapsed=0f;const float duration=MotionTokens.Fast;
      while(elapsed<duration&&card!=null)
      {
        elapsed+=Time.unscaledDeltaTime;var t=Mathf.Clamp01(elapsed/duration);var eased=MotionTokens.Overshoot(t);
        card.anchoredPosition=Vector2.LerpUnclamped(start,originalPosition,eased);card.localScale=Vector3.LerpUnclamped(Vector3.one*1.08f,Vector3.one,eased);card.localRotation=Quaternion.Lerp(Quaternion.Euler(0,0,4f),Quaternion.identity,eased);group.alpha=Mathf.Lerp(.9f,1f,eased);yield return null;
      }
      if(card!=null){card.anchoredPosition=originalPosition;card.localScale=Vector3.one;card.localRotation=Quaternion.identity;group.alpha=1f;}
      var remaining=Mathf.Max(0f,MotionTokens.Normal-MotionTokens.Fast);
      if(remaining>0f)yield return new WaitForSecondsRealtime(remaining);
      rejectFeedbackEnded?.Invoke();
      ending=false;
    }
  }
}
