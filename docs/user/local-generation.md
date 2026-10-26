# Local card generation

The packaged server and Unity client are self-contained. Local card generation additionally requires Python 3.10+, Ollama, the `qwen3.5:latest` model and Stable Diffusion `stabilityai/sd-turbo`. These models are not embedded in the Windows download. Reserve at least 20 GB beyond the game files; a compatible GPU is recommended. CPU generation can take substantially longer. First-time installation requires internet access. Cached local models can subsequently run without a cloud API key.

Install Python and Ollama, start Ollama, and open PowerShell in the extracted release folder. Review the bundled scripts before running them. Install the PyTorch build appropriate for your hardware using https://pytorch.org/get-started/locally/, then install the other image dependencies into a dedicated virtual environment:

```powershell
python -m venv .local-models
# Run the hardware-specific PyTorch installation command with this interpreter:
.\.local-models\Scripts\python.exe -m pip install -r .\tools\local-models\requirements.txt
ollama pull qwen3.5:latest
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\local-models\start-generation-gateways.ps1 -ProviderMode Local -Python .\.local-models\Scripts\python.exe
```

Wait for the image backend to finish downloading/loading. Check readiness before choosing **Local** in Forge Cards:

```powershell
Invoke-RestMethod http://127.0.0.1:11434/api/tags
Invoke-RestMethod http://127.0.0.1:7860/sdapi/v1/options
```

The gateways bind only to loopback: structured cards on port 8011, image assets on 8012 and the image model on 7860. Startup prints their process IDs. Stop those processes after closing the game when you no longer need generation. Diagnostics and generated assets are written under `artifacts/` in the extracted folder; do not share that folder as a clean release.

When running from source, replace `tools/local-models` in the commands with `scripts/local-models`. Model licenses remain those of their publishers; weights are downloaded directly rather than redistributed with the game.

OpenAI Connector is an alternative and requires your own API key and a network connection. It does not require the local gateway processes. Store the key in `.env` beside `PLAY.bat`, never in a shared archive.
