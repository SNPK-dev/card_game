# Generation provider modes

Provider selection happens outside the match and cannot affect gameplay state or replay hashes.

## Local

Local mode is model-backed. `Game.Api` calls the configured Ollama structured-card gateway and Stable Diffusion image gateway; both must be running and their models must be available locally. Provider or model failures fail the generation job visibly. There is no successful template/procedural fallback.

## OpenAI

Put `OPENAI_API_KEY` in `.env` beside `PLAY.bat` for an extracted release, or in the repository-root `.env` during development. The server loads the nearest `.env` without overwriting process-level environment variables.

The connector calls the official OpenAI Responses API directly and defaults to `gpt-5.6-terra`. Static artwork uses the official Images API and defaults to `gpt-image-2`. Override them with `Generation__OpenAIStructuredModel` and `Generation__OpenAIImageModel` environment variables.

The server never prints the API key. Provider errors and evidence contain sanitized error codes only. External generation requires internet access and may take several minutes for 15 cards; the client polls for up to 20 minutes and displays per-card progress.

## Generate a production card pack

```powershell
dotnet run --project src/Game.UnityCardPackExporter --configuration Release -- --mode production --count 15 --skip-assets --structured-model gpt-5.6
```

Remove `--skip-assets` only when all requested image, animation, and audio providers are configured. Generated data is still constrained by schema, semantic, ruleset, runtime, simulation, and replay gates before publication.
