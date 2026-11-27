# API v1

OpenAPI JSON is generated at `/openapi/v1.json`. All request and response bodies use JSON.

| Method | Path | Authentication | Purpose |
|---|---|---|---|
| GET | `/health` | None | Process and PostgreSQL health |
| GET | `/api/v1/version` | None | Product/API version |
| POST | `/api/v1/auth/register` | Rate limited | Create user/avatar/session |
| POST | `/api/v1/auth/login` | Rate limited | Authenticate by username or email |
| POST | `/api/v1/auth/refresh` | Rate limited | Rotate refresh session |
| POST | `/api/v1/auth/logout` | Rate limited | Revoke refresh session; idempotent |
| GET | `/api/v1/auth/sessions` | Bearer JWT | The account's open sign-ins: `id`, `current`, `device`, `signedInAt`, `lastSeenAt` |
| DELETE | `/api/v1/auth/sessions/{id}` | Bearer JWT | End one of the account's sign-ins (404 if it is not open) |
| DELETE | `/api/v1/auth/sessions` | Bearer JWT | End every sign-in but the current one |
| GET, PUT | `/api/v1/users/me/privacy` | Bearer JWT | `onlineVisibility`, `privateMessages`, `groupInvites` (`everyone`, `friends`, `nobody`) and `friendRequests` (`everyone`, `nobody`); a PUT changes only what it sends |
| GET | `/api/v1/social/blocks` | Bearer JWT | The people the account blocked |
| GET | `/api/v1/users/me` | Bearer JWT | Current profile |
| GET | `/api/v1/users/me/avatar` | Bearer JWT | Load appearance, equipment, and camera preferences |
| PUT | `/api/v1/users/me/avatar` | Bearer JWT | Validate and persist avatar customization |
| GET | `/api/v1/worlds/islands` | None | List public islands |
| POST | `/api/v1/worlds/islands` | Bearer JWT | Create an island |
| POST | `/api/v1/platform/servers/register` | Service key | Register/upsert instance |
| PUT | `/api/v1/platform/servers/{id}/heartbeat` | Service key | Update health |
| DELETE | `/api/v1/platform/servers/{id}` | Service key | Mark stopped |

Registration passwords require 6–128 characters with uppercase, lowercase, and numeric characters. Username is 3–32 alphanumeric/underscore characters. Duplicate active username/email returns HTTP 409. Invalid credentials and invalid/expired/reused sessions return HTTP 401.

Avatar writes accept only server-owned catalog keys. Supported Phase 02 bases are `human_a` and `human_b`; appearance colors use `#RRGGBB` or `#RRGGBBAA`. Required equipment slots are `top`, `bottom`, and `shoes`; `accessory` is optional. Repeating a slot or supplying an unknown item returns HTTP 400 validation details.
