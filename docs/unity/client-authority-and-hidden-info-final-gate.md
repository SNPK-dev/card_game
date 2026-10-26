# Unity Client Authority And Hidden-Info Final Gate

## Gate Result

Status: ready for scoped release-candidate handoff.

## Required Assertions

- Unity never computes legal actions locally.
- Unity never infers target legality locally.
- Unity never merges DebugBoth hidden state.
- Unity never displays opponent hand identities.
- Unity never displays deck order or deck identity.
- Unity never displays private choices to spectators.
- Unity never uses backend internals.
- Unity submits only server-provided command templates plus selected server-approved refs.
- All command submissions use expected pre-state hash.
- Stale hash forces reconnect and fresh legal actions.
- Replay mismatch is blocking.
- Hidden-info assertions remain in full live smoke.

## Evidence

- Full live PlayMode smoke.
- `docs/unity/state.md`
- `docs/unity/commands.md`
- `docs/unity/client-playtest-checklist.md`
- Unity view-model tests and integration tests.

Future UI surfaces must update this gate before claiming RC readiness.
