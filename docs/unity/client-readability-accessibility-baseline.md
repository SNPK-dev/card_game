# Unity Client Readability And Accessibility Baseline

## RC Baseline

The current generated/in-repo theme targets readable gameplay at 1920x1080, 1366x768, and 1280x720. Formal accessibility certification is future work.

## Required Checks

- Card title/type/cost/stat/rules slots are non-empty when server projection exposes values.
- Opponent hidden cards remain backs/counts only.
- Target rings and choice modal are visually distinct.
- Error/reconnect/replay states are readable and do not use stack traces as primary UX.
- Diagnostics are available but secondary.
- Audio mute is available.
- Master volume is available.
- Category volume is available in the audio settings model.
- Animation fallback exists for unknown events; reduced-motion remains future work unless product scope requires it.
- Event feed bounded.
- Presentation queue bounded.

## Validation

```bash
bash scripts/unity/run-unity-client-quality-gates.sh
```
