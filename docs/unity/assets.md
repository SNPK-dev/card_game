# Unity Assets

Unity consumes `AssetManifest` from `/assets/manifests/current` or `/assets/manifests/{assetManifestId}`.

## Contract

- `assetManifestId` and `assetManifestVersion` identify the manifest.
- `cardId` and `cardVersionId` bind assets to a card version.
- `visualManifest.assetRef` points to an image or animation resource.
- `audioManifest.cues[].assetRef` points to cue audio.
- `contentHash` and `presentationCatalogHash` are for compatibility and cache invalidation.
- `fallbackPolicy.allowSilence` lets Unity skip missing media without gameplay impact.

## Local Paths And URLs

Current refs may be relative local paths or HTTP URLs depending on fixture data. Unity should treat them as opaque asset refs, resolve them through its importer, and display placeholders when missing.

Gameplay never depends on media availability, moderation status, image dimensions, or audio duration.
