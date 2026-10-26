# Unity Versioning And Compatibility

Unity must call `GET /engine/capabilities` at boot.

Check:

- API contract version.
- Minimum supported Unity client contract version.
- State schema version.
- Event schema version.
- Asset manifest version.
- Playback schema version.
- Supported command and event types.
- Feature flags.

Unknown fields are forward-compatible and should be ignored. Unknown enum values should be handled defensively: log, refresh state, and fall back to generic UI. Unknown commands must not be submitted. Unknown events may be ignored unless they are terminal/fault indicators.

On compatibility failure, Unity should fail fast before creating sessions or starting matches.
