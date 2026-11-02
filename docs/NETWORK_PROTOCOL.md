# Network protocol

Version 1 business operations use HTTPS REST at `/api/v1`. Access tokens are JWT bearer tokens with a 15-minute lifetime. Refresh tokens are opaque 512-bit random values sent only in JSON over TLS and stored server-side only as SHA-256 hashes.

The island server authenticates to Platform endpoints through `X-LifeVerse-Service-Key`. This bootstrap key is a deployment secret and is not a player credential.

Gameplay transport is reserved for ENet/UDP on port 7777. Phase 01 deliberately does not open that listener because temporary island-entry token issuance and server-side validation are not implemented yet. The eventual handshake must validate a single-use, short-lived token before adding a peer to the multiplayer tree.
