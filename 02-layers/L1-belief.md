# L1 — Belief

The single source of truth about the world as known at a point in time. It answers
exactly one question — "what could the engine have known at `asOf`, over
`[t, t + H_hot]`?" — and it answers it in a way that makes the wrong answer
structurally unreachable rather than merely discouraged.

**Input:** cold tier (immutable Parquet) + warm frames + derived artefacts, addressed by the run manifest
**Output:** `BeliefSnapshot` (C1)
**Purity:** pure read over immutable, content-addressed data. No writes, no clock, no randomness, no network. Ingestion is a separate process and never runs inside a tick.

---

## 1. Ingestion pipeline

Ingestion is offline with respect to the tick loop. It is also the place where
almost all of the correctness of this layer is decided, because a fact stamped
wrongly here is indistinguishable from a correct fact everywhere above.

```
source adapter ──▶ normalise ──▶ bitemporal stamp ──▶ validate ──▶ cold write
      │                │                 │                │             │
      │                │                 │                │             └─ append-only Parquet,
      │                │                 │                │                content-named batch
      │                │                 │                └─ C0 §3 universal invariants
      │                │                 │                   + cadence and duplicate checks
      │                │                 └─ (validSlot, knowledgeTime, revisionOrdinal)
      │                └─ units, signs, typed quantities (ADR-003), UTC, 15-min grid
      └─ declares PublicationSemantics, or the series is quarantined
```

### 1.1 Publication semantics are mandatory

ADR-004's ingestion consequence is enforced as a schema, not a habit. Every
series is admitted only with a `PublicationSemantics` record:

| Field | Meaning |
|---|---|
| `knowledgeTimeRule` | `PublishedAt` \| `ValidTimePlusLag` \| `ArrivalObserved` \| `Declared` |
| `publicationLag` | `SlotSpan`, for `ValidTimePlusLag` |
| `revisionPolicy` | `Never` \| `Scheduled` \| `Unbounded` |
| `finalityLag` | Slots after `validSlot` beyond which a value is final |
| `clockSkewBound` | Upper bound on the source's own clock error |
| `latenessTolerance` | How late an arrival may be before it is an ingestion incident |
| `cadence` | Expected publication interval; the basis of gap detection |
| `fallbackChain` | Ordered secondary sources (ADR-014 §4 rung 3) |

**The rounding rule.** Where `knowledgeTime` cannot be established exactly, it is
always rounded **later**, never earlier: `knowledgeTime = observedArrival +
clockSkewBound`. Because reads filter on `knowledgeTime ≤ asOf`, rounding later
makes the engine know *less* than it did. That is a loss of realism in the
conservative direction; rounding earlier is lookahead, which is not a loss of
realism but a fabrication of alpha. `INV-D-20`.

**Quarantine, not guesswork.** A source that cannot answer "when could we first
have known this?" is written to the cold tier under a `quarantine/` prefix with
`knowledgeTime = NULL`. Quarantined partitions are readable by offline tooling
and are structurally unreachable from a production cursor (§3). The run manifest
records the quarantined series, so a backtest states what it excluded rather than
silently excluding it. `INV-D-11`.

### 1.2 Late arrival, revision, restatement

All three are the same mechanism: **append a row**.

| Event | Rows written | `revisionOrdinal` | Effect on a replay at `asOf` |
|---|---|---|---|
| First publication | 1 | `0` | Visible from its `knowledgeTime` |
| Late arrival (published on time, ingested late) | 1 | `0` | Visible from the *publication* instant — lateness of ingestion is an operational fact, not a knowledge fact |
| Revision / restatement | 1 new row, same `validSlot` | `n+1` | Old value visible before, new value after; both permanently retained |
| Retraction | 1 tombstone row, `quality = Missing` | `n+1` | Field falls through the ladder (§7) from that instant |

Nothing is ever updated or deleted in place. `INV-D-12`. Compaction rewrites
files but never rows: it produces new content-named files and a new catalogue
version, and the old catalogue remains resolvable for replay.

The distinction that this buys is the one Settlement needs: **"we were wrong"**
(row 0 was a bad forecast) versus **"the data was later corrected"** (row 1
superseded row 0). Collapsing revisions to last-value-wins destroys the ability
to attribute a P&L gap between C5 §6's `forecastError` bucket and a data
restatement, and there is no way to recover it afterwards.

### 1.3 The ingestion watermark

Per source, `watermark(source)` is the `knowledgeTime` up to which ingestion
guarantees completeness — everything publishable at or before it has been
written. The run's watermark is the minimum over the sources the manifest
requires.

**No snapshot may be built with `asOf > watermark`.** `INV-D-13`. Without this
rule a replay can read a series that had not yet been loaded, see less than the
engine actually saw, and produce a result that is neither the live result nor
reproducible. This is the mirror image of lookahead and it is easier to miss,
because it makes backtests look *worse*, not better, so nobody investigates.

The watermark is itself a recorded artefact, part of the manifest (ADR-013), and
is replayed with the run. A backtest of 2024 uses 2024's watermark trajectory,
including the days when a feed was six hours late.

---

## 2. The three tiers

ADR-004 §2 fixes the tiers. This section fixes their structure.

### 2.1 Cold — immutable Parquet

```
cold/
  series=<SeriesId>/
    source=<SourceId>/
      valid_date=YYYY-MM-DD/          # UTC date of validSlot
        part-<batchContentHash>.parquet
  features/
    spec=<FeatureSpecHash>/
      <artefact files>
  quarantine/
    series=<SeriesId>/ ...
```

Row schema, sorted within a file by `(validSlot, knowledgeTime, revisionOrdinal)`:

```
validSlot        int64    SlotId — UTC quarter-hours since epoch
knowledgeTime    int64    microseconds since epoch, UTC
revisionOrdinal  int32    0 = first publication
scenarioIndex    int32    ensemble series only; -1 for scalar series
value            float32  (forecasts, ensembles)  |  float64 (money, SOC)
provenance       uint8    Measured|Forecast|Revised|Imputed|Default
quality          uint8    Good|Degraded|Stale|Imputed|Missing
sourceId         int32    dictionary-encoded
```

`knowledgeTime` is deliberately finer than `SlotId`. Publication events do not
align to quarter hours, and a gate that closes at 12:00:00 must not be beaten by
a price published at 12:00:00.4 rounded down to the 12:00 slot.

Partitioning by `valid_date` rather than by `knowledge_date` is chosen because
the dominant access pattern is a forward replay over valid time; revisions
scatter across knowledge time but land in the partition they describe, which is
the one the reader wants.

### 2.2 Warm — memory-mapped frames, one per `(series, day)`

A frame is a derived artefact, reproducible from cold plus a `FrameSpecHash`. It
is not a second source of truth; it may be deleted and rebuilt at any time.

```
Frame layout (single mmap'd file, all offsets 64-byte aligned)

  header  64 B   magic, formatVersion, seriesId, frameSpecHash,
                 dayStartSlot, slotCount, scenarioCount,
                 revisionCount, runOffset, ktOffset, valueOffset,
                 qualityOffset, contentHash

  runs    [slotCount]  int32 pairs (start, length) into the revision arrays
  kt      [revisionCount]  int64   knowledgeTime, ascending within each run
  values  [revisionCount × scenarioCount]  float32|float64, scenario-major
  quality [revisionCount]  uint16  packed (provenance, quality)
```

`slotCount` is **not** 96. It is whatever the `CivilCalendar` says the frame's
day contains — 92, 96 or 100 (`00-overview/02-conventions.md` §4.2). A frame
covers a *local* civil day, because every consumer that cares about day
boundaries (peak accounting, HLZF windows) cares about local ones.

Resolving `asOf` inside a frame is: index the slot's run, binary-search `kt` for
the last entry `≤ asOf`, read the value. For the overwhelmingly common case
`revisionPolicy = Never`, every run has length 1 and the search is a load.

### 2.3 Hot — a preallocated struct-of-arrays window

One instance per cursor, allocated at cursor open, never reallocated, refilled in
place per tick.

```csharp
sealed class HotWindow {
    readonly int      _h;              // H_hot slots
    readonly int      _s;              // S scenarios
    readonly int      _kEns, _kScalar; // series counts

    // scenario-indexed, [series][scenario][slot] flattened, slot-contiguous
    readonly float[]  _ensemble;       // _kEns * _s * _h,  ArrayPool-backed
    // scalar, [series][slot]
    readonly double[] _scalar;         // _kScalar * _h
    readonly ushort[] _quality;        // (_kEns + _kScalar) * _h
    readonly long[]   _knowledgeTime;  // (_kEns + _kScalar) * _h

    long _slotStart;                   // first slot in the window
    long _asOf;                        // knowledge boundary
    int  _generation;                  // bumped on every refill (§8)

    void Refill(SlotId t, KnowledgeTime asOf);   // allocates nothing
}
```

Slot-contiguous, scenario-major layout is chosen because the dominant hot-path
operation is a weighted reduction *across scenarios at a fixed slot* (`PeakView`'s
CVaR, L2 §2) and a *scan across slots at a fixed scenario* (SOC recursion in the
Planner). Making the slot axis contiguous keeps the second pattern at streaming
bandwidth and leaves the first as a strided gather with a fixed, prefetchable
stride.

**Refill strategy is per-series and derived from the declared publication
semantics.** Consecutive ticks overlap by `H_hot − 1` slots, so the obvious
optimisation is a ring buffer with a modular base index: advancing one slot then
copies one slot per series rather than the whole window. That is valid only for
series with `revisionPolicy = Never`, because a new `asOf` can change values at
slots the window already holds. Series that may be revised are fully refilled.
The split is not a heuristic — it reads the same declaration ingestion enforced.

### 2.4 Why memory is `O(#series × H_hot × S)`

Nothing in the read path is indexed by history:

| Tier | Resident bytes | Grows with backtest length? |
|---|---|---|
| Cold | 0 | — (never mapped by the tick loop) |
| Warm | configured frame budget, LRU-bounded | no |
| Hot | `#series × H_hot × S × sizeof(value)` + small side arrays | no |

Concretely, with the ADR-005 working numbers `K = 8` ensemble series, `S = 64`,
`H_hot = 192`, `float32`: `8 × 64 × 192 × 4 B ≈ 393 kB`. Scalar series, quality
and knowledge-time arrays add well under 100 kB. A four-year backtest touches
~140,000 ticks and the resident set at tick 140,000 is byte-for-byte the same as
at tick 1.

This is the property that makes the scenario axis affordable at all (ADR-005
Consequences). It is also fragile in exactly one way: any code that accumulates
per-tick history — a list of snapshots, a diagnostic buffer, a recorded seam
stream held in memory — reintroduces the growth term. Seam recording is on-demand
and streams to disk (ADR-013) for this reason.

---

## 3. The cursor

```csharp
sealed class BeliefCursor : IDisposable {
    void           SeekTo(SlotId t, KnowledgeTime asOf);
    void           Advance(SlotSpan by, KnowledgeTime asOf);
    BeliefSnapshot Snapshot(in SnapshotRequest req);
}
```

A replay advances a cursor; it does not issue queries. The cursor owns the hot
window, the mapped frame set, and the LRU.

### 3.1 Advancing

1. `Advance` moves `_slotStart` forward and `_asOf` forward. Both are monotone by
   default; a backwards `Advance` is rejected.
2. For each required series, compute the frame set covering
   `[t, t + H_hot)`. At `H_hot = 192` (two days) this is two or three frames.
3. Map any frame not already mapped; register it in the LRU.
4. Refill the hot window per §2.3.
5. Bump `_generation`.

### 3.2 LRU eviction and prefetch

Eviction is over a **byte budget**, not a frame count, because frame sizes differ
by series and by DST day. Evicting unmaps the frame; the OS page cache retains
the pages, so re-mapping a recently evicted frame costs a syscall and no I/O.
This is the division of labour ADR-004 §2 intends: the LRU manages address space,
the page cache manages memory.

Prefetch runs when the cursor crosses a configured fraction of the current frame:
the next frame per series is mapped and advised `WILLNEED` on a background
thread. Prefetch is **purely a performance concern and cannot affect output** —
frames are immutable and content-addressed, so a prefetched frame and a
demand-mapped frame are the same bytes. `T3` asserts this by running an audit day
with prefetch disabled and comparing artefact hashes.

### 3.3 Why sequential is the right bet

| Pattern | Frame maps per tick | Bound by |
|---|---|---|
| Sequential forward, prefetch on | ~0 amortised (one frame per series per civil day) | memory bandwidth of the refill copy |
| Sequential forward, prefetch off | 0, except a burst at each day boundary, on the critical path | as above, plus a periodic stall |
| Backward seek inside the LRU budget | 0 | as sequential |
| Random jump | `#series`, all cold | storage IOPS |

The production tick loop and the primary backtest are strictly sequential
forward. That is the access pattern mmap plus page cache is best at: pages are
read ahead by the kernel, touched once, and evicted in the order they were
loaded, with no user-space bookkeeping and no copy into a managed heap.

### 3.4 The random-access case, and how it degrades

Jumping cursors are real. Three harnesses need them: sampled-day scenario
analysis, the counterfactual re-runs behind C5 §6's four-bucket decomposition,
and targeted debugging of a single historical tick.

A jump invalidates the entire frame set. Cost per jump is `#series` cold frame
maps and a page-fault storm on first touch; throughput falls from sequential
bandwidth to random-read IOPS, typically one to two orders of magnitude on
spinning or networked storage and a factor of a few on local NVMe.

Mitigations, in order of preference:

1. **Sort the jumps.** A harness that visits a *set* of ticks can visit them in
   slot order and is then sequential again with gaps. The C5 §6 counterfactual
   re-runs allow this; make it the default in the harness rather than an option.
2. **Raise the frame budget.** If the sampled day set's frames fit in the budget,
   the second pass is free.
3. **Accept it.** A jumping harness is a research tool run on tens of days, not
   the production path run on years.

What is explicitly **not** done is adding a global secondary index or a
row-oriented store to make jumping fast. Both would cost the sequential path,
which carries 99% of the work, to accelerate a path that runs occasionally and is
not latency-sensitive. This is the "database as the hot path" alternative ADR-004
already rejected, arriving by a side door.

---

## 4. Derived feature artefacts

ADR-004 §4: anything expensive and reused is computed once, offline, and read
back as data.

### 4.1 FeatureSpecHash

The key is a hash over the complete derivation, not over the output:

```
FeatureSpecHash = H( featureType
                   , generatorCodeRevision
                   , hyperparameters (canonical ordering)
                   , inputSeriesIds + their catalogue versions
                   , training asOf boundary and validRange
                   , tzdata version
                   , generator seed )
```

Two consequences follow directly. A backtest cannot silently use a different
definition than the one recorded, because the definition *is* the key. And a
change to any input — including tzdata, including the seed — produces a different
artefact rather than a quietly different result.

The run manifest pins every `FeatureSpecHash` in force. Reading an artefact whose
spec hash is not in the manifest is a hard failure, not a fallback. `INV-D-14`.

### 4.2 What is materialised

| Artefact | Shape | Reaches C1 as | Refresh |
|---|---|---|---|
| Joint scenario ensemble (full) | `[S_gen, H, K]` float32 + generator metadata | never directly | daily, `C_slow` |
| Reduced ensemble + weights | `[S, H, K]` float32, `double[S]` | §1 `scenarioWeights`, §3, §4 arrays | daily, `C_slow` |
| Fill-probability surface | `P(fill | product, priceBand, timeToGate, volume)` | §5 `idReliableVolume*` (volumes only) | recalibration, `C_slow` |
| Price-impact curve | `volume → expected price concession` | **not C1** — quoting policy only (ADR-012) | recalibration |
| Activation-probability model | exported PWL / lookup table | §4 `activationUp`, `activationDn` | `C_slow` |
| Peak / load climatology | quantile surfaces by calendar bucket | ladder rung 4 imputation (§7) | seasonal |

The value function `V` is also a materialised, content-hashed artefact, but it is
produced by the slow loop and lives in **L0**, not L1 — see
`02-layers/L0-state-value-store.md` §5. L1 does not read it and does not carry it
across C1.

### 4.3 Notation, and a warning about `S`

ADR-005 uses `K` for the series count and, separately, `K ≪ S` for the reduced
scenario count. Throughout this document and in C1:

| Symbol | Meaning |
|---|---|
| `K` | number of uncertain **series** on the shared axis |
| `S_gen` | scenarios in the **generated** ensemble, before reduction |
| `S` | scenarios in the **reduced** ensemble — this is C1 §1's `scenarioCount` |

Only the reduced ensemble crosses C1. `INV-D-06` (weights) and `INV-D-07`
(marginal-mean preservation) are checked at artefact write time *and* again at
snapshot validation, because the artefact could be correct and the load path
wrong.

### 4.4 The rule

**The engine never recomputes a feature artefact online.** Exactly three
operations on an artefact are permitted inside the tick loop:

1. bounds-checked lookup in a precomputed table,
2. evaluation of a piecewise-linear function at a point,
3. a weighted reduction over the scenario axis with fixed partitioning (§6).

Anything else — fitting, interpolating across a dimension the artefact does not
tabulate, resampling, re-reducing an ensemble — is an offline concern (ADR-001).
A model too complex to export into one of these three forms is a model too
complex to be in the tick loop, and that is a finding about the model, not a
limitation of the layer.

---

## 5. Order book handling

### 5.1 The raw book stays cold

LOB deltas are ingested at source fidelity into the cold tier, partitioned
`(productId, tradingDate)`, ordered by exchange sequence number, with sequence
gaps recorded explicitly as tombstone rows rather than interpolated. They are
never built into warm frames for a production cursor and never mapped by the tick
loop. `INV-D-17`.

### 5.2 What crosses C1

Only C1 §5: `idReliableVolumeBuy`, `idReliableVolumeSell`, and optionally
`idVolumeByPriceBand`. Volumes, in kWh, per slot. **No price field exists in that
section**, and that is the C1-side enforcement of the rule that fill probability
constrains the Planner but never prices for it (ADR-008, ADR-012, and `INV-V-16`
on the L2 side).

"Reliable" has a definition, not a vibe: the volume `q` such that
`P(fill of q within the residual window) ≥ θ`, where `θ` is configuration and
deliberately conservative. It is a quantile of a fitted surface, not an
expectation, because the Planner uses it as a **bound** and a bound that is
right on average is wrong half the time.

### 5.3 The calibration path

Offline, against the existing market simulator (ADR-015 OPEN-3 — Execution is
fixed and pre-existing, so the fill model's job is to predict *the simulator's*
behaviour, which is a narrower and more tractable problem than a general LOB
model).

```
1. Replay recorded book state into the simulator at a recorded asOf.
2. Submit a grid of synthetic limit orders:
       product × price band × volume × time-to-gate × book-state bucket
3. Record realised fill rate, partial-fill distribution, time-to-fill.
4. Fit P(fill | ·); invert to the θ-quantile reliable volume per slot.
5. Content-hash under FeatureSpecHash; write to cold/features/.
6. T4 characterisation: realised vs. predicted fill rate by price band,
   product and time-to-gate, reported as a calibration curve, not a scalar.
```

**Standing caveat, carried from ADR-015 OPEN-3:** whatever is calibrated against
the simulator inherits the simulator's biases. When the engine is pointed at a
real venue the fill model must be recalibrated, and the gap between
simulator-calibrated and venue-calibrated behaviour is a named risk, recorded
now so it is not discovered later.

### 5.4 Research fidelity versus production features

| Tier | Contents | Consumers | In the tick loop |
|---|---|---|---|
| **Research fidelity** | full LOB deltas, per-order events, sequence numbers, book reconstruction | calibration (§5.3), high-fidelity replay, `T4`, incident forensics | never |
| **Production features** | reliable volume per slot (and per band), plus its `QualityStamp` | C1 §5 | yes, via the hot window |

The split is enforced structurally, not by convention. The two live in different
**frame families**, and a cursor is constructed with the production family only.
Reaching the raw book from the tick loop would require constructing a second
cursor over the research family — a visible, reviewable code change, not an
accident of a stray query. This is the same design move as ADR-004's missing
`asOf` overload: make the wrong thing require an edit to the layer, not a lapse
by the caller.

The corollary is that OPEN-3's eventual resolution — scalar volume cap versus
per-band cap — changes how much of the cold tier must hold raw book data and the
shape of the production feature, but not this split and not the C1 contract.

---

## 6. Hot-path C# discipline

Referenced from ADR-001 as `02-layers/L1-belief.md` §6. Normative for every
assembly on the tick path: L1's read path, L2, L3's model construction, L5.

### 6.1 The rules

| Rule | Reason | Enforcement |
|---|---|---|
| Struct-of-arrays for every slot- and scenario-indexed quantity | Reductions become contiguous scans; SIMD becomes available; no per-element header overhead | Design review; the hot window has no alternative representation |
| `Span<T>` / `ReadOnlySpan<T>` for every slice crossing an internal boundary | Zero-copy, no array allocation, bounds-check elision on the common loop shape | Analyzer: no `T[]` return type on a hot-path method |
| `ArrayPool<T>.Shared` for transient buffers, `clearArray: false`, returned on a deterministic path | The alternative is a per-tick LOH allocation for anything ≥ 85 kB, which the ensemble arrays exceed | Analyzer + rent/return balance assertion in tests |
| No LINQ anywhere in the tick loop | Allocates an enumerator and usually a closure per call, and — worse — hides iteration order, which ADR-013 requires to be fixed | Roslyn analyzer banning `System.Linq` in hot assemblies |
| No per-slot or per-scenario object allocation | `H × S × K` objects per tick is gen0 churn measured in hundreds of kB per tick | Allocation-count assertions (§9) |
| No boxing of typed quantities | `EnergyKwh`, `PoiPowerKw` etc. are readonly structs (ADR-003); boxing them allocates and defeats the entire type discipline | Analyzer: no `object`, no non-generic interfaces, no `string.Format` on typed quantities in hot paths |
| `readonly struct` + `in` parameters for snapshot passing | Prevents silent defensive copies of a large struct at every call site | Analyzer: `in` required for struct parameters above a size threshold |
| No `async` / `Task` in the tick loop | Allocates a state machine, and its presence implies an I/O dependency that must not exist on this path | Analyzer |
| No `Dictionary<string, _>` lookup in the tick loop | String hashing per lookup, and unordered iteration | Series and slot indices are resolved to `int` once, at cursor open |
| Fixed partitioning for every parallel reduction | Floating-point summation order must not depend on core count or on the thread pool's dynamic partitioner (ADR-013) | `INV-D-18`, verified by `T3` across two machine shapes |

The last one deserves emphasis because it is the one that produces a bug nobody
can reproduce. A weighted mean over `S = 64` scenarios partitioned by
`Parallel.For`'s default range partitioner sums in an order that depends on
thread scheduling. The result differs in the last bits, the MILP takes a
different branch, and two runs with identical manifests disagree. The fix is to
partition by a fixed block size derived from `S` alone and to reduce the partial
sums in a fixed order.

### 6.2 Allocation-bound before CPU-bound

The claim in ADR-001 is quantitative. Take a four-year backtest at quarter-hourly
ticks: `4 × 365 × 96 ≈ 140,000` ticks. A naive implementation that materialises
one `double[H_hot]` per `(series, scenario)` allocates
`8 × 64 = 512` arrays of 192 doubles ≈ 800 kB per tick, which is **~112 GB of
gen0 traffic over the run**. The same run against a preallocated, pooled hot
window allocates zero bytes per tick after warm-up.

The solve itself is milliseconds and is dominated by the solver, which is native
code the GC never sees. The engine's own contribution is copies and reductions
over a few hundred kB. In that regime, allocation rate and the resulting gen0
pauses are the throughput ceiling, and they are the ceiling long before any
arithmetic is. Backtest throughput is the binding constraint on how fast the
strategy can be improved, so this is not micro-optimisation — it is the
difference between a research loop measured in hours and one measured in days.

---

## 7. Quality and the fallback ladder at ingestion

ADR-014 §4's six rungs, applied per field, with `maxStale` and `Default` taken
from the C1 field tables.

### 7.1 Where each rung executes

| Rung | Action | Executes in | `provenance` | `quality` |
|---|---|---|---|---|
| 1 | Primary source, fresh | cursor read | `Measured` / `Forecast` / `Revised` | `Good` |
| 2 | Primary source, stale within `maxStale` | cursor read | as published | `Stale` |
| 3 | Secondary source from `fallbackChain` | snapshot builder | as published | `Degraded` |
| 4 | Model imputation — persistence, climatology, ensemble mean | snapshot builder, artefact-backed | `Imputed` | `Imputed` |
| 5 | Conservative default from the C1 field table | snapshot builder | `Default` | `Imputed` |
| 6 | No safe default exists | snapshot builder | `Default` | `Missing` |

**Ingestion never imputes.** Rungs 3–6 are read-path operations. Writing an
imputed value into the cold tier would put invented data behind the same API as
observed data and destroy the "we were wrong" versus "the data was corrected"
distinction that ADR-004 §1 exists to preserve. The cold tier contains
`Imputed` or `Default` provenance only where the *source itself* published an
imputed value and declared it as such.

Note that `provenance` and `quality` are not parallel enumerations: rungs 4 and 5
both yield `quality = Imputed` and are distinguished by `provenance`. Provenance
is the finer discriminator and is what the risk mapping in L2 §6 should key on
when the two disagree.

### 7.2 `maxStale`

`maxStale` is declared per field in C1 and is not always a duration:

```
MaxStale = Slots(n)          → stale when  asOf − knowledgeTime > n slots
         | Gate(marketId)    → stale when a gate for marketId has closed
                               since knowledgeTime   (C1 uses "gate")
         | None              → never stale; structural data from the calendar
```

`Gate` is the correct semantics for `daPrice` and `afrrCapPrice` (C1 §4): a
day-ahead price belief from before the gate is not merely old, it has been
superseded by an event. Expressing that as a slot count would either be too
lenient at 03:00 or too strict at 11:59.

Staleness is computed against `asOf`, never against a wall clock (`INV-G-05`).

### 7.3 Computing `criticalMissing`

```
isCritical(f)   ≔  C1 field table has  Null = no   ∧   Default = "—"
criticalMissing ≔  { f : quality(f) = Missing  ∧  isCritical(f) }
```

The archetype is `socNow` (C1 §2: "Measured; if telemetry lost, escalate — never
default"). A field with `Null = no` and a declared conservative default is not
critical, because rung 5 always succeeds for it; a field with no safe default
falls to rung 6 by construction.

**Mode escalation, and the one-tick lag.** C1 §8 carries both `criticalMissing`
(same-tick evidence, computed here) and `degradationMode` (read from L0, and
therefore one tick old — ADR-006). C1 §8 also states that non-empty
`criticalMissing` implies `mode ≥ DEFENSIVE`. Those two facts are only compatible
if L1 may raise the mode within the tick, so:

```
degradationMode(C1) = max( modeFromL0 , modeFloor(criticalMissing) )
```

L1 applies a **monotone escalation floor** derived from its own same-tick input.
It can raise the mode; it can never lower it. De-escalation stays centrally
governed and hysteretic (ADR-014 §2). This does not violate ADR-006: the floor is
computed from L1's own inputs, not read from a same-tick write by another
component, and the evidence is published so the central mode computation sees it
in the next tick's `StateUpdate`.

---

## 8. Snapshot construction

### 8.1 Copied versus referenced

| Element | Treatment | Why |
|---|---|---|
| Envelope (C1 §1), scalars (C1 §2), `scenarioWeights` | copied by value | Tens to hundreds of bytes |
| Quality stamps, `criticalMissing`, `degradationMode` | copied by value | Small, and consumers index them irregularly |
| Slot- and scenario-indexed arrays (C1 §3–§5) | referenced as `ReadOnlyMemory<T>` over hot-window buffers | Copying ~400 kB per tick to no purpose |
| Reserve product structure (C1 §7), tariff structure (C1 §6) | referenced into an immutable, content-addressed calendar artefact | Never changes within a run |

### 8.2 Reconciling zero-copy with "a contract is a value"

C0 §1 requires a contract to be a value with no handles into another layer's
storage, and C1 §10 explicitly excludes "any handle, cursor or reference into the
Belief store". ADR-004 §3 describes `BeliefSnapshot` as carrying a "hot window
reference". These are reconciled by distinguishing two things that are easily
conflated:

- A **handle** — a cursor, a frame, anything that can be advanced or that aliases
  storage whose contents may change under the reader. Forbidden.
- A **read-only view** over a buffer that is contractually immutable for the
  lifetime of the tick. Permitted, and it is what makes the snapshot cheap.

Two forms therefore exist:

| Form | Representation | Used for |
|---|---|---|
| `BeliefSnapshot` | readonly struct: envelope + `ReadOnlyMemory<T>` views + generation token | in-process, hot path |
| `BeliefSnapshotRecord` | fully materialised value; stable binary and JSON | seam recording (ADR-013), golden tests, cross-process replay |

C0's "contract is a value" requirement is satisfied by the existence and byte
stability of the record form, and by the round-trip property
`Record(s) == Record(Deserialise(Serialise(Record(s))))`. Materialisation happens
off the hot path, on audit days only.

The generation token makes the immutability claim checkable rather than
aspirational: the cursor bumps `HotWindow._generation` on every refill, the
snapshot captures it, and every span accessor validates it. A component that
retains a snapshot past the end of its tick and dereferences it throws instead of
reading whatever the next tick refilled. In production builds this check is
retained on the coarse accessors and elided inside inner loops.

### 8.3 Content hashing, and why it is cheap

The snapshot's content is **completely determined** by its derivation:

```
contentHash = H( asOf , slotStart , slotCount , scenarioCount
               , orderedSeriesIds
               , frameContentHash per series          (immutable, precomputed)
               , featureSpecHash per artefact used
               , scenarioWeightsHash
               , qualityStampDigest
               , criticalMissing , degradationMode )
```

Because frames are immutable and content-addressed, hashing the derivation is
exactly as discriminating as hashing the data, and it is `O(#series)` instead of
`O(#series × H_hot × S)`. Two snapshots with equal derivation hashes hold
identical arrays; that implication is `INV-D-16` and is verified on audit days by
computing the full data hash alongside and asserting agreement.

Total per-tick cost of building a snapshot:

| Step | Cost |
|---|---|
| Frame resolution and mapping | amortised ~0 on the sequential path (§3.3) |
| Hot-window refill | one bounded copy, `≤ #series × H_hot × S × 4 B` |
| Derivation hash | `O(#series)` — tens of hashes |
| C0 §3 + C1 §9 validation | one linear scan over memory already hot in L1/L2 cache |
| Allocation | zero after warm-up (`INV-D-15`) |

This is the concrete content of ADR-004 §3's "cheap to create". The dominant term
is a sub-megabyte memcpy, which is why the layer can afford to build a fresh,
fully validated snapshot on every tick rather than mutating one in place.

---

## 9. Testing this layer

Detail in `04-compliance/T2`; the lookahead audit is `T6`; determinism is `T3`.
The properties that matter most:

- **No-lookahead, structurally.** Two tests, at different levels. (a) `T6`:
  re-run a backtest with every fact whose `knowledgeTime > t` replaced by
  garbage; assert bit-identical artefact hashes (ADR-004 Consequences). (b) A
  reflection test over the storage layer's public surface asserting that **no
  read method exists without an `asOf` parameter**. The second is the one that
  keeps the first true as the code grows, because it fails the moment someone
  adds a convenience overload.

- **Revision replay.** Take a series revised three times: publications at
  `k0 < k1 < k2 < k3` for the same `validSlot`. Assert that a read at each of the
  four `asOf` points between and after them returns exactly the revision in
  force, that a forward-advancing cursor observes each revision exactly once and
  never a later one, and that the frame-resolved answer equals a naive
  cold-tier scan at every point. Extend with a cross-frame revision: a
  restatement arriving on day `d` for a `validSlot` on day `d − 1`.

- **DST fixture days.** Both Europe/Berlin transition days are fixtures in every
  calendar-dependent test (`INV-T-01` … `INV-T-04`, ADR-002). Assert: the frame
  for the spring-forward day has 92 slots and the autumn day 100; frame
  boundaries sit at local midnight, not at a fixed UTC offset; and — the subtlety
  most likely to be coded wrong — a hot window spanning a transition still has
  `slotCount = H_hot`, because `SlotId` is a UTC quarter-hour count and is
  DST-independent (`00-overview/02-conventions.md` §4.1). The frame *count*
  changes; the window *length* does not.

- **Frame boundary.** Windows spanning two and three frames; a window starting
  exactly on a boundary; windows ending one slot before and one slot after a
  boundary; a series whose frame is absent for one day (assert the ladder is
  entered, not an exception); a frame whose `contentHash` does not verify (assert
  `HALT`).

- **Allocation counts in the tick loop.** After a warm-up of `W` ticks, run `N`
  ticks and assert `GC.GetAllocatedBytesForCurrentThread()` is unchanged and the
  gen0 collection count is unchanged (`INV-D-15`). Run it as a CI gate, not as an
  occasional benchmark: allocation regressions arrive one LINQ call at a time and
  are invisible in correctness tests.

- **Ensemble coherence.** Mirrors L2 §7. Permute the scenario axis of *one*
  series only and assert (a) `INV-D-05` validation fails, and (b) the snapshot's
  content hash changes. Separately assert `INV-D-06` (weights non-negative, sum
  to 1 ± 1e-9) and `INV-D-07` (reduced-ensemble marginal means match the full
  ensemble within tolerance) both at artefact write time and at snapshot
  validation, because the artefact can be right and the load path wrong.

- **Watermark and quarantine.** Assert a snapshot request with
  `asOf > watermark` fails (`INV-D-13`); assert a quarantined series is
  unreachable from a production cursor and that its presence in the manifest is
  required for the run to start (`INV-D-11`).

- **Access-pattern equivalence.** A jumping cursor and a sequential cursor
  visiting the same ticks must produce identical snapshot hashes. This catches
  state accidentally carried in the cursor across `Advance`, which is the most
  likely place for a hidden dependence on replay order.

- **Prefetch and partitioning determinism.** Same audit day with prefetch on and
  off; same day on two machines with different core counts (`INV-D-18`). Both
  must produce identical artefact hashes.

---

## 10. Invariants introduced here

| ID | Invariant | On failure |
|---|---|---|
| `INV-D-11` | Every admitted series declares `PublicationSemantics`; quarantined series are unreachable from a production cursor and are named in the manifest | `HALT` at manifest validation |
| `INV-D-12` | The cold tier is append-only: no row is updated or deleted; retraction is a tombstone row | `HALT` |
| `INV-D-13` | No snapshot is built with `asOf` beyond the run's ingestion watermark | `HALT` |
| `INV-D-14` | Every derived artefact read has its `FeatureSpecHash` pinned in the run manifest | `HALT` |
| `INV-D-15` | Hot-window refill and snapshot construction allocate zero managed bytes after warm-up | CI gate |
| `INV-D-16` | Equal snapshot derivation hashes imply identical underlying arrays | `HALT` (checked on audit days) |
| `INV-D-17` | No raw order-book row is reachable from a production cursor | `HALT` |
| `INV-D-18` | Parallel reductions over the scenario axis use a fixed partitioning independent of core count and scheduler | `HALT`, verified by `T3` |
| `INV-D-19` | `criticalMissing` equals exactly the `Missing` fields with `Null = no` and no declared conservative default | `HALT` |
| `INV-D-20` | An assigned `knowledgeTime` is never earlier than the source's true publication instant; uncertainty rounds later | quarantine the source |
