using System;
using System.Collections.Generic;
using System.Linq;

namespace CardGame.Unity.Client.UI
{
  public static class CardEffectGlossary
  {
    private static readonly (string Key,string En,string Ru)[] Entries=
    {
      ("BATTLECRY","Battlecry — resolves after this card is played from hand.","Боевой клич — срабатывает после розыгрыша карты из руки."),
      ("DEATHRATTLE","Deathrattle — resolves after this minion dies.","Предсмертный хрип — срабатывает после гибели существа."),
      ("TAUNT","Taunt — enemies must attack this character first.","Провокация — противники должны сначала атаковать этого персонажа."),
      ("DIVINE SHIELD","Divine Shield — prevents the next damage instance.","Божественный щит — предотвращает следующий получаемый урон."),
      ("STEALTH","Stealth — cannot be targeted by enemies until it attacks.","Маскировка — враги не могут выбрать цель, пока она не атакует."),
      ("SECRET","Secret — hidden response revealed when its trigger occurs.","Секрет — скрытая реакция, раскрывающаяся при выполнении условия."),
      ("QUEST","Quest — progresses from committed events and grants a reward.","Задание — продвигается событиями матча и выдаёт награду."),
      ("AURA","Aura — continuously modifies valid targets while active.","Аура — постоянно изменяет подходящие цели, пока активна."),
      ("DISCOVER","Discover — choose one card from a deterministic offer.","Раскопка — выберите одну карту из предложенных вариантов."),
      ("LIFESTEAL","Lifesteal — damage dealt also restores your hero.","Похищение жизни — нанесённый урон также лечит вашего героя."),
      ("RANDOM","Random — selection uses the logged gameplay RNG stream.","Случайный выбор — использует журналируемый игровой RNG."),
      ("LOCATION","Location — can be activated while it has durability.","Локация — может активироваться, пока у неё есть прочность."),
      ("HERO POWER","Hero Power — an active ability with its own mana cost.","Сила героя — активная способность с собственной стоимостью.")
    };

    public static string Describe(string rules,IEnumerable<string> keywords,bool russian)
    {
      var source=((rules??string.Empty)+" "+string.Join(" ",keywords??Array.Empty<string>())).ToUpperInvariant().Replace("_"," ");
      if(string.IsNullOrWhiteSpace(source))return string.Empty;
      var lines=Entries.Where(entry=>source.Contains(entry.Key,StringComparison.Ordinal)).Select(entry=>russian?entry.Ru:entry.En).Distinct().ToArray();
      return lines.Length==0?(russian?"Эффект разрешается авторитетным игровым движком.":"The authoritative game engine resolves this effect."):string.Join("\n",lines);
    }
  }
}
