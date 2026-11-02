# Security baseline

- Passwords use ASP.NET Core `PasswordHasher`, which provides salted, versioned PBKDF2 hashes. Passwords and tokens are never logged.
- JWT access tokens are short-lived and validate issuer, audience, signature, lifetime, and a 30-second clock skew.
- Refresh tokens rotate on every use. Reuse of a revoked token revokes its entire session family.
- Authentication endpoints are rate-limited by remote address. Request validation and RFC 7807-style problem responses are centralized.
- User authorization and platform service authentication are separate trust domains.
- Database, Redis, and MinIO ports are not published by development Compose. API is bound to host loopback only.
- Secrets are injected by environment and `.env` is ignored. Production requires TLS termination and secret-manager injection.

Known Phase 01 gaps: email verification/recovery, MFA, refresh-token cleanup job, key rotation/JWKS, per-user rate limiting, audit-event persistence, island entry credentials, and production secret-provider integration.
