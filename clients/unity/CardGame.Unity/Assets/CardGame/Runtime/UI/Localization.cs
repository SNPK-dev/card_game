using System;
using System.Collections.Generic;
using UnityEngine;

namespace CardGame.Unity.Client.UI
{
  public sealed class LocalizationService
  {
    public const string LocaleKey = "cardgame.locale";
    private readonly Dictionary<string, Dictionary<string, string>> values = new(StringComparer.Ordinal)
    {
      ["en"] = new(StringComparer.Ordinal)
      {
        ["brand"]="CHRONICLE FORGE", ["tagline"]="Shape legends. Master the board.", ["play"]="PLAY", ["collection"]="COLLECTION", ["forge"]="CARD FORGE", ["decks"]="DECKS", ["replays"]="REPLAYS", ["settings"]="SETTINGS", ["profile"]="PROFILE",
        ["login.title"]="RETURN TO THE OBSERVATORY", ["register.title"]="BECOME A WAYFINDER", ["email"]="Email", ["password"]="Password", ["displayName"]="Display name", ["enter"]="ENTER", ["createAccount"]="CREATE ACCOUNT", ["haveAccount"]="I ALREADY HAVE AN ACCOUNT",
        ["back"]="BACK", ["logout"]="LOG OUT", ["language"]="LANGUAGE", ["theme"]="VISUAL THEME", ["local"]="LOCAL", ["openai"]="OPENAI CONNECTOR", ["generate"]="GENERATE 15 CARDS", ["saveDeck"]="SAVE 30-CARD DECK", ["continue"]="CONTINUE", ["playBot"]="PLAY VS BOT", ["refresh"]="REFRESH",
        ["collection.empty"]="Your collection awaits its first legend.", ["forge.subtitle"]="Describe a card set. Every result is validated before publication.", ["deck.subtitle"]="Build exactly 30 cards. Maximum two copies per card.", ["replay.subtitle"]="Verified playback from the canonical command log.", ["settings.subtitle"]="Presentation settings never affect gameplay or replay hashes.", ["loading"]="The archive is waking…"
      },
      ["ru"] = new(StringComparer.Ordinal)
      {
        ["brand"]="КУЗНИЦА ХРОНИК", ["tagline"]="Создавай легенды. Покори игровую доску.", ["play"]="ИГРАТЬ", ["collection"]="КОЛЛЕКЦИЯ", ["forge"]="КУЗНИЦА КАРТ", ["decks"]="КОЛОДЫ", ["replays"]="ПОВТОРЫ", ["settings"]="НАСТРОЙКИ", ["profile"]="ПРОФИЛЬ",
        ["login.title"]="ВЕРНУТЬСЯ В ОБСЕРВАТОРИЮ", ["register.title"]="СТАТЬ ПУТЕВОДИТЕЛЕМ", ["email"]="Электронная почта", ["password"]="Пароль", ["displayName"]="Имя игрока", ["enter"]="ВОЙТИ", ["createAccount"]="СОЗДАТЬ АККАУНТ", ["haveAccount"]="У МЕНЯ УЖЕ ЕСТЬ АККАУНТ",
        ["back"]="НАЗАД", ["logout"]="ВЫЙТИ", ["language"]="ЯЗЫК", ["theme"]="ВИЗУАЛЬНАЯ ТЕМА", ["local"]="ЛОКАЛЬНО", ["openai"]="КОННЕКТОР OPENAI", ["generate"]="СОЗДАТЬ 15 КАРТ", ["saveDeck"]="СОХРАНИТЬ КОЛОДУ ИЗ 30 КАРТ", ["continue"]="ПРОДОЛЖИТЬ", ["playBot"]="ИГРАТЬ ПРОТИВ БОТА", ["refresh"]="ОБНОВИТЬ",
        ["collection.empty"]="Ваша коллекция ждёт первую легенду.", ["forge.subtitle"]="Опишите набор карт. Каждый результат проходит проверку перед публикацией.", ["deck.subtitle"]="Соберите ровно 30 карт. Не более двух копий каждой карты.", ["replay.subtitle"]="Проверенное воспроизведение из канонического журнала команд.", ["settings.subtitle"]="Настройки отображения не влияют на игровой процесс и хеши повторов.", ["loading"]="Архив пробуждается…"
      }
    };

    public string Locale { get; private set; }
    public event Action Changed;

    public LocalizationService() => Locale = Normalize(PlayerPrefs.GetString(LocaleKey, "en"));

    public string Text(string key)
    {
      if (values.TryGetValue(Locale, out var locale) && locale.TryGetValue(key, out var localized)) return localized;
      return values["en"].TryGetValue(key, out var fallback) ? fallback : string.Empty;
    }

    public void SetLocale(string locale)
    {
      var normalized = Normalize(locale);
      if (normalized == Locale) return;
      Locale = normalized; PlayerPrefs.SetString(LocaleKey, Locale); PlayerPrefs.Save(); Changed?.Invoke();
    }

    private static string Normalize(string locale) => string.Equals(locale, "ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";
  }
}
