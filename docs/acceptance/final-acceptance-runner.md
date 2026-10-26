# Final Acceptance Runner

`AcceptanceAlgorithm.md` is the source of truth for the final backend/platform acceptance decision.

Run:

```bash
bash scripts/ai/final-acceptance.sh
```

The runner writes a timestamped report under `artifacts/final-acceptance/<timestamp>/final-acceptance-report.json`.

The report status is:

- `Accepted` only when all executed acceptance gates pass;
- `Blocked` when any gate lacks real evidence or the local environment cannot execute the required production-like checks.

## Latest Accepted Baseline


Latest accepted final acceptance evidence:

- report: `artifacts/final-acceptance/20260702-205817/final-acceptance-report.json`
- report id: `final-acceptance-20260702-205817`
- status: `Accepted`
- branch: `codex/full-flow-completion-bootstrap`
- evidence baseline commit: `3e76f3489bc89351a0b778e351103ebbf425a2b3`
- closeout tag: `unity-ready-backend-baseline-2026-07-02`

The runner intentionally does not create mock or fake provider evidence. Local provider acceptance requires these environment variables to point at running local gateways:

- `CCG_LOCAL_STRUCTURED_ENDPOINT`
- `CCG_LOCAL_STRUCTURED_MODEL`
- `CCG_LOCAL_IMAGE_ENDPOINT`
- `CCG_LOCAL_IMAGE_MODEL`
- `CCG_LOCAL_ANIMATION_ENDPOINT`
- `CCG_LOCAL_ANIMATION_MODEL`
- `CCG_LOCAL_AUDIO_ENDPOINT`
- `CCG_LOCAL_AUDIO_MODEL`

Production-like deployment acceptance requires Docker daemon access and a healthy `docker compose up --build` stack exposing `http://localhost:5000/health`.

For a fast environment/blocker audit after the normal validation suite has already run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\ai\final-acceptance.ps1 -SkipValidation
```
