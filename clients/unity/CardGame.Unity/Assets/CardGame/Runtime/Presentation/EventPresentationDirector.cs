using System;
using System.Collections.Generic;
using System.Linq;
using Game.UnityClient.Contracts;

namespace CardGame.Unity.Client.Presentation
{
  public enum EventChoreography
  {
    QuietRefresh, MatchOpening, TurnRibbon, CardFlight, DrawArc, SummonBurst, DamageImpact,
    HealBloom, DeathDissolve, DeathrattleEcho, SecretReveal, QuestProgress, StatRise, StatFall,
    KeywordSigil, AuraWave, ChoiceFan, EquipmentForge, LocationPulse, HeroTransform, HeroPower,
    RandomResolve, Victory, Defeat, Fault
  }

  public sealed class EventPresentationDescriptor
  {
    public string EventType { get; set; } = string.Empty;
    public EventChoreography Choreography { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Glyph { get; set; } = "✦";
    public string Accent { get; set; } = "#46D8FF";
    public string SourceRef { get; set; } = string.Empty;
    public string TargetRef { get; set; } = string.Empty;
    public int? Amount { get; set; }
    public bool ShowBanner { get; set; }
    public bool IsGeneric { get; set; }
    public string AnimationSignature { get; set; } = string.Empty;
  }

  public sealed class EventPresentationDirector
  {
    public EventPresentationDescriptor Resolve(PublicMatchEventRecord evt, bool russian = false)
    {
      var type = (evt?.EventType ?? string.Empty).ToUpperInvariant();
      var mutation = StringPayload(evt, "mutationType").ToUpperInvariant();
      var amount = MeaningfulAmount(evt);
      var descriptor = type switch
      {
        "GAME_STARTED" => D(type, EventChoreography.MatchOpening, T(russian,"Battle begins","Битва начинается"), T(russian,"The first hand is dealt","Стартовая рука роздана"), "⚔", "#E9B949", true),
        "TURN_STARTED" => D(type, EventChoreography.TurnRibbon, T(russian,"Your turn","Ваш ход"), T(russian,"Plan your move","Продумайте действие"), "◆", "#55D8FF", true),
        "EXTRA_TURN_SCHEDULED" => D(type, EventChoreography.TurnRibbon, T(russian,"Time bends","Время изгибается"), T(russian,"An extra turn awaits","Назначен дополнительный ход"), "∞", "#B88CFF", true),
        "TURN_ENDED" => D(type, EventChoreography.QuietRefresh, string.Empty, string.Empty, "", "#55D8FF"),
        "CARD_PLAYED" => D(type, EventChoreography.CardFlight, T(russian,"Card played","Карта разыграна"), string.Empty, "✦", "#E7B64C"),
        "CARD_DRAWN" => D(type, EventChoreography.DrawArc, T(russian,"Card drawn","Карта получена"), string.Empty, "▰", "#3FA9FF"),
        "CARD_MOVED_ZONE" => D(type, EventChoreography.CardFlight, string.Empty, string.Empty, "↗", "#3FA9FF"),
        "CARD_DISCARDED" => D(type, EventChoreography.DeathDissolve, T(russian,"Discarded","Сброшено"), string.Empty, "◇", "#88748F"),
        "CARD_RETURNED_TO_HAND" => D(type, EventChoreography.DrawArc, T(russian,"Returned","Возвращено"), T(russian,"Back to hand","Снова в руке"), "↶", "#61B8FF"),
        "MINION_SUMMONED" => D(type, EventChoreography.SummonBurst, T(russian,"Summoned","Призыв"), T(russian,"A new ally enters the board","Существо выходит на поле"), "✦", "#E7B64C"),
        "ENTITY_DAMAGED" => D(type, EventChoreography.DamageImpact, amount.HasValue?$"−{Math.Abs(amount.Value)}":T(russian,"Damage","Урон"), T(russian,"Health lost","Потеря здоровья"), "✹", "#FF3E32"),
        "DAMAGE_WOULD_BE_DEALT" => D(type, EventChoreography.KeywordSigil, T(russian,"Incoming damage","Входящий урон"), T(russian,"A protection effect may respond","Защитный эффект может сработать"), "◈", "#FF8A3D"),
        "REPLACEMENT_RESOLVED" => Replacement(evt, russian),
        "ENTITY_HEALED" => D(type, EventChoreography.HealBloom, amount.HasValue?$"+{Math.Abs(amount.Value)}":T(russian,"Restored","Исцеление"), T(russian,"Health restored","Здоровье восстановлено"), "+", "#4DFF88"),
        "ENTITY_DESTROYED" => D(type, EventChoreography.DeathDissolve, T(russian,"Destroyed","Уничтожено"), T(russian,"Removed from the battlefield","Покидает поле боя"), "✕", "#A57A74"),
        "ENTITY_DIED" => D(type, EventChoreography.DeathDissolve, T(russian,"Fallen","Погибло"), T(russian,"Death effects now resolve","Срабатывают эффекты смерти"), "☠", "#8D72A8"),
        "DEATHRATTLE_TRIGGERED" => D(type, EventChoreography.DeathrattleEcho, T(russian,"Deathrattle","Предсмертный хрип"), T(russian,"A final effect awakens","Пробуждается последний эффект"), "☠", "#B98CFF", true),
        "SECRET_REVEALED" => D(type, EventChoreography.SecretReveal, T(russian,"Secret revealed!","Секрет раскрыт!"), T(russian,"The hidden trap is sprung","Скрытая ловушка сработала"), "?", "#FF5BC8", true),
        "QUEST_PROGRESS_CHANGED" => D(type, EventChoreography.QuestProgress, T(russian,"Quest progress","Прогресс задания"), amount.HasValue?$"{amount.Value}":T(russian,"Objective advanced","Цель продвинулась"), "◆", "#FFD35A"),
        "MODIFIER_APPLIED" => Modifier(type, mutation, amount, russian, true),
        "MODIFIER_EXPIRED" => Modifier(type, mutation, amount, russian, false),
        "AURA_REFRESHED" => D(type, EventChoreography.AuraWave, T(russian,"Aura renewed","Аура обновлена"), T(russian,"Continuous effects recalculated","Постоянные эффекты пересчитаны"), "◉", "#62E6D5"),
        "MANA_CHANGED" => D(type, EventChoreography.QuietRefresh, string.Empty, string.Empty, "", "#3D8CFF"),
        "WEAPON_EQUIPPED" => D(type, EventChoreography.EquipmentForge, T(russian,"Weapon equipped","Оружие экипировано"), T(russian,"Ready to strike","Готово к атаке"), "⚔", "#FF9F43", true),
        "LOCATION_PLAYED" => D(type, EventChoreography.LocationPulse, T(russian,"Location opened","Локация разыграна"), T(russian,"A new battlefield power is ready","Новая сила поля готова"), "⌂", "#5DE0C0"),
        "LOCATION_ACTIVATED" => D(type, EventChoreography.LocationPulse, T(russian,"Location activated","Локация активирована"), string.Empty, "⌂", "#5DE0C0"),
        "HERO_CARD_PLAYED" => D(type, EventChoreography.HeroTransform, T(russian,"Hero transformed","Герой преобразился"), T(russian,"Armor and powers renewed","Броня и силы обновлены"), "♛", "#FFD05A", true),
        "HERO_POWER_USED" => D(type, EventChoreography.HeroPower, T(russian,"Hero power","Сила героя"), T(russian,"Power unleashed","Сила применена"), "✧", "#60C8FF"),
        "HERO_POWER_REPLACED" => D(type, EventChoreography.HeroTransform, T(russian,"New hero power","Новая сила героя"), string.Empty, "♛", "#FFD05A"),
        "CHOICE_CREATED" => D(type, EventChoreography.ChoiceFan, T(russian,"Choose","Сделайте выбор"), T(russian,"Select one of the offered outcomes","Выберите один из вариантов"), "◇", "#59C7FF", true),
        "CHOICE_RESOLVED" => D(type, EventChoreography.ChoiceFan, T(russian,"Choice made","Выбор сделан"), string.Empty, "✓", "#59C7FF"),
        "RNG_ROLLED" => D(type, EventChoreography.RandomResolve, string.Empty, string.Empty, "✣", "#9A8CFF"),
        "GAME_ENDED" => D(type, EventChoreography.Victory, T(russian,"Match complete","Матч завершён"), T(russian,"The battle is decided","Исход битвы решён"), "♛", "#FFD05A", true),
        "ENGINE_FAULTED" => D(type, EventChoreography.Fault, T(russian,"Match interrupted","Матч прерван"), T(russian,"The authoritative engine stopped safely","Игровой движок безопасно остановлен"), "!", "#FF3030", true),
        _ => new EventPresentationDescriptor { EventType=type, Choreography=EventChoreography.QuietRefresh, IsGeneric=true }
      };
      descriptor.Amount = amount;
      descriptor.SourceRef = evt?.SourceRefs?.FirstOrDefault()?.RefId ?? string.Empty;
      descriptor.TargetRef = evt?.TargetRefs?.FirstOrDefault()?.RefId ?? string.Empty;
      descriptor.AnimationSignature = descriptor.IsGeneric
        ? string.Empty
        : type + (string.IsNullOrWhiteSpace(mutation) ? string.Empty : ":" + mutation);
      return descriptor;
    }

    public static IReadOnlyCollection<string> CoveredEventTypes { get; } = new[]
    {
      "GAME_STARTED","TURN_STARTED","EXTRA_TURN_SCHEDULED","TURN_ENDED","CARD_PLAYED","CARD_MOVED_ZONE","CARD_DRAWN","CARD_DISCARDED","CARD_RETURNED_TO_HAND","MINION_SUMMONED","ENTITY_DAMAGED","DAMAGE_WOULD_BE_DEALT","REPLACEMENT_RESOLVED","ENTITY_HEALED","MANA_CHANGED","ENTITY_DESTROYED","ENTITY_DIED","DEATHRATTLE_TRIGGERED","SECRET_REVEALED","QUEST_PROGRESS_CHANGED","MODIFIER_APPLIED","MODIFIER_EXPIRED","AURA_REFRESHED","WEAPON_EQUIPPED","LOCATION_PLAYED","LOCATION_ACTIVATED","HERO_CARD_PLAYED","HERO_POWER_USED","HERO_POWER_REPLACED","CHOICE_CREATED","CHOICE_RESOLVED","RNG_ROLLED","GAME_ENDED","ENGINE_FAULTED"
    };

    private static EventPresentationDescriptor Modifier(string type,string mutation,int? amount,bool ru,bool applied)
    {
      if(mutation.Contains("ATTACK"))return D(type,amount.GetValueOrDefault()>=0?EventChoreography.StatRise:EventChoreography.StatFall,amount.HasValue?$"{(amount.Value>=0?"+":"")}{amount.Value} {T(ru,"Attack","Атака")}":T(ru,"Attack changed","Атака изменена"),applied?T(ru,"Power increased","Сила изменена"):T(ru,"Effect faded","Эффект завершён"),"⚔",amount.GetValueOrDefault()>=0?"#FFB347":"#C36BFF");
      if(mutation.Contains("HEALTH"))return D(type,amount.GetValueOrDefault()>=0?EventChoreography.StatRise:EventChoreography.StatFall,amount.HasValue?$"{(amount.Value>=0?"+":"")}{amount.Value} {T(ru,"Health","Здоровье")}":T(ru,"Health changed","Здоровье изменено"),string.Empty,"♥",amount.GetValueOrDefault()>=0?"#52E889":"#C36BFF");
      if(mutation.Contains("KEYWORD"))return D(type,EventChoreography.KeywordSigil,applied?T(ru,"Ability gained","Получена способность"):T(ru,"Ability faded","Способность исчезла"),HumanMutation(mutation,ru),"✦","#AA7CFF");
      if(mutation.Contains("FATIGUE"))return D(type,EventChoreography.StatFall,T(ru,"Fatigue grows","Усталость растёт"),string.Empty,"☠","#9C6A8F");
      return D(type,applied?EventChoreography.StatRise:EventChoreography.StatFall,applied?T(ru,"Effect applied","Эффект применён"):T(ru,"Effect ended","Эффект завершён"),HumanMutation(mutation,ru),"✦",applied?"#66E2A3":"#9571B8");
    }

    private static EventPresentationDescriptor Replacement(PublicMatchEventRecord evt,bool ru)
    {
      var result=StringPayload(evt,"result").ToUpperInvariant();
      return result.Contains("CANCEL")||result.Contains("PREVENT")
        ?D("REPLACEMENT_RESOLVED",EventChoreography.KeywordSigil,T(ru,"Damage prevented","Урон предотвращён"),T(ru,"A protective effect absorbed the impact","Защитный эффект поглотил удар"),"◈","#55D8FF",true)
        :D("REPLACEMENT_RESOLVED",EventChoreography.KeywordSigil,T(ru,"Effect redirected","Эффект перенаправлен"),string.Empty,"↝","#B78CFF");
    }

    private static EventPresentationDescriptor D(string type,EventChoreography choreography,string title,string subtitle,string glyph,string accent,bool banner=false)=>new(){EventType=type,Choreography=choreography,Title=title,Subtitle=subtitle,Glyph=glyph,Accent=accent,ShowBanner=banner};
    private static string T(bool ru,string en,string russian)=>ru?russian:en;
    private static string HumanMutation(string mutation,bool ru)=>string.IsNullOrWhiteSpace(mutation)?string.Empty:mutation switch{"ADD_KEYWORD"=>T(ru,"New keyword","Новое свойство"),"REMOVE_KEYWORD"=>T(ru,"Keyword removed","Свойство удалено"),"APPLY_MODIFIER"=>T(ru,"Ongoing effect","Продолжительный эффект"),_=>string.Empty};
    private static string StringPayload(PublicMatchEventRecord evt,string key)=>evt?.Payload?.FirstOrDefault(x=>string.Equals(x.Key,key,StringComparison.OrdinalIgnoreCase))?.Value?.StringValue??string.Empty;
    private static int? MeaningfulAmount(PublicMatchEventRecord evt)
    {
      var preferred=new[]{"amount","delta","damage","healing","attackDelta","healthDelta","manaDelta","progress","newValue"};
      foreach(var key in preferred){var found=evt?.Payload?.FirstOrDefault(x=>string.Equals(x.Key,key,StringComparison.OrdinalIgnoreCase))?.Value?.IntValue;if(found.HasValue)return found;}
      return evt?.Payload?.FirstOrDefault(x=>x.Value?.IntValue!=null)?.Value?.IntValue;
    }
  }
}
