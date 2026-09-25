# Plan state and conventions

`docs/plan/state.json` is the canonical machine-readable project status. The
human surfaces `docs/status.md` and the `PLANS.md` milestone table are generated
from it. Read this file before editing plan state.

## Single-writer rule

| Surface | Written by |
| --- | --- |
| `docs/plan/state.json` | worker (task status/reports), orchestrator (phase gate/tag, release), documentation-maintainer (reconciliation) |
| `PLANS.md` active scope, task checkbox/status token | worker, orchestrator, planner |
| `docs/status.md`, `PLANS.md` milestone table | **generated** — `dotnet run scripts/render-docs-state.cs` |
| `docs/changelog/<release>.md` | worker / documentation-maintainer |
| `docs/limitations/` | worker / documentation-maintainer |

Never hand-edit a generated block. If `PLANS.md` and `state.json` disagree, stop
and reconcile before proceeding; `scripts/render-docs-state.cs --check` reports
disagreements and `scripts/check-docs.sh` runs it.

## `state.json` shape

```jsonc
{
  "schema": 1,
  "updated_utc": "<iso8601>",
  "active_scope": null,              // or "docs/planning/v1.2.md" when accepted
  "active_scope_note": "<one line>",
  "releases": [ { "version", "tag", "status", "phases": ["1", "..."] } ],
  "current_release": {
    "version": "1.1.0.0", "tag": "v1.1.0", "status": "COMPLETE",
    "published": false,
    "artifact": { "path", "bytes", "sha256", "md5", "entries" },
    "suite": { "default": { "failed", "passed", "skipped", "total" },
               "host_guarded": { ... }, "forced_native": { ... } },
    "live_verification": "<path>", "security_review": "<path>",
    "release_review": "<path>", "publish_note": "<one line>"
  },
  "phases": [
    {
      "id": 15,
      "name": "<phase name>",
      "release": "1.2.0",
      "status": "PLANNED",
      "gate": "Gate 15",
      "tag": "v1.2.0-phase15",       // set when the phase gate passes
      "execution_order": ["15.1", "15.2"],
      "tasks": [
        { "id": "15.1", "done": false, "name": "<task name>",
          "reports": ["docs/implementation/15.1/worker-report.json"] }
      ]
    }
  ],
  "limitations": { "open": [], "resolved": [], "accepted": [] }
}
```

Status vocabulary: `PLANNED`, `IN_PROGRESS`, `COMPLETE`, `BLOCKED`, `DEFERRED`,
`OUT_OF_SCOPE`. The orchestrator treats a task as complete only when `state.json`
says `COMPLETE` and the `PLANS.md` checkbox is `[x]`.

## Lifecycle

1. **New scope.** The accepted plan is placed in `docs/planning/`. The planner
   sets `active_scope` and adds phases (continuing the global numbering) to
   `PLANS.md` and `state.json`, plus the proposal record under
   `docs/implementation/planning/`.
2. **Task.** The worker records the implementation in
   `docs/implementation/<task-id>/`, sets the task's status/report in
   `state.json`, ticks the `PLANS.md` checkbox, and adds the changelog and
   limitations updates.
3. **Phase.** After the phase review passes, the orchestrator sets the phase
   `status`, `gate`, and `tag` in `state.json`, regenerates the status surfaces,
   and creates the tag.
4. **Release.** After the release review passes, the orchestrator sets
   `current_release` and the matching `releases[]` entry.
5. **Archive.** When a release completes, move its phases from `PLANS.md`
   verbatim to `docs/plan/archive/<release>.md`, set `active_scope` to `null`
   with an explanatory `active_scope_note`, and regenerate.
