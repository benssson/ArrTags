# AGENTS.md

## Project

This is a Jellyfin plugin targeting Jellyfin 12.

## General rules

- Follow the existing project structure and Jellyfin 12 plugin conventions.
- Prefer existing Jellyfin APIs over custom implementations.
- Do not introduce dependencies unless necessary.
- Do not modify unrelated files.
- Do not create commits unless explicitly asked.
- Do not claim something works without building/testing it.

## Development workflow

For significant changes:

1. Inspect the relevant existing code.
2. Explain the proposed approach before implementing it.
3. Make the smallest appropriate change.
4. Build the project.
5. Run relevant tests.
6. Review the resulting diff.

## Compatibility

Maintain compatibility with the project's declared Jellyfin and .NET versions.

## Documentation

- Start with `docs/INDEX.md`; it maps what to read for what task. The always-read
  set is deliberately small: `AGENTS.md`, `GOALS.md`, `docs/INDEX.md`,
  `docs/status.md`, `PLANS.md`, and `docs/plan/state.json`. Do not read the whole
  corpus; fetch only the documents and sections a task needs.
- Status has one editable source, `docs/plan/state.json`. `docs/status.md` and
  the `PLANS.md` milestone table are generated from it by
  `scripts/render-docs-state.cs`; never hand-edit between their markers.
- History is immutable: `docs/implementation/**`, `docs/reviews/**`,
  `docs/changelog/**`, `docs/plan/archive/**`, and ADR text. Do not rewrite it to
  match newer paths or status. Research notes under `docs/research/**` are
  current evidence and may be updated by the research agents when a finding is
  superseded, but are not rewritten to fit newer status or paths. Former
  single-file documents remain as pointer stubs so historical references resolve.
- Agent reports (paths, status/severity enums, attempt rule) are defined in
  `docs/agent-contracts.md`; follow it and each agent's own schema.
- Completed phases do not accumulate in `PLANS.md`; archive them verbatim to
  `docs/plan/archive/`.
- Run `scripts/check-docs.sh` before committing documentation changes.