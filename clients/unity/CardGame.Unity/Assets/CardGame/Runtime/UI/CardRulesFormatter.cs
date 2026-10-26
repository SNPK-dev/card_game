using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CardGame.Unity.Client.UI
{
  public static class CardRulesFormatter
  {
    private static readonly IReadOnlyDictionary<string,string> English = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
      ["Battlecry"]="BATTLECRY",["Deathrattle"]="DEATHRATTLE",["Taunt"]="TAUNT",["Divine Shield"]="DIVINE SHIELD",
      ["Lifesteal"]="LIFESTEAL",["Discover"]="DISCOVER",["Secret"]="SECRET",["Quest"]="QUEST",["Aura"]="AURA",
      ["Rush"]="RUSH",["Charge"]="CHARGE",["Windfury"]="WINDFURY",["Overkill"]="OVERKILL",["Resurrect"]="RESURRECT"
    };
    private static readonly IReadOnlyDictionary<string,string> Russian = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
      ["Боевой клич"]="БОЕВОЙ КЛИЧ",["Предсмертный хрип"]="ПРЕДСМЕРТНЫЙ ХРИП",["Провокация"]="ПРОВОКАЦИЯ",
      ["Божественный щит"]="БОЖЕСТВЕННЫЙ ЩИТ",["Похищение жизни"]="ПОХИЩЕНИЕ ЖИЗНИ",["Раскопка"]="РАСКОПКА",
      ["Секрет"]="СЕКРЕТ",["Задание"]="ЗАДАНИЕ",["Аура"]="АУРА",["Рывок"]="РЫВОК",["Натиск"]="НАТИСК",
      ["Неистовство ветра"]="НЕИСТОВСТВО ВЕТРА",["Сверхурон"]="СВЕРХУРОН",["Воскрешение"]="ВОСКРЕШЕНИЕ"
    };

    public static string Format(string rulesText, bool russian = false)
    {
      if(string.IsNullOrWhiteSpace(rulesText))return string.Empty;
      var text=Regex.Replace((russian?TranslateToRussian(rulesText):rulesText).Trim(),@"\s+"," ");
      foreach(var pair in russian?Russian:English)
        text=Regex.Replace(text,Regex.Escape(pair.Key),$"<color=#71D9FF><b>{pair.Value}</b></color>",RegexOptions.IgnoreCase);
      text=Regex.Replace(text,@"(?<=[.!?])\s+","\n");
      return text;
    }

    public static string TranslateToRussian(string text)
    {
      if(string.IsNullOrWhiteSpace(text))return "Нет дополнительных эффектов.";
      var result=text;
      result=Regex.Replace(result,@"Randomly split (\d+) damage among enemy minions\.?",m=>$"Случайным образом распределите {m.Groups[1].Value} ед. урона между вражескими миньонами.",RegexOptions.IgnoreCase);
      result=Regex.Replace(result,@"Prevent the next (\d+) damage dealt to a friendly minion\.?",m=>$"Предотвратите следующие {m.Groups[1].Value} ед. урона дружественному миньону.",RegexOptions.IgnoreCase);
      result=Regex.Replace(result,@"Resurrect a (\d+)/(\d+) friendly minion\.?",m=>$"Воскресите дружественного миньона {m.Groups[1].Value}/{m.Groups[2].Value}.",RegexOptions.IgnoreCase);
      result=Regex.Replace(result,@"Other friendly minions have \+(\d+) Attack\.?",m=>$"Другие дружественные миньоны получают +{m.Groups[1].Value} к атаке.",RegexOptions.IgnoreCase);
      result=Regex.Replace(result,@"Deal (\d+) damage to (an enemy minion|a friendly minion|a minion)\.?",m=>$"Нанесите {m.Groups[1].Value} ед. урона {RussianMinionTarget(m.Groups[2].Value)}.",RegexOptions.IgnoreCase);
      result=Regex.Replace(result,@"Restore (\d+) Health to a friendly minion\.?",m=>$"Восстановите {m.Groups[1].Value} ед. здоровья дружественному миньону.",RegexOptions.IgnoreCase);
      var phrases=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
      {
        ["Battlecry:"]="Боевой клич:", ["Deathrattle:"]="Предсмертный хрип:", ["Activate:"]="Активация:", ["damage to"]="ед. урона",
        ["Deal"]="Нанесите", ["an enemy minion"]="вражескому миньону", ["a friendly minion"]="дружественному миньону", ["a minion"]="миньону",
        ["Draw a card"]="Возьмите карту", ["Draw"]="Возьмите", ["cards"]="карты", ["card"]="карту",
        ["Restore"]="Восстановите", ["Health to"]="ед. здоровья", ["Resurrect"]="Воскресите", ["Gain"]="Получите",
        ["Armor"]="брони", ["Destroy"]="Уничтожьте", ["Taunt"]="Провокация", ["Divine Shield"]="Божественный щит",
        ["Lifesteal"]="Похищение жизни", ["Discover"]="Раскопка", ["Secret:"]="Секрет:", ["Quest:"]="Задание:", ["Aura:"]="Аура:"
      };
      foreach(var pair in phrases.OrderByDescending(x=>x.Key.Length))result=Regex.Replace(result,Regex.Escape(pair.Key),pair.Value,RegexOptions.IgnoreCase);
      return result;
    }

    private static string RussianMinionTarget(string target)=>target.ToLowerInvariant() switch
    {
      "an enemy minion"=>"вражескому миньону",
      "a friendly minion"=>"дружественному миньону",
      _=>"миньону"
    };

    public static bool ContainsRawTechnicalIdentifier(string text)=>!string.IsNullOrWhiteSpace(text)&&
      (Regex.IsMatch(text,@"\b(effect|trigger|mutation|card[-_]?version|option|source|target)[-_]?(id)?\s*[:=]?\s*[a-z0-9_-]+\b",RegexOptions.IgnoreCase)
       || Regex.IsMatch(text,@"\b[a-z]+_(instance|version|effect|mutation)_id\b",RegexOptions.IgnoreCase)
       || text.Contains("InstanceId",StringComparison.OrdinalIgnoreCase));
  }
}
