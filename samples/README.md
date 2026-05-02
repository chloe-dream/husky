# Sample configs

Drop one of these next to `Husky.exe` as `husky.config.json` and adjust the
fields. The launcher reads strict JSON — comments are accepted (per
LEASH §3.7 / `JsonCommentHandling.Skip`) but trailing commas are not.

## Files

- **`husky.config.github.json`** — pulls releases from a GitHub repo. The
  asset filename pattern uses `{version}` (no `v` prefix) — Husky strips a
  leading `v` from the GitHub `tag_name` before substitution.
- **`husky.config.http.json`** — points at a JSON manifest on any HTTP(S)
  server. The manifest must look like `manifest.sample.json` — version, url,
  optional sha256.
- **`manifest.sample.json`** — the shape Husky expects when `source.type` is
  `"http"`.

## Defaults that apply if you omit fields

| field | default | min |
|---|---|---|
| `checkMinutes` | 60 | 5 |
| `shutdownTimeoutSec` | 60 | 1 |
| `killAfterSec` | 10 | 0 |
| `restartAttempts` | 3 | 0 |
| `restartPauseSec` | 30 | 0 |
| `source.allowPreRelease` | `false` | — |

## Bootstrap deployment

You can ship just `Husky.exe` + `husky.config.json` (no `app/` directory). On
first run the launcher pulls the configured app from the source, installs it
into `app/`, and starts it. After that, every release replaces `app/` in
place during a brief cutover.
