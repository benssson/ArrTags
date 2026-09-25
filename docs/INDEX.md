# ArrTags documentation index

This is the map of the repository documentation. Read it first to decide what to
read for the task at hand. It is deliberately short and stable across releases:
adding a release must not grow the always-read set.

## Documentation classes

| Class | Rate of change | Editable source of truth | Location |
| --- | --- | --- | --- |
| Normative reference | Rare | Human-edited; decisions recorded as ADRs | `docs/architecture/`, `docs/data-model/`, `docs/decisions/` |
| Plan | Per phase | `PLANS.md` (active scope) + `docs/plan/state.json` | `PLANS.md` |
| Status | Per task/phase | `docs/plan/state.json` | rendered into `docs/status.md` and the `PLANS.md` milestone table |
| History / evidence | Append-only | Never rewritten | `docs/changelog/`, `docs/implementation/`, `docs/reviews/`, `docs/research/` |
| Product goals | Rare | Human-edited | `GOALS.md` |

**One editable source per fact.** A fact may appear in `docs/status.md` or the
`PLANS.md` milestone table only because those are rendered from
`docs/plan/state.json` by `scripts/render-docs-state.cs`. Anywhere else, use a
link. Do not hand-copy a status fact into a second document.

## Default read (every task)

1. `AGENTS.md`
2. `GOALS.md`
3. `docs/INDEX.md` (this file)
4. `docs/status.md` — current shipped state
5. `PLANS.md` — the active plan, and `docs/plan/state.json` for machine state
6. The role-specific documents below

Do not read the whole corpus. Fetch the documents and sections relevant to the
task. `docs/implementation/**` is the largest tree and is evidence only; read the
specific task/phase report you need, never the whole tree.

## Read by role

| Role | Read beyond the default |
| --- | --- |
| `orchestrator` | `PLANS.md` active scope + a phase's execution order, `docs/plan/README.md`, `docs/plan/state.json`, the target task's `docs/implementation/<task-id>/` reports |
| `implementation-planner` | `GOALS.md`, `docs/plan/README.md`, `docs/architecture/`, `docs/data-model/`, `docs/decisions/`, `docs/limitations/` |
| `implementation-worker` | The task's ADRs/sections in `docs/architecture/` and `docs/data-model/`, `docs/limitations/` |
| `implementation-reviewer` | The task's requirements and ADRs, the diff, `docs/limitations/`, `docs/plan/state.json` |
| `phase-reviewer` | The phase's tasks and execution order, the phase review JSON, `docs/plan/state.json` |
| `release-reviewer` | `GOALS.md`, every ADR in `docs/decisions/`, `docs/limitations/`, `docs/changelog/`, `docs/release/build-and-release.md`, the release artifact |
| `architecture-reviewer` | `docs/architecture/`, `docs/data-model/`, `docs/decisions/` |
| `jellyfin-expert` | `docs/architecture/` and `docs/research/jellyfin-12-architecture.md` |
| `arr-api-researcher` | `docs/research/sonarr-api.md`, `docs/research/radarr-api.md`, `docs/research/media-metadata-mapping.md` |
| `security-reviewer` | The security-relevant sections of `docs/architecture/` and `docs/decisions/`, `docs/limitations/` |
| `test-quality-reviewer` | The task's tests, `docs/architecture/`, `docs/data-model/`, `docs/decisions/` |
| `live-host-verifier` | `docs/testing/jellyfin-12-musl-test-host.md`, `docs/limitations/` |
| `documentation-maintainer` | Every editable current-state surface: `PLANS.md`, `docs/status.md`, `docs/plan/state.json`, `docs/changelog/`, `docs/limitations/`, `README.md`, `docs/release/build-and-release.md` |

## Map

| Path | What it is |
| --- | --- |
| `GOALS.md` | Product goals and success criteria; per-release goal sections |
| `PLANS.md` | The **active** plan only: scope pointer, active phases and tasks, execution orders, milestone table, decision gates, risks, backlog |
| `docs/plan/state.json` | Canonical machine-readable plan/status state |
| `docs/plan/README.md` | Plan-state schema, single-writer rules, and release lifecycle |
| `docs/plan/archive/` | Completed release plans (verbatim history) |
| `docs/status.md` | One-page current shipped state, rendered from `state.json` |
| `docs/architecture/` | Normative architecture, one file per original section; `00-index.md` maps section numbers |
| `docs/data-model/` | Normative data model, one file per original section; `00-index.md` maps section numbers |
| `docs/decisions/` | One ADR per file; `00-index.md` lists them with status |
| `docs/limitations/` | Indexed register of open, accepted, excluded, and resolved limitations |
| `docs/changelog/` | Per-release completed-work history |
| `docs/planning/` | The next accepted release/scope plan (empty when none is accepted) |
| `docs/research/` | Source evidence and alternatives; does not override decisions |
| `docs/release/build-and-release.md` | Release process and per-release artifact identity |
| `docs/testing/jellyfin-12-musl-test-host.md` | Pinned-host live-verification matrix and procedure |
| `docs/reviews/` | Pre-implementation and architecture review records |
| `docs/implementation/` | Per-task worker/reviewer/orchestration reports and phase reviews (evidence) |

## Compatibility stubs

The former single-file documents remain at their old paths as **non-normative
stubs**: `docs/architecture.md`, `docs/data-model.md`, `docs/decisions.md`,
`docs/changelog.md`, `docs/project-status.md`, and
`docs/implementation-readiness.md`. Each contains only a pointer (and, for
architecture and data-model, the section-number map). They exist so that
references in immutable history (`docs/implementation/**`, `docs/reviews/**`,
archived plans, ADR text) still resolve. Do not add content to a stub.

## History is immutable

`docs/implementation/**`, `docs/reviews/**`, `docs/research/**`, `docs/changelog/**`,
archived plans, and ADR text are historical records. Do not rewrite them to match
newer paths, names, or status. Historical reports may cite pre-refactor paths or
line numbers; the stubs resolve the path, not the line number. That is accepted.

## Doc budget

- `docs/status.md`, `PLANS.md`, `docs/INDEX.md`, and `AGENTS.md` are bounded and
  must stay small; they are the always-read set.
- Status facts are edited only in `docs/plan/state.json` and rendered outward.
- New releases add a plan under `docs/planning/`, archive it under
  `docs/plan/archive/`, and add a `docs/changelog/` file. They must not grow the
  active `PLANS.md` with completed phases or the status page with narrative.
- `scripts/check-docs.sh` verifies these rules; run it before committing
  documentation changes. It is a manual gate (the repository has no CI).
