# scripts/git — fast commit & push

**One command, zero thinking:**

```bash
scripts/git/push.sh "feat(ui): add pet profile card"
```

What it does (in order, silently): `add -A` → `commit` → `fetch` + `rebase --autostash` → `push -u`.
Prints exactly one `✓`/`✗` line.

| Form | Effect |
|---|---|
| `push.sh "msg"` | commit all changes with `msg`, sync, push |
| `push.sh` | same, auto-generated message from changed files |
| `push.sh -b name "msg"` | switch/create branch `name` first |

Exit codes: `0` ok · `2` rebase conflict (repo left clean, fix & re-run) · `3` no credentials.

## Fresh session / new account
The script has no baked-in tokens. If it exits with code `3`, the model must call the
**`setup_github_environment`** tool once (it writes `~/.git-credentials` + `user.name/email`),
then re-run the same command. Nothing else is required.
