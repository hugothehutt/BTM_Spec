# TN-03 — One word, one meaning: the vocabulary rulings

**Ticket:** [#45](https://github.com/hugothehutt/BTM_Spec/issues/45) · **Class:** analytic ·
**Method:** corpus harvest, no data touched · **Branch:** `worktree-wf-45-glossary`

Harvests the vocabulary the seven contracts, six layers, seventeen ADRs, seven
test levels, two technical notes and the verification protocol actually use;
finds where one word carries two meanings; and rules each collision that matters.
The glossary `00-overview/04-glossary.md` carries the resulting definitions and,
in §14, the rulings themselves. This note carries the reasoning and nothing else.

Nothing here is an ADR. §5 lists what the specification gains and loses; the act
is Hugo's.

**NOTE** The corpus this note harvested included
`07-verification/`, since deleted. References below to `PROTOCOL`, to
`claims.yaml` and to the ruling set as a separate data file are historical: they
describe the corpus as it stood, not as it is. Two rulings, `gate` §3.2 and
`primitive` §3.5, were argued against senses `PROTOCOL` owned and now stand on
their remaining senses alone; both are still unpropagated.

---

## 1. Method

Six independent readers, one per corpus slice, each recording every term of
consequence with the meaning **as used** rather than as it ought to be, then
listing every word carrying two or more meanings inside its own slice. Slices
were `00-overview` plus `01-adr`; `L0`–`L1`; `L2`–`L5`; `03-contracts` plus
`stubs`; `04-compliance`; and `05-implementation` plus `06-theory` plus the
then-existing `07-verification`.

Independence is the point. A single reader normalises as it goes — it sees
`stage` in `L2` and `stage` in `L5`, understands both, and never records that
they differ. Six readers who cannot see each other's output record the local
meaning honestly, and the collisions fall out of the merge rather than out of
anyone's judgement.

**278 collisions across the six slices.** Most are ordinary English used
ordinarily and are not vocabulary: `book`, `run`, `cold`, `floor`, `error`,
`margin`. Those are out of scope by construction — the glossary holds terminology
a reader needs defined in order to work, not every polysemous word in the prose.

## 2. What makes a collision rulable

A collision is ruled when **both senses are defined terms**, or when one is a
defined term and the other appears where a reader could take it for the defined
one. That bar produces eleven rulings from 278 collisions.

The bar matters because the alternative does not terminate. `gap` carries eight
senses in `04-compliance` alone, of which three are named EUR quantities and five
are English. Ruling on the English ones would rewrite the prose of every document
and buy nothing; ruling on the three named quantities buys the ability to read a
sentence and know which number it means.

## 3. The rulings

Each ruling names the sense that keeps the word. The losing spelling is retired
into the glossary's §14 table.

### 3.1 `frame` — the sign frame keeps it

`00-overview/02-conventions.md` §1 owns three sign frames and `INV-G-02` encodes
their tokens mechanically, so a machine already depends on the word. `L1` used the
same word first, for the memory-mapped storage artefact per `(series, civil day)`,
with `FrameSpecHash`, `frameContentHash`, frame set and frame family hanging off
it.

The storage sense gives way to **slab**. The deciding argument is that one of the
two is machine-checked and the other is not: a word a checker reads cannot also be
a word a designer reuses freely.

### 3.2 `gate` — the market commit point keeps it

Six senses: the commit points `S0`–`S5`; CI, release and merge checkpoints;
milestone gates `G0`–`G9`; the golden test called "the gate"; the freeze
precondition `PROTOCOL` §2.1 calls the analytic gate; and a *kind of regulatory
primitive* at `PROTOCOL` §1.

`TN-02` §2 already made **Gate** normative for `S0`–`S5`, and the market sense is
the one that appears in every layer document. The process senses become
**checkpoint**, **milestone** and **analytic precondition**.

This is the most expensive ruling in the note — it reaches `P0`, `P1`, `T0`, `T3`,
`T5`, `T6` and `PROTOCOL`. It is worth paying because `T4` §5's report template
carries "at 1/5/15min/gate" and "a release proceeds when…" on one page, meaning
two unrelated things by one word within a screen of each other.

### 3.3 `tier` — the co-optimiser ladder keeps it

Five senses: `Tier 1`/`2`/`3`; cold, warm and hot storage; the §19(2) tariff tier
percentages; `L1`'s research-versus-production split; and degradation escalation.
`P0` §2 puts "solve-time distribution per tier per gate type" three lines from
"the cold tier's order-book retention policy".

Storage becomes **cold store**, **warm store**, **hot store**. The
research-versus-production split becomes **slab family** — `L1` §5 already coined
"frame family" for exactly that, so the concept needed no new word, only the
`frame`→`slab` substitution. The §19(2) tier is a regulatory primitive and keeps
its name, written **tariff tier** and never bare.

### 3.4 `oracle` — the testing term of art keeps it

`T2` §1 rests on the oracle problem: for almost every interesting input nobody
knows the correct output, which is *why* metamorphic testing exists. `T5` §2 uses
the same word for the Tier-1 reference solve, which is precisely a construction
that knows the correct answer.

`T5` gives it up. Two reasons: the term of art is not ours to redefine, and `T5`
§2 already calls the same object "the accuracy reference", so the replacement
costs nothing but a substitution.

### 3.5 `primitive` — the regulatory primitive keeps it

`PROTOCOL` §1 makes "regulatory primitive" load-bearing: it is the word that
separates what is given from what is modelled, and the whole masking regime is
scoped by it. `ADR-008` uses "linearizable primitives" for the five C2 term
shapes. Both appear bare: `PROTOCOL`:285 "the primitives and the interfaces are
kept" is the first, `README`:127 "Five linearizable primitives" is the second.

The C2 sense becomes **term shape**. `L2` §1 and `ADR-008` §2 already say
"shapes"; only `README` and `00-overview/01-system-model.md` carry "primitives"
prominently. Note that `01-adr/ADR-008-linearizable-primitives.md` keeps its
filename: ADR ids and filenames are stable, and renaming one to chase a term
would break every citation for no gain.

### 3.6 `stage` — the measurability level keeps it

`TN-02` §2 reserved `stage` for the measurability level and declared gate-versus-
stage repaired. It was not: two further senses survived, and one document had
never been reached.

- **Composer stage 1–6** (`L2` §4, `ADR-009` §5) becomes **composition step**.
  `compositionOrder` already exists as the C2 field name, so the noun follows the
  field rather than the reverse.
- **Settlement timeline stage** (`L5` §3 — intraday, D+1, M+X) becomes
  **settlement point**. `L5` already spends "Phase" on Phase A and Phase B, so
  that word was unavailable.
- **`README` read "solved at staged market gates"** — the exact conflation
  `TN-02`:68 declares repaired, which had never reached the front page. Renamed
  with batch one.

The composer sense is the one that bites hardest: `L2`:153 writes "at stage 3"
bare, and `S3` is the post-DA rebalance gate.

### 3.7 `V` — the terminal value function keeps it

Four senses, two of them inside one `L2` section: the terminal value function
`V(SOC, peakState, qualState)` at `L2`:78, and the aFRR capacity bid-curve
expected value `V(E)` at `L2`:91. Both are concave functions entering the same
objective, thirteen lines apart, distinguished only by their argument.

`V(E)` becomes **`B(E)`**. `V_del` keeps its subscript — it is already
disambiguated and its name carries its domain. `INV-V-*` keeps its letter: a
family letter and a function symbol occupy different syntactic positions and a
reader has never confused them.

### 3.8 `generation` and `scenarioCount` — the state senses keep them

Two counters both named `generation` and both guarding the staleness of a
snapshot: `L0`'s state version incremented per `Commit`, and `L1`'s
`HotWindow._generation` bumped per refill. The hot window's becomes
**`refillCount`**, which is what it counts.

`scenarioCount` names two different populations: C1 §1's `S`, the reduced ensemble
that reaches the Planner, and `ValueFunctionArtefact.diagnostics.scenarioCount`,
the slow loop's own larger and lower-resolution ensemble. `L0`:401 states they are
different ensembles; nothing in the name does. The artefact's becomes
**`fitScenarioCount`**.

### 3.9 `peak_to_go` — retired outright

Not a collision. `peak_to_go` means the peak **already realised** in the current
accounting period (`00-overview/01-system-model.md`:42) and reads as the peak
remaining to be incurred. A name that inverts its own meaning is worse than a
verbose one.

It is also redundant: `pPoiRealisedPeakMw` is the field, carries its frame and
unit per `INV-G-02`, and is what `C5` §2 actually publishes. `peak_to_go` is
retired with no replacement coined.

### 3.10 `INV-X-07` — two invariants, one id

`L3`:106 defines `INV-X-07` as "every net market position is physically backed".
`L4`:100 defines `INV-X-07` as "re-submitting an `intentId` yields at most one
live order". Unrelated statements, one id. `T1`:178 registers only the first and
does not mention the second; `T5`:387 and `TN-02` §4 both lean on the first.

**The physical-backing sense keeps `INV-X-07`.** It is the registered one, and it
is what kills perfect foresight as a benchmark — a solve that buys and sells the
same delivery period violates the constraint the policy obeys, so it does not
bound the right feasible region. Renumbering it would strand that argument.

The idempotency invariant takes the next free id in its family. `INV-X-08`–`10`
are already allocated in `L4` §5 and are themselves absent from `T1`, so the next
free id is **`INV-X-11`**.

This is a naming act only. Which of the two statements is *correct* is not in
question — both are, separately.

### 3.11 `INV-G-11`–`16` — two disjoint sets, one block

`L0`:658-663 defines `INV-G-11`–`16` as an L0 concurrency and journal family,
inside a contiguous block running to `INV-G-19`. `C6`:200-205 defines the same six
ids as a snapshot family. Neither set appears in `T1`.

**`L0` keeps `INV-G-11`–`19`**, because splitting the block would strand `17`–`19`
with no home. `C6`'s six are renumbered to **`INV-G-20`–`25`**.

Also a naming act. The `INV-G-` prefix means "universal, applied at every seam by
the shared validator" for `01`–`10` and something narrower for everything above,
which `stubs/Envelope.cs`'s `UniversalInvariants.All` — asserted to be "all ten" —
cannot express. That is a register-structure question, not a vocabulary one, and
it goes to the invariant-register ticket.

## 4. What the harvest found that is not vocabulary

Eight findings surfaced by a vocabulary sweep that are not vocabulary defects.
None is ruled here; each is a modelling or register decision with its own
consequences.

| Finding | Where |
|---|---|
| `(16)` and `(28)` each carry two different formulas, equal only under `(12) = 0`, which is asserted rather than modelled | `03-mispel-reference.md`:68,169; `ADR-017`:14,85 |
| Composer step 3 is `Reserve coupling` in `ADR-009`, `Delineation` in `ADR-017`, and `L2` numbers `ReserveCoupling` fourth | `ADR-009`:68; `ADR-017`:77; `L2`:189 |
| `EconomicEffect` totals thirteen in `ADR-009` *before* the two members `ADR-017` presents as additions | `ADR-009`:44; `ADR-017`:69 |
| `λ_SOC` is ruled an output by `ADR-007` and still appears as a composer input term in `ADR-009` | `ADR-007`:41; `ADR-009`:10 |
| `INV-G-19` has three inequivalent trigger sets across three statements, and gates `DEFENSIVE` | `L0`:366,643,666 |
| `INV-S-10` says four buckets zero; its own test asserts five quantities zero | `L5`:493,519 |
| `INV-S-16` asserts `(12) = 0` where §5.1 computes `(12)` unconditionally | `L5`:297,499 |
| `T1` claims to be the single authoritative register and omits 35 invariants; two tests consequently cite the wrong ids | `T1`:3,11,184; `T2`:966,1003 |

The last is the most consequential. `T2` §5.3's zero-gap test — its own text calls
it "the cleanest single test in Settlement" — implements `INV-S-10` and cites
`INV-S-02`/`INV-S-03`, because `INV-S-10` is missing from the register it read.
`RestatementIdempotency` implements `INV-S-09` and is attributed to `INV-S-08`.
An incomplete register does not merely omit; it silently redirects.

Two further items are stub-versus-markdown divergence rather than vocabulary, and
belong with the contract work: `INV-G-12` and `INV-G-16` assert properties of
fields absent from `stubs/C5_StateUpdate.cs`, and `stubs/C5_StateUpdate.cs`:235
computes `forecastErrorEur` by the exact method `C5`:188-193 names, distinguishes
and forbids.

## 5. What the specification gains and loses

**Gains.** A named vocabulary with one meaning per word. Eleven collisions
closed, of which two — `INV-X-07` and `INV-G-11`–`16` — were live id collisions
that a register consumer could not have resolved by reading.

**Loses.** `oracle`, `primitive`, `frame`, `gate` and `tier` each lose a sense
they currently carry, across 81 distinct sites in 24 files — `T5` alone holds 34,
all of them `oracle`. The propagation lands in batches: the ruling enters
glossary §14 and its sites are renamed in one change, so no ruling is recorded
before it is true. Batch one carried `frame`, `stage` and `peak_to_go`; `gate`,
`tier`, `primitive` and `oracle` follow, and two of those need a decision of
their own first.

**Not addressed.** The eight §4 findings, each of which needs its own decision.
`06-theory` remains Advisory: nothing in this note binds until Hugo acts on it.

## 6. Where the words go

Two artefacts, two disjoint jobs, so that neither can state the other's fact.
`00-overview/04-glossary.md` defines what a term means, names the document that
owns it, and records the rulings in §14. This note argues each ruling, in §3, and
glossary §14 anchors into the §3 section that argues it — so those section ids
are load-bearing and must stay stable.

`CLAUDE.md` and `docs/agents/domain.md` both named `00-overview/02-conventions.md`
as the glossary. Both now name the glossary by file, and `domain.md` names no
glossary section number at all, so neither breaks when a document is
reorganised.

`02-conventions.md` is unchanged and remains what it always was: units, signs,
time, naming and numeric policy. It was never a glossary, which is why the
pointers were wrong rather than merely stale.
