# Game design foundation

LifeVerse is an original persistent 3D social sandbox for Windows. Players create an account and avatar, enter independently hosted islands, move through a shared world, communicate, create, and manage persistent content.

Phase 01 covers only the executable foundation: identity/session management, a minimal 3D world and player controller, island-instance registration, durable schemas, and local infrastructure. Marketplace, advanced building, Lua, currency, voice, inventory behavior, and asset upload are explicitly deferred.

The first playable loop is: register or sign in, enter the provisional world scene, move with WASD, and jump with Space. This loop is intentionally small and backed by the real identity API rather than mock data.
