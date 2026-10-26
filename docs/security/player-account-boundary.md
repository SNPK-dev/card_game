# Player account security boundary

Player passwords are normalized only for email lookup and are hashed with PBKDF2-SHA256, a random 256-bit salt, 210,000 iterations, and fixed-time verification. Access and refresh tokens use cryptographically secure random bytes; only SHA-256 token hashes are persisted. Responses and release logs must never contain password material or raw tokens.

All profile, collection, generation-job, and deck handlers resolve ownership from the authenticated account instead of accepting a player id from the request. Unknown resources return the same not-found shape across accounts. Account sessions and match-scoped sessions remain separate conceptual boundaries even though the Unity transport uses the existing token header for compatibility.

Integration coverage verifies login/refresh/logout, profile updates, durable restart, collection/deck persistence, non-ownership behavior, and absence of raw tokens in storage. The release server listens on loopback by default; remote deployment requires TLS termination, secret-managed configuration, rate-limit tuning, and an external security review.
