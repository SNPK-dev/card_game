# Unity Client Media License Policy

## Allowed Sources

- `repo-generated`: deterministic placeholder refs, generated sprites/audio created inside the repo, or code-defined theme values.
- `unity-native`: Unity built-in UI primitives, materials, fonts, and editor-generated metadata allowed by the Unity project.
- `not-applicable`: docs, summaries, manifests, and validation evidence that are not media assets.

## Disallowed Without Review

- Downloaded copyrighted art/audio.
- Marketplace assets without license metadata.
- AI/generated binary art/audio without deterministic generation notes and ownership review.
- Large binary media without rationale.
- Build outputs or cache artifacts staged for commit.

## Approved Directories

```text
Assets/CardGame/Art/Generated/
Assets/CardGame/Art/UI/
Assets/CardGame/Art/Cards/
Assets/CardGame/Audio/Generated/
Assets/CardGame/Audio/UI/
Assets/CardGame/Audio/Game/
Assets/CardGame/Resources/AppShell/
Assets/CardGame/Resources/Audio/Presentation/
Assets/CardGame/Resources/CardArt/
Assets/CardGame/Resources/CardFrames/
Assets/CardGame/Resources/HeroArt/
Assets/CardGame/Resources/PresentationVfx/
Assets/CardGame/Resources/ThemeBoards/
```

Original full-resolution shell, board, portrait, card-art, frame, and presentation-decal source textures in the approved `Resources` subdirectories may be up to 3 MiB when they are listed in `client-media-inventory.md`; all other media retains the 1 MiB gate.

Run:

```bash
bash scripts/unity/check-unity-media-inventory.sh
```

The validation fails on unknown media locations, missing source/status documentation, large unexplained media, or staged build artifacts.
