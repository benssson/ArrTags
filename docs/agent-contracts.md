# Agent report contracts

This is the single source of truth for **where** agents persist reports, the
**status and severity vocabularies** they use, and the **attempt/overwrite
rule**. Each reviewer and research agent prompt references this file; the worker,
planner, and producer prompts follow it for their report location. The prompt and
this table together are the contract.

Status and severity values are contracts, not free text. Do not invent values.

## Where reports go

1. Persist to the path the orchestrator specifies in the delegation.
2. Otherwise use the default path in the table below.
3. Never overwrite an earlier attempt. Write a new attempt to
   `<report>`.attempt-`<n>`.json` (for example
   `reviewer-report.attempt-2.json`) and preserve the prior file.
4. A release-scope audit (the subject is the release candidate, not one task)
   goes under `docs/implementation/final-review/`.
5. A task's `reports` array in `docs/plan/state.json` lists the evidence reports
   committed for that task, not only the worker report: the base report for every
   kind that exists for the task, and every preserved `.attempt-<n>.json` on disk
   for every kind it lists (not only `reviewer-report.json`).
   `scripts/check-agents.sh` enforces this so the canonical machine state cannot
   omit the review evidence the commit gate requires.

| Agent | Report kind | Default path | Status enum |
| --- | --- | --- | --- |
| `implementation-worker` | worker report | `docs/implementation/<task-id>/worker-report.json` | `COMPLETE`, `BLOCKED`, `NEEDS_RESEARCH`, `NEEDS_USER_INPUT`, `FAILED` |
| `implementation-reviewer` | review report | `docs/implementation/<task-id>/reviewer-report.json` | `APPROVED`, `CHANGES_REQUIRED`, `BLOCKED` |
| `test-quality-reviewer` | test-quality review | `docs/implementation/<task-id>/test-quality-review.json` | `APPROVED`, `PASS_WITH_FINDINGS`, `CHANGES_REQUIRED`, `BLOCKED` |
| `security-reviewer` | security review | `docs/implementation/<task-id>/security-review.json` | `APPROVED`, `PASS_WITH_FINDINGS`, `CHANGES_REQUIRED`, `BLOCKED` |
| `phase-reviewer` | phase review | `docs/implementation/phase-<n>/phase-review.json` | `APPROVED`, `APPROVED_WITH_FINDINGS`, `CHANGES_REQUIRED`, `BLOCKED` |
| `release-reviewer` | release review | `docs/implementation/final-review/release-review.json` | `APPROVED`, `APPROVED_WITH_ACCEPTED_LIMITATIONS`, `CHANGES_REQUIRED`, `BLOCKED` |
| `live-host-verifier` | live verification | `docs/implementation/<task-id>/live-verification.json` | `VERIFIED`, `PARTIAL`, `FAILED` |
| `architecture-reviewer` | architecture review | `docs/reviews/architecture/<subject>.json` | `APPROVED`, `CHANGES_REQUIRED`, `BLOCKED` |
| `jellyfin-expert` | research note | `docs/research/jellyfin-expert/<subject>.json` | — |
| `arr-api-researcher` | research note | `docs/research/arr-api-researcher/<subject>.json` | — |
| `documentation-maintainer` | documentation update (producer; release scope -> `final-review/`) | `docs/implementation/phase-<n>/documentation-update.json` | — |
| `documentation-maintainer` | documentation review (reviewer mode) | `docs/implementation/<task-id>/documentation-review.json` | `CONSISTENT`, `PASS_WITH_FINDINGS`, `INCONSISTENT` |
| `implementation-planner` | planning record | `docs/implementation/planning/<name>.json` | — |

## Shared enums

* **Severity:** `BLOCKER`, `HIGH`, `MEDIUM`, `LOW`, `INFORMATIONAL`. Use
  `INFORMATIONAL`; never `INFO`.
* **Finding status:** `open`, `resolved`, `not_required`, `noted`. Fold
  `accepted`, `closed`, and `informational` into these four.
* Severity and status are uppercase/lowercase exactly as written here.

## Corrective and re-derivation tasks

* After any artifact re-derivation, sweep the current-state surfaces that name
  the artifact identity - `docs/testing/jellyfin-12-musl-test-host.md`,
  `docs/release/build-and-release.md`, the release changelog, and ADR
  implementation notes - and mark every occurrence of a superseded hash
  superseded. A superseded hash must never be presented as current.
* A release candidate with an open corrective finding set does not begin a full
  artifact/live/security re-derivation until a consolidated adversarial review of
  the fix batch reports no further open `BLOCKER`/`HIGH`/`MEDIUM` finding, or the
  user explicitly accepts re-running.
* Corrective work continues the task-ID sequence (single dotted numbers) so the
  state guard keeps parsing and report paths stay keyed by task id.

## Invocation and evidence integrity

* An attempt-0 invocation whose report is absent on disk is retried before it is
  treated as an outcome, and recorded as a retried attempt, not a completed one.
* Destructive pre-fix probes run in an isolated export or worktree, never the
  shared working tree. A no-build test result is valid only after a rebuild when
  another agent may have touched the tree.
* A procedure finding that asserts a causal mechanism is checked against the
  pinned-host research or the source before it enters a canonical procedure
  document; a disproved prior attribution is recorded as withdrawn or superseded.

## Phase review gate conformance

A phase review is the gate that decides whether a phase's stated exit condition
was met. It therefore carries a required `gate_conformance` block, so that the
determination is a recorded artifact rather than prose inside `summary`:

```json
"gate_conformance": {
  "gate_text_audited": "<the Gate <n> text verbatim from PLANS.md>",
  "matches_plan_of_record": true,
  "amendment": null,
  "basis": "<why the applied gate is or is not the plan's gate>"
}
```

* `gate_text_audited` is the gate text as it stands when the gate is applied,
  quoted verbatim. It is the baseline a later audit diffs against.
* `matches_plan_of_record` is true only when the applied gate is the gate the
  accepted plan states, with no criterion weakened, narrowed, or replaced.
* `amendment` is `null`, or an object with `date`, `approved_by` (the user),
  `original`, `amended`, `reason`, citing a recorded plan amendment. An agent may
  not amend a gate or criterion to match a result it produced; only the user may,
  and the amendment is recorded before the gate is applied.
* A phase that cannot satisfy this block is not gateable: report
  `CHANGES_REQUIRED` rather than proceeding with an unrecorded amendment.

## Phase gate obligations

At each phase gate the orchestrator records an explicit disposition - `fix-now`,
`accept-with-register-entry`, or `defer-with-rationale`, the accept being the
user's - for every new security finding the phase raised, before the next phase
starts. A finding left `open` without a recorded disposition is a gate defect.

## Phase-level orchestration record

`docs/implementation/phase-<n>/orchestration.json` aggregates the phase's task
records. It carries `status`, `gate`, `gate_status`, `phase_review`, and
`phase_review_status`, plus the same `subagents` per-invocation shape as the
task-level record (including the nested `execution` object) and an `aggregate`.

* `gate_status` is `MET` once the gate is applied. `PENDING_PHASE_REVIEW` is a
  pre-gate value only; a `COMPLETE` phase must not still record it.
* Records written before this contract used earlier field names (for example
  `phase_complete` and `ready_for_next_phase`, or a flattened `execution`). They
  are history and are not rewritten; see **Enforcement scope**.
* A release phase may use the release review as its gate (the Phase 14 and
  Phase 21 precedent); `phase_review` and `phase_review_status` then point at the
  release review, and there is no separate `phase-reviewer` record.

## Enforcement scope

`scripts/check-agents.sh` enforces the rules in this contract. Rules below apply
to phase records at phase 20 and later; earlier records predate them and history
under `docs/implementation/**` is immutable. The script fails if this number and
its own `APPLIES_FROM_PHASE` disagree.

* **from phase 20** — a phase review carries the `gate_conformance` block above.
* **from phase 20** — a phase-level orchestration record carries `status`,
  `gate`, `gate_status`, `phase_review`, and `phase_review_status`, and does not
  record `gate_status: PENDING_PHASE_REVIEW` while `status` is `COMPLETE`.
* **from phase 20** — a phase review and a release review carry an
  `agent_process` section with `scope`, `measurements`, and `observations`. It is
  advisory and is excluded from the completeness and gate determination.
* **all phases** — a task that has a `worker-report.json`, and is not listed in
  `docs/implementation/review-exemptions.json`, lists its `reviewer-report.json`
  in `docs/plan/state.json` `reports`. Tasks predating the per-task report
  convention have no task directory and are not affected.
* **from phase 20** — a task's `orchestration.json` carries a `conformance`
  block, and when `conformance.required` is true the cited reports exist, are
  backed by a recorded specialist invocation, and that invocation precedes the
  task's first `implementation-worker` invocation. See **Task conformance
  precondition**.
* **from phase 20** — a task's `reports` array in `docs/plan/state.json` lists
  every on-disk preserved `.attempt-<n>.json` for each report kind it lists, plus
  the base report for each kind that exists. See **Where reports go** rule 5.
* **from phase 26** — a phase-level orchestration record carries a `subagents`
  array and an `aggregate` whose numeric fields equal the sums recomputed from
  the task-level records. Phases before 26 predate this rule and are not
  rewritten.

`scripts/check-agents.sh` enforces both rules and fails if its scope constants
disagree with the phases stated here.

## Task conformance precondition

A task whose acceptance criteria depend on an unverified Jellyfin platform
behaviour, a provider API contract, or the implementability of an ADR clause must
have that verified *before* the worker is dispatched. Research that arrives after
implementation is rework against sunk work, not a precondition.

Every task's `docs/implementation/<task-id>/orchestration.json` therefore carries
a `conformance` object with `required` (`true`/`false`), a `reason`, and a
`reports` array:

* `required: false` — the task depends on nothing unverified; `reports` is empty
  and `reason` says why.
* `required: true` — `reports` lists the conformance artifacts, each of which must
  exist and be backed by a `jellyfin-expert`, `arr-api-researcher`, or
  `architecture-reviewer` invocation recorded **before** the first
  `implementation-worker` invocation in the same file.
* A worker that discovers the dependency mid-task returns `NEEDS_RESEARCH` or
  `BLOCKED` without implementing. The orchestrator runs the check, then
  re-dispatches. Implementing first and researching afterwards does not satisfy
  this rule.

The worked example, and where the determination is made, is the orchestrator
prompt's *Governing-Clause Conformance Precondition* section.

## Agent-process review

`phase-reviewer` and `release-reviewer` also review how the agents that produced
the phase or release performed, using recorded `orchestration.json` metadata. It is
**advisory**: recorded in its own `agent_process` section, excluded from
`findings`, and never affecting `reviewer_status`, `phase_complete`,
`ready_for_next_phase`, or `release_decision`. Nothing is held open for a harness
opinion. `phase-reviewer` scopes to its own phase; `release-reviewer` to the
release in scope.

`agent_process` carries `scope` (the phase or release just completed),
`measurements` (invocations per task, first-pass approval rate, correction
rounds, cost, duration, and any `BLOCKED`/`FAILED`/`NEEDS_RESEARCH` invocation),
and `observations`. Each observation carries `observation`, `evidence` (a
non-empty array of record paths), `proposed_change` (a concrete file and change —
"the worker should be more careful" is not a proposal), and `rationale`. An
observation requires evidence; a hunch is not an observation, and
`observations: []` is a valid, honest result. Field lists and worked examples
are in the two reviewer prompts.

## Review exemptions

A task may omit the `implementation-reviewer` report only when it is listed in
`docs/implementation/review-exemptions.json` with a reason and the covering
review. `scripts/check-agents.sh` enforces the pairing for every other task.

## Read-only reviewers

Review, research, and advisory agents deny the `edit` action (the Edit/write/
patch tools) and allow only their report directory and owned temporary directory,
via each agent's `permissions` frontmatter. `shell` remains available for
building, testing, diffs, and inspection, so this prevents direct file edits, not
shell-based mutation; treat it as a guardrail, not a full sandbox. The allow-list
is the report directory (for example `docs/implementation/**`), not a single
file.
