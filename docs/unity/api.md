# Unity API

Base URL for local development is `http://localhost:5000`. Match viewer endpoints require `X-Session-Token`; command submission also includes `sessionToken` in the JSON body.

## Boot Endpoints

- `GET /health`
- `GET /engine/capabilities`
- `GET /catalog/snapshots/current`
- `GET /catalog/snapshots/{snapshotId}`
- `POST /catalog/snapshots`
- `GET /catalog/snapshots/{snapshotId}/compatibility`
- `GET /rulesets/profiles/current`
- `GET /rulesets/profiles/{profileId}`
- `POST /rulesets/profiles`
- `GET /decks/current`
- `GET /decks/{deckId}`
- `POST /decks`

The POST storage endpoints are for local/demo boot and smoke fixtures. A production Unity client normally reads published catalog/profile/deck data instead of writing it.

## Match Endpoints

- `POST /sessions`
- `POST /matches`
- `GET /matches/{matchId}/state`
- `GET /matches/{matchId}/players/{playerId}/legal-actions`
- `POST /matches/{matchId}/commands`
- `GET /matches/{matchId}/events?afterEventSeq={seq}`
- `POST /matches/{matchId}/reconnect?afterEventSeq={seq}`
- `POST /matches/{matchId}/replay/verify`

Unity should render state and actions from these endpoints. It should not inspect backend stores or derive legality locally.

## Asset And Presentation Endpoints

- `GET /assets/manifests/current`
- `GET /assets/manifests/{assetManifestId}`
- `POST /assets/manifests`
- `GET /assets/manifests/{assetManifestId}/compatibility`
- `GET /assets/manifests/{assetManifestId}/presentation-hash`
- `POST /presentation/timelines`
- `POST /presentation/playback-plans`
- `POST /presentation/playback-instructions`

Gameplay must continue when assets or presentation plans are missing. Use placeholders or silence according to fallback policies.
