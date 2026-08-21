# Domain Docs

How the engineering skills should consume this repo's domain documentation. This
repo is a specification, not an implementation — the spec itself is the domain
documentation, so the paths below replace the generic `CONTEXT.md` + `docs/adr/`
layout the skills assume by default.

## Before exploring, read these

- **`00-overview/01-*`** — the layer model `L0`–`L5` and the four clocks. Read
  first; everything else is scoped to a layer.
- **`00-overview/02-conventions.md`** — the glossary in this repo: normative
  units, the two sign frames, the 15-minute `SlotId` grid, naming conventions.
  This is the vocabulary source, in place of a root `CONTEXT.md`.
- **`00-overview/03-mispel-reference.md`** — the MiSpel delineation regime.
- **`01-adr/`** — ADRs, named `ADR-NNN-<slug>.md` (`ADR-001` … `ADR-016`). Read
  the ones touching the area you are about to work in. **Not `docs/adr/`.**
- **`01-adr/ADR-015-open-decisions.md`** — the open-decision register. Check it
  before proposing anything in a contested area.
- **`03-contracts/`** — the frozen seams `C0`–`C6`. Anything crossing a layer
  boundary is constrained by a versioned payload here.
- **`RECONCILIATION.md`** — cross-document consistency findings and small open
  questions.

If a file listed above does not exist, **proceed silently**. Don't flag its
absence; don't suggest creating it upfront.

## File structure

Single-context repo. There is no `CONTEXT.md`, no `CONTEXT-MAP.md` and no
`src/` — do not look for them.

```
/
├── 00-overview/        ← layer model, conventions (the glossary), MiSpel reference
├── 01-adr/             ← ADR-NNN-<slug>.md, incl. ADR-015 open decisions
├── 02-layers/          ← L0–L5 internals
├── 03-contracts/       ← C0–C6 frozen seams
├── 04-compliance/      ← T0–T6 test levels
├── 05-implementation/  ← P0 sequencing, P1 agent playbook
├── stubs/              ← illustrative C# type sketches
└── docs/agents/        ← this file, issue-tracker.md, triage-labels.md
```

New ADRs continue the `01-adr/ADR-NNN-<slug>.md` numbering. Do not create
`docs/adr/`.

## Use the repo's vocabulary

When your output names a domain concept — an issue title, a refactor proposal, a
hypothesis, a test name — use the term exactly as defined in
`00-overview/02-conventions.md` and the relevant `03-contracts/` payload. Never
conflate the battery and POI sign frames. Time is `SlotId` on the 15-minute UTC
grid; monthly and annual accounting is Europe/Berlin civil calendar. The
objective is denominated in EUR and nothing else.

If a concept you need has no term yet, that's a signal — either you're inventing
language the spec doesn't use (reconsider) or there's a real gap (note it for
`/domain-modeling`, which extends `02-conventions.md`).

## Flag ADR conflicts

If your output contradicts an existing ADR, surface it explicitly rather than
silently overriding:

> _Contradicts ADR-010 (cross-market tier ladder) — but worth reopening because…_

An open item in `ADR-015` is not a licence to decide it unilaterally: surface the
decision point, per working rule 3 in `CLAUDE.md`.
