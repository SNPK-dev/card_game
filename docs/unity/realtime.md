# Realtime Decision

HTTP polling remains the v1 Unity transport.

## Decision

Do not implement WebSocket/SignalR before the first Unity client milestone. The game is deterministic and turn based, the event sequence model is already explicit, reconnect returns a safe state projection plus missed events, and commands remain HTTP-authoritative.

## Polling Policy

- Poll `/matches/{matchId}/events?afterEventSeq={lastSeen}` every 750-1500 ms while a match scene is active.
- Poll immediately after submitting a command.
- Back off to 5-10 seconds when the app is backgrounded.
- On transport failure, call `/matches/{matchId}/reconnect?afterEventSeq={lastSeen}`.
- If local state hash differs after reconnect, discard local state and render the reconnect state.

Future realtime should push only event availability or viewer-filtered public events. HTTP remains source of truth for commands, state, reconnect, and replay.

Browser/WebGL transport uses the configurable CORS policy documented in `webgl.md`; realtime remains independent from that v1 HTTP policy.
