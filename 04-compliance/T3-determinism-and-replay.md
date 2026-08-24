# T3 — Determinism and Replay

Normative. How a run is pinned, how it is compared, how a divergence is
localised, and how the tick's acyclicity is verified mechanically.

---

## 1. What this level proves

ADR-013 states the requirement: two runs with the same manifest **must** produce
byte-identical outputs, and this is tested, not asserted. `T3` is that test.

Four claims are under test, in dependency order:

1. **Reproducibility.** Same manifest → byte-identical artefacts, on the same
   machine, on a different machine, and after an unrelated refactor.
2. **Localisation.** When artefacts differ, the Merkle chain names the first
   upstream artefact that diverged, so "the backtest changed" is one comparison
   rather than a day of bisecting.
3. **Isolation.** Every recorded seam is a sufficient input for replaying its
   consumer with no ambient state.
4. **Acyclicity.** Within a tick, no component reads a value written later in
   the same tick (ADR-006, `00-overview/01-system-model.md` §2).

Everything in `T5` and `T6` depends on (1). A measured optimality gap between two
runs is noise if the runs are not reproducible; a lookahead audit that asserts
"the result did not change" proves nothing if the result never repeats anyway.

---

## 2. The run manifest

A run is **defined** by its manifest. Nothing outside the manifest may influence
output; anything that does is a defect of exactly the kind this level exists to
find.

| Field | Content | Why it is in the manifest |
|---|---|---|
| `codeRevision` | Git commit hash of the engine, plus a dirty-tree flag | A dirty tree may run, but its artefacts are marked non-reproducible and cannot be promoted to a golden baseline |
| `configHash` | Hash of the fully-resolved configuration, after all layering and overrides | The *resolved* config, not the file set — layered config is where "it works on my machine" lives |
| `calendarArtefact` | `MarketCalendar` hash: gates, product blocks, holidays, tick/lot sizes, HLZF tables per DSO per year (ADR-002) | A backtest of 2024 uses the 2024 calendar |
| `tzdataVersion` | Pinned IANA release | Civil time is versioned data; `INV-T-04` |
| `modelArtefacts` | Hash per artefact: scenario generator, ensemble reduction, value function `V`, fill model, activation model, Tier-3 surface `μ̂`, price-impact curves | ADR-004 §4 — a backtest can never silently use a different definition |
| `dataSnapshot` | `asOf` range **and** the ingestion watermark | Two runs a day apart see different revisions of the same series unless the watermark is pinned |
| `solverIdentity` | Backend name and version | `IOptimizationBackend` is pluggable (ADR-001); the choice is part of the run |
| `solverDeterminism` | Seed, thread count, deterministic work limit, MIP gap tolerance, presolve and cut settings | See §5 |
| `tieBreakRule` | The declared lexicographic tie-break for degenerate optima | Without it, degenerate alternates flap |
| `runMode` | `Reproducibility` \| `Production` | Explicitly different manifests; see §5.3 |

`manifestId` is the hash of all of the above and is carried in **every** payload
(`C0` §7). An artefact whose `manifestId` does not match the run that claims to
have produced it is a `HALT`.

### 2.1 Manifest completeness test

```
property ManifestCompleteness():
    m = Manifest.Current()
    # every artefact the run loaded is named in the manifest
    assert set(loaded_artefact_hashes()) == set(m.modelArtefacts.values())
    # no unlisted environment variable, file, or service was read
    assert sandbox_report().external_reads ⊆ m.declared_inputs
    # a run started with a dirty tree cannot be promoted
    if m.codeRevision.dirty: assert not promotable_to_golden(run)
```

The sandbox report is produced by the same instrumentation as the acyclicity
audit (§6): the run executes with file, network and clock access mediated, and
every access is recorded. An undeclared read is a manifest defect.

---

## 3. Golden replay

### 3.1 What is golden

Per tick, the golden record is the tuple of content hashes:

```
GoldenTick = ( manifestId,
               tickId,
               hash(BeliefSnapshot),      # C1
               hash(ValuationBundle),     # C2
               hash(PlanResult),          # internal, recorded (L3 §8)
               hash(ExecutionIntent),     # C3
               hash(ExecutionOutcome),    # C4
               hash(SettlementResult),
               hash(StateUpdate) )        # C5
```

Per run, the golden record is the hash of the ordered sequence of `GoldenTick`
rows plus a small set of **scalar summaries** carried alongside for human
readability: total objective, total realised P&L, the four error buckets, tier
usage counts, mode-transition counts, and solve-time percentiles.

The scalars are not the gate — the hashes are. The scalars exist so that a
reviewer approving a baseline change can see *what* moved, not merely *that*
something did.

### 3.2 The three golden scopes

| Scope | Content | Runs on | Budget |
|---|---|---|---|
| Smoke | One tick, one canonical `C1` fixture | Pre-commit | < 5 s |
| Canonical window | 7 days (`T0` §7): one DST transition, one negative-price episode, one peak-critical day, one high aFRR clearing block, one degraded-feed day | Every PR | ≤ 8 min |
| Full history | The whole backtest corpus | Nightly, and pre-release | ≤ 3 h |

### 3.3 Per-layer golden replay

Because each contract is a serialisable value with no object references
(ADR-013, `C0` §1), each layer is replayed alone against a recorded seam.

| Replay | Input | Assert | Proves |
|---|---|---|---|
| `L2` alone | Recorded `C1` stream + `StateSnapshot` stream | `hash(C2)` matches the recorded `C2` | Valuation is a function of belief and state, with no Belief store, no network and no clock present |
| `L3` alone | Recorded `C2` stream + `StateSnapshot` stream | `hash(C3)` and `hash(PlanResult)` match | The plan is a function of the primitives; the Planner never re-derives economics |
| `L5` alone | Recorded `C3` + `C4` streams | `hash(C5)` matches | Settlement reconstructs truth from outcome alone, uncontaminated by ex-ante belief |
| `L1` alone | Cold-tier snapshot + `asOf` sequence | `hash(C1)` matches | Belief is a pure read; `INV-D-02` holds through the whole read path |

Each replay runs in a **stripped host**: the Belief store is not constructed, the
network stack is unavailable, and the clock API throws. A layer that needs any of
them fails immediately and loudly rather than passing by accident because the
ambient service happened to be there.

```
test PerLayerReplay(seam, recorded_stream):
    host = StrippedHost(no_belief_store, no_network, clock_throws)
    for payload in recorded_stream:
        out = host.Run(seam.consumer, payload)
        assert out.contentHash == recorded_next(payload).contentHash
```

---

## 4. The Merkle chain over tick artefacts

Every payload carries `contentHash` (of itself) and `inputHashes` (of the
payloads that produced it) — `C0` §1, `INV-G-04`. Across a tick this forms a
DAG, and across a run a chain.

```
StateSnapshot(t-1) ─┐
                    ├─▶ BeliefSnapshot ─▶ ValuationBundle ─▶ PlanResult ─▶ ExecutionIntent
model artefacts   ──┘                                                            │
                                                                                 ▼
StateUpdate(t) ◀── SettlementResult ◀────────────────────────── ExecutionOutcome ┘
        │
        └────────────────────────────────────────────────▶ StateSnapshot(t+1)
```

### 4.1 Chain integrity tests

```
property ChainIntegrity(run):
    for artefact in run.artefacts:
        assert recompute_hash(artefact) == artefact.contentHash        # INV-G-04
        assert artefact.inputHashes non-empty
        for h in artefact.inputHashes:
            assert h in run.artefacts.hashes    # no dangling reference
        assert artefact.manifestId == run.manifestId

    # the chain is a DAG and it is the DAG the architecture declares
    assert edges(run) ⊆ declared_edges       # C1..C5 plus L0 read/write only
    assert is_acyclic(edges(run))
    # the only backwards edge is C5 -> L0, and it lands in the NEXT tick
    for e in backwards_edges(run):
        assert e == (SettlementResult(t) -> StateSnapshot(t+1))
```

`edges(run) ⊆ declared_edges` is the strong clause. An artefact whose
`inputHashes` names something the architecture says it cannot depend on is a
coupling violation caught by data, before anyone has to read the code.

### 4.2 Divergence localisation

When two runs of the same manifest differ, the chain turns the search into a
single walk:

```
procedure Localise(runA, runB):
    assert runA.manifestId == runB.manifestId    # else it is not a divergence,
                                                 # it is a different run
    for tick in ticks(runA) in order:
        for artefact in topological_order(tick):
            if hashA(artefact) != hashB(artefact):
                if all(inputHashes agree):
                    report FIRST_DIVERGENCE at (tick, artefact)
                    report field_level_diff(artefactA, artefactB)
                    return
    report "no divergence"
```

The first artefact whose **inputs all agree** but whose **own hash differs** is
the culprit. Everything downstream of it is a consequence, not a cause. This
converts "the backtest changed" from a day of bisecting into a single comparison,
which is the entire point of content-addressing (ADR-013).

The field-level diff is part of the tooling, not an afterthought: reporting "the
`ValuationBundle` differs" is not actionable; reporting "`term
PeakView.zPeak.annual`, field `pPoiFloorMw`, `0.4127 → 0.4183 MW`" is.

---

## 5. Solver determinism configuration

The solver is the single largest source of non-determinism and the settings are
part of the manifest, not of the environment.

| Setting | Reproducibility configuration | Production configuration |
|---|---|---|
| Seed | Fixed, in the manifest | Fixed, in the manifest |
| Threads | 1 | `> 1`, recorded |
| Time limit | **Deterministic work units** where the backend supports it | Wall-clock permitted, and the manifest records that it is |
| MIP gap tolerance | Fixed | Fixed |
| Presolve / cuts / heuristics | Fixed and explicit, never "auto" | Fixed and explicit |
| Parallel reduction order | Fixed partitioning scheme | Fixed partitioning scheme |
| Numerical focus / scaling | Fixed | Fixed |

### 5.1 The rules, restated as tests

```
test SolverDeterminism(model):
    r1 = Backend.Solve(model, cfg_reproducibility)
    r2 = Backend.Solve(model, cfg_reproducibility)
    assert bytes(r1.solution) == bytes(r2.solution)

test NoWallClockTimeLimit():
    assert cfg_reproducibility.timeLimit.unit == DeterministicWorkUnits
       or  backend_declares_no_support and runMode == Production

test SingleThreadedFallback():
    # a backend that cannot be deterministic under parallelism runs
    # single-threaded in the reproducibility configuration (ADR-013)
    if not Backend.DeterministicUnderParallelism:
        assert cfg_reproducibility.threads == 1

test NoRuntimeRandomness():
    assert no RNG is constructed in L1..L5 at runtime      # analyzer rule
    assert scenario draws are loaded from an artefact with a recorded seed

test IterationOrder():
    # no iteration over unordered collections in any output-affecting path
    assert analyzer_finds_no(Dictionary/HashSet enumeration in tick-loop assemblies)

test FloatingPointReduction():
    # parallel reductions use a fixed partitioning scheme, so summation order
    # is stable regardless of thread scheduling
    for threads in {1, 2, 4, 8}:
        assert reduce(large_array, threads) is bit-identical across runs
```

### 5.2 Wall-clock ban — `INV-G-05`

Two enforcement points, both tested:

- **Analyzer rule.** Clock APIs (`DateTime.Now`, `DateTime.UtcNow`,
  `Stopwatch` used for control flow, `Environment.TickCount`) are banned in the
  `L1`–`L5` assemblies. The analyzer has its own test suite: a fixture file that
  must produce the diagnostic, and a fixture that must not.
- **Payload check.** No contract field is populated from a construction-time
  timestamp. Time enters only as `tickId`/`SlotId` and `asOf`, supplied by the
  driver.

Timing *instrumentation* is permitted and is explicitly carved out: solve-time
measurement writes to a side channel that is excluded from `contentHash`. The
carve-out is narrow and is itself tested — a timing value that leaks into a
hashed field fails the golden comparison immediately, which is the desired
outcome.

### 5.3 Two manifests, honestly

Production runs with more threads and therefore a different manifest, and ADR-013
is explicit that this is recorded rather than pretended away. The consequence for
this level:

- Production artefacts are compared against production-manifest goldens, not
  against reproducibility-manifest goldens.
- The **reproducibility manifest is the one that gates merges**, because it is
  the only one where a hash difference is unambiguous.
- The measured throughput loss of the reproducibility configuration is reported
  in `T5`, so the cost of determinism is a number rather than an argument.

---

## 6. The acyclicity audit

ADR-006 and `00-overview/01-system-model.md` §2 claim the tick is a DAG:
`L0` is snapshotted at the top of the tick and written only at the end; no
component reads a value written later in the same tick. `T3` verifies it
mechanically rather than by reading code.

### 6.1 Instrumentation

During an instrumented replay, every `L0` access and every seam access is
recorded as `(tickId, layer, key, direction, sequence)`.

```
AccessRecord { tickId, layer ∈ {L0..L5}, key, direction ∈ {Read, Write}, seq }
```

`L0` is accessed through a single instrumented facade in the audit build, so
there is no path that escapes recording. A layer that obtains state by any other
route produces no record — which is why the audit also asserts that each layer's
recorded reads *account for* the inputs its output depends on (§6.3).

### 6.2 The assertions

```
audit Acyclicity(instrumented_run):
    for tick in ticks:
        writes = { r for r in tick.accesses if r.direction == Write }
        reads  = { r for r in tick.accesses if r.direction == Read }

        # 1. no read of a same-tick write
        for w in writes:
            for r in reads:
                assert not (r.key == w.key and r.seq > w.seq)

        # 2. L0 is read only in the read phase and written only in the
        #    write phase — the phases do not interleave
        assert max(seq of L0 reads)  <  min(seq of L0 writes)

        # 3. only L5 writes L0 within a tick (L3 may write Pending ledger
        #    entries, which is declared — ADR-006)
        assert writers(L0, tick) ⊆ {L5, L3-pending}

        # 4. seam order is strictly forward
        assert seq(C1) < seq(C2) < seq(C3) < seq(C4) < seq(C5)

        # 5. no layer reads a downstream layer's output within the tick
        for r in reads:
            assert not produced_by_downstream_layer_this_tick(r.key, r.layer)

        # 6. the state read at the top of the tick is the state written at
        #    the end of the previous tick, and nothing else
        assert tick.StateSnapshot.contentHash == prev_tick.StateUpdate.contentHash
```

Assertion 6 is the one that catches the subtle version of the bug: not a
same-tick read, but a *stale-by-two-ticks* read caused by a caching layer that
did not invalidate. The lag is supposed to be exactly one tick, explicit and
priceable (ADR-006). A lag of two is a different, unpriced system.

### 6.3 Read-set sufficiency

The complement of the acyclicity check, and the one that catches hidden state:

```
audit ReadSetSufficiency(instrumented_run):
    for tick, layer in layers:
        recorded_reads = tick.accesses[layer].reads
        # perturb an input the layer did NOT record reading; output must not move
        for key not in recorded_reads:
            perturbed = perturb(state_or_input, key)
            assert layer.Run(perturbed).contentHash == layer.Run(base).contentHash
        # perturb an input it DID record reading; output should move
        # (a recorded read that never matters is dead weight, reported as a warn)
```

A layer whose output changes when an unrecorded input is perturbed is reading
something through a path the audit cannot see — a static, a cached singleton, an
ambient service. That is precisely the hidden state that `00-overview/01-system-
model.md` §1 says destroys the compliance architecture.

### 6.4 When the audit runs

Nightly, on the canonical window, in an instrumented build. It is not run in CI
because the instrumentation cost is roughly `3–5 ×`, and it is not run in
production at all. A failure is a **merge blocker for the following day** and
pages the platform owner, because an acyclicity violation invalidates every
per-layer replay taken since the last clean audit.

---

## 7. Seam recording policy

Recorded seams for a long backtest are large. ADR-013 sets the policy and this
section makes it operational.

| Mode | What is written | Applies to | Retention |
|---|---|---|---|
| **Hashes only** | `GoldenTick` rows: `manifestId`, `tickId` and one content hash per artefact. Plus the scalar summaries of §3.1. | Every bulk backtest, every CI run, every production tick | Permanent — a `GoldenTick` row is tens of bytes |
| **Full seams** | Every `C1`…`C5` payload, plus `PlanResult` and `StateSnapshot`, in canonical binary | **Designated audit days** | 24 months, or until the manifest they belong to is retired |
| **Full seams + solver logs + IIS** | The above plus the solver's own log and any irreducible infeasible subsystem | On-demand, for incident investigation | 90 days |

### 7.1 Designated audit days

A day becomes a designated audit day if **any** of the following holds. The list
is configuration, versioned with the calendar.

| Trigger | Why |
|---|---|
| It is in the canonical CI window | The fixtures the whole suite depends on |
| A DST transition | `INV-T-02`, `INV-T-03` |
| An accounting period boundary | `INV-S-01` reset behaviour |
| A peak-critical or qualification-critical day (`peakCritical` / `qualCritical`) | The days where the largest downside lives (ADR-011) |
| A negative-price episode | Where `INV-P-07` becomes non-redundant |
| A day the engine spent in `DEFENSIVE`, `SAFE` or `HALT` | Degradation behaviour is only auditable if it was recorded |
| A day with a reserve activation above a threshold | Commitment feasibility under stress |
| A day with `unexplainedRatio` above threshold (`INV-S-07`) | The days that need forensics |
| Sampled: 1 in `N` ordinary days, deterministically chosen by hash | So the corpus is not composed only of exciting days |

The final row matters. A fixture corpus made entirely of interesting days trains
the suite on the tail and lets an ordinary-day regression through.

### 7.2 Promotion to fixture

A recorded seam becomes a permanent test fixture when it is referenced by a test.
At that point it is copied into the versioned fixture corpus with its manifest,
and it stops being subject to the retention policy. Fixtures are content-
addressed like any other artefact, so a fixture cannot be edited in place — an
"updated" fixture is a new artefact and the test that references it changes in
the same commit.

---

## 8. The bisect workflow when a golden hash changes

A golden hash change is either intentional or a regression. The workflow decides
which, in a fixed order, and it is short by design.

```
1. IS IT THE SAME MANIFEST?
   diff manifestA manifestB
   → if the manifest differs, this is not a regression. It is a different run.
     Identify the changed field (code, config, calendar, tzdata, a model
     artefact, the data watermark, the solver settings) and go to step 5.
   → tzdata and calendar changes land here regularly and are legitimate
     (INV-T-04). They still require a reviewed re-baseline.

2. LOCALISE
   Localise(runA, runB)  → (tick, artefact, field-level diff)
   → the first artefact whose inputs agree and whose own hash differs.

3. REPLAY THAT LAYER ALONE
   Replay the identified layer against the recorded input seam from run A.
   → reproduces the divergence  ⇒ the layer changed. Go to 4.
   → does not reproduce         ⇒ the divergence is environmental: threads,
     iteration order, a floating-point reduction, an undeclared input.
     Re-run under the reproducibility manifest and under the sandbox report
     (§2.1) to find the undeclared read.

4. BISECT THE COMMIT RANGE — on that layer only
   The unit of bisection is the single-layer replay from step 3, not the whole
   backtest. It runs in seconds, so a 200-commit range is ~8 replays.
   git bisect run ./replay-layer.sh <layer> <recorded-seam> <expected-hash>

5. CLASSIFY AND ACT
   intentional  → update the baseline. The PR must state: which artefact moved,
                  the field-level diff, the scalar summary delta (objective,
                  P&L, the four buckets), and why the new value is correct.
                  Reviewed by the layer owner AND the platform owner.
   regression   → revert or fix. The shrunk case from step 3 becomes a T0/T2
                  regression fixture in the same commit.
   environmental→ fix the determinism defect. Never re-baseline around it —
                  a baseline that absorbs non-determinism destroys the ability
                  to detect the next small regression, which is the ones that
                  matter (ADR-013, rejected: approximate reproducibility).
```

### 8.1 Baseline update discipline

- A baseline update is a **separate commit** from the change that caused it,
  referenced by it. Mixing them makes the history unreadable.
- The commit message records the manifest field that changed and the scalar
  delta. "Updated goldens" is not an acceptable message.
- Baselines are never updated on a dirty tree (§2.1).
- More than one baseline update per release is a signal, not a routine. The
  release checklist counts them.

### 8.2 What is explicitly not allowed

- **Tolerance-based golden comparison.** ADR-013 rejects approximate
  reproducibility: it removes the ability to detect small regressions.
  Comparison is on hashes.
- **Excluding a "noisy" field from the hash.** If a field is non-deterministic,
  the non-determinism is the bug. The only exclusions are the declared timing
  side-channel (§5.2), and adding to that list is an ADR-level decision.
- **Re-baselining to make CI green before a release.** The release gate in
  `T0` §5.2 requires the golden state to be explained, not merely accepted.
