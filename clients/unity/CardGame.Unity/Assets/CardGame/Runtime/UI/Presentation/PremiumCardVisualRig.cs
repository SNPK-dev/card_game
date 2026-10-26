using UnityEngine;
using UnityEngine.EventSystems;
using CardGame.Unity.Client.UI.Theme;
using UnityEngine.UI;

namespace CardGame.Unity.Client.UI.Presentation
{
  public enum CardInteractionState
  {
    Neutral,
    Playable,
    Unplayable,
    ReadyToAttack,
    ReadyForAction,
    Exhausted,
    SelectedSource,
    ValidTarget
  }

  /// Presentation-only hierarchical motion rig. It never reads or mutates match state.
  public sealed class PremiumCardVisualRig : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
  {
    private RectTransform root;
    private bool hovered;
    private bool pressed;
    private bool emphasized;
    private bool target;
    private float phase;
    private RectTransform frameBone;
    private RectTransform artBone;
    private RectTransform manaBone;
    private Text titleText;
    private Canvas priorityCanvas;
    private Shadow liftShadow;
    private Vector2 settledPosition;
    private bool settledPositionCaptured;
    private bool dragging;
    private CardInteractionState interactionState;
    private Outline stateOutline;

    public void Configure(bool shouldEmphasize, bool isTarget, CardInteractionState state=CardInteractionState.Neutral, string stateLabel="")
    {
      root = transform as RectTransform;
      emphasized = shouldEmphasize;
      target = isTarget;
      interactionState=state;
      phase = (Mathf.Abs(gameObject.name.GetHashCode()) % 1000) / 1000f * Mathf.PI * 2f;
      foreach(var child in GetComponentsInChildren<RectTransform>(true))
      {
        if(child.name=="Authored Card Frame")frameBone=child;
        else if(child.name=="Card Art")artBone=child;
        else if(child.name=="Mana Gem")manaBone=child;
        else if(child.name=="Card Name")titleText=child.GetComponent<Text>();
      }
      priorityCanvas=GetComponent<Canvas>();
      if(priorityCanvas==null)priorityCanvas=gameObject.AddComponent<Canvas>();
      priorityCanvas.overrideSorting=true;priorityCanvas.sortingOrder=10;
      // A nested override-sorting canvas owns its graphics, so it also needs a raycaster
      // to keep hover, press, drag, and button input available.
      if(GetComponent<GraphicRaycaster>()==null)gameObject.AddComponent<GraphicRaycaster>();
      liftShadow=GetComponent<Shadow>();
      if(liftShadow==null)liftShadow=gameObject.AddComponent<Shadow>();
      liftShadow.effectColor=new Color(0,0,0,.52f);liftShadow.effectDistance=new Vector2(3,-4);
      var stateGroup=GetComponent<CanvasGroup>();if(stateGroup==null)stateGroup=gameObject.AddComponent<CanvasGroup>();
      stateGroup.alpha=(state is CardInteractionState.Unplayable or CardInteractionState.Exhausted)?.68f:1f;
      stateOutline=GetComponent<Outline>();if(stateOutline==null)stateOutline=gameObject.AddComponent<Outline>();
      stateOutline.useGraphicAlpha=true;stateOutline.effectDistance=StateOutlineDistance(state);
      stateOutline.effectColor=StateColor(state,.9f);
      if(!string.IsNullOrWhiteSpace(stateLabel)&&state is not CardInteractionState.Unplayable and not CardInteractionState.Exhausted and not CardInteractionState.Neutral)CreateStateBadge(stateLabel);
    }

    public void ApplyTheme(GameTheme theme)
    {
      var image=GetComponent<Image>();
      if(image!=null)image.color=target?new Color(.96f,.24f,.16f,1f):emphasized?ThemeColorParser.ParseOrWhite(theme.Palette.Playable):new Color(.48f,.34f,.16f,1f);
      if(titleText!=null)titleText.color=theme.Id=="cyberpunk"?new Color(.78f,.98f,1f,1f):new Color(.12f,.075f,.025f,1f);
    }

    private void Update()
    {
      if (root == null) return;
      if(!settledPositionCaptured){settledPosition=root.anchoredPosition;settledPositionCaptured=true;}
      var breathe = emphasized ? Mathf.Sin(Time.unscaledTime * 2.4f + phase) * .012f : 0f;
      var desired = dragging ? 1.14f : pressed ? .965f : hovered ? 1.085f : 1f + breathe;
      root.localScale = Vector3.Lerp(root.localScale, Vector3.one * desired, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 18f));
      var tilt = hovered ? Mathf.Sin(Time.unscaledTime * 1.9f + phase) * 1.2f : 0f;
      root.localRotation = Quaternion.Slerp(root.localRotation, Quaternion.Euler(0f, 0f, target ? tilt * 1.5f : tilt), 1f - Mathf.Exp(-Time.unscaledDeltaTime * 12f));
      var boneT=1f-Mathf.Exp(-Time.unscaledDeltaTime*15f);
      if(frameBone!=null)frameBone.localScale=Vector3.Lerp(frameBone.localScale,Vector3.one*(hovered?1.025f:1f),boneT);
      if(artBone!=null)
      {
        artBone.localScale=Vector3.Lerp(artBone.localScale,Vector3.one*(hovered?1.045f:1f),boneT);
        artBone.localRotation=Quaternion.Slerp(artBone.localRotation,Quaternion.Euler(0,0,hovered?-tilt*.55f:0),boneT);
      }
      if(manaBone!=null)manaBone.localRotation=Quaternion.Slerp(manaBone.localRotation,Quaternion.Euler(0,0,hovered?tilt*2.2f:0),boneT);
      if(!dragging)
      {
        var lift=hovered?14f:pressed?5f:0f;
        root.anchoredPosition=Vector2.Lerp(root.anchoredPosition,settledPosition+Vector2.up*lift,1f-Mathf.Exp(-Time.unscaledDeltaTime*20f));
      }
      if(priorityCanvas!=null)priorityCanvas.sortingOrder=dragging?220:hovered||pressed?120:10;
      if(liftShadow!=null)
      {
        liftShadow.effectDistance=Vector2.Lerp(liftShadow.effectDistance,hovered||dragging?new Vector2(7,-11):new Vector2(3,-4),boneT);
        liftShadow.effectColor=Color.Lerp(liftShadow.effectColor,hovered||dragging?new Color(0,0,0,.78f):new Color(0,0,0,.52f),boneT);
      }
      if(stateOutline!=null)
      {
        var actionable=interactionState is CardInteractionState.Playable or CardInteractionState.ReadyToAttack or CardInteractionState.ReadyForAction;
        var pulse=actionable?.72f+Mathf.Sin(Time.unscaledTime*3.4f+phase)*.2f:.9f;
        stateOutline.effectColor=StateColor(interactionState,pulse);
      }
    }

    public void OnPointerEnter(PointerEventData eventData) { settledPosition=root==null?Vector2.zero:root.anchoredPosition;settledPositionCaptured=true;hovered = true; }
    public void OnPointerExit(PointerEventData eventData) { hovered = false; pressed = false; }
    public void OnPointerDown(PointerEventData eventData) => pressed = true;
    public void OnPointerUp(PointerEventData eventData) => pressed = false;
    public void SetDragging(bool value)
    {
      dragging=value;pressed=false;hovered=value;
      if(value&&root!=null)settledPosition=root.anchoredPosition;
    }

    private void CreateStateBadge(string label)
    {
      var node=new GameObject("Interaction State Badge");node.transform.SetParent(transform,false);
      var rect=node.AddComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);rect.pivot=new Vector2(.5f,0);rect.sizeDelta=new Vector2(58,16);rect.anchoredPosition=new Vector2(0,1);
      var stateBadge=node.AddComponent<Image>();stateBadge.color=StateColor(interactionState,.94f);stateBadge.raycastTarget=false;
      var textNode=new GameObject("Interaction State Label");textNode.transform.SetParent(node.transform,false);var textRect=textNode.AddComponent<RectTransform>();textRect.anchorMin=Vector2.zero;textRect.anchorMax=Vector2.one;textRect.offsetMin=Vector2.zero;textRect.offsetMax=Vector2.zero;
      var stateBadgeText=textNode.AddComponent<Text>();stateBadgeText.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");stateBadgeText.fontSize=8;stateBadgeText.fontStyle=FontStyle.Bold;stateBadgeText.alignment=TextAnchor.MiddleCenter;stateBadgeText.color=Color.white;stateBadgeText.text=label;stateBadgeText.resizeTextForBestFit=true;stateBadgeText.resizeTextMinSize=6;stateBadgeText.resizeTextMaxSize=8;stateBadgeText.raycastTarget=false;
    }

    private static Vector2 StateOutlineDistance(CardInteractionState state)=>state switch
    {
      CardInteractionState.ValidTarget=>new Vector2(5,-5),
      CardInteractionState.SelectedSource=>new Vector2(4,-4),
      CardInteractionState.Playable or CardInteractionState.ReadyToAttack or CardInteractionState.ReadyForAction=>new Vector2(3,-3),
      _=>Vector2.zero
    };

    private static Color StateColor(CardInteractionState state,float alpha)=>state switch
    {
      CardInteractionState.Playable=>new Color(.16f,.86f,.72f,alpha),
      CardInteractionState.ReadyToAttack=>new Color(.3f,1f,.3f,alpha),
      CardInteractionState.ReadyForAction=>new Color(.25f,.78f,1f,alpha),
      CardInteractionState.SelectedSource=>new Color(.08f,.66f,1f,alpha),
      CardInteractionState.ValidTarget=>new Color(1f,.16f,.08f,alpha),
      _=>new Color(0,0,0,0)
    };
  }
}
