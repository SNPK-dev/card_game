# Unity Client Visual Direction

## Status

The polished gameplay stage uses an intentional generated in-repo visual system. It is not final commissioned production art.

## Theme

Theme version: `polished-generated-v1`.

The client uses a tactical tabletop palette:

- dark neutral background and board surfaces;
- warm gold playable highlights;
- blue target rings;
- red damage/fault states;
- green heal states;
- teal connection/replay states;
- high-contrast card-face text.

## Generated Assets

Generated sprite references are deterministic placeholder refs under:

```text
Assets/CardGame/Art/Generated/
Assets/CardGame/Art/UI/
Assets/CardGame/Art/Cards/
```

The current implementation stores the generation plan and theme refs in code rather than importing external image files. Future art can replace the refs without changing gameplay logic.

## UI Treatment

Cards expose face/back/frame/cost/stats/rules/keyword/glow/disabled overlay view models. Opponent hand and deck views remain hidden-info safe. Board entities expose deterministic sort keys, damage badges, keyword badges, and target rings. Diagnostics remain available but secondary.

## Limitations

The visual system is clean and intentional, but still generated/in-repo placeholder presentation. Production illustration, VFX, and accessibility art review remain future work.
