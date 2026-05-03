# Sample configs

Templates for both sides of the Husky deployment story — what users put
next to `Husky.exe`, and what app authors ship with their releases.

The launcher reads strict JSON — line comments are accepted (per
LEASH §3.7 / `JsonCommentHandling.Skip`) but trailing commas in the local
file are not.

## User-side files (drop next to `Husky.exe`)

- **`husky.config.minimal.json`** — the smallest possible local config.
  Just `source`, nothing else (and the `source.asset` pattern omitted —
  Husky picks the first `.zip` asset on the release). Use this when the
  app author has shipped a `husky.config.json` in their repo / release /
  manifest.
- **`husky.config.github.json`** — local override for a GitHub-sourced
  app. Includes `name`, `executable`, and timing knobs explicitly. Use
  when you want full control or the app author hasn't shipped any
  source-supplied config.
- **`husky.config.http.json`** — local override for an HTTP-manifest
  source.

`source.asset` is optional. When set, `{version}` has no `v` prefix —
Husky strips a leading `v` from the GitHub `tag_name` before
substitution. When omitted, Husky picks the first asset on the release
whose name ends with `.zip` (skipping `husky.config.json` itself).

## Author-side files (you ship these with your app)

- **`husky.config.author.json`** — what an app author commits to their
  repo root (GitHub source) so users can run with the minimal local file.
  No `source` block: the source-supplied config is intentionally limited
  to deployment metadata, never identity (LEASH §9.2 anti-redirect).
- **`manifest.sample.json`** — the shape Husky expects when
  `source.type` is `"http"`. The optional `config:` block plays the same
  role as `husky.config.author.json` for HTTP-manifest distributions.

## Config resolution at runtime

LEASH §5.2 spells it out in detail; the short version is:

```
local file  >  source-supplied config  >  built-in defaults
```

Field-by-field merge. The local file's `source` always wins, even if a
source-supplied config tries to specify one (it's silently dropped — the
launcher logs a warning so you can spot it).

## Defaults that apply if nothing sets them

| field                      | default | min |
|----------------------------|---------|-----|
| `checkMinutes`             | 60      | 5   |
| `shutdownTimeoutSec`       | 60      | 1   |
| `killAfterSec`             | 10      | 0   |
| `restartAttempts`          | 3       | 0   |
| `restartPauseSec`          | 30      | 0   |
| `source.allowPreRelease`   | `false` | —   |

## Bootstrap deployment

You can ship just `Husky.exe` + `husky.config.json` (no `app/`
directory). On first run the launcher pulls the configured app from the
source, installs it into `app/`, and starts it. After that, every
release replaces `app/` in place during a brief cutover.

Combine with `husky.config.minimal.json` and you have the smallest
deployment artefact possible: two files, one of them six lines long.
