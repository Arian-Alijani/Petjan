# Animora.Sync.Abstractions

The client-side state-replication seam (ARCHITECTURE.md §8.2). It defines the
single engine port `IStateSyncEngine` plus its vocabulary `SyncScope`,
`SyncOutcome` and `SyncResult`, and ships `NullSyncEngine` — the offline
fallback registered by `AddAnimoraSyncEngine()` (TryAddSingleton). Pull-only:
business intent travels through `ICommandTransport`, never this engine. No
transport, database or server references — the real HTTP adapter is `Animora.Sync.Custom` (G02).
