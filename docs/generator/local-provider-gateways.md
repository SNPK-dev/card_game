# Local Provider Gateways

This document records the local-model evidence path used before Unity client development. The gateways are local HTTP services around real local models; they are not production mocks and they do not synthesize successful artifacts when a model backend is unavailable.

## Services

Structured card generation:

```powershell
ollama serve
python scripts/local-models/gateways/ollama_structured_gateway.py 8011
```

The structured gateway has two modes:

- compatibility mode: default, returns simple normalized minion output for legacy local-provider evidence;
- production-native mode: selected by `contextRefs.generationMode=production-native`, asks Ollama for full structured DSL, runs provider-side repair attempts, and writes raw traces to `artifacts/local-provider-gateways/structured/`.

Use production-native mode through the exporter:

```bash
bash scripts/local-models/run-unity-test-card-pack.sh --mode production
```

Default endpoint: `http://127.0.0.1:8011/v1/structured-card`.
Default model: `qwen3.5:latest`.

The supported launcher starts all required gateways and refuses to report success when the Ollama model is absent:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/local-models/start-generation-gateways.ps1 -ProviderMode Local -PullMissingModel
```

`-PullMissingModel` is only needed after a cache cleanup or first installation. The first Diffusers launch downloads `stabilityai/sd-turbo`; generation remains pending/failed with an explicit provider error until the real model service is ready. It never substitutes instant fixture cards.

Static image generation:

```powershell
$env:CCG_SD_WEBUI_URL = "http://127.0.0.1:7860"
python scripts/local-models/gateways/stable_diffusion_asset_gateway.py 8012
```

Default endpoint: `http://127.0.0.1:8012/v1/assets`.
The gateway expects an AUTOMATIC1111-compatible Stable Diffusion API at `CCG_SD_WEBUI_URL`.

The repository also includes a local diffusers-backed AUTOMATIC1111-compatible API for environments where full A1111 WebUI is not already installed:

```powershell
$env:CCG_DIFFUSERS_MODEL = "stabilityai/sd-turbo"
python scripts/local-models/gateways/diffusers_a1111_api.py 7860
```

It exposes `GET /sdapi/v1/options` and `POST /sdapi/v1/txt2img` on `127.0.0.1:7860` and performs real GPU image generation through diffusers.

The earlier structured/image-only run intentionally excluded GIF/animation. Full provider evidence now uses the separate local animation/audio gateway below.
Idle GIF/animation and audio evidence can be produced by the local animation/audio gateway:

```powershell
python scripts/local-models/gateways/local_animation_audio_gateway.py 8013
```

Default endpoint: `http://127.0.0.1:8013/v1/assets`.
Supported asset kinds: `IDLE_ANIMATION` and `AUDIO`.
The animation path generates a real GIF file from Stable-Diffusion-generated key art plus deterministic idle-motion frames. The audio path generates a real WAV cue file for local provider evidence.

## Ten-Card Evidence Run

```powershell
dotnet run --project src\Game.LocalProviderEvidence --configuration Release -- --count 10 --output-root artifacts\local-provider-evidence\manual-10-card-run
```

The runner uses the production HTTP provider adapters, builds generated card candidates, runs validation, evaluates balance, publishes accepted card versions, and then requests static image assets. It exits with code `2` when any requested provider artifact is missing.

Current accepted local result from `artifacts/local-provider-evidence/manual-10-card-run-images-v3/local-provider-10-card-evidence.json`:

```text
structured model: qwen3.5:latest through Ollama
image model: stabilityai/sd-turbo through diffusers A1111-compatible API
cards requested: 10
cards validated: 10
cards published: 10
balance: 10 Accepted / Low risk
images generated: 10
image artifacts: artifacts/local-provider-gateways/images/local-provider-card-001-local_provider_card_001_v1.png through local-provider-card-010-local_provider_card_010_v1.png
visual review: accepted for local evidence; fantasy CCG style, non-empty PNG files, stable asset manifest refs
```

This is valid local evidence for the structured model path and static image path.

Current full local provider result from the final acceptance provider evidence report:

```text
structured: qwen3.5:latest through Ollama
image: stabilityai/sd-turbo through local A1111-compatible API
animation: stabilityai/sd-turbo-idle-gif through local animation gateway
audio: local-procedural-chime-v1 through local audio gateway
provider artifacts: STRUCTURED_OUTPUT, STATIC_IMAGE, IDLE_ANIMATION, AUDIO
status: Succeeded
```

The final automated acceptance runner accepted this provider evidence with all configured gates passed.
