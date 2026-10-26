# Unity Client Media Inventory

## Policy Summary

Only `repo-generated`, `unity-native`, and `not-applicable` media/source categories are present for this RC. The runtime resource pack contains original repository-owned presentation assets reviewed in the packaged client; it contains no copied third-party game art or audio.

| Asset/Ref | Directory/Owner | Source | Finality | Generation Method | Notes |
| --- | --- | --- | --- | --- | --- |
| Theme palette, typography, spacing | `Assets/CardGame/Runtime/UI/Theme/` | repo-generated | placeholder | deterministic code constants | No external media file. |
| Generated sprite refs | `Assets/CardGame/Art/Generated/` | repo-generated | placeholder | deterministic ref naming | Legacy/fallback resource route; directories currently contain Unity metadata only. |
| UI/card art refs | `Assets/CardGame/Art/UI/`, `Assets/CardGame/Art/Cards/` | repo-generated | placeholder | deterministic theme refs | Legacy/fallback resource route. |
| Audio cue refs | `Assets/CardGame/Audio/Generated/`, `Assets/CardGame/Audio/UI/`, `Assets/CardGame/Audio/Game/` | repo-generated | placeholder | deterministic cue ids | Legacy/fallback resource route. |
| Application-shell backgrounds | `Assets/CardGame/Resources/AppShell/` | repo-generated | production candidate | original repository asset production | Three reviewed theme backgrounds; no external trademarks or copied trade dress. |
| Card faces and concealed card back | `Assets/CardGame/Resources/CardArt/` | repo-generated | production candidate | original repository asset production | Packaged catalog faces plus authored fallbacks and hidden-information card back. |
| Card frames | `Assets/CardGame/Resources/CardFrames/` | repo-generated | production candidate | original repository asset production | Original arcane/cyberpunk/zen frame variants. |
| Starter hero portraits | `Assets/CardGame/Resources/HeroArt/` | repo-generated | production candidate | original repository asset production | Astra and Varg identity art used by registration and match hero cards. |
| Board surfaces | `Assets/CardGame/Resources/ThemeBoards/` | repo-generated | production candidate | original repository asset production | Original full-board theme textures. |
| Presentation decal | `Assets/CardGame/Resources/PresentationVfx/` | repo-generated | production candidate | original repository asset production | Presentation-only effect texture; never enters gameplay state or hashes. |
| Presentation audio | `Assets/CardGame/Resources/Audio/Presentation/` | repo-generated | production candidate | deterministic local bake via `scripts/assets/bake_presentation_audio.py` | Original event/UI cues; no match-time generation or external audio pack. |
| Unity UI primitives | built-in Unity UI | unity-native | placeholder | Unity-native components | Used for panels/text/buttons. |
| Build/playtest summaries | `artifacts/` | not-applicable | not final media | generated local validation output | Ignored by git. |

The 1024px shell, board, portrait, card-art, frame, and presentation-decal source textures may be up to 3 MiB so their checked-in sources do not introduce visible compression artifacts; Unity platform import settings compress them for the player build. Every other media file remains limited to 1 MiB. Future external or commissioned art/audio must add explicit inventory/license entries before import.
