# Unity Commands

Unity must build command submissions from `legalActions` returned by:

`GET /matches/{matchId}/players/{playerId}/legal-actions`

Submit a command with:

`POST /matches/{matchId}/commands`

Required body fields are `sessionToken` and `command`. `commandId`, `correlationId`, and `expectedPreStateHash` are recommended.

## Supported Public Commands

- `PLAY_CARD`: refs include `cardInstanceId`; payload includes cost/stat fields needed by the resolved legal action.
- `ATTACK`: refs include `attackerEntityInstanceId`, `targetPlayerId`, and optionally `targetEntityInstanceId`.
- `END_TURN`: no target refs required.
- `CONCEDE`: ends the match for the acting player.
- `ACTIVATE_LOCATION`: refs include `locationEntityInstanceId`, `sourceCardInstanceId`, and target refs when required.
- `USE_HERO_POWER`: refs include `sourceCardInstanceId` and target refs when required.
- `RESOLVE_CHOICE`: refs include `choiceId`, `selectedOptionId` or `selectedOptionIds`, and option card refs when present.

## Client Behavior

- Submit only commands returned in legal actions.
- Include `expectedPreStateHash` from the state used to render the command.
- On `COMMAND_PRE_STATE_HASH_MISMATCH`, refresh state and legal actions.
- Treat duplicate `commandId` behavior as non-idempotent unless a future capability explicitly advertises idempotency.
- Unknown command types are unsupported for this Unity contract and should fail fast through the error mapper.
