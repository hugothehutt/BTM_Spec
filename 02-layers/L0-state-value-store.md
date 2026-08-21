# L0 — State / Value Store

Everything that must survive a tick, and nothing else. It is the only backwards
path in the system, it is always read at a lag of at least one tick, and it is
the component whose existence makes the within-tick dependency graph acyclic.

**Input:** `StateUpdate` (C5), committed once at the end of a tick; `ValueFunctionArtefact` from the slow loop
**Output:** `StateSnapshot`, taken once at the top of a tick and read by L1, L2, L3, L5
**Purity:** stateful by definition — the only mutable component. Mutation is confined to a single commit point per tick.

---

## 1. Why it exists

The five-layer pipeline is intended to be one-directional, and three dependencies
break that: `λ_SOC` is a dual of the Planner's own problem, `peak_to_go` and the
§19(2) qualification state depend on realised history, and the commitment ledger
is Execution output feeding a later Planner run (ADR-006 Context;
`00-overview/01-system-model.md` §2).

ADR-006's resolution is not "be careful with the ordering". It is structural:

> Add L0, the State/Value Store. It is the only backwards path, and it is always
> read at a lag of at least one tick.

The consequences are worth restating because everything in this document follows
from them:

- **The within-tick graph is a DAG.** Every arrow points right; the single
  backwards edge (C5) is not traversed until the tick has ended. This is what
  makes each seam independently recordable, replayable and property-testable, and
  it is the foundation of `04-compliance/`.
- **The lag is explicit and therefore priceable.** The Planner knows the peak
  state is authoritative only up to `unsettledGapFrom` and can be conservative
  about the gap (§6) rather than pretending it is current.
- **The DAG is mechanically verifiable.** `04-compliance/T3` instruments every
  read and write during a replay and asserts no read of a same-tick write
  (`INV-G-11`, §3.4).
- **λ_SOC becomes an output.** L0 holds `V(SOC, peakState, qualState)` as a
  concave PWL curve; the Planner obtains `λ_SOC` as its subgradient at the
  optimum and reports it for diagnostics (ADR-007, L3 §8).

The alternatives ADR-006 rejected all fail in the same way: a global mutable
service, a fixed-point iteration within a tick, or Valuation calling the Planner
each produce a system where two components can legitimately read different
answers to the same question during the same tick, at which point nothing is
reproducible and no seam test means anything.

---

## 2. Contents

Six items, per ADR-006's table and `00-overview/01-system-model.md` §1. Field
tables mirror C5 exactly; nothing here introduces a field C5 does not carry.

| Item | Written by | Read by | Clock |
|---|---|---|---|
| Realised POI peak per accounting period and tariff window | L5 (via C5 §2) | L2 `PeakView` | `C_accounting` |
| §19(2) qualification state | L5 (via C5 §3) | L2 `TariffView`, slow loop | `C_accounting` |
| Commitment ledger | L5 confirmed / L3 pending (via C5 §4) | L3 | `C_gate` |
| Value function `V(SOC, peakState, qualState)` | slow loop (§5) | L2 `OppCostView` → L3 | `C_slow` |
| Data quality state and degradation mode | central mode computation (via C5 §7) | all | `C_tick` |
| Model artefact versions in force | run manifest | all | run |

### 2.1 Peak state — C5 §2

| Field | Type | Unit | Card. | Notes |
|---|---|---|---|---|
| `realisedPeak` | `PoiPowerKw` | kW | per regime | The epigraph floor for the next tick |
| `realisedPeakSlot` | `SlotId` | — | per regime | When it occurred |
| `periodStart`, `periodEnd` | `SlotId` | — | per regime | Local-calendar derived (ADR-002) |
| `peakIsProvisional` | `bool` | — | per regime | True while meter data is unfinal |
| `unsettledGapFrom` | `SlotId` | — | per regime | Earliest slot not yet settled — see §6 |

Per regime, not per site: a site under `AtypicalHlzf` and a volumetric charge has
two peak accumulators over different slot sets and different period bounds
(ADR-011).

### 2.2 Qualification state — C5 §3

| Field | Type | Unit | Notes |
|---|---|---|---|
| `annualEnergyKwh` | `EnergyKwh` | kWh | Accumulated in the qualification year |
| `annualPeakKw` | `PoiPowerKw` | kW | Denominator of full-load hours |
| `fullLoadHours` | `double` | h | `annualEnergy / annualPeak` (`INV-S-06`) |
| `hlzfPeakKw` | `PoiPowerKw` | kW | Peak within HLZF windows only |
| `qualificationStatus` | `Qualified \| AtRisk \| Lost \| NotApplicable` | — | Per applicable §19(2) path |
| `qualificationMarginHours` | `double` | h | Distance to the threshold |
| `projectedYearEndFlh` | `double` | h | Forward projection from the slow loop |
| `qualCritical` | `bool` | — | Within the configured margin; forces tier escalation and the protective bound |

The margin, not just the status, is what the value function conditions on. A
binary flag tells the engine nothing until it is too late (C5 §3, ADR-011).

### 2.3 Commitment ledger — C5 §4

| Field | Type | Notes |
|---|---|---|
| `entryId` | `string` | |
| `kind` | `SpotPosition \| ReserveAward \| OpenOrder` | |
| `status` | `Pending \| Confirmed \| Settled \| Cancelled` | Load-bearing (ADR-006) |
| `market`, `productId`, `slot`/`block` | — | |
| `signedVolume` | `EnergyKwh` / `ReserveMw` | Market frame |
| `price` | `EnergyPrice` / `CapacityPrice` | |
| `feasibilityRequirement` | `SocCorridor?` | Hard constraint in every degradation mode |

`Confirmed` entries are hard constraints; `Pending` entries are probabilistic
exposure that the Planner models as a scenario-dependent position (L3 §4). This
distinction is what lets the continuous intraday loop re-optimise without either
double-selling or ignoring live exposure.

### 2.4 Value function — produced by the slow loop

The one section that does not arrive through C5.

```
ValueFunctionArtefact {
  specHash        FeatureSpecHash      // pinned in the run manifest
  fittedAt        KnowledgeTime
  validityHorizon SlotSpan
  method          RollingHorizonDE | Sddp | BackwardRecursion
  conditioning    { peakBuckets, qualStates, calendarBuckets }
  curves          map<(peakBucket, qualState, calendarBucket), Breakpoints>
  diagnostics     { scenarioCount, gridResolution, fitError, dualBound? }
}

Breakpoints { (e_i, v_i) : i = 0..n,  slopes strictly decreasing }   // INV-V-11
```

L0 stores the artefact's **identity and the loaded curves**, not a model object.
The curve is a table of breakpoints; evaluating it is a PWL lookup (ADR-001, and
L1 §4.4's three permitted hot-path operations).

### 2.5 Quality and mode — C5 §7

| Field | Type | Notes |
|---|---|---|
| `degradationMode` | `DegradationMode` | Computed centrally, committed here |
| `modeTransitions` | `(SlotId, from, to, cause)[]` | Audit trail for the period |
| `ticksByMode` | `map<DegradationMode, int>` | Lets P&L be conditioned on operating mode |
| `seriesQualitySummary` | `map<SeriesId, QualityStats>` | Feeds data-source SLAs |

Also carried, from C5 §8 and consumed by the slow loop trigger logic (§5.1):
`vSocStale`, `stateDriftSignal`, `refreshRequested`.

### 2.6 Artefact versions in force

The manifest identities of every artefact the run is entitled to read: scenario
generator, reduced ensemble, fill model, activation model, value function, market
calendar, tzdata (ADR-013). L0 holds identities only. The bytes live in the
Belief cold tier (L1 §4) and are addressed by hash.

### 2.7 What L0 does **not** hold

Stated to keep it out.

- **No beliefs.** Forecasts, prices, ensembles and quality *values* live in L1.
  L0 holds only the *summary* quality state that must survive a tick.
- **No plans.** `PlanResult` is recorded for diagnostics and settlement
  (L3 §8, ADR-013); it is not state. The only Planner output that survives a tick
  is a ledger entry.
- **No artefact contents.** Identities only (§2.6).
- **No wall-clock time** (`INV-G-05`).
- **No derived caches.** Anything recomputable from the above is recomputed, so
  that a restored snapshot is unambiguously equal to the one persisted.

### 2.8 A note on the ledger's writers

ADR-006 lists the ledger's writer as "L5 (confirmed) / L3 (pending)", but also
states that L0 is written only at the end of the tick. Both hold, because
**there is exactly one writer of L0 — the tick driver's `Commit` — and the
`StateUpdate` it commits is assembled from two sources**:

- L5 owns the settled and confirmed sections: fills, awards, activations, meter
  reality, P&L attribution, error decomposition (C4 → C5).
- The driver reconciles submitted intent (C3) against Execution's dispositions
  (C4 §3) to produce the `Pending` / `OpenOrder` delta. C4 deliberately carries
  no Planner reasoning (C4 §8), so L5 cannot see a still-live order; the driver
  can, because it holds both payloads.

This preserves C4 §8 and ADR-006 simultaneously, and keeps "one backwards edge"
literally true: two contributors, one payload, one atomic commit.

---

## 3. The snapshot / commit protocol

### 3.1 The tick

```
Tick(t, asOf):
  1.  s = L0.Snapshot()                          // immutable, hashed, generation g
  2.  b = L1.Snapshot(t, asOf)                   // C1
  3.  v = L2.Evaluate(in b, in s)                // C2
  4.  p = L3.Solve(in v, in s)                   // C3 + PlanResult (recorded)
  5.  x = L4.Execute(p)                          // C4
  6.  u = L5.Settle(in x, in s) ⊕ driverLedgerDelta(p, x)   // C5 StateUpdate
  7.  L0.Commit(u, expectedGeneration: g)        // atomic; generation → g+1
```

Steps 1 and 7 are the only interactions with L0. Everything between them operates
on the value `s`.

### 3.2 Read side: `StateSnapshot`

`L0.Snapshot()` is the **only** read API. There is no `L0.Get(field)`, no
property accessor, no injected store reference. A component holds a
`StateSnapshot` value; it does not hold L0.

- `StateSnapshot` is an immutable record. Nothing in it aliases a live structure.
  The ledger is copied — it is `O(open orders + awarded blocks)`, tens of entries.
  The value function is referenced by hash into an immutable, content-addressed
  curve cache, which cannot change under the reader.
- It carries a `contentHash` and appears in the `inputHashes` of every payload
  produced during the tick (C0 §1, ADR-013). This is what puts the state version
  into the Merkle chain: an artefact records which generation of state it saw.
- It carries `generation` and `asOfGeneration` so a consumer can state the lag it
  is working with, which is what makes the lag priceable rather than hidden.

This is the same structural move as ADR-004's missing `asOf` overload. To read a
same-tick write, a developer would have to add a getter to L0 — a visible change
to this component — not merely forget to be careful in theirs.

### 3.3 Write side: `Commit`

- **One commit per tick.** `Commit` takes the expected generation and fails if it
  does not match. A mismatch means two ticks overlapped, which is a driver defect
  and goes to `HALT`. `INV-G-12`.
- **Atomic.** The update is applied to all sections or to none. There is no
  partially-updated intermediate state visible to any reader.
- **Validating.** C5's invariants (`INV-S-01` … `INV-S-08`) plus C0 §3's universal
  set are checked on receipt. Consumer-side validation failure is `HALT`
  (C0 §4) — a contract violation means the producer is in an unknown state.
- **Ordering.** `INV-S-08`: state is never written for a slot earlier than the
  previous update's `effectiveFrom`, except as an explicit `revisionOf` (§7).

### 3.4 How "no same-tick read" is enforced and audited

Three layers, in increasing strength:

| Mechanism | Catches | Cost |
|---|---|---|
| Type system — `StateSnapshot` is a value with no back-reference; L0 exposes no getter | The accidental case, at compile time | free |
| Reflection test over L0's public surface asserting no read method other than `Snapshot()` | Someone adding a convenience accessor later | one test |
| `T3` acyclicity audit — instrument every read and write during a replay as `(tickId, component, section, direction)` and assert no read of a section written in the same `tickId` | The deliberate or indirect case, including via a shared helper | a replay with recording on |

The audit's own overhead must not change results: the audit day is run with and
without instrumentation and artefact hashes compared. A fourth, independent check
falls out of the Merkle chain for free — every payload's `inputHashes` must
contain the tick's `StateSnapshot.contentHash` (`INV-G-14`), so a same-tick read
would have to reference a generation hash that did not exist when the payload was
sealed.

---

## 4. Durability and restart

L0 is the entire engine state. Everything else is either pure (L2, L3, L5), a
read over immutable data (L1), or external (L4). An engine restart therefore
loads a `StateSnapshot` and continues (ADR-006 Consequences).

### 4.1 Persistence format

Content-addressed, per ADR-013, with an append-only journal supplying order:

```
state/
  blobs/<sectionContentHash>            # immutable section blobs
  journal.log                           # append-only, checksummed records

journal record:
  tickId, generation, parentSnapshotHash, updateHash,
  snapshotHash, sectionHashes[6], recordChecksum
```

Sections (§2.1 – §2.6) are hashed and stored **independently**, and a snapshot is
the ordered list of its section hashes. Peak state and the value function change
rarely; the ledger and the quality summary churn every tick. Structural sharing
therefore means a typical commit writes one or two small blobs and one journal
record, not a full state image. Restart hydrates by fetching sections by hash.

### 4.2 Restart

```
1. Scan journal.log backwards to the last record with a valid checksum.
2. Verify every referenced section blob resolves and rehashes correctly.
   Any mismatch → HALT (ADR-014: "state store unreadable").
3. Hydrate StateSnapshot at generation g.
4. Reconcile against Execution (§4.4).
5. Resume at tickId + 1. L1's cursor is repositioned from asOf and the hot
   window refilled from warm/cold; nothing else needs restoring because
   L2, L3 and L5 are pure.
```

`INV-G-18`: a restored snapshot is field-identical to the one persisted and its
recomputed content hash matches.

### 4.3 Crash consistency

**The journal record is the commit point**, and it is written after the section
blobs are durable.

| Crash point | State on disk | Outcome on restart |
|---|---|---|
| During steps 1–6 of the tick | generation `g`, unchanged | The tick was never committed; it replays |
| After section blobs, before the journal record | generation `g`, plus orphan blobs | Orphans are unreferenced garbage, collected by hash sweep; the tick replays |
| During the journal append | torn tail | Tail truncated at the last valid checksum; the tick replays |
| After the journal record | generation `g+1` | The tick is committed; the engine resumes at `tickId + 1` |

Blobs are written to a temporary name, fsynced, then renamed; the journal is
appended with a per-record checksum and fsynced. `INV-G-13`: a tick that does not
reach `Commit` leaves L0 byte-identical at the previous generation.

Because a replayed tick begins from the identical `StateSnapshot` and identical
recorded belief inputs, and because L2/L3/L5 are pure and the solver is
deterministic (ADR-013), the replayed tick produces byte-identical outputs to the
one that was lost. That is the whole reason "the tick replays" is an acceptable
answer.

### 4.4 The one qualifier on "L0 is the entire state"

L0 is the entire *engine* state. It is not the entire *world* state, and the
venue is the authority on what was submitted. A crash between step 4 (orders
submitted) and step 7 (commit) leaves orders live at Execution that the restored
ledger does not know about. Replaying the tick blindly would re-submit them.

So restart performs a **reconciliation pass** before the first tick:

1. Query Execution for live and recently-terminal orders and awards for the asset.
2. Diff against the restored ledger.
3. Insert every unmatched acknowledged order or award via a synthetic
   `StateUpdate` tagged `Reconciliation`, with `effectiveFrom` set to the
   restored generation's slot.
4. Commit it as generation `g+1`; the replayed tick becomes `g+2`.

The reconciliation update is a first-class, recorded artefact, not a repair
script. `INV-X-06` (every award has a ledger entry by end of tick) is then
checkable across a restart boundary rather than being vacuously true because the
award was forgotten.

---

## 5. The slow loop

The component that produces `V(SOC, peakState, qualState)`. It is not a layer and
it is not in the tick path: it runs on `C_slow`, reads L0 plus cold-tier
artefacts, and writes exactly one thing — a versioned `ValueFunctionArtefact`
whose identity is committed to L0.

**It never blocks a tick.** `00-overview/01-system-model.md` §3: "C_slow output is
an input, never a dependency." If it has not run, the Planner uses the last valid
`V` with a staleness penalty (L2 §6, ADR-014).

### 5.1 Triggers

| Trigger | Source | Condition | Kind |
|---|---|---|---|
| Periodic | `C_slow` | Daily, at a configured local time, after the day's ensemble artefact is published | staleness |
| Validity expiry | C5 §8 `vSocStale` | `asOf − fittedAt > validityHorizon` | staleness |
| State drift | C5 §8 `stateDriftSignal` | Above a configured threshold | staleness |
| Explicit request | C5 §8 `refreshRequested` | Set by Settlement | staleness |
| Accounting rollover | `C_accounting` | `periodEnd` crossed; `realisedPeak` resets | **invalidation** |
| Qualification transition | C5 §3 `qualificationStatus` change, or `qualCritical` set | Discrete conditioning state changed | **invalidation** |
| Regime change | `MarketCalendar` / ADR-011 | A regime becomes active or inactive | **invalidation** |

The distinction in the last column is load-bearing and is easy to collapse by
accident. A **stale** curve is an out-of-date approximation of the right curve:
it attracts `stalenessPenalty` shrinkage of its slopes (L2 §6) and the engine
carries on. An **invalidated** curve is the *wrong* curve — one fitted under
`Qualified` says nothing useful about an `AtRisk` world, and one fitted against
last month's `realisedPeak` prices a floor that no longer exists. Invalidation
forces `DEFENSIVE` until a refit lands (`INV-G-19`), because shrinking the slopes
of a curve conditioned on the wrong state does not make it less wrong.

`stateDriftSignal` is what makes the loop event-driven rather than purely
periodic (C5 §8): a large peak event invalidates `V` faster than the calendar
does.

### 5.2 Horizon and grid

The horizon must be long enough to see the end of the accounting period —
otherwise the truncation the value function exists to price is present inside the
value function itself.

```
H_slow ≥ max over active regimes of (periodEnd − t)   +  a tail margin
```

For `AnnualLeistungspreis` and `IntensiveUse` that is up to a year of
quarter-hours (~35,000 slots). This is why the slow loop cannot simply run the
Planner's formulation at a longer horizon, and why it needs a state-space method
of its own.

Two accommodations, both declared artefact parameters rather than implementation
details:

- **Coarsened time.** 15-minute resolution in the near field, hourly or 4-hourly
  in the far field. The coarsening schedule is part of the `FeatureSpecHash`.
- **A longer-horizon, lower-resolution ensemble.** Generated by the *same* joint
  model as the tick-loop ensemble (ADR-005), so the dependence structure is
  consistent between what `V` was fitted against and what the Planner optimises
  against. Assembling the slow-loop ensemble from marginals would reintroduce
  exactly the bias ADR-005 exists to prevent, at the horizon where it matters
  most.

### 5.3 Output shape and concavity

`V` is fitted **conditional on the discrete state** and is concave in SOC given
that state (ADR-007, ADR-011). Concavity is enforced by projecting the fit onto
the concave hull and validated at the contract boundary (`INV-V-11`, strictly
decreasing slopes). Because `V` is concave and enters as a maximisation term, the
PWL needs no binaries and the LP relaxation is exact — which is the property that
makes it affordable in the tick.

Genuine non-concavity — the §19(2) qualification cliff — is **not** smoothed away.
It is represented as a separate discrete dimension of the conditioning, with a
concave curve inside each cell. If a fit is non-concave and the projection loses
material value, that is a signal that a conditioning dimension is missing, not
that the projection needs loosening.

Every cell of the conditioning table that is reachable must have a curve, or a
declared fallback cell. An unreachable-in-training but reachable-in-operation cell
is exactly where the engine will be when something unusual happens.

### 5.4 Candidate methods

There is no settled answer here, and pretending otherwise would be the wrong kind
of confidence. Three credible approaches, with their real costs:

| Method | Mechanism | Strengths | Weaknesses |
|---|---|---|---|
| **Rolling-horizon deterministic equivalent** | Solve the multi-stage problem over the reduced ensemble as one deterministic-equivalent LP/MILP, parametrically in initial SOC; `V(e)` is the optimal value as a function of that SOC | Reuses the Planner's own formulation and `IOptimizationBackend`, so there is no second physics model to keep in sync; concave by construction in the LP case; fastest route to something trustworthy | Perfect foresight within a scenario: it ignores non-anticipativity beyond whatever stage structure is imposed, so it is **systematically optimistic**, not merely noisy. Size grows as `S × H_slow`, which bites hardest at exactly the annual horizon that matters |
| **SDDP-style approximate dynamic programming** | Forward passes sample scenario paths; backward passes add supporting hyperplanes to a concave outer approximation of the value-to-go | Handles multi-stage non-anticipativity correctly; the cut set **is** a concave PWL, so no separate fitting or hull projection step; scales in horizon rather than in scenario count; provides a convergence bound, so "is `V` converged?" is answerable | Requires stagewise independence, which price processes violate — needs an explicit Markov lattice over a low-dimensional exogenous state, and that lattice is itself a modelling decision. The discrete qualification state multiplies the problem count. Highest implementation and tuning cost of the three |
| **Backward recursion on a discretised state grid** | Discretise `(SOC, peakState, qualState)`, recurse backwards over the coarsened slot grid taking weighted expectations over the ensemble at each step | Conceptually simple and exactly correct on its own discretisation; handles the discrete cliff and any non-concavity natively; produces the entire conditioning table in one sweep | Curse of dimensionality. `peakState` is a *running max* — a path variable — so it must be discretised too, and coarse peak buckets misprice precisely the threshold region that dominates the BTM business case. Tractable only with aggressive coarsening, and the coarsening error is hard to bound |

**Recommended sequencing.** Start with the rolling-horizon deterministic
equivalent, because it reuses the Planner's backend and reaches a trustworthy
baseline quickly; run backward recursion on a coarse grid as an independent
cross-check that the cliff is priced at all — disagreement between the two around
the qualification threshold is the signal that matters; treat SDDP as the target
once the state space and the conditioning dimensions have stopped moving.
Whatever is running, the deterministic-equivalent version remains as a reference
oracle for small instances.

Method choice, grid resolution and scenario count are all recorded in
`diagnostics` and in the `FeatureSpecHash`, so a change of method is a change of
artefact and shows up in the Merkle chain rather than as an unexplained shift in
behaviour.

### 5.5 Why this is where research effort compounds

ADR-007 states it; the mechanism is worth being precise about. A better `V`
improves **every** downstream decision — every tick, every gate, every market —
because it is the only thing pricing everything beyond `H_plan`. And it does so
at **no additional cost in the tick loop**: the Planner's expense is a function of
the *number of breakpoints*, not of how well those breakpoints were fitted. A
curve fitted by a month of SDDP work and a curve fitted by a crude heuristic cost
the solver the same.

That asymmetry — unbounded upside in fit quality, zero marginal runtime cost — is
rare, and it is why the slow loop deserves disproportionate research attention
relative to its share of the codebase. The measurement is the one ADR-007 names:
does a better `V` produce better *realised* P&L, evaluated through C5's
attribution rather than through the fit's own objective.

---

## 6. The unsettled gap

```
period start ──────────── unsettledGapFrom ──────────── t (now) ──────── horizon
     |<──── settled, authoritative ────>|<─── the gap ───>|<── planned ──>|
      realisedPeak is a max over this     no meter data     Planner's own
                                          yet               trajectory
```

`realisedPeak` (C5 §2) is a max over the **settled prefix only**. The gap exists
because meter data is published with a lag — provisional telemetry in minutes to
hours, final metered values in days — and market settlement is later still.
`peakIsProvisional` and `unsettledGapFrom` are the two fields that make this
visible rather than implicit.

### 6.1 How the Planner treats the gap

Conservatively, and specifically: it uses **its own modelled POI trajectory over
the gap as a provisional peak contribution** (C5 §2, L2 §2 `PeakView`). The
epigraph floor is

```
zPeak  ≥  max(  realisedPeak ,
                max over τ ∈ [unsettledGapFrom, t) of  p̂_poi[τ]  )
```

where `p̂_poi` is the engine's own trajectory over the gap — telemetered where
telemetry exists, modelled from the plan and the load/PV beliefs where it does
not. Assuming the gap contained nothing is the failure mode C5 §2 names
explicitly, and it is a particularly bad one: the gap is the *most recent* window,
so it is disproportionately likely to contain a peak the engine just caused.

Where the gap trajectory is itself uncertain, it receives the same CVaR treatment
as the forward peak and picks up `riskProfile.peakSafetyMarginKw` (L2 §6). The
asymmetry is deliberate: overstating the provisional peak costs a little
optimisation freedom, understating it can cost an entire period's demand charge.

### 6.2 Provisional peak is not `realisedPeak`

Worth stating because it is exactly the kind of thing that gets coded as an
assertion and then fires in production. `INV-S-01` requires `realisedPeak` — the
*settled* quantity — to be non-decreasing within a period. The Planner's
provisional working value is a different quantity, and it may legitimately exceed
the peak that eventually settles, because the modelled gap contribution is
deliberately biased high.

```
INV-G-16:  provisionalPeak ≥ realisedPeak                    (always)
           provisionalPeak ≤ eventual settled peak           (NOT required)
```

When settlement arrives, `unsettledGapFrom` advances, `realisedPeak` may step up,
and the modelled gap contribution is discarded rather than blended. The step is a
`stateDriftSignal` contribution (C5 §8), which is the correct response: the fitted
`V` was conditioned on the old peak state.

### 6.3 Monitoring the gap

`unsettledGapFrom` is non-decreasing and never exceeds `t` (`INV-G-17`). Gap
*length* is a first-class quality signal: a gap that stops advancing means the
meter feed is dead, and beyond a configured `maxGapSlots` the peak floor is
almost entirely modelled rather than measured. That escalates the degradation
mode (ADR-014), because peak protection is one of the two things never degraded
away (ADR-014 §3) and it cannot be protected against a quantity nobody is
measuring.

---

## 7. Provisional versus final state, and restatement

### 7.1 The two axes

Every update carries `isFinal` (C5 §1) and, per regime, `peakIsProvisional`
(C5 §2). These are not the same axis:

| | Source | Becomes final when |
|---|---|---|
| Provisional peak | Telemetry, preliminary meter reads | The settled meter series arrives for those slots |
| Provisional imbalance | Preliminary imbalance price | reBAP final publication, often weeks later (C4 §6) |
| Provisional award | Acknowledged but unsettled | Market settlement statement |

The engine cannot wait for final data to keep operating, and it must not treat
provisional data as final (C4 §1). Both are first-class, and the flag is carried
into every downstream artefact so that a P&L number can always be qualified.

### 7.2 Restatement

A restatement is a `StateUpdate` with `revisionOf` set (C5 §1). **L0 does not
overwrite.** The journal is append-only, exactly as the Belief cold tier is
(ADR-004 §1, L1 §1.2): the current state is the fold of the journal, and a
restatement appends a new entry carrying the corrected sections at a new
generation. `INV-G-15`.

Consequences, all of them intentional:

- **Replay honesty.** A restatement takes effect at the generation where it
  arrived, not retroactively. A replay from a manifest therefore sees the
  restatement exactly when the engine saw it. This is the L0 analogue of ADR-004's
  bitemporality, and it is the reason the journal is append-only rather than a
  mutable row per field.
- **Attribution.** A restatement is a distinct cause from a forecast error and
  must not be absorbed into C5 §6's `forecastError` bucket. It is attributed and
  reported separately; a restatement that lands after an accounting period has
  closed is booked to the period it belongs to and flagged, never smeared into
  the current one.
- **No retroactive re-planning.** A restatement never triggers a re-plan of past
  ticks. It contributes to `stateDriftSignal` and, if material, triggers a
  slow-loop refit (§5.1). Past decisions were correct given what was known; that
  is the whole premise of the architecture and it does not stop being true because
  the data improved.
- **Ordering.** `INV-S-08` constrains out-of-order writes: state is never written
  for a slot earlier than the previous update's `effectiveFrom` unless the update
  is an explicit `revisionOf`.

---

## 8. Testing this layer

Detail in `04-compliance/T2`; acyclicity and determinism are `T3`.

- **State machine tests.** Enumerate the permitted transitions of each discrete
  state and assert the forbidden ones throw rather than being absorbed.
  `qualificationStatus` is directional within a qualification year —
  `Qualified → AtRisk → Lost` is reachable, `Lost → Qualified` inside the same
  year is not. `peakIsProvisional` is a one-way latch per slot range:
  provisional → final, never back. Ledger status transitions:
  `Pending → Confirmed → Settled`, `Pending → Cancelled`, and nothing else.

- **Restart-mid-tick replay determinism.** Fault-inject a process kill at each of
  the seven lifecycle steps in §3.1, restart, replay, and assert the resulting
  generation-`g+1` snapshot hash equals the crash-free run's. Two cases deserve
  their own fixtures: the kill between blob write and journal append (assert
  orphan blobs are collected and the tick replays cleanly), and the kill after
  L4 submission but before commit (assert the §4.4 reconciliation pass
  reconstructs the ledger exactly, and that the replayed tick does not
  double-submit).

- **Commit protocol.** `Commit` with a stale expected generation fails
  (`INV-G-12`); two overlapping ticks cannot both commit; a partially-applied
  update is not observable; a reflection test asserts L0's public surface exposes
  no read method other than `Snapshot()` (§3.4).

- **Acyclicity audit instrumentation.** A replay with the read/write recorder
  enabled asserting: no read of a section written in the same `tickId`
  (`INV-G-11`); every payload's `inputHashes` contains the tick's
  `StateSnapshot.contentHash` (`INV-G-14`); and — the meta-test — that enabling
  the recorder does not change any artefact hash.

- **`realisedPeak` monotonicity and reset (`INV-S-01`).** Property test over a
  generated year of settlement updates: non-decreasing within every accounting
  period, and reset **exactly** at the local-calendar boundary. The reset slot must
  equal `CivilCalendar.LocalMidnight(periodStart)` expressed as a `SlotId`, not
  UTC midnight and not a fixed offset from the previous reset.

- **DST-affected boundaries.** Both Europe/Berlin transition days as fixtures
  (`INV-T-01` … `INV-T-04`, ADR-002). Assert: the March and October month
  boundaries reset at local midnight despite the UTC offset change; the period
  containing the spring-forward day is 92 slots shorter than a naive `96 × days`
  computation and the period containing the autumn day 100 slots longer; the
  annual boundary behaves identically; and, for `AtypicalHlzf`, that the HLZF
  slot subset is recomputed from the calendar table for the new period rather
  than being carried across.

- **Unsettled gap.** `unsettledGapFrom` non-decreasing and `≤ t` (`INV-G-17`);
  `provisionalPeak ≥ realisedPeak` always (`INV-G-16`); a settlement arrival that
  raises `realisedPeak` above the previously modelled gap contribution is handled
  without an assertion failure; a stalled gap beyond `maxGapSlots` escalates the
  mode.

- **Value function.** Concavity of every curve (`INV-V-11`); conditioning
  coverage — every reachable cell has a curve or a declared fallback; staleness
  produces `vSocStale` and the `stalenessPenalty` path rather than a failure;
  **invalidation** (a `qualState` change or an accounting rollover) forces
  `DEFENSIVE` and not merely a penalty (`INV-G-19`); a refit that arrives mid-run
  changes the artefact identity in L0 and appears in the Merkle chain.

- **Durability.** Torn blob write, torn journal tail, and section-hash mismatch on
  load each produce the declared outcome (`HALT` for the last). Structural-sharing
  correctness: a snapshot restored from shared sections is field-identical to the
  in-memory original (`INV-G-18`). Round-trip a full year of journal and assert
  the fold reproduces the final snapshot hash.

---

## 9. Invariants introduced here

| ID | Invariant | On failure |
|---|---|---|
| `INV-G-11` | No component reads an L0 section written during the same `tickId` | `HALT`; audited by `T3` |
| `INV-G-12` | Exactly one `Commit` per `tickId`, succeeding only against the expected generation | `HALT` |
| `INV-G-13` | A tick that does not reach `Commit` leaves L0 byte-identical at the previous generation | `HALT` |
| `INV-G-14` | Every payload produced within a tick carries that tick's `StateSnapshot.contentHash` in `inputHashes` | `HALT` |
| `INV-G-15` | The L0 journal is append-only; current state is the fold of the journal, never an in-place edit | `HALT` |
| `INV-G-16` | The Planner's provisional peak floor is `≥ realisedPeak`; it is **not** required to be `≤` the eventual settled peak | `HALT` on the `≥` side only |
| `INV-G-17` | `unsettledGapFrom` is non-decreasing and never exceeds the current slot | `HALT` |
| `INV-G-18` | A restored `StateSnapshot` is field-identical to the persisted one and rehashes to the same `contentHash` | `HALT` |
| `INV-G-19` | A change to `V`'s discrete conditioning state invalidates `V` — mode `≥ DEFENSIVE` until refit — rather than marking it stale | escalate per ADR-014 |
