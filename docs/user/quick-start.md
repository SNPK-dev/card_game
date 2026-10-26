# Chronicle Forge — Windows quick start

Russian version: [quick-start.ru.md](quick-start.ru.md).

## Requirements

- Windows 10/11 x64.
- The bundled server includes its .NET runtime; no separate runtime is required.
- 4 GB free disk space and a network loopback connection.

## Start

1. Extract the release archive to a writable folder.
2. Run `PLAY.bat`. Windows starts the local authoritative server, waits for it, and opens `client/windows/CardGame.exe`.
3. Create an account with a unique email and a password of at least 12 characters.
4. Open **Forge Cards**, choose Local or OpenAI Connector, generate a 15-card set, then open **Deck Workshop** and save the 30-card deck.
5. Select the deck, choose **Play vs Bot**, complete the match with the visible card/target/choice controls, and open the verified replay from Results.

Local generation runs through local Ollama and Stable Diffusion models. Follow `LOCAL-GENERATION.md` in the release (or [the source setup guide](local-generation.md)) before choosing Local; first-time model downloads require additional disk space and generation will not be instant. For OpenAI generation, create `.env` beside `PLAY.bat` containing `OPENAI_API_KEY=...` before starting the game. Never include that file when sharing the release folder.

## Start the local server manually

The client expects the authoritative API at `http://127.0.0.1:5000`.

From an extracted Windows release, open PowerShell in the release directory and run:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
./server/Game.Api.exe --urls http://127.0.0.1:5000 --Storage:RootPath=./data
```

Keep that terminal open. Wait until the server reports that it is listening on `http://127.0.0.1:5000`, then launch the client in a second terminal:

```powershell
./client/windows/CardGame.exe
```

When working from the repository instead of a packaged release, use:

```powershell
dotnet run --project src/Game.Api --configuration Release --no-launch-profile --urls http://127.0.0.1:5000 --Storage:RootPath=artifacts/local-player-data
```

Check server availability in another terminal:

```powershell
Invoke-RestMethod http://127.0.0.1:5000/health
```

The expected response contains `status: ok`. Always provide `Storage:RootPath` for a player-facing run; otherwise the API uses in-memory development stores and progress will not survive a restart.

Account, profile, collection, decks, settings, match logs, and replay inputs are stored under `data/`. Do not delete this directory if you want to preserve progress. Board themes and audio settings can be changed from Settings; they never affect gameplay state or replay hashes.

## Troubleshooting

- If the client reports that the service is unavailable, close both windows and run `PLAY.bat` again.
- Allow `Game.Api.exe` and `CardGame.exe` through local firewall prompts. The default server listens only on `127.0.0.1:5000`.
- Logs are under `logs/`; they intentionally omit passwords and raw account tokens.
- Verify package integrity with `SHA256SUMS.txt` before first launch.
