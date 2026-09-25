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
