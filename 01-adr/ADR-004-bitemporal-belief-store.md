# ADR-004 — Bitemporal, three-tier, content-addressed Belief store

**Status:** Accepted **Reversibility:** Very expensive — build this first

## Context

Two requirements pull in opposite directions.

**Accuracy.** A backtest is only worth running if it is impossible for the engine
to have used information it did not have at the time. Lookahead leakage is
insidious: it produces excellent backtests and losing live performance, and it
is usually introduced by an innocuous convenience — a forecast series that was
silently revised, a settlement value stamped with the delivery time rather than
the publication time, a "latest available" lookup with no `as_of`.

**Efficiency.** The full information set — order book deltas for every intraday
product, ensemble forecasts, aFRR data, meter data — cannot be resident in
memory, and does not need to be. A tick needs a window, not a history.

The naive resolution ("load what you need, be careful about timestamps") fails on
both counts: it is slow because the access pattern is unpredictable, and it is
unsafe because correctness depends on discipline.

## Decision

### 1. Bitemporal by construction

Every fact is stored with **two** times:

- `valid_time` — the slot the fact describes.
- `knowledge_time` — when the engine could first have known it.

The only read API is:

```
Get(series, validRange, asOf: knowledgeTime) → column slice
```

There is **no** overload without `asOf`, and no API that returns
`knowledge_time > asOf`. The filter is applied inside the storage layer against
an index, not by the caller. Lookahead safety is therefore *structural*: to leak,
a developer or an agent would have to change the storage layer, not merely forget
a filter.

Revisions are appended, never updated in place. A price series that is revised
three times has three rows with the same `valid_time` and three different
`knowledge_time`s. `asOf` selects the latest row at or before the read time —
which is exactly what the engine saw.

### 2. Three tiers

| Tier | Medium | Contents | Lifetime | Accessed by |
|---|---|---|---|---|
| **Cold** | Immutable Parquet, partitioned `(series, source, date)` | Everything, at source fidelity, including raw order-book deltas and derived feature artefacts | Permanent | Offline calibration, cold-start of a replay |
| **Warm** | Memory-mapped columnar frames, LRU-evicted, one frame per `(series, day)` | What the current replay position needs | Session | The Belief cursor |
| **Hot** | Preallocated struct-of-arrays window covering `[t, t + H_hot]` | Only the series and slots the current tick's contract requires | One tick, refilled in place | Valuation, via C1 |

The **hot window is bounded by the horizon, not by history**. Memory is
`O(#series × H_hot × #scenarios)` regardless of backtest length. This is what
makes a multi-year backtest tractable: nothing about it grows with time.

The warm tier is memory-mapped so the OS page cache does the eviction work;
sequential replay is exactly the access pattern page caching is good at.

### 3. Belief snapshots are cursors, not containers

A `BeliefSnapshot` is an immutable value carrying `(asOf, revisionId)` and
**read-only views** (`ReadOnlyMemory<T>`) over the hot window's buffers, which
are contractually immutable for the duration of the tick. It is cheap to create,
cannot be mutated, and is content-addressed by the hash of the inputs that
produced it.

The distinction that matters, and which C0 §1 and C1 §10 turn on: a snapshot may
carry a *read-only view over a buffer frozen for the tick*; it may **not** carry
a *handle* — anything advanceable, refreshable, or aliasing state that another
component can mutate. A materialised value form, `BeliefSnapshotRecord`, exists
for seam recording and satisfies C0's serialisability requirement; the hot form
exists to avoid copying in the tick loop. They carry identical content and hash
identically. See `02-layers/L1-belief.md` §8.

### 4. Derived features are materialised, versioned artefacts

Anything expensive to compute and reused — fill-probability surfaces, price-impact
curves, scenario ensembles, activation-probability models — is computed **once,
offline**, written to the cold tier keyed by `FeatureSpecHash`, and read back as
data. Two consequences: backtests never recompute them, and a backtest can never
silently use a different definition than the one recorded.

### 5. Order books are never materialised in the hot path

The raw book stays cold. What crosses C1 is the *derived* quantity the Planner
actually needs: for a given product and slot, a compact representation of
achievable volume as a function of limit price and of the probability of fill.
The raw book exists for calibration and for high-fidelity replay, not for the
tick loop.

## Consequences

- Lookahead auditing becomes a cheap, decisive test: run a backtest with all
  facts having `knowledge_time > t` replaced by garbage, and assert bit-identical
  results (`T6`, `04-compliance/`). If any component peeks, the run diverges.
- Data ingestion becomes strict: every source must declare its publication
  semantics. "When did we know this?" must be answerable for every series before
  it is admitted. Series that cannot answer are quarantined, not guessed.
- Revisions are first-class, which also gives Settlement the ability to
  distinguish "we were wrong" from "the data was later corrected".
- Storage cost is higher than a last-value-wins store. This is the correct trade.
- Cost: the ingestion layer is more work than it looks, and it must be built
  before anything above it. See sequencing in `05-implementation/P0`.

## Rejected

- **Single-temporal store with careful queries.** Correctness by discipline; the
  failure mode is a backtest that looks like alpha.
- **Everything in memory.** Does not survive order-book data or multi-year runs.
- **Recompute features per tick.** Slow, and worse, silently version-drifting
  between a research run and a production run.
- **Database as the hot path.** Query latency and non-determinism in the tick
  loop; a memory-mapped columnar frame is orders of magnitude faster and exactly
  reproducible.
