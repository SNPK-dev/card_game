using System;
using System.Collections.Generic;
using UnityEngine;

namespace CardGame.Unity.Client.UI.Theme
{
  public sealed class GameThemePalette
  {
    public string Background { get; set; } = "#16181D";
    public string BoardSurface { get; set; } = "#25312D";
    public string CardFace { get; set; } = "#F1E3C3";
    public string CardBack { get; set; } = "#243E63";
    public string TextPrimary { get; set; } = "#F7F2E8";
    public string TextOnCard { get; set; } = "#211B15";
    public string Playable { get; set; } = "#D8A942";
    public string Target { get; set; } = "#4BA3FF";
    public string Disabled { get; set; } = "#555B63";
    public string Damage { get; set; } = "#D94F45";
    public string Heal { get; set; } = "#54B879";
    public string Connection { get; set; } = "#41B6A6";
    public string Fault { get; set; } = "#B8363A";
    public string Panel { get; set; } = "#16181DE8";
    public string PanelRaised { get; set; } = "#252932F2";
    public string Border { get; set; } = "#D8A942";
    public string Mana { get; set; } = "#4BA3FF";
    public string Armor { get; set; } = "#AFC7D8";
  }

  public sealed class GameThemeTypography
  {
    public int HeaderSize { get; set; } = 18;
    public int BodySize { get; set; } = 12;
    public int CardTitleSize { get; set; } = 13;
    public int CardRulesSize { get; set; } = 10;
    public int BadgeSize { get; set; } = 10;
  }

  public sealed class GameThemeSpacing
  {
    public int Unit { get; set; } = 8;
    public int CardWidth { get; set; } = 128;
    public int CardHeight { get; set; } = 178;
    public int BoardEntityWidth { get; set; } = 116;
    public int BoardEntityHeight { get; set; } = 96;
    public int PanelPadding { get; set; } = 12;
  }

  public sealed class GameThemeSprites
  {
    public string CardBackSpriteRef { get; set; } = "Assets/CardGame/Art/Cards/generated-card-back.asset";
    public string CardFrameSpriteRef { get; set; } = "Assets/CardGame/Art/UI/generated-card-frame.asset";
    public string TargetRingSpriteRef { get; set; } = "Assets/CardGame/Art/UI/generated-target-ring.asset";
    public string BoardLaneSpriteRef { get; set; } = "Assets/CardGame/Art/UI/generated-board-lane.asset";
    public string CardFrameResource { get; set; } = "CardFrames/frame-arcane-v1";
    public string ParticleResource { get; set; } = "PresentationVfx/arcane-compass-v1";
  }

  public sealed class GameThemeAudio
  {
    public string ButtonClickCue { get; set; } = "ui.button.click";
    public string CommandSubmitCue { get; set; } = "ui.command.submit";
    public string FaultCue { get; set; } = "fault.blocking";
  }

  public sealed class GameTheme
  {
    public const string CurrentVersion = "semantic-themes-v2";

    public string Version { get; set; } = CurrentVersion;
    public string Id { get; set; } = "arcane";
    public string DisplayName { get; set; } = "Arcane Foundry";
    public string BoardTextureResource { get; set; } = "ThemeBoards/board-arcane-v1";
    public GameThemePalette Palette { get; set; } = new();
    public GameThemeTypography Typography { get; set; } = new();
    public GameThemeSpacing Spacing { get; set; } = new();
    public GameThemeSprites Sprites { get; set; } = new();
    public GameThemeAudio Audio { get; set; } = new();

    public static GameTheme CreateDefault() => CreateArcane();

    public static GameTheme CreateArcane() => new();

    public static GameTheme CreateCyberpunk() => new()
    {
      Id = "cyberpunk",
      DisplayName = "Neon Nexus",
      BoardTextureResource = "ThemeBoards/board-cyberpunk-v1",
      Sprites = new GameThemeSprites { CardFrameResource = "CardFrames/frame-cyberpunk-v1", ParticleResource = "PresentationVfx/arcane-compass-v1" },
      Palette = new GameThemePalette
      {
        Background = "#05070D",
        BoardSurface = "#101727",
        CardFace = "#DDEAF2",
        CardBack = "#111A35",
        TextPrimary = "#EAFBFF",
        TextOnCard = "#09111D",
        Playable = "#00E5FF",
        Target = "#B84DFF",
        Disabled = "#4C5266",
        Damage = "#FF3D71",
        Heal = "#30F2A0",
        Connection = "#00E5FF",
        Fault = "#FF245F",
        Panel = "#080D18E8",
        PanelRaised = "#101A2CF2",
        Border = "#00D9FF",
        Mana = "#00B8FF",
        Armor = "#A56BFF"
      }
    };

    public static GameTheme CreateZen() => new()
    {
      Id = "zen",
      DisplayName = "Quiet Garden",
      BoardTextureResource = "ThemeBoards/board-zen-v1",
      Sprites = new GameThemeSprites { CardFrameResource = "CardFrames/frame-zen-v1", ParticleResource = "PresentationVfx/arcane-compass-v1" },
      Palette = new GameThemePalette
      {
        Background = "#111713",
        BoardSurface = "#263229",
        CardFace = "#E9E2D0",
        CardBack = "#344A42",
        TextPrimary = "#F2EFE6",
        TextOnCard = "#232820",
        Playable = "#D7B66D",
        Target = "#8FB7A1",
        Disabled = "#646A62",
        Damage = "#B85C50",
        Heal = "#78A878",
        Connection = "#8FB7A1",
        Fault = "#A84343",
        Panel = "#18211BE8",
        PanelRaised = "#28332BF2",
        Border = "#C9B77C",
        Mana = "#7EA7A0",
        Armor = "#CDD2C5"
      }
    };
  }

  public sealed class GameThemeCatalog
  {
    private readonly IReadOnlyList<GameTheme> themes;

    public GameThemeCatalog()
    {
      themes = new[] { GameTheme.CreateArcane(), GameTheme.CreateCyberpunk(), GameTheme.CreateZen() };
    }

    public IReadOnlyList<GameTheme> Themes => themes;

    public GameTheme Find(string id)
    {
      foreach (var theme in themes)
      {
        if (string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase))
        {
          return theme;
        }
      }

      return themes[0];
    }

    public GameTheme Next(string id)
    {
      for (var index = 0; index < themes.Count; index++)
      {
        if (string.Equals(themes[index].Id, id, StringComparison.OrdinalIgnoreCase))
        {
          return themes[(index + 1) % themes.Count];
        }
      }

      return themes[0];
    }
  }

  public sealed class ThemedSurfaceStyle
  {
    public string BackgroundColor { get; set; } = string.Empty;
    public string AccentColor { get; set; } = string.Empty;
    public string TextColor { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
  }

  public static class GameThemeStyles
  {
    public static ThemedSurfaceStyle CardFace(GameTheme theme, bool playable, bool disabled) =>
      new()
      {
        BackgroundColor = theme?.Palette?.CardFace ?? string.Empty,
        AccentColor = disabled ? theme?.Palette?.Disabled ?? string.Empty : playable ? theme?.Palette?.Playable ?? string.Empty : theme?.Palette?.BoardSurface ?? string.Empty,
        TextColor = theme?.Palette?.TextOnCard ?? string.Empty,
        State = disabled ? "disabled" : playable ? "playable" : "normal"
      };

    public static ThemedSurfaceStyle CardBack(GameTheme theme) =>
      new()
      {
        BackgroundColor = theme?.Palette?.CardBack ?? string.Empty,
        AccentColor = theme?.Palette?.Connection ?? string.Empty,
        TextColor = theme?.Palette?.TextPrimary ?? string.Empty,
        State = "hidden"
      };

    public static ThemedSurfaceStyle Entity(GameTheme theme, bool damaged, bool highlighted) =>
      new()
      {
        BackgroundColor = theme?.Palette?.BoardSurface ?? string.Empty,
        AccentColor = highlighted ? theme?.Palette?.Target ?? string.Empty : damaged ? theme?.Palette?.Damage ?? string.Empty : theme?.Palette?.Playable ?? string.Empty,
        TextColor = theme?.Palette?.TextPrimary ?? string.Empty,
        State = highlighted ? "target-candidate" : damaged ? "damaged" : "normal"
      };
  }

  public static class ThemeColorParser
  {
    public static Color ParseOrWhite(string htmlColor)
    {
      return ColorUtility.TryParseHtmlString(htmlColor, out var color) ? color : Color.white;
    }
  }
}
