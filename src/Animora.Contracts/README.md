# Animora.Contracts

Shared DTOs and query/service **interfaces** consumed by the UI. This
roadmap's UI phases add contracts here (e.g. `IPatientsQueryService` + its
DTOs); their **mock** implementations live in `Animora.Desktop/Mocks`. A
future backend/sync roadmap implements these same interfaces for real —
swapping the mock via DI with no View/ViewModel changes.
