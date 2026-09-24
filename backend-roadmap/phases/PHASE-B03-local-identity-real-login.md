# Phase B03 — Local identity & real login (index only — split into 3 sessions)

**Band:** M · **Track:** B · **Depends on:** B02 · **Satisfies:** R-B04, R-B09
**Runnable after this phase:** desktop offline yes · server required no

Do not implement from this file. Execute one sub-phase per session, in order; each file is self-contained:

| Session | File | Scope |
|---|---|---|
| B03a | `PHASE-B03a-identity-foundation-tables-hashing-session.md` | Identity package pin, `User.MustChangePassword`, 4 error codes, `LocalSession`/`LoginAttemptLog` tables + migration `005_LocalIdentity`, `IPasswordHasher`/`PasswordPolicy`/`ISessionService` + Infrastructure.Local impls + DI |
| B03b | `PHASE-B03b-identity-use-cases-provider-repositories.md` | 7 identity use cases (sign-in/out, lock/unlock, change-password, first-run, current-session), `SessionCurrentUserProvider` (deletes `SingleUserProvider`), `FindByUsernameAsync` + 3 new repositories, application DI |
| B03c | `PHASE-B03c-identity-contracts-desktop-tests.md` | `Animora.Contracts` auth seam, `Client.Services` (`AuthenticationService`/`CurrentUserContextService`/`RoleMapping`), Desktop login + first-run rewrite, mock deletion, seeding change, all B03 tests + build/test/run gate |

B03 is ✅ only when B03c is done. The split is equal-weight by design (foundation → application →
contracts/desktop/tests); quality and acceptance rigor are unchanged — every original B03 acceptance
criterion is preserved and assigned to a session (all tests + the runnable gate land in B03c).

## Goal (whole phase)
Replace the hardcoded `admin`/`admin` gate with a real local credential store: hashed passwords,
lockout, a first-run owner-setup path, a durable session with app-lock, and a real
`ICurrentUserContext`/`ICurrentUserProvider` so every command from now on carries a genuine `UserId`
and `DeviceId`.

## Out of scope (whole phase — deferred to the named phases)
- Permission evaluation, screen/action gating, capability snapshots → B04.
- Server-side identity, bearer/refresh tokens, device revocation → D03, E07.
- Role management UI → C10 (Profile) consumes B04's catalog.
- Password reset by an operator → D03/E01.
- Any other mock deletion → the owning Track C phase.
