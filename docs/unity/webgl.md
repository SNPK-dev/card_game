# Unity WebGL And CORS

Browser/WebGL clients require CORS. Standalone/editor Unity clients are not affected.

## Default

CORS is disabled by default. Production deployments must explicitly configure allowed origins.

## Configuration

Use configuration keys or environment variables:

```text
Api:Cors:Enabled=true
Api:Cors:AllowedOrigins=http://localhost:5173,http://localhost:8080,http://127.0.0.1:8080
Api:Cors:AllowedMethods=GET,POST,OPTIONS
Api:Cors:AllowedHeaders=Content-Type,X-Session-Token
```

Environment variable form:

```text
Api__Cors__Enabled=true
Api__Cors__AllowedOrigins=http://localhost:5173,http://localhost:8080,http://127.0.0.1:8080
Api__Cors__AllowedMethods=GET,POST,OPTIONS
Api__Cors__AllowedHeaders=Content-Type,X-Session-Token
```

The API does not use wildcard credentials. Add only trusted WebGL origins.

## Smoke

```bash
bash scripts/unity/run-webgl-cors-smoke.sh
```

The smoke validates disabled-by-default behavior, allowed origin preflight, disallowed origin rejection, and `X-Session-Token` preflight support.

Unity WebGL development player generation is separate from this backend CORS smoke. `scripts/unity/run-unity-dev-build-smoke.sh --webgl-dev` and `scripts/unity/run-unity-release-candidate-build.sh --webgl-rc` write build summary JSON under `artifacts/unity-build-logs/<timestamp>/build-summary.json`. If the WebGL module is unavailable it reports a skip reason; if it passes, build output must exist under ignored `artifacts/unity-builds/webgl/<timestamp>/`.

Polished gameplay presentation uses generated/in-repo theme and audio cue refs. WebGL builds must include those runtime scripts and prefab references, but no external media downloads are required.
