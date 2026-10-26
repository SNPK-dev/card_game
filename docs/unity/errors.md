# Unity Errors

All Unity-facing errors should map to a stable `UnityError` shape:

```json
{ "code": "VALIDATION_ERROR", "message": "Request failed validation.", "retryable": false, "suggestedUnityBehavior": "Fix request DTO and retry after user action." }
```

| HTTP | Code | Retry | Unity behavior |
| --- | --- | --- | --- |
| 400 | `MALFORMED_JSON` | no | log and fix client serializer |
| 400 | `VALIDATION_ERROR` | no | show nonfatal request error |
| 401/403 | `MATCH_SESSION_FORBIDDEN` | no | recreate session or reconnect |
| 401/403 | `SESSION_EXPIRED` | no | create a new session |
| 403 | `MATCH_COMMAND_FORBIDDEN` | no | disable action, refresh state/actions |
| 403 | `SPECTATOR_COMMAND_FORBIDDEN` | no | keep spectator read-only |
| 404 | `MATCH_NOT_FOUND` | no | return to match setup |
| 404 | `DECK_NOT_FOUND` | no | reload demo/published deck |
| 400 | `CATALOG_PROFILE_INCOMPATIBLE` | no | fail boot compatibility check |
| 400 | `ILLEGAL_COMMAND` | no | refresh state/actions |
| 400 | `COMMAND_PRE_STATE_HASH_MISMATCH` | yes | refresh state/actions and retry user intent |
| 400 | `UNSUPPORTED_COMMAND` | no | fail capability check |
| 400 | `UNSUPPORTED_CLIENT_VERSION` | no | block boot with upgrade message |
| 413 | `REQUEST_TOO_LARGE` | no | reduce payload |
| 429 | `RATE_LIMITED` | yes | back off |
| 500 | `SERVER_FAULT` | yes | retry after backoff or reconnect |
| 500 | `ENGINE_FAULTED` | no | show terminal fault state |
| 409 | `REPLAY_MISMATCH` | no | mark match evidence invalid |

Realtime-specific errors are deferred because realtime push is not implemented for v1.
