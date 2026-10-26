# Unity State Contract

Unity consumes `MatchStateView`. The server projects authoritative state into a viewer-safe public shape.

## View Kinds

- `PLAYER`: returned for a session bound to a match player.
- `SPECTATOR`: returned for a spectator session.

`viewerPlayerId` is set only for `PLAYER`.

## Player Relationships

- `SELF`: the session owner's player state.
- `OPPONENT`: another player as visible to the owner.
- `SPECTATED`: a player as visible to spectators.

## Visibility Rules

- Self hand cards are visible.
- Self deck count is visible, but deck order and card identities are never visible.
- Opponent hand, deck, secret-zone, and set-aside identities are hidden; only counts are exposed.
- Spectators cannot see either player's hidden hand, deck, secret-zone, or set-aside identities.
- Graveyard and board are public.
- Unrevealed secrets do not expose source card instance, card version, reveal event, or effect id to opponents/spectators.
- Private pending choices are visible only to their owner.
- Public pending choices are visible to all authorized viewers.
- RNG state, debug traces, and authoritative-only internals are not part of live Unity state.

Reconnect returns the same projection rules as `GET /matches/{matchId}/state`.
