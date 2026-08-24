# 07 — Verification Protocol

Normative. How a claim in this repository earns trust.

Every mathematical or empirical statement in this specification is a **claim**.
A claim is registered, labelled with the evidence it has earned, and may only
change label through the mechanisms below. An unlabelled claim is not
"probably fine"; it is unlabelled.

---

## 1. Scope

**Subject.** This protocol governs claims made *by the specification* — the
modelling decisions, derivations, approximations and estimator choices in
`00-overview/`, `01-adr/`, `02-layers/`, `03-contracts/` and `04-compliance/`.

**Not the subject.** `04-compliance/T0`–`T6` verify an *implementation* against
this specification. That is a different subject with different artefacts and
different gates. The two never share a gate. A `T`-level test is itself a claim
and is registered here like any other.

**Reflexive.** All five mechanisms apply to every empirical claim, including the
output of this project's own prototypes. A study run to defend the specification
is evidence under exactly the rules a study run to attack it is.

**Regulatory primitives are out of scope.** A regulatory primitive — any
formula, rate, gate, qualification rule or product parameter *as transcribed in
this repository* — is given. It is never re-verified, never researched and never
masked. Everything done *to* a primitive is a modelling decision and is in
scope. See §8.

---

## 2. Claims, classes and evidence labels

Every claim carries an id `CLM-NNN`, a class, a blast radius and an evidence
label. The register is `07-verification/claims.yaml`.

| Class | Meaning |
|---|---|
| `analytic` | True by derivation from the primitives, the conventions and the physics. No datum can refute it; only a derivation can. |
| `empirical` | True or false of the world. Requires a band and a frozen procedure. |
| `regulatory` | A transcribed primitive. Given. |

### 2.1 The five evidence labels

| Label | What it requires as proof |
|---|---|
| `derived` | A technical note in `06-theory/` carrying the analytic form: the **sign** of the effect, the **branch condition** that flips that sign, and a **bound** with the assumptions the bound needs. Adjudicated by masked re-derivation (§8) reaching the same treatment from the primitives alone. No data involved. |
| `prototyped` | `derived`, **plus** a frozen procedure (§4), **plus** a whole-grid result inside the pre-committed band in **every** declared cell (§6), **plus** an adjudication verdict of `no-refutation-found` (§5). |
| `empirical-pending` | The claim is empirical, its **band is declared in the owning document**, and no admissible result exists yet. This is the only label a claim may permanently rest at — it is what "no unnamed unknowns" means. A claim at `empirical-pending` whose band is not written into the specification is a protocol violation, not a pending claim. |
| `refuted` | An adjudication verdict of `refuted`, or a grid result outside the band in any cell. Not a resting state: the labelling commit is the same commit that deletes the text (§9). |
| `assumed-declared` | No derivation and no data. The assumption is stated in the owning document together with its blast radius and the acceptance test that would refute it. Admissible only at blast radius `cheap` or `moderate` — never at `expensive-later` or `very-expensive` (§10). |

`unlabelled` is the initial state, not a sixth label.

### 2.2 Regulatory claims carry no label

A claim of class `regulatory` is exempt from labelling. Its register entry
carries the transcription source instead — document, section, and the
regulator's own identifier. Attaching an evidence label to a primitive is a
category error and a machine check rejects it: a label invites re-derivation of
something that is given.

---

## 3. Mechanism 1 — the analytic gate

**No number before its sign.**

A claim is not measured until its analytic form is derived. The derivation must
state three things:

1. the **sign** of the effect,
2. the **branch condition** under which that sign flips,
3. a **bound** on the magnitude, with its assumptions named.

**Enforcement.** A procedure record (§4) cannot be frozen for a claim whose
register entry is not already labelled `derived`. The gate is a precondition on
freezing, checked by the harness — not an instruction to be remembered.

**Why three parts.** With all three fixed in advance, a measured number that
contradicts the claim contradicts a *named* part of it, and the diagnosis
follows: a wrong sign means the derivation is wrong; the right sign outside the
bound means the bound's assumptions are violated; a sign flip in a region the
branch condition did not predict means the branch condition is incomplete. A
number that merely "disagrees" with an underived claim diagnoses nothing.

---

## 4. Mechanism 2 — the frozen procedure, and void-not-amend

The measurement procedure is content-hashed before data is touched. Changing it
after seeing a result **does not amend the claim; it voids the procedure id and
forces a new one.**

### 4.1 What is hashed

A **procedure record**, `07-verification/procedures/PRC-<12hex>.yaml`, whose
fields are exhaustively:

| Field | Content |
|---|---|
| `claim` | The `CLM-NNN` this procedure measures. Exactly one. |
| `arms` | Every treatment and control configuration, enumerated. |
| `benchmark` | The reference the statistic is taken against. |
| `statistic` | The estimator as an executable expression, or a harness function path with bound arguments. Prose is not a statistic. |
| `inputs` | Series identities, selection predicates and the window — as *predicates*, never as data. |
| `filters` | Every exclusion and every tie-break rule. |
| `grid` | Every free parameter with its enumerated levels, and `n_cells`, the declared cross-product size (§6). |
| `band` | The acceptance band. |
| `consequence` | What happens if the statistic lands outside the band (§7). |
| `seed` | The seed and the seed policy. |
| `harness_sha` | The harness commit the procedure is written against. |

The record is canonically serialised — keys sorted, numbers normalised per
`00-overview/02-conventions.md` §6 — hashed with SHA-256, and the first 12 hex
digits form the id. The record's filename **is** its id, so a record whose
recomputed hash differs from its filename is inadmissible.

**Data hashes are deliberately absent.** Input *data* hashes live in the run
manifest, which is produced after the run. If data hashes sat inside the
procedure record, the procedure could not be frozen before the data was touched,
which is the entire point of freezing it.

### 4.2 When

The procedure record is committed **to trunk** before the study branch is cut,
and the study branch is cut from that commit. The run manifest records its
parent trunk sha; a manifest whose parent does not contain the procedure record
is inadmissible. Back-dating a freeze therefore requires rewriting trunk
history rather than editing a file.

The harness refuses to execute a procedure whose record is absent, whose hash
does not match its id, or whose claim is not `derived` (§3).

### 4.3 What voiding does

Any change to any field of the record produces a different hash and therefore a
**different id**. The old id is not updated, superseded or annotated in place —
it is **voided**.

A voided id is written to `07-verification/voided.yaml`:

```yaml
- id: PRC-9f2c1a80b3de
  claim: CLM-041
  successor: PRC-4b71d0e5ca92
  results_seen: yes | no
  reason: <one sentence: what changed, and why>
```

Voided ids are **never recycled and never re-issued**, on the precedent of
`04-compliance/T1` §10: an id that has appeared in a note, a commit message or
an adjudication must never later mean something else.

### 4.4 Blind and sighted voids

The consequence of a void depends entirely on whether any output of the voided
procedure had been seen.

| | Definition | Consequence |
|---|---|---|
| **Blind void** | The record changed before any output of the procedure was observed — no run, or a run whose outputs the harness sealed and can attest were never read. | The successor is untainted. It may earn any label on its own evidence. |
| **Sighted void** | The record changed after any output was observed, in any cell, at any fidelity. | The successor carries `derived_from_sighted_void`. It **cannot** reach `prototyped` on the data the voided procedure ran against. It rests at `empirical-pending` until executed on a sample disjoint from that data. The full void chain goes into the adjudicator's packet (§5.2). |

Without this asymmetry the freeze protects nothing: a procedure re-specified
after its result is seen is a procedure chosen to produce that result, and
issuing it a fresh id would launder precisely what the mechanism exists to
catch. Voiding is cheap and expected — a blind void costs one line in the
register. Only sight is expensive.

---

## 5. Mechanism 3 — adversarial adjudication, structurally separated

**The agent that builds a study never scores it.** Same construction as `L5`
Phase A being structurally denied the plan (`02-layers/L5-settlement.md` §1).

### 5.1 Roles

| Role | Does | Never does |
|---|---|---|
| **Builder** | Derives, writes the procedure record, runs the study, writes the technical note. | Scores its own study. Edits a verdict. |
| **Adjudicator** | Attempts to refute the claim from the sealed packet. | Sees the builder's reasoning. Proposes a fix. |
| **Owner** | Assembles corpora (§8.2), accepts or rejects, writes ADRs. | Delegates the acceptance. |

### 5.2 The sealed packet

The adjudicator's entire input. It **may see**:

1. the frozen procedure record, verbatim;
2. the run manifest — config hash, input hashes, git sha, solver version, seed;
3. the raw outputs: **every cell of the declared grid**, passing and failing,
   unordered and unannotated;
4. the claim register entry as it stood *before* the study — statement, class,
   blast radius, band;
5. the regulatory primitives as transcribed, and `00-overview/02-conventions.md`;
6. the void chain for this claim, if any (§4.4).

It **must not see**:

1. the technical note's narrative, interpretation or conclusion;
2. the builder's session transcript, commit messages or pull-request description;
3. any prior adjudication of the same claim;
4. the specification's own reasoning about the claim — the same mask as §8;
5. which cells the builder considered headline.

Item 5 is not a courtesy. An adjudicator told which result matters adjudicates
that result; an adjudicator handed an unordered grid has to find the corner
itself, which is the thing worth knowing.

### 5.3 Structural enforcement

Instruction is not separation: an agent with filesystem access reads what it was
told not to. The packet is therefore **assembled by the harness into
`07-verification/packets/<PRC-id>/`**, and the adjudicator session runs with
that directory as its only root. The study branch, the technical note and the
specification are not present on its filesystem.

Packet contents are regenerable and are not committed. The packet **manifest** —
the exact file list with hashes — is committed as
`07-verification/packets/<PRC-id>.manifest.yaml`, so what the adjudicator was
and was not shown stays auditable after the fact.

### 5.4 The verdict

The adjudicator writes `07-verification/adjudications/ADJ-<PRC-id>.md` in a
fixed shape: the refutations it attempted, the refutations that landed, the
cells contradicting the claim, the corners it found, and exactly one verdict:

| Verdict | Meaning |
|---|---|
| `no-refutation-found` | Attacks were run and none landed. Requires the attacks to be enumerated — the adjudicator may not return approval, only failure to refute. |
| `refuted` | An attack landed. Triggers §9 immediately. |
| `underdetermined` | The packet cannot decide the claim. Names what is missing. The claim returns to `empirical-pending`; it does not fall back to the builder's word. |

The verdict is committed **before** the builder sees it. A builder that has read
a verdict may not revise the study; it may only cut a new procedure under §4,
which is a sighted void.

---

## 6. Mechanism 4 — report the whole degrees-of-freedom grid

Every free parameter, arm choice and filter is enumerated in `grid` in the
frozen record, together with `n_cells`, the declared size of the cross product.

- The study runs **every** declared cell. A run producing fewer cells than
  `n_cells` is inadmissible, not partial.
- A deliberately reduced grid is legitimate only if the reduction and its reason
  sit inside the frozen record, and therefore inside the hash.
- The technical note reports **every cell, passing and failing**, as a table.
  Failing cells ship in the note.
- The headline figure is the **worst admissible cell**, never the best.
- A claim reaches `prototyped` only if the band holds in **every** cell.

**Narrowing.** If the band holds in one region and not another, the claim is not
narrowed in place. The original claim is `refuted`, and the restricted statement
is registered as a **new** `CLM` with its own evidence. A claim that quietly
acquires a qualifier once the results are in is a corner wearing a general
statement.

---

## 7. Mechanism 5 — pre-commit the consequence, not just the threshold

`band` and `consequence` are both fields of the frozen record and are therefore
both inside the hash. A threshold without a consequence is decoration; a
consequence added after the result is a negotiation.

The consequence is written as an action on the specification, not as a sentiment:

> If the statistic lands below the band, the tier-3 surface is deleted from
> `02-layers/L3-planner.md` §3 and the ladder terminates at tier 2.

The slogan *freeze the estimator, not the threshold* is misleading read
literally, and is corrected here: the threshold **is** frozen. What the slogan
means is that freezing a threshold alone constrains nothing, because a fixed
threshold with a movable estimator is not a commitment.

---

## 8. Masking rules for re-derivation

The **mathematical argument** is masked; the primitives and the interfaces are
kept. The derivation agent re-derives the treatment from the given formulas, and
the result is diffed against the specification. A disagreement is then
unambiguously a modelling disagreement, never a reading-of-the-law
disagreement. Silent agreement is evidence.

### 8.1 What is visible, what is masked

| Visible — the primitives and the interface | Masked — the argument |
|---|---|
| Regulatory primitives as transcribed: `00-overview/03-mispel-reference.md`, tariff sheets, aFRR product parameters | Every ADR's *Decision*, *Consequences* and *Rejected alternatives* prose for this component |
| `00-overview/02-conventions.md` — units, signs, grid, naming | The owning `02-layers/` section's derivation and its justification |
| Battery physics and asset parameters | Any technical note on the claim |
| The seam signature it must produce: `03-contracts/` **field tables only** — names, types, units, cardinality — with the surrounding prose stripped | Prior adjudications of the claim |
| The claim statement it is asked to derive a treatment for | `07-verification/claims.yaml` beyond that one statement — the other entries' labels, bands and `depends_on` edges are the specification's own view of what is already settled |

The test for the boundary: a document is **visible** if it states what is true of
the world or required at the seam, and **masked** if it states why this
specification chose what it chose.

### 8.2 Who assembles the corpus

The **owner**, per component, into `07-verification/corpora/<component>/`.

Not the builder. A builder assembling its own mask chooses what to hide from
itself, and will hide the parts it least wants challenged — without intending
to, which is worse. The corpus manifest is content-hashed and cited by the
technical note, so the mask is auditable after the fact.

Because primitives are given rather than researched (§1), corpus assembly is a
transcription-and-supply task the owner performs on request. It is never a
research ticket.

---

## 9. Delete → Alert → Grill → Rebuild

Per `CLAUDE.md` rule 3. On a `refuted` verdict:

| Step | Action |
|---|---|
| **Delete** | The flawed text is removed in the **same commit** that applies the `refuted` label. Not commented out, not struck through, not moved to an appendix, not softened. |
| **Alert** | The deletion leaves a **hole marker** where the text was, and every document that cited the deleted text acquires one too. |
| **Grill** | The hole becomes a ticket. The replacement is derived with the owner in the loop, at the promoted depth (§10). |
| **Rebuild** | The replacement lands with a freshly earned label, and the marker is removed in the same commit. |

Text that stays readable gets read, cited and built on. That is how a refuted
argument survives its own refutation, and deletion is the only reliable defence
against it.

### 9.1 The hole marker

```markdown
> [!HOLE] CLM-041 — refuted 2026-08-21 by ADJ-PRC-9f2c1a80b3de. Ticket #47.
> The peak-term proration rule stated here was deleted. Nothing downstream may
> assume a replacement exists. Cited by: `02-layers/L2-valuation.md` §4,
> `03-contracts/C5-settlement-to-state.md` §2.
```

Fixed shape: the claim id, the date, the adjudication id, the live ticket, one
sentence on what was deleted, and the list of citing sections.

### 9.2 Machine checks

Three, run in CI:

1. Every register entry labelled `refuted` has exactly one live hole marker.
2. Every hole marker names an open ticket.
3. **No document cites a section carrying a hole marker unless it carries one
   itself.** This is the check that makes a hole impossible to read past
   silently; without it a hole is a footnote.

### 9.3 The `Holed` status

A part with a hole in it is neither normative nor absent. `README.md`'s status
table therefore takes a third value:

| Status | Meaning |
|---|---|
| **Normative** | Binding as written. |
| **Advisory** | Guidance; may be departed from with reason. |
| **Holed** | Binding except where a hole marker stands. The marked sections have been deleted and nothing may assume a replacement. Carries the count of open holes. |

A part goes `Holed` in the deletion commit and returns to `Normative` only when
its last marker is removed by a rebuild.

---

## 10. Depth by blast radius, and promotion

Blast radius is a property of the claim, copied from `01-adr/README.md` where an
ADR owns it. **Treatment depth** is what this section sets.

| Blast radius | Required treatment | Minimum admissible label |
|---|---|---|
| `very-expensive` | Masked re-derivation (§8) + frozen study (§4) + adjudication (§5) | `derived`, and `prototyped` if the claim is empirical |
| `expensive-later` | Masked re-derivation + adjudication; frozen study only if empirical | `derived` |
| `moderate` | Derivation, unmasked; adjudication optional | `derived` |
| `cheap` | A register line | `assumed-declared` |

`assumed-declared` is inadmissible at `expensive-later` and `very-expensive`.

### 10.1 Promotion on refutation

The register carries `depends_on`, a list of `CLM` ids. Claim `Y` **depends on**
`X` if `Y`'s technical note or owning section cites `X`. The key is omitted when
the list is empty, so an absent `depends_on` means *no dependency recorded* — it
is not a claim that none exists. Because no claim yet carries a technical note,
the graph is currently empty and fills as derivations land.

The machine check enforces what makes the promotion below terminate: every
dependency names a live claim, no claim depends on itself, and the graph is
acyclic.

When `X` is refuted, then for every `Y` with `X` in its transitive `depends_on`:

1. `Y`'s evidence label is reset to `unlabelled` — whatever it held was earned
   on a foundation that has since been deleted;
2. `Y`'s **treatment depth** is promoted one level (`cheap` → `moderate` →
   `expensive-later` → `very-expensive`).

`Y`'s `blast_radius` field is **not** edited: blast radius describes the cost of
being wrong, which a refutation upstream does not change. Promotion is not
reversed when `X` is rebuilt — the neighbourhood has demonstrated that it is a
place where derivations fail, and that is durable information.

---

## 11. Artefacts and identifiers

| Path | Contents |
|---|---|
| `06-theory/TN-NN-<slug>.md` | Technical notes. Numbered independently of ADRs; a note may span several ADRs or none. |
| `07-verification/PROTOCOL.md` | This document. |
| `07-verification/claims.yaml` | The claim register. One entry per claim. Hand-maintained: ids are allocated by appending, never by renumbering. |
| `07-verification/check_claims.py` | The register's machine check — the subset of §12 that is decidable from the register and the specification alone. Stdlib only, no venv. |
| `07-verification/no-claim-sections.yaml` | Specification sections that legitimately own no claim, each with its reason. Input to the coverage check. |
| `07-verification/voided.yaml` | The void register. Append-only. |
| `07-verification/procedures/PRC-<12hex>.yaml` | Frozen procedure records. Immutable once committed. |
| `07-verification/adjudications/ADJ-<PRC-id>.md` | Verdicts. |
| `07-verification/packets/<PRC-id>.manifest.yaml` | What the adjudicator was shown. |
| `07-verification/corpora/<component>/` | Owner-assembled masked corpora. |
| `07-verification/harness/` | The measuring instrument. Harness on trunk; per-claim study code on throwaway branches. Only the note, the fixtures and the register entries land on trunk. |

| Id | Form | Allocation |
|---|---|---|
| `CLM-NNN` | Sequential | Never recycled. An id whose owning text is deleted (§9) or replaced by a narrower claim (§6) is **retired**, listed in `check_claims.py`'s `RETIRED` set, and never reissued. Retirement is not voiding: voiding applies to procedure ids and has its own register (§4.3). |
| `PRC-<12hex>` | Content hash of the procedure record | Determined by content; voided ids never re-issued. |
| `ADJ-<PRC-id>` | Derived from the procedure id | One adjudication per procedure. |
| `TN-NN` | Sequential | Never recycled. |

---

## 12. What is machine-checked

| Check | Rejects |
|---|---|
| Every claim in the specification has a register entry | A claim asserted in prose and never registered |
| No `regulatory` claim carries an evidence label | Re-deriving a given primitive |
| A `PRC` record's recomputed hash equals its filename | An edited frozen procedure |
| A `PRC` exists only for a claim already labelled `derived` | A number before its sign |
| A run manifest's parent trunk sha contains its `PRC` record | A back-dated freeze |
| A voided id never reappears as a live `PRC` | A recycled id |
| A run's cell count equals the record's `n_cells` | A silently truncated grid |
| `prototyped` implies an `ADJ` verdict of `no-refutation-found` | A self-scored study |
| A packet manifest exists for every `ADJ` | An unauditable mask |
| Every `refuted` entry has exactly one live hole marker | A refutation applied on paper only |
| Every hole marker names an open ticket | An abandoned hole |
| No document cites a holed section without carrying a hole marker | Reading past a hole |
| `assumed-declared` never appears at `expensive-later` or `very-expensive` | An assumption where a derivation is required |
| Every `empirical-pending` claim's band appears in its owning document | An unnamed unknown |
| No `depends_on` names a retired or non-existent claim, and the graph is acyclic | A promotion sweep (§10.1) that cannot terminate |

**Live today.** `check_claims.py` implements the checks decidable from the
register and the specification alone: register coverage, id uniqueness and
non-reuse of retired ids, owner resolution, `INV-*` anchoring, section coverage,
band-once-labelled, no label on a `regulatory` claim, and the `depends_on` graph.

`check_units.py` is the second live check, and it is an invariant rather than a
register check: it is `INV-G-02` (`00-overview/02-conventions.md` §5.2, §2.3),
decided from the contract field tables alone. For every numeric field in `C1`–`C6`
it asserts that a dimensioned identifier's unit suffix equals the Unit column of
its own row, that a MW or MWh field carries one of the five declared frame
tokens, and that a dimensionless field carries `—` plus a declared range. It
exists because the audit that deleted `ADR-003` moved unit and frame safety out
of the type system and into the identifier, and a naming rule that no machine
reads is a review convention wearing an invariant's number.
The remaining rows above are gated on artefacts that do not exist yet —
procedure records, run manifests, packets and adjudications — and land with the
harness. Two known gaps carry no check at all and are tracked as tickets rather
than silently deferred:

| Gap | Why it is not yet checkable |
|---|---|
| §2.2's transcription source on `regulatory` entries | The register has no `source` field; the 177 regulatory claims need their regulator identifiers transcribed before a check can require one. |
| §2.1's requirement that an `empirical-pending` band appear *in the owning document* | The check verifies the register's `band` field, not the owning document's prose. Closing it needs the band's prose location fixed by convention. |
