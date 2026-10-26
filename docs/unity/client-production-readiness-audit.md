# Unity Client Production Readiness Audit

Claim: `release-candidate-ready-with-known-limitations`.

| Category | Status | Evidence Path | Latest Validation Command | Remaining Risk | Owner/Follow-Up |
| --- | --- | --- | --- | --- | --- |
| Backend/API integration | ready | `docs/api/openapi.yaml` | `bash scripts/unity/validate-openapi.sh` | Local backend deployment is separate. | Backend/API |
| Full live gameplay flow | ready | `artifacts/unity-test-logs/` | `CARDGAME_UNITY_LIVE_BACKEND_TESTS=1 bash scripts/unity/run-unity-playmode-smoke.sh` | Requires local backend. | Unity client |
| Repeatability | ready | `artifacts/unity-test-logs/` | `CARDGAME_UNITY_LIVE_BACKEND_TESTS=1 bash scripts/unity/run-unity-playmode-smoke.sh` | Smoke-scope proof, not balance proof. | Unity client |
| RC validation | ready | `artifacts/unity-rc-validation/` | `bash scripts/unity/run-unity-client-rc-validation.sh` | Local Unity modules can affect builds. | Unity client |
| MCP inspection | ready-with-limitations | `docs/unity/mcp-inspection-latest.md` | `bash scripts/unity/check-unity-mcp-endpoint.sh` plus MCP tools | Active editor attachment is environment-controlled. | Unity client |
| Standalone build | ready | `artifacts/unity-build-logs/` | `bash scripts/unity/run-unity-release-candidate-build.sh --standalone-rc` | Requires installed standalone module. | Unity client |
| WebGL build | ready | `artifacts/unity-build-logs/` | `bash scripts/unity/run-unity-release-candidate-build.sh --webgl-rc` | Hosting/deployment out of scope. | Unity client |
| Scene/prefab integrity | ready | `artifacts/unity-build-logs/` | `bash scripts/unity/run-unity-release-candidate-build.sh --validate-only` | Runtime UI is bootstrapper-owned. | Unity client |
| UI/UX polish | ready-with-limitations | `docs/unity/client-polished-gameplay-presentation.md` | `bash scripts/unity/run-unity-client-quality-gates.sh` | Generated placeholder art remains. | UX/art |
| Presentation animation | ready-with-limitations | `docs/unity/event-animation-map.md` | `dotnet test --configuration Release` | Placeholder animation treatment. | Unity client |
| Audio routing | ready-with-limitations | `docs/unity/client-audio-direction.md` | `bash scripts/unity/check-unity-media-inventory.sh` | Generated cue refs, not mastered audio. | Audio |
| Hidden-info safety | ready | `docs/unity/client-authority-and-hidden-info-final-gate.md` | full live smoke | Future views must keep gate. | Unity client |
| Failure/recovery UX | ready-with-limitations | `docs/unity/client-release-candidate.md` | `dotnet test --configuration Release` | Manual visual review recommended. | Unity client |
| Playtest evidence | ready-with-limitations | `docs/unity/examples/client-rc-playtest-report.example.json` | manual export or smoke evidence | Manual notes may be absent. | QA |
| Asset/license safety | ready | `docs/unity/client-media-inventory.md` | `bash scripts/unity/check-unity-media-inventory.sh` | Future external media needs metadata. | Art/audio |
| Performance baseline | ready-with-limitations | `docs/unity/client-performance-baseline.md` | `bash scripts/unity/run-unity-client-quality-gates.sh` | Full profiler budget future. | Unity client |
| Accessibility/readability baseline | ready-with-limitations | `docs/unity/client-readability-accessibility-baseline.md` | `bash scripts/unity/run-unity-client-quality-gates.sh` | Formal accessibility audit future. | UX |
| Documentation | ready | `docs/unity/README.md` | OpenSpec validation | Keep examples synchronized. | Unity client |
| Known limitations | ready-with-limitations | `docs/unity/client-release-candidate.md` | doc review | Final media/live services out of scope. | Product |

The Unity client can claim `release-candidate-ready-with-known-limitations`, not unqualified final production readiness.
