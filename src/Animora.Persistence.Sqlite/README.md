# Animora.Persistence.Sqlite

Local EF Core 10 store for the desktop client: `AnimoraLocalDbContext`, entity
configurations, value converters, named query filters, and migrations. This is
the on-device SQLite database that lets the app work fully offline.

Currently an empty scaffold (Phase A01 only creates the project, its references,
and the SQLite package pins). The DbContext, core mappings, and first migration
are added by **Phase A09** (local persistence I — DbContext core mappings).
