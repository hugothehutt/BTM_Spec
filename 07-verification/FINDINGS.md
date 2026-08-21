# Claim register — findings

Answer record for the *Extract the claim register* ticket. The register itself is
`claims.yaml`; the vague entries are listed in `VAGUE.md`; the machine check is
`check_claims.py`.

---

## 1. What was built

| Artefact | Contents |
|---|---|
| `claims.yaml` | 3054 claims, generated. One entry per claim the specification asserts. |
| `_extract/*.tsv` | The transcription source, one fragment per corpus. Reviewable line by line. |
| `_extract/gen.py` | Assembles `claims.yaml` from the fragments; assigns `CLM-NNNN` in reading order. |
| `check_claims.py` | The coverage test. Stdlib only, no venv. |
| `no-claim-sections.yaml` | Sections that legitimately own no claim, each with a reason. |
| `VAGUE.md` | The 104 claims that are not testable as written. |

`check_claims.py` reports **0 failures**: 3054 claims, 100 declared invariants all
anchored, all 258 top-level sections of the five in-scope corpora either owning a
claim or explicitly exempted.

### The mechanical test

Same trick as the `W0` field-table-to-DTO test (`05-implementation/P0` W0 DoD (a)):
parse the specification markdown, derive what must be covered, assert
correspondence with the register. Six checks — schema and enum validity; `CLM` id
uniqueness and non-recycling; every `owner` resolving to a real section of a real
file; every `INV-*` declared anywhere anchored to at least one claim; every `##`
section owning a claim or being exempted with a reason; an acceptance band required
once an empirical claim is labelled.

The fifth check is the one that earns its keep: a new section cannot be added
without either registering its claims or writing down why it has none.

---

## 2. Claim count by class and by blast radius

|  | analytic | empirical | regulatory | **total** |
|---|---|---|---|---|
| very-expensive | 1665 | 41 | 125 | **1831** |
| expensive-later | 261 | 8 | 33 | **302** |
| moderate | 756 | 51 | 19 | **826** |
| cheap | 88 | 7 | 0 | **95** |
| **total** | **2770** | **107** | **177** | **3054** |

2724 claims take their blast radius from an owning ADR; 330 are argued, having no
ADR that owns them.

**The class split is the first finding.** 91% of the specification is `analytic` —
claims that are true by derivation or construction and therefore falsifiable by
argument alone, at no data cost. Only 107 claims (3.5%) are `empirical`. The
verification programme is overwhelmingly a derivation exercise, not a measurement
exercise, and the data-blocked fraction is small enough that no ticket needs to
wait for a data pipeline.

**Where the empirical claims sit is the second finding.** They concentrate in
`ADR-004` (12 of the 41 very-expensive empiricals) and `ADR-005` (8) — performance
and correlation-structure assertions about the belief store and the joint
ensemble. These are the load-bearing empirical claims and they are the ones whose
truth the reference model would have to establish.

---

## 3. Ranked list — the order the tickets should run in

Primary key blast radius; secondary key independence, and within that, the density
of unearned claims (`vague` and `empirical` per very-expensive claim) rather than
raw mass. Raw mass mostly measures how much prose a document spent.

| # | Area | Map ticket | VE claims | vague | empirical | Why here |
|---|---|---|---|---|---|---|
| 1 | Belief store, `ADR-004` | *Re-derive the belief store's requirements* | 272 | 13 | 12 | Highest unearned density of any very-expensive area, and everything reads through it. Its 12 empirical claims are performance assertions on which the affordability of the whole scenario axis rests. |
| 2 | Uncertainty representation, `ADR-005` | *Re-derive the uncertainty representation* | 85 | 7 | 8 | Worst vague ratio of any very-expensive area (8.2%), and it sits upstream of every risk term in the objective. `INV-D-07` lives here. |
| 3 | Invariant register | *Revise the invariant register* | — | — | — | Cheap, and it blocks the interpretation of everything else: the register has four structural defects (§4). Now fully specified by §4, so it can run immediately. |
| 4 | Settlement decomposition, `ADR-009` | *Re-derive the settlement decomposition* | 93 | 11 | 5 | Worst vague ratio in the whole register (11.8%). Every "early warning signal" claim about `unexplainedRatio` is unquantified. |
| 5 | Determinism, `ADR-013` | *Challenge determinism* | 276 | 2 | 2 | Enormous mass, tightly stated — but `L4` §6 declares the simulator's determinism a **named gap**, and `T6` §2.3 shows the entire lookahead audit is purchased by determinism. The gap is the work, not the prose. |
| 6 | Feedback resolution, `ADR-006` | *Challenge feedback resolution and the one-tick lag* | 185 | 1 | 1 | Large and almost entirely analytic. Cheap to adjudicate, and it is the claim the acyclicity of every seam test rests on. |
| 7 | Linearizable primitives, `ADR-008` | *Challenge the linearizable primitives* | 156 | 4 | 5 | The five-shape algebra and the binary-count claims. Mostly derivable. |
| 8 | Delineation, `ADR-017` | *Delineation: confirm the layer interactions* | 324 | 4 | 1 | Largest mass in the register and the **least vague per claim** (1.2%). Consistent with the map's assessment that the delineation modelling is the most trusted work in the repository — the register corroborates it quantitatively. |

Two areas fall outside the ranking because they are not single tickets:

- **`argued` — 234 very-expensive claims with no owning ADR.** The POI bridge, the
  EUR-only objective, the two-phase settlement wall, and C4's
  reconstruct-truth-from-outcome property are all load-bearing and own no decision
  record. Not a defect, but worth knowing before an ADR is written over the top of
  one.
- **`ADR-014` (61 VE, 4 vague) and `ADR-010` (35 VE, 0 vague)** rank low on
  very-expensive mass because most of their claims are `moderate`. They are not
  low-priority, only low-blast-radius.

---

## 4. Structural defects found while transcribing

These are findings about the specification, surfaced by the act of registering
every claim in one place. Each is registered verbatim in `claims.yaml` — the
register records what the documents say, including where they disagree.

### 4.1 `INV-G-11` … `INV-G-16` are defined twice, with different meanings

`02-layers/L0` §9 and `03-contracts/C6` §8 both allocate this range:

| ID | L0 §9 | C6 §8 |
|---|---|---|
| `INV-G-11` | No component reads an L0 section written in the same `tickId` | Denormalised ledger views reconcile with `entries` |
| `INV-G-12` | Exactly one `Commit` per `tickId`, against the expected generation | `stateAsOf < snapshotAt`; `lagSlots` consistent |
| `INV-G-13` | A tick that does not reach `Commit` leaves L0 byte-identical | The snapshot is immutable for the tick's duration |
| `INV-G-14` | Every payload carries the tick's `StateSnapshot.contentHash` | Every `Null = no` field present or defaulted |
| `INV-G-15` | The L0 journal is append-only | `requiredSocCorridor` non-empty where reserve is non-zero |
| `INV-G-16` | `provisionalPeak ≥ realisedPeak` | On cold start, no speculative intent until reconciled |

Both readings are cited elsewhere by their own documents, so this is not a typo in
one place. Twelve invariants are competing for six IDs.

### 4.2 `T1` is incomplete as the consolidated register

`T1` §1 claims to be "the single authoritative list" of "every invariant defined
anywhere in this specification". It is not:

- Its `INV-G-*` table stops at `INV-G-10`. `INV-G-11` … `INV-G-19` — defined in L0
  §9 and C6 §8 — are absent under either reading.
- Its `INV-S-*` table runs `INV-S-01` … `INV-S-08`, then jumps to `INV-S-16`,
  `INV-S-17`, `INV-S-18`. `INV-S-09` … `INV-S-15` are absent, even though §7's own
  source note cites "`L5-settlement.md` §10 for `INV-S-09`–`INV-S-18`", and L5 §10
  does define all ten.

Seventeen invariants are defined in the layer and contract documents and missing
from the register that claims to consolidate them.

### 4.3 The POI bridge has two inconsistent statements

- `00-overview/02-conventions.md` §1: `p_poi[t] = load[t] − Σ_k (pv_avail[t,k] − q[t,k]) − p_batt[t]`
- `01-adr/ADR-003` §Decision: "exactly one sanctioned conversion between frames,
  `p_poi = load − pv − p_batt`", citing that same conventions section.

ADR-003's form omits curtailment and the per-plant sum. `ADR-016` exists precisely
to establish that the `q`-free bridge is infeasible under an ordinary condition, so
the ADR-003 statement is the stale one. Both are registered.

### 4.4 The contract range is stated as `C0`–`C5`

`00-overview/02-conventions.md` §5 names the contract naming convention as
`C0 … C5`, and `05-implementation/P1-agent-playbook.md`'s orientation table says of
`C0`–`C5` that "the field table is complete and closed". `C6` exists, is normative,
and carries six of its own invariants.

### 4.5 `INV-P-11` and `INV-V-17` are referenced as the next free numbers

`T1` §10 says a new invariant "takes the next free number above the highest
allocated in its family — `INV-V-17`, `INV-P-11`". `INV-V-17` **is** allocated
(provenance flows). `INV-P-11` is not allocated anywhere, so the sentence names one
occupied ID and one free one as if both were free.

### 4.6 The two math gaps the ticket named, transcribed

Both are registered verbatim and flagged `vague`:

- **`INV-D-07`** — "The reduced ensemble preserves each marginal's mean, against
  the full ensemble, within tolerance." No tolerance is given, in `C1` §9, in
  `T1` §3, or in `ADR-005`. `T1` §1.3's tolerance table does not cover it. It is
  the only `HALT`-family invariant downgraded to `warn` + `DEGRADED`, and the
  quantity it bounds — reduction fidelity — is what makes `S` a defensible choice.
- **`INV-S-03`** — "The four error buckets sum to `(planned − realised)` within
  tolerance", severity `warn`. `ADR-017` then adds a `linearizationGap` sub-bucket
  to `modelError` and states that without the split the zero-gap test "fails
  permanently from the first day the delineation regime is active". So the
  register's most valuable test is gated on a tolerance that is nowhere stated, and
  a budget for `linearizationGap` that is nowhere stated either. `T2` §5.9 asserts
  a `linearizationGap` of zero is "as suspicious as one above budget" — with no
  budget defined, neither condition is checkable.

---

## 5. The vague claims

104 of 3054 (3.4%) are not testable as written; the full list is `VAGUE.md`.

| Blast radius | vague | analytic | empirical | regulatory |
|---|---|---|---|---|
| very-expensive | 57 | 30 | 27 | 0 |
| expensive-later | 17 | 8 | 8 | 1 |
| moderate | 22 | 9 | 13 | 0 |
| cheap | 8 | 1 | 7 | 0 |

55 of the 104 are `empirical` — which is 51% of every empirical claim in the
register. **Half of everything the specification asserts about the world is
asserted without a threshold, a band, or a metric.** That is the single most
actionable number here, and it is what the verification protocol's "pre-commit the
consequence, not the threshold" mechanism exists to fix.

The other 49 are `analytic` or `regulatory` claims that are vague for a different reason: they
assert a dominance or a preference without a metric — "strictly more accurate than
marking a scalar", "the seam that carries the most design load", "this is the
correct trade". They are not measurement problems; they are arguments that have
not been made yet.

---

## 6. Deviations from the ticket's schema

Three optional keys were added to the entry schema; the ticket's ten are otherwise
unchanged and always present.

| Key | Why |
|---|---|
| `anchors` | The list of `INV-*`, `ADR-*`, `C*` and `T*` ids a claim carries. Without it the mechanical test cannot assert that every declared invariant is covered — the coverage check has nothing to join on. |
| `br_source` | The ADR whose reversibility was copied, or `argued`. Makes the 330 argued radii auditable rather than indistinguishable from copied ones. |
| `vague` | Present only on the 104 untestable entries. The ticket asks for these as an answer record; carrying the flag in the register makes the list machine-derivable instead of a document that goes stale. |

One scoping rule was needed and is recorded in the register header: **a claim is
registered iff it constrains the engine, its mathematics, its data, or the
regulation.** Prose about how to read the documents is not registered. Without
some such rule the register absorbs every rhetorical sentence in the repository;
with it, vague claims about the *system* are still registered vague, as the ticket
requires.
