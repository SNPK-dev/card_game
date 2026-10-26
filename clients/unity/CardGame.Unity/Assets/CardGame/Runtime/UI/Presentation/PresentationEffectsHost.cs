using System;
using System.Collections;
using System.Collections.Generic;
using CardGame.Unity.Client.Audio;
using CardGame.Unity.Client.Presentation;
using CardGame.Unity.Client.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace CardGame.Unity.Client.UI.Presentation
{
  public sealed class PresentationEffectsHost : MonoBehaviour
  {
    private readonly Dictionary<string, AudioClip> clips = new(StringComparer.OrdinalIgnoreCase);
    private Func<string,Vector3?> anchorResolver;
    private CanvasGroup layer;
    private Image flash;
    private Image ring;
    private Image beam;
    private RawImage authoredDecal;
    private Text banner;
    private Text glyph;
    private Text subtitle;
    private readonly List<Image> particles = new();
    private AudioSource audioSource;
    private Coroutine active;
    private readonly Queue<PendingEffect> pending = new();
    private const int MaximumPendingEffects = 12;

    private sealed class PendingEffect
    {
      public PresentationIntent Intent;
      public AudioPlaybackRequest Audio;
      public bool ReducedMotion;
      public EventPresentationDescriptor Descriptor;
    }

    public int PendingCount=>pending.Count;
    public bool IsPlaying=>active!=null;
    public int DroppedLowPriorityCount { get; private set; }

    public static PresentationEffectsHost Attach(Transform parent)
    {
      if (parent == null) return null;
      var node = new GameObject("Presentation Effects Host");
      node.transform.SetParent(parent, false);
      var rect = node.AddComponent<RectTransform>();
      rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
      var host = node.AddComponent<PresentationEffectsHost>();
      host.Initialize();
      return host;
    }

    public void SetAnchorResolver(Func<string,Vector3?> resolver)=>anchorResolver=resolver;

    private void Awake() => Initialize();

    private void Initialize()
    {
      if (layer != null) return;
      layer = gameObject.GetComponent<CanvasGroup>();
      if (layer == null) layer = gameObject.AddComponent<CanvasGroup>();
      layer.blocksRaycasts = false; layer.interactable = false;
      flash = CreateImage("Event Flash", transform, Vector2.zero, Vector2.one);
      flash.raycastTarget = false;
      ring = CreateImage("Event Pulse", transform, new Vector2(.39f, .32f), new Vector2(.61f, .68f));
      ring.raycastTarget = false; ring.sprite=RadialSprite(true); ring.preserveAspect=true;
      beam = CreateImage("Directed Event Beam", transform, new Vector2(.45f,.47f), new Vector2(.55f,.53f)); beam.raycastTarget=false;
      var decalNode=new GameObject("Authored Mechanic Decal");decalNode.transform.SetParent(transform,false);
      var decalRect=decalNode.AddComponent<RectTransform>();decalRect.anchorMin=decalRect.anchorMax=new Vector2(.5f,.5f);decalRect.sizeDelta=new Vector2(250,250);
      authoredDecal=decalNode.AddComponent<RawImage>();
      var selectedTheme=new GameThemeCatalog().Find(PlayerPrefs.GetString("cardgame.board-theme","arcane"));
      authoredDecal.texture=Resources.Load<Texture2D>(selectedTheme.Sprites.ParticleResource);authoredDecal.raycastTarget=false;
      banner = CreateText("Event Banner", transform);
      glyph = CreateText("Event Glyph", transform); glyph.fontSize=62;
      var glyphRect=glyph.rectTransform; glyphRect.anchorMin=new Vector2(.42f,.5f);glyphRect.anchorMax=new Vector2(.58f,.7f);
      subtitle = CreateText("Event Subtitle", transform); subtitle.fontSize=18; subtitle.fontStyle=FontStyle.Normal;
      var subtitleRect=subtitle.rectTransform;subtitleRect.anchorMin=new Vector2(.2f,.38f);subtitleRect.anchorMax=new Vector2(.8f,.45f);
      for(var i=0;i<32;i++){var particle=CreateImage("Event Particle "+i,transform,new Vector2(.5f,.5f),new Vector2(.5f,.5f));particle.rectTransform.sizeDelta=i%3==0?new Vector2(4,24):new Vector2(8+i%4*2,8+i%4*2);particle.raycastTarget=false;particle.sprite=RadialSprite(i%3!=0);particle.preserveAspect=true;particles.Add(particle);}
      audioSource = gameObject.AddComponent<AudioSource>();
      audioSource.playOnAwake = false; audioSource.spatialBlend = 0f;
      SetVisible(false);
    }

    public void Play(PresentationIntent intent, AudioPlaybackRequest audio, bool reducedMotion, EventPresentationDescriptor descriptor = null)
    {
      if (intent == null) return;
      var resolved=descriptor??new EventPresentationDirector().Resolve(null);
      // Turn ownership has a dedicated compact ribbon. Keep its audio accent but do
      // not enqueue the old full-screen wash behind normal gameplay effects.
      if(resolved.Choreography==EventChoreography.TurnRibbon)
      {
        if(audio!=null&&!audio.Suppressed)PlayTone(audio);
        return;
      }
      // Card movement/draw already has a concrete zone-transfer animation, and mana
      // changes are visible on the resource plaque. Replaying them as extra flashes
      // made a vanilla summon look like several unrelated effects.
      if(resolved.Choreography is EventChoreography.CardFlight or EventChoreography.DrawArc or EventChoreography.QuietRefresh or EventChoreography.DeathDissolve or EventChoreography.DamageImpact)
      {
        // Damage and death already have dedicated board-bound animation/audio.
        // Replaying their generic host cue creates late flashes and stacked sounds.
        if(resolved.Choreography is not (EventChoreography.DeathDissolve or EventChoreography.DamageImpact)&&audio!=null&&!audio.Suppressed)PlayTone(audio);
        return;
      }
      if((resolved.Choreography is EventChoreography.QuietRefresh or EventChoreography.RandomResolve)&&pending.Count>0)
      {
        DroppedLowPriorityCount++;
        return;
      }
      // Long trigger chains stay ordered, but low-importance refreshes cannot create an
      // ever-growing presentation tail after the authoritative state has already settled.
      if(pending.Count>=MaximumPendingEffects&&!UsesFullScreenStaging(resolved.Choreography)){DroppedLowPriorityCount++;return;}
      pending.Enqueue(new PendingEffect{Intent=intent,Audio=audio,ReducedMotion=reducedMotion,Descriptor=resolved});
      if(active==null)active=StartCoroutine(Drain());
    }

    private IEnumerator Drain()
    {
      while(pending.Count>0)
      {
        var effect=pending.Dequeue();
        if(effect.Audio!=null&&!effect.Audio.Suppressed)PlayTone(effect.Audio);
        yield return Animate(effect.Intent,effect.ReducedMotion,effect.Descriptor);
      }
      SetVisible(false);active=null;
    }

    private IEnumerator Animate(PresentationIntent intent, bool reducedMotion, EventPresentationDescriptor descriptor)
    {
      var color = ParseColor(descriptor.Accent, ColorFor(intent.IntentType));
      flash.color = new Color(color.r, color.g, color.b, 0f);
      ring.color = new Color(color.r, color.g, color.b, .8f);
      beam.color = new Color(color.r,color.g,color.b,.8f);
      banner.color = Color.white;
      banner.text = descriptor.Title;
      var major=UsesFullScreenStaging(descriptor.Choreography);
      var localScale=major?1f:.46f;
      banner.gameObject.SetActive(major&&descriptor.ShowBanner&&!string.IsNullOrWhiteSpace(descriptor.Title));
      glyph.text=descriptor.Glyph; glyph.color=color;
      subtitle.text=descriptor.Subtitle; subtitle.color=new Color(1f,1f,1f,.88f);
      SetVisible(true);
      ConfigureComposition(descriptor,color);
      var signatureHash=StableHash(descriptor.AnimationSignature);
      var signaturePhase=(signatureHash%360)*Mathf.Deg2Rad;
      var signatureDirection=(signatureHash&1)==0?1f:-1f;
      var signatureSpread=.78f+(signatureHash%11)*.055f;
      var signatureLift=((signatureHash/11)%9-4)*4.5f;
      var signatureTurns=.65f+(signatureHash%7)*.17f;
      var duration = reducedMotion ? .12f : DurationFor(descriptor.Choreography,intent.DurationSeconds);
      var elapsed = 0f;
      while (elapsed < duration)
      {
        elapsed += Time.unscaledDeltaTime;
        var t = Mathf.Clamp01(elapsed / duration);
        var pulse = Mathf.Sin(t * Mathf.PI);
        var flashStrength=major
          ? descriptor.Choreography is EventChoreography.Victory or EventChoreography.Defeat or EventChoreography.Fault ? .2f : .1f
          : 0f;
        flash.color = new Color(color.r, color.g, color.b, pulse * flashStrength);
        var inverse=descriptor.Choreography==EventChoreography.DeathDissolve;
        ring.rectTransform.localScale = Vector3.one * localScale * (inverse?Mathf.Lerp(1.35f,.3f,t):Mathf.Lerp(.58f,1.42f,t));
        ring.color = new Color(color.r, color.g, color.b, (1f - t) * .78f);
        var source=ResolveAnchor(descriptor.SourceRef,true);var target=ResolveAnchor(descriptor.TargetRef,false);
        var point=Vector2.Lerp(source,target,Ease(t));
        ring.rectTransform.anchoredPosition=major?Vector2.zero:point;
        beam.rectTransform.anchoredPosition=point;beam.rectTransform.localScale=new Vector3(Mathf.Lerp(.2f,1.8f,pulse)*localScale,.12f,1f);
        beam.color=new Color(color.r,color.g,color.b,pulse*.7f);
        authoredDecal.rectTransform.anchoredPosition=target;
        authoredDecal.rectTransform.localRotation=Quaternion.Euler(0,0,signatureDirection*(95f+signatureTurns*70f)*t);
        var decalPulse=descriptor.Choreography==EventChoreography.DamageImpact?Mathf.Lerp(.35f,1.35f,pulse):Mathf.Lerp(.45f,1.1f,t);
        authoredDecal.rectTransform.localScale=Vector3.one*decalPulse;
        authoredDecal.color=new Color(color.r,color.g,color.b,(1f-t)*.88f);
        glyph.rectTransform.anchoredPosition=(major?Vector2.zero:target)+TargetOffset(descriptor.Choreography,t)+new Vector2(Mathf.Sin(t*Mathf.PI+signaturePhase)*signatureLift*pulse,signatureLift*pulse*.35f);
        glyph.rectTransform.localScale=Vector3.one*localScale*Mathf.Lerp(.7f,descriptor.Choreography==EventChoreography.DamageImpact?1.65f:1.15f,pulse);
        glyph.color=new Color(color.r,color.g,color.b,1f-t*.45f);
        for(var i=0;i<particles.Count;i++){var lane=i%4;var angle=(i/(float)particles.Count)*Mathf.PI*2f+signaturePhase+signatureDirection*t*signatureTurns*Mathf.PI*2f+lane*.18f;var radius=Mathf.Lerp(4f,(descriptor.Choreography==EventChoreography.DeathrattleEcho?180f:88f+lane*16f)*signatureSpread*localScale,Ease(t));var drift=new Vector2(Mathf.Sin(i*12.31f+t*(6f+signatureTurns)),Mathf.Cos(i*7.17f+t*(5f+signatureTurns)))*10f*pulse*localScale;particles[i].rectTransform.anchoredPosition=point+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius+drift+new Vector2(0,signatureLift*t*localScale);particles[i].rectTransform.localRotation=Quaternion.Euler(0,0,angle*Mathf.Rad2Deg+90+t*(170f+signatureTurns*80f)*(i%2==0?signatureDirection:-signatureDirection));particles[i].color=new Color(color.r,color.g,color.b,(1f-t)*(.45f+lane*.12f));}
        banner.rectTransform.anchoredPosition = new Vector2(0f, reducedMotion ? 0f : Mathf.Lerp(-8f,14f,t));
        banner.color = new Color(1f, 1f, 1f, pulse);
        subtitle.color=new Color(1f,1f,1f,pulse*.86f);
        yield return null;
      }
      SetVisible(false);
    }

    private void PlayTone(AudioPlaybackRequest request)
    {
      if (!clips.TryGetValue(request.CueId, out var clip))
      {
        if(!string.IsNullOrWhiteSpace(request.AssetRef)&&request.AssetRef.StartsWith("resource://",StringComparison.OrdinalIgnoreCase))
          clip=Resources.Load<AudioClip>(request.AssetRef.Substring("resource://".Length));
      }
      // Missing presentation audio is silent-safe. It must never synthesize or call a provider during a match.
      if(clip==null)return;
      clips[request.CueId] = clip;
      audioSource.volume = Mathf.Clamp01(request.Volume);
      audioSource.PlayOneShot(clip);
    }

    private void SetVisible(bool visible) { if (layer != null) layer.alpha = visible ? 1f : 0f; }
    private void ConfigureComposition(EventPresentationDescriptor descriptor,Color color)
    {
      var quiet=descriptor.Choreography==EventChoreography.QuietRefresh||descriptor.Choreography==EventChoreography.RandomResolve;
      var directed=!string.IsNullOrWhiteSpace(descriptor.SourceRef)||!string.IsNullOrWhiteSpace(descriptor.TargetRef);
      var staged=UsesFullScreenStaging(descriptor.Choreography);
      // A local cue without a resolvable participant is misleading: it appears at a
      // deterministic fallback point that looks like a target. Keep only its subtitle.
      ring.gameObject.SetActive(!quiet&&(staged||directed));beam.gameObject.SetActive(!quiet&&directed);
      authoredDecal.gameObject.SetActive(!quiet&&authoredDecal.texture!=null&&descriptor.Choreography is EventChoreography.DeathrattleEcho or EventChoreography.SecretReveal or EventChoreography.QuestProgress or EventChoreography.AuraWave or EventChoreography.HeroTransform);
      glyph.gameObject.SetActive(!quiet&&(staged||directed)&&!string.IsNullOrWhiteSpace(descriptor.Glyph));subtitle.gameObject.SetActive(!quiet&&(staged||directed)&&!string.IsNullOrWhiteSpace(descriptor.Subtitle));
      var semanticParticles=descriptor.Choreography is EventChoreography.DeathrattleEcho or EventChoreography.SecretReveal or EventChoreography.QuestProgress or EventChoreography.AuraWave or EventChoreography.HeroTransform or EventChoreography.DamageImpact or EventChoreography.HealBloom or EventChoreography.DeathDissolve;
      foreach(var particle in particles)particle.gameObject.SetActive(!quiet&&(staged||directed)&&semanticParticles);
    }
    private static float DurationFor(EventChoreography choreography,float requested)=>choreography switch
    {
      EventChoreography.DeathrattleEcho or EventChoreography.SecretReveal or EventChoreography.HeroTransform=>.82f,
      EventChoreography.DeathDissolve=>.62f, EventChoreography.DamageImpact=>.38f,
      EventChoreography.QuietRefresh or EventChoreography.RandomResolve=>.08f,
      _=>Mathf.Clamp(requested,.28f,.68f)
    };
    public static bool UsesFullScreenStaging(EventChoreography choreography)=>choreography is
      EventChoreography.MatchOpening or EventChoreography.DeathrattleEcho or
      EventChoreography.SecretReveal or EventChoreography.QuestProgress or EventChoreography.HeroTransform or
      EventChoreography.Victory or EventChoreography.Defeat or EventChoreography.Fault;
    public static bool HasVisualAnchor(EventPresentationDescriptor descriptor)=>descriptor!=null&&
      (UsesFullScreenStaging(descriptor.Choreography)||!string.IsNullOrWhiteSpace(descriptor.SourceRef)||!string.IsNullOrWhiteSpace(descriptor.TargetRef));
    private static Vector2 AnchorFor(string value,bool source){var hash=StableHash(value);var x=(hash%3-1)*210f;return new Vector2(x,source?-105f:65f);}
    private Vector2 ResolveAnchor(string value,bool source)
    {
      var world=anchorResolver?.Invoke(value);
      if(world.HasValue&&transform is RectTransform rect)
      {
        var screen=RectTransformUtility.WorldToScreenPoint(null,world.Value);
        if(RectTransformUtility.ScreenPointToLocalPointInRectangle(rect,screen,null,out var local))return local;
      }
      return AnchorFor(value,source);
    }
    private static int StableHash(string value){var hash=17;foreach(var ch in value??string.Empty)hash=unchecked(hash*31+ch);return Math.Abs(hash==int.MinValue?0:hash);}
    private static float Ease(float t)=>1f-Mathf.Pow(1f-t,3f);
    private static Vector2 TargetOffset(EventChoreography choreography,float t)=>choreography switch
    {
      EventChoreography.DamageImpact=>new Vector2(Mathf.Sin(t*45f)*(1f-t)*18f,0),
      EventChoreography.HealBloom=>new Vector2(0,Mathf.Lerp(-28f,40f,t)),
      EventChoreography.DeathDissolve=>new Vector2(0,Mathf.Lerp(0,55f,t)),
      EventChoreography.DeathrattleEcho=>new Vector2(0,Mathf.Sin(t*Mathf.PI*2f)*12f),
      _=>Vector2.zero
    };
    private static Color ParseColor(string html,Color fallback)=>ColorUtility.TryParseHtmlString(html,out var color)?color:fallback;
    private static Sprite ringSprite,particleSprite;
    private static Sprite RadialSprite(bool hollow)
    {
      var cached=hollow?ringSprite:particleSprite;if(cached!=null)return cached;
      const int size=64;var texture=new Texture2D(size,size,TextureFormat.RGBA32,false){name=hollow?"Soft Mechanic Ring":"Soft Effect Particle",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
      for(var y=0;y<size;y++)for(var x=0;x<size;x++)
      {
        var distance=Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(size/2f,size/2f))/(size/2f);
        var alpha=hollow?Mathf.Clamp01(1f-Mathf.Abs(distance-.72f)*13f):Mathf.Pow(Mathf.Clamp01(1f-distance),2.2f);
        texture.SetPixel(x,y,new Color(1,1,1,alpha));
      }
      texture.Apply(false,true);cached=Sprite.Create(texture,new Rect(0,0,size,size),new Vector2(.5f,.5f),100f);
      if(hollow)ringSprite=cached;else particleSprite=cached;return cached;
    }
    private static Color ColorFor(PresentationIntentType type) => type switch
    {
      PresentationIntentType.BoardCombat => new Color(1f, .22f, .12f),
      PresentationIntentType.CardZone => new Color(.18f, .62f, 1f),
      PresentationIntentType.ModifierStatus => new Color(.72f, .28f, 1f),
      PresentationIntentType.Terminal => new Color(1f, .72f, .12f),
      PresentationIntentType.Fault => new Color(1f, .05f, .05f),
      _ => new Color(.28f, .92f, .76f)
    };
    private static Image CreateImage(string name, Transform parent, Vector2 min, Vector2 max)
    {
      var node = new GameObject(name); node.transform.SetParent(parent, false);
      var rect = node.AddComponent<RectTransform>(); rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
      return node.AddComponent<Image>();
    }
    private static Text CreateText(string name, Transform parent)
    {
      var node = new GameObject(name); node.transform.SetParent(parent, false);
      var rect = node.AddComponent<RectTransform>(); rect.anchorMin = new Vector2(.2f, .44f); rect.anchorMax = new Vector2(.8f, .58f); rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
      var text = node.AddComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.alignment = TextAnchor.MiddleCenter; text.fontSize = 34; text.fontStyle = FontStyle.Bold; text.raycastTarget = false;
      return text;
    }
  }
}
