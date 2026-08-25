# Domain Docs

How the engineering skills should consume this repo's domain documentation. This
repo is a specification, not an implementation — the spec itself is the domain
documentation, so the paths below replace the generic `CONTEXT.md` + `docs/adr/`
layout the skills assume by default.

## Before exploring, read these

- **`00-overview/01-*`** — the layer model `L0`–`L5` and the four clocks. Read
  first; everything else is scoped to a layer.
- **`00-overview/04-glossary.md`** — the glossary: what each term means, and the
  document that owns the concept. This is the vocabulary source, in place of a
  root `CONTEXT.md`. §2 records where one word carried two meanings and which
  meaning kept it; §15 lists the retired spellings.
- **`00-overview/02-conventions.md`** — normative units, the three sign frames,
  the 15-minute `SlotId` grid, naming and numeric policy. Rules, not
  definitions.
- **`00-overview/03-mispel-reference.md`** — the MiSpel delineation regime.
- **`01-adr/`** — ADRs, named `ADR-NNN-<slug>.md` (`ADR-001` … `ADR-017`). Read
  the ones touching the area you are about to work in. **Not `docs/adr/`.**
- **`03-contracts/`** — the frozen seams `C0`–`C6`. Anything crossing a layer
  boundary is constrained by a versioned payload here.

If a file listed above does not exist, **proceed silently**. Don't flag its
absence; don't suggest creating it upfront.

## File structure

Single-context repo. There is no `CONTEXT.md`, no `CONTEXT-MAP.md` and no
`src/` — do not look for them.

```
/
├── 00-overview/        ← layer model, conventions, MiSpel reference, glossary
├── 01-adr/             ← ADR-NNN-<slug>.md
├── 02-layers/          ← L0–L5 internals
├── 03-contracts/       ← C0–C6 frozen seams
├── 04-compliance/      ← T0–T6 test levels
├── 05-implementation/  ← P0 sequencing, P1 agent playbook
├── 06-theory/          ← TN-NN-<slug>.md technical notes
├── 07-verification/    ← PROTOCOL.md, the claim register, the machine checks
├── stubs/              ← illustrative C# type sketches
└── docs/agents/        ← this file, issue-tracker.md, triage-labels.md
```

New ADRs continue the `01-adr/ADR-NNN-<slug>.md` numbering. Do not create
`docs/adr/`. Technical notes are numbered independently of ADRs: a note may span
several ADRs or none.

## Use the repo's vocabulary


## Flag ADR conflicts

If your output contradicts an existing ADR, surface it explicitly rather than
silently overriding:

> _Contradicts ADR-010 (cross-market tier ladder) — but worth reopening because…_

Per working rule 3 in `CLAUDE.md`: surface the decision point rather than
deciding it unilaterally.
