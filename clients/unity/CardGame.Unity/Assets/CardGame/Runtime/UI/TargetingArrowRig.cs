using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using System.Collections;
using CardGame.Unity.Client.UI.Presentation;

namespace CardGame.Unity.Client.UI
{
  public sealed class TargetingArrowRig:MonoBehaviour
  {
    private const int MaxSegments=64;
    private RectTransform host;
    private TargetingArrowGraphic graphic;
    private Canvas canvas;
    private RectTransform sourceAnchor;
    private Vector3 fallbackSourceWorld;
    private bool useFallbackSource;
    private Vector3 fixedTargetWorld;
    private bool useFixedTarget;
    private Coroutine fixedLifetime;
    private bool visible;

    public int SegmentCapacity=>MaxSegments;
    public int RenderedSegmentCount=>graphic?.RenderedSegmentCount??0;
    public bool IsVisible=>visible&&host!=null&&host.gameObject.activeSelf;

    public void Initialize(Canvas owner)
    {
      canvas=owner;
      var hostObject=new GameObject("Targeting Arrow Canvas Host");hostObject.transform.SetParent(owner.transform,false);host=hostObject.AddComponent<RectTransform>();host.anchorMin=Vector2.zero;host.anchorMax=Vector2.one;host.offsetMin=Vector2.zero;host.offsetMax=Vector2.zero;
      var overlayCanvas=hostObject.AddComponent<Canvas>();overlayCanvas.overrideSorting=true;overlayCanvas.sortingOrder=32000;
      var hostGroup=hostObject.AddComponent<CanvasGroup>();hostGroup.interactable=false;hostGroup.blocksRaycasts=false;
      graphic=hostObject.AddComponent<TargetingArrowGraphic>();graphic.raycastTarget=false;graphic.color=Color.white;
      hostObject.SetActive(false);
    }

    public void Show(RectTransform source)
    {
      CancelFixedLifetime();useFixedTarget=false;
      sourceAnchor=source;
      useFallbackSource=source!=null;
      if(source!=null)fallbackSourceWorld=source.TransformPoint(source.rect.center);
      ShowHost();
    }

    public void Show(Vector3 worldSource)
    {
      CancelFixedLifetime();useFixedTarget=false;
      sourceAnchor=null;fallbackSourceWorld=worldSource;useFallbackSource=true;ShowHost();
    }

    public void ShowFixed(Vector3 worldSource,Vector3 worldTarget,bool reducedMotion=false)
    {
      if(fixedLifetime!=null)StopCoroutine(fixedLifetime);
      sourceAnchor=null;fallbackSourceWorld=worldSource;fixedTargetWorld=worldTarget;
      useFallbackSource=true;useFixedTarget=true;ShowHost();
      fixedLifetime=StartCoroutine(HoldFixed(reducedMotion?MotionTokens.Micro:MotionTokens.Normal));
    }

    public void Hide()
    {
      visible=false;sourceAnchor=null;useFallbackSource=false;useFixedTarget=false;fixedLifetime=null;graphic?.ClearVector();if(host!=null)host.gameObject.SetActive(false);
    }

    public void HideInteractive(){if(!useFixedTarget)Hide();}

    private void CancelFixedLifetime(){if(fixedLifetime!=null){StopCoroutine(fixedLifetime);fixedLifetime=null;}}

    private void ShowHost()
    {
      visible=true;if(host!=null){host.gameObject.SetActive(true);host.SetAsLastSibling();}
    }

    private void LateUpdate()
    {
      if(!visible||canvas==null||host==null)return;
      var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
      if(sourceAnchor!=null)fallbackSourceWorld=sourceAnchor.TransformPoint(sourceAnchor.rect.center);
      if(sourceAnchor==null&&!useFallbackSource){HideGeometry();return;}
      var sourceWorld=fallbackSourceWorld;
      var sourceScreen=RectTransformUtility.WorldToScreenPoint(camera,sourceWorld);var pointer=useFixedTarget?RectTransformUtility.WorldToScreenPoint(camera,fixedTargetWorld):PointerScreenPosition();
      if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(host,sourceScreen,camera,out var from)
        ||!RectTransformUtility.ScreenPointToLocalPointInRectangle(host,pointer,camera,out var to)){HideGeometry();return;}
      ApplyGeometry(from,to);
    }

    private IEnumerator HoldFixed(float duration)
    {
      var elapsed=0f;while(elapsed<duration&&useFixedTarget){elapsed+=Time.unscaledDeltaTime;yield return null;}
      if(useFixedTarget)Hide();else fixedLifetime=null;
    }

    private static Vector2 PointerScreenPosition()
    {
      return Mouse.current?.position.ReadValue()??new Vector2(Screen.width*.5f,Screen.height*.5f);
    }

    private void ApplyGeometry(Vector2 from,Vector2 to)
    {
      if((to-from).sqrMagnitude<42f*42f){HideGeometry();return;}graphic?.SetVector(from,to,MaxSegments,Time.unscaledTime);
    }

    private void HideGeometry()
    {
      graphic?.ClearVector();
    }
  }

  public sealed class TargetingArrowGraphic:MaskableGraphic
  {
    private Vector2 from;
    private Vector2 to;
    private int maximumSegments;
    private float animationTime;
    private bool hasVector;

    public int RenderedSegmentCount { get; private set; }

    public void SetVector(Vector2 start,Vector2 end,int segmentLimit,float time)
    {
      from=start;to=end;maximumSegments=Mathf.Max(1,segmentLimit);animationTime=time;hasVector=true;SetVerticesDirty();
    }

    public void ClearVector()
    {
      if(!hasVector&&RenderedSegmentCount==0)return;hasVector=false;RenderedSegmentCount=0;SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
      vh.Clear();RenderedSegmentCount=0;if(!hasVector)return;
      var delta=to-from;var distance=delta.magnitude;if(distance<42f)return;
      var direction=delta/distance;var perpendicular=new Vector2(-direction.y,direction.x);
      const float startInset=20f;const float endInset=44f;const float spacing=9f;
      var usable=Mathf.Max(0,distance-startInset-endInset);var count=Mathf.Clamp(Mathf.CeilToInt(usable/spacing),0,maximumSegments);
      var dark=new Color32(70,0,0,245);var red=new Color32(255,30,12,255);var orange=new Color32(255,98,22,255);
      for(var i=0;i<count;i++)
      {
        var along=startInset+(i+.5f)*spacing;if(along>distance-endInset)break;
        var progress=Mathf.Clamp01(along/distance);var pulse=.5f+.5f*Mathf.Sin(animationTime*8f-progress*5f);
        var width=Mathf.Lerp(13f,8f,progress);var center=from+direction*along;
        AddQuad(vh,center,direction,perpendicular,spacing+3f,width+5f,dark);AddQuad(vh,center,direction,perpendicular,spacing+2f,width,Color32.Lerp(red,orange,pulse*.35f));RenderedSegmentCount++;
      }
      var tip=to-direction*12f;var baseCenter=tip-direction*31f;
      AddTriangle(vh,tip+direction*3f,baseCenter+perpendicular*20f,baseCenter-perpendicular*20f,dark);
      AddTriangle(vh,tip,baseCenter+direction*5f+perpendicular*13f,baseCenter+direction*5f-perpendicular*13f,red);
    }

    private static void AddQuad(VertexHelper vh,Vector2 center,Vector2 direction,Vector2 perpendicular,float length,float width,Color32 tint)
    {
      var forward=direction*(length*.5f);var side=perpendicular*(width*.5f);var index=vh.currentVertCount;
      AddVertex(vh,center-forward-side,tint);AddVertex(vh,center-forward+side,tint);AddVertex(vh,center+forward+side,tint);AddVertex(vh,center+forward-side,tint);
      vh.AddTriangle(index,index+1,index+2);vh.AddTriangle(index,index+2,index+3);
    }

    private static void AddTriangle(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Color32 tint)
    {
      var index=vh.currentVertCount;AddVertex(vh,a,tint);AddVertex(vh,b,tint);AddVertex(vh,c,tint);vh.AddTriangle(index,index+1,index+2);
    }

    private static void AddVertex(VertexHelper vh,Vector2 position,Color32 tint)
    {
      var vertex=UIVertex.simpleVert;vertex.position=position;vertex.color=tint;vertex.uv0=Vector2.zero;vh.AddVert(vertex);
    }
  }
}
