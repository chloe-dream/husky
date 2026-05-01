# CLAUDE.md — Working on Husky

> *Read this file first. Then read [LEASH.md](./LEASH.md). Then start work.*

---

## Who you are working for

You are working for **Chloe** (also **Marcus** — genderfluid, both names valid; either can appear in any commit, message, or context). She is the developer and product owner of Husky.

She communicates with you in **German**. All conversation, design discussions, clarifications, and status updates directed at her must be in German.

The code you produce is **English-only**. See "Language" below for the strict rules — they are absolute.

---

## Project vibe

Husky is **not** enterprise software. It is an indie tool with personality.

- **Pragmatic but sexy.** Things should work, and they should feel cool. Not "look at all my abstractions."
- **Minimal by default.** Every config knob, every interface, every layer is a tax. Skip what is not needed *now*. The spec is small on purpose.
- **Modern .NET.** .NET 9, `async`/`await`, records, primary constructors, file-scoped namespaces, collection expressions, pattern matching — the new stuff is the default.
- **Cross-platform.** Windows-first, Linux must work. macOS is a nice-to-have.
- **Charming.** Husky speaks. See [LEASH §10.4](./LEASH.md#104-husky-voice).

---

## Language

This is non-negotiable.

### German (with Chloe)

- All replies, questions, and status updates directed at Chloe.
- Suggestions, design discussions, clarification requests.
- Anything she will read directly outside of source files.

### English (everywhere else)

- All identifiers (classes, methods, properties, fields, variables, parameters, namespaces, project names, file names).
- All code comments (`//`, `/* */`, `///`).
- All string literals: log lines, console output, exception messages, UI text.
- README files, inline docs, API docs, JSON property names.
- Test names and test descriptions.
- Git commit messages (subject and body).

**No mixed-language code.** Not a single German variable, not a single German comment, not a single German log line.

---

## How to work

### When in doubt — ask

[LEASH.md](./LEASH.md) is the contract. If something is unclear, contradictory, or silent on a meaningful question — **ask Chloe in German**. Do not invent design decisions. Scope creep is the enemy.

When you ask: be specific. Suggest 2–3 concrete options. Recommend one with a one-line reason. No open "what should I do?" questions — that wastes her time.

### When the spec is silent on a small detail

Trivial implementation choices (helper-method names, internal data-structure choices like `List<T>` vs `IEnumerable<T>`, whether a field is `private` or `internal`) — use your judgment, pick the simpler option, move on.

If the choice could affect how Chloe uses or perceives the tool — file layout, naming visible in logs, API shape, defaults — **ask**.

### Code style baseline

- File-scoped namespaces.
- Primary constructors where they reduce noise.
- Records for immutable data; classes for behavior.
- `async`/`await` everywhere I/O happens. No `.Result`, no `.Wait()`, no sync-over-async.
- Nullability enabled. `?` and `!` used correctly. Never use `#nullable disable` to silence warnings.
- `var` when the type is obvious from the right-hand side; explicit type otherwise.
- No Hungarian notation. No `_field` underscores for private fields — name them like properties (`field`, not `_field`). Exception: backing fields for explicitly implemented properties.
- One public type per file. File name matches type name.
- Prefer composition over inheritance. Sealed by default; open up only when needed.

### Testing

- Test what is worth testing: protocol parsing, message dispatch, version comparison, source-provider URL building, watchdog state transitions, file operations on the update flow.
- **Do not** test trivial getters/setters. Do not chase coverage percentages.
- xUnit. FluentAssertions if it makes assertions noticeably cleaner.
- Mock the pipe in unit tests. Use real pipes only in end-to-end tests.

### Commits

- All commit messages in **English**.
- Small, focused commits. One logical change per commit.
- Format: `<type>: <short summary>` where `<type>` is one of `feat`, `fix`, `refactor`, `test`, `docs`, `chore`, `build`.
- Body optional but encouraged for non-trivial changes; explain *why*, not *what*.

### Status updates to Chloe

After completing a step from the implementation order, post a short status update **in German**:

- What is done.
- What is next.
- Any decisions that came up and how you resolved them (or that you need her to weigh in on).
- Anything you skipped, deferred, or compromised on — flag it explicitly.

---

## What you must never do

- Add features that are not in [LEASH.md](./LEASH.md). If you think one is missing, **ask first**.
- Add config options "for flexibility." See "Minimal by default."
- Inject DI containers, mediator patterns, CQRS, or other ceremony into a 3-project tool.
- Use `Microsoft.Extensions.Logging` for the launcher's user-facing console — that is [Spectre.Console](https://spectreconsole.net) territory. `Microsoft.Extensions.Logging` is fine for internal diagnostics if needed at all.
- Pull in NuGet libraries casually. Each dependency is a maintenance burden and a security surface. If [LEASH.md](./LEASH.md) does not call a library out, **ask before adding it**.
- Leave silent `TODO` comments in the code. If something is unfinished, mention it in the status update.
- Use AI-flavored stylistic clichés in user-visible text ("delve into," "navigate the landscape of," etc.). Husky is curt. Plain words.

---

## Working order

[LEASH §Appendix A](./LEASH.md#appendix-a--recommended-implementation-order) lists the recommended implementation order. Follow it unless you have a reason not to (in which case: ask).

Per step:

1. Implement.
2. Write tests where they make sense.
3. `dotnet build` and `dotnet test` — both must be green.
4. Commit.
5. Brief status update to Chloe in German.

---

## Final note

Husky is a side project that should feel like a craft. It does not have a deadline. Quality over speed.

If you are about to cut a corner, **say so explicitly** — Chloe decides whether it is the right corner to cut.

🐺 *woof.*
