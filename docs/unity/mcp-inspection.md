# Unity MCP inspection

Version: Unity Editor **6000.3.2f1**, package/server **10.0.0**. The project package is pinned in `clients/unity/CardGame.Unity/Packages/manifest.json`.

## Start the server

Prerequisites: Git, Python and `uv` (`uvx` on PATH). Run from the repository root; the first startup downloads the pinned server and dependencies.

macOS / Git Bash:

```bash
bash scripts/unity/start-unity-mcp.sh
```

Windows PowerShell:

```powershell
.\scripts\unity\start-unity-mcp.ps1
```

Default base URL: `http://127.0.0.1:18088`; health: `/health`; MCP endpoint: `/mcp`. This avoids OpenSpace's default API port 8080. Set `CARDGAME_UNITY_MCP_ENDPOINT` to the same base URL in both the server and Unity Editor launch environments to override it. In an already-running Editor, the `CardGame.UnityMcpEndpoint` EditorPrefs value is the fallback configuration.

```bash
bash scripts/unity/check-unity-mcp-endpoint.sh
```

This checks server health only, not whether the project is connected. The earlier `mcp-inspection-latest.md` is historical evidence and uses the old port/path.

## Connect the editor

Open `clients/unity/CardGame.Unity` with the pinned Editor. Wait for Package Manager and C# compilation. The repository connector starts the bridge automatically; `CardGame → Tools → Connect Unity MCP` retries it. Configure your MCP client with `http://127.0.0.1:18088/mcp`.

MCP for Unity requires HTTPS for remote HTTP endpoints. The repository connector opts into plaintext HTTP only when the configured endpoint is loopback (`127.0.0.1`/`localhost`); do not weaken this check for a LAN or Internet address.

## Required inspection

1. List instances; select the instance whose project path ends in `clients/unity/CardGame.Unity`.
2. Read project/editor state; verify version 6000.3.2f1, idle/not compiling, not playing.
3. Load `Assets/Scenes/CardGameLocalMatch.unity`.
4. Verify `CardGameClientRoot`, expected UI hierarchy and no missing scripts.
5. Read console errors; run a short enter/exit Play Mode smoke against the local backend.
6. Read console again and save a fresh dated report. Server health alone is not an MCP inspection pass.

Close the normal Editor before batchmode tests; two Editors cannot safely use the same project simultaneously. MCP does not replace EditMode/PlayMode XML, live backend testing, build artifacts or manual gameplay acceptance.

## Common failures

- Health fails: server stopped or port conflict; inspect server logs.
- Health passes, no instances: Editor not open/compiled or connector URL differs.
- Compilation fails: inspect Editor.log and package resolution before reconnecting.
- Editor activation required: sign in and activate an eligible license in Unity Hub.
- Batchmode project lock: close the interactive Editor first.
