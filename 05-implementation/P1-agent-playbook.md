# P1 — Agent Playbook

How to build this with Claude Code. Operational, not aspirational.

---

## 1. The premise

An agent asked to "build a cross-market battery optimiser" produces something
that compiles, looks reasonable, and is unverifiable. The task is
underdetermined: there is no definition of correct, no boundary on what may be
touched, and no way to check the output short of reading every line — at which
point the agent saved nothing.

An agent asked to "implement `AfrrCapacityView` against `stubs/C2_ValuationBundle.cs`
and `02-layers/L2-valuation.md` §2, such that the seven property tests in L2 §7
pass and `INV-V-12` holds" produces something checkable in one command.

The difference is not the model. It is that the second task has a **frozen
contract surface** and a **decidable acceptance criterion**. This specification
exists to make the second kind of task the normal kind.

Three properties of this architecture make agent-assisted implementation work
unusually well here, and they are not accidents:

- **Every layer is a pure function of declared inputs** (`01-system-model.md` §1).
  An agent can be handed one layer and a recorded input stream and never need to
  understand the rest of the system.
- **Every seam is a serialisable value type** (ADR-013), so a recorded seam is a
  test fixture. Valuation can be replayed against a recorded C1 stream with no
  Belief store present.
- **The invariant registers are the specification of correctness**, written down,
  with IDs. "Make `INV-V-01` through `INV-V-16` pass" is an instruction; "make it
  correct" is not.

Where the spec is silent, none of this holds, and the work is not delegable. See
§6.

---

## 2. Repo layout

The spec is not the engine. It is copied — or submoduled — into the engine
repository, because an agent working in the engine repo must be able to read
`ADR-009` without leaving the working tree.

```
engine/
  CLAUDE.md                        # §3 below. The operating rules.
  docs/
    arch/                          # this specification, verbatim
      00-overview/
      01-adr/
      02-layers/
      03-contracts/                # the frozen surface, as prose
      04-compliance/
      05-implementation/
  src/
    Flexbid.Btm.Contracts/         # generated from 03-contracts/, committed
      Quantities.cs                # ADR-003
      Enums.cs
      Envelope.cs                  # C0
      C1_BeliefSnapshot.cs   …  C5_StateUpdate.cs
      Validators/                  # one per invariant register
    Flexbid.Btm.Abstractions/      # Interfaces.cs — the layer interfaces
    Flexbid.Btm.Belief/            # L1  (W1)
    Flexbid.Btm.Valuation/         # L2  (W4)
      Views/                       # one file per view
      Composer/
    Flexbid.Btm.Planner/           # L3  (W5, W8, W9)
      Model/                       # MILP construction
      Backends/                    # IOptimizationBackend implementations
      CoOptimizers/                # Tier1 / Tier2 / Tier3
    Flexbid.Btm.Quoting/           # W6
    Flexbid.Btm.Execution/         # adapter onto the existing simulator
    Flexbid.Btm.Settlement/        # L5  (W3)
    Flexbid.Btm.State/             # L0  (W3)
    Flexbid.Btm.Harness/           # W2 — manifest, replay, audits, Merkle diff
  tests/
    Conformance/                   # one project per contract; INV-* coverage
    Property/                      # L2 §7, L3 §9 metamorphic and property tests
    Golden/
      fixtures/                    # recorded seams for designated audit days
      weeks/                       # the recorded weeks; the gate of §5
    Determinism/                   # T3 byte-identity, T6 lookahead, acyclicity
    Calendar/                      # DST fixtures, HLZF tables, gate schedules
  artefacts/                       # content-hashed model artefacts, or pointers
```

Two placement rules that matter:

**Generated contracts are committed, not generated at build time.** A generated
artefact that is not in the tree cannot be diffed, and a contract change that
does not show up as a diff defeats the change discipline. Generate once, review
the diff, commit.

**`docs/arch/` is read-only to agents by convention.** An agent may cite it and
must not edit it. A change to `03-contracts/` is a human decision with a version
bump attached (C0 §5).

---

## 3. `CLAUDE.md`

Paste this at the engine repo root. It is long because the failure modes it
prevents are specific.

````markdown
# Flexbid BTM Engine — Operating Rules

A behind-the-meter battery optimisation engine. The architecture specification
lives in `docs/arch/` and is **normative**. This file is the short form; when the
two disagree, `docs/arch/` wins and this file is wrong and should be fixed.

## Where to look things up

Do not infer. Cite.

| Question | Source |
|---|---|
| What crosses a seam? | `docs/arch/03-contracts/C0`–`C5`. The field table is complete and closed. |
| Why is it built this way? | `docs/arch/01-adr/ADR-001`…`ADR-015`. Each has context, decision, consequences, rejected alternatives. |
| Units, signs, time grid | `docs/arch/00-overview/02-conventions.md`. Non-negotiable. |
| What a layer does internally | `docs/arch/02-layers/L0`–`L5`. |
| Who owns an economic effect | `docs/arch/02-layers/L2-valuation.md` §5, the term ownership matrix. |
| What must be true | The invariant registers: `INV-G-*` (C0 §3), `INV-D-*` (C1 §9), `INV-V-*` (C2 §8), `INV-P-*` (C3 §5), `INV-X-*` (C4 §7), `INV-S-*` (C5 §9), `INV-T-*` (conventions §4.2). |
| Build order and why | `docs/arch/05-implementation/P0-workstreams.md`. |

Reference by exact ID in code comments, commit messages and PR descriptions:
`ADR-004`, `C2 §3.2`, `INV-V-01`. An ID is checkable; "per the design" is not.

## 1. Contract change discipline

**The single most important process rule in this project.**

A change to anything in `docs/arch/03-contracts/` or `src/Flexbid.Btm.Contracts/`
requires, **in the same commit**:

1. the documentation change in `docs/arch/03-contracts/`;
2. the version bump — additive optional field with a default is minor; removing a
   field, changing a type, changing a **unit**, changing a semantic, or tightening
   a range is **major**;
3. the conformance test update in `tests/Conformance/`;
4. an update to every affected layer design in `docs/arch/02-layers/`.

Unit changes are **always** major, even when the C# type is unchanged. A field
that silently moves from EUR/MWh to EUR/kWh is the archetypal catastrophic change.

Adding a `VarSymbol` (C2 §2) is a major version bump. Adding an `EconomicEffect`
additionally requires an owner in the term ownership matrix — do not add one
without asking (see §8).

If you find yourself wanting to add "just one more field" to get something
working: stop, and say so. That is a design conversation, not an edit.

## 2. No wall-clock reads

**No component reads the system clock.** Not `DateTime.Now`, not
`DateTime.UtcNow`, not `DateTimeOffset.UtcNow`, not `Environment.TickCount`, not
`Stopwatch` in any path that affects output.

Time enters through the tick's `SlotId` and `asOf`, both supplied by the driver
(ADR-013). A clock read in a layer is a defect (`INV-G-05`) and an analyzer rule
fails the build on it. If you need a duration, it is a `SlotSpan`. If you need
"now", you need the tick's `SlotId` passed in — change the signature, do not read
a clock.

Solver time limits are expressed in **deterministic work units**, never wall
clock (`SolveBudget`). A wall-clock limit makes the run non-reproducible.

## 3. No lookahead

**Every Belief read takes `asOf`.** There is exactly one read API:

```csharp
ColumnSlice<T> Get<T>(SeriesId series, SlotId validFrom, SlotSpan length, SlotId asOf);
```

Do **not** add an overload without `asOf`, a `GetLatest`, a default parameter, or
a helper that captures a "current" `asOf` at construction. The filter lives inside
the storage layer against an index, so lookahead safety is structural (ADR-004
§1). Any convenience that erodes it deletes the property the whole store exists
to guarantee, and the failure mode is a backtest that looks like alpha.

Related, and easy to get wrong: **no same-tick backwards reads.** Within a tick
every arrow points right (ADR-006). L0 is snapshotted at the top of the tick and
written at the end. If a layer needs something a later layer produces, it gets it
next tick, from `StateSnapshot`, at a lag. If that seems awkward, it is the
architecture working; say so rather than routing around it.

## 4. Typed quantities — no bare doubles across a seam

Quantities are distinct value types (ADR-003): `BatteryPowerKw`, `PoiPowerKw`,
`LoadKw`, `PvKw`, `ReserveMw`, `EnergyKwh`, `EnergyPrice` (EUR/MWh),
`CapacityPrice` (EUR/MW/h), `PeakPrice` (EUR/kW/period), `Money` (EUR),
`Efficiency`, `SocFraction`, `SlotId`, `SlotSpan`.

- **No bare `double` in any contract field** (`INV-G-02`). Analyzer-enforced.
- **No implicit conversion between frames or units.** There is exactly one
  sanctioned frame bridge — `PowerFrames.PoiFromSite(load, pv, battery)`
  implementing `p_poi = load − pv − p_batt` — and exactly one kW↔MW conversion
  per direction, in `MarketUnits`. There is one `1000.0` in the codebase per
  direction. If you write a second one, you have introduced a bug class.
- **Battery frame:** positive = discharge. **POI frame:** positive = import.
  Never conflate them. A sign error here does not scale the answer, it inverts
  the strategy.
- SOC is `EnergyKwh` internally, always. `SocFraction` is presentation only and
  never appears in the objective.
- The objective is denominated in **EUR** and nothing else.
- Hot numeric kernels may operate on raw `ReadOnlySpan<double>` **inside a typed
  façade**. The type safety belongs at the boundary where errors occur, not
  inside a loop the JIT must vectorise.

## 5. Determinism

Two runs with the same manifest must produce byte-identical outputs (ADR-013).
This is tested in CI. A failing determinism test is a defect, never a flake.

- Fixed solver seed, fixed thread count, deterministic work limits.
- **No runtime randomness.** Scenario draws are offline artefacts with recorded
  seeds.
- **No iteration over unordered collections** in any path that affects output.
  Use ordered keys. `Dictionary` enumeration order is not a contract.
- Parallel reductions use a fixed partitioning scheme so summation order is
  stable.
- Model artefacts, the market calendar and the tzdata version are pinned in the
  run manifest. Never load a model at runtime that is not in the manifest.
- **No live ML inference in the tick loop.** Export to a PWL table, a tree
  ensemble or a small tensor that C# evaluates natively, or keep it out of the
  hot path.

## 6. Invariant validation stays on

Contract validation runs **in every environment, including production** (C0 §3).
It costs microseconds against a solve measured in milliseconds.

- Do not add a flag to disable it.
- Do not wrap it in `#if DEBUG`.
- Do not "optimise" it out of a hot path.
- Validate **twice per seam**: the producer before emitting, the consumer on
  receipt (C0 §4). Producer failure enters the degradation ladder; **consumer
  failure is `HALT`**, unconditionally.

The temptation to disable validation in production is precisely the temptation to
stop noticing corruption.

## 7. Degradation is parameters, not branches

Quality is data (ADR-014 §1). Map it to risk parameters — CVaR level, position
scale, peak safety margin, chance level, staleness shrink. Do **not** write
`if (quality == Degraded)` in a layer. One code path, parameterised.

Two things are never degraded away, in any mode including `SAFE`: **peak
protection** and **commitment feasibility**. If a confirmed reserve award cannot
be served, `HALT` and alert — never quietly under-deliver.

## 8. Hot-path allocation discipline

A year-long backtest with scenario ensembles is allocation-bound long before it
is CPU-bound (ADR-001).

In the tick loop and in Belief:

- struct-of-arrays, not array-of-structs;
- `Span<T>` / `ReadOnlySpan<T>` / `ReadOnlyMemory<T>`; `ArrayPool<T>` for
  transients;
- **no LINQ**;
- no per-slot object allocation, no per-slot closure, no per-slot boxing;
- preallocate the hot window once and refill it in place;
- `readonly record struct` for quantities; pass large payloads by `in`.

Outside the tick loop — calibration, fitting, reporting — write whatever is
clearest.

## 9. What to ask about rather than decide

Stop and ask if the change touches:

- the term ownership matrix, or adds an `EconomicEffect`;
- the composition order or a stage precondition;
- the tier ladder's escalation policy;
- the value function's state space or conditioning;
- a `VarSymbol`, or any contract field table;
- what counts as a critical input for degradation-mode purposes.

These are where the economics lives. Everything else, the spec determines — see
`docs/arch/05-implementation/P1-agent-playbook.md` §6.

## 10. Commits and PRs

- One workstream per worktree; do not touch another workstream's project.
- Cite the ADR / contract / invariant IDs the change implements.
- A PR that changes behaviour must show the golden-week plan hash before and
  after, and explain the diff. An unexplained hash change is a blocking review
  comment.
````

---

## 4. Workstream-to-worktree mapping

One worktree per workstream, branched from a commit in which the contract surface
is already frozen.

| Worktree | Workstream | May write | May read | Must not touch |
|---|---|---|---|---|
| `wt-contracts` | W0 | `src/Flexbid.Btm.Contracts`, `src/Flexbid.Btm.Abstractions`, `tests/Conformance` | all | anything else — **this worktree closes before the others open** |
| `wt-belief` | W1 | `src/Flexbid.Btm.Belief`, `tests/Calendar` | Contracts, Abstractions | Valuation, Planner, Settlement |
| `wt-harness` | W2 | `src/Flexbid.Btm.Harness`, `tests/Determinism`, `tests/Golden` | all (it instruments everything) | any layer's logic — it observes, it does not fix |
| `wt-settlement` | W3 | `src/Flexbid.Btm.Settlement`, `src/Flexbid.Btm.State` | Contracts, Abstractions, recorded C4 fixtures | Belief internals, Valuation, Planner |
| `wt-valuation` | W4 | `src/Flexbid.Btm.Valuation` | Contracts, Abstractions, recorded C1 + `StateSnapshot` fixtures | Belief internals, Planner |
| `wt-planner` | W5 | `src/Flexbid.Btm.Planner/{Model,Backends,CoOptimizers/Tier1}` | Contracts, Abstractions, recorded C2 fixtures | Valuation internals |
| `wt-quoting` | W6 | `src/Flexbid.Btm.Quoting`, `src/Flexbid.Btm.Execution` | Contracts, recorded `PlanResult` | Planner internals |
| `wt-slowloop` | W7 | `src/Flexbid.Btm.Planner/SlowLoop`, artefact fitting | Contracts, recorded state and ensembles | Planner model |
| `wt-tier2` | W8 | `src/Flexbid.Btm.Planner/CoOptimizers/Tier2` | Tier1 as an oracle | Tier1's model construction |
| `wt-tier3` | W9 | `src/Flexbid.Btm.Planner/CoOptimizers/Tier3` | Tier1 and Tier2 as oracles | Tier1, Tier2 |

**Why the surface must be frozen and committed before any parallel work starts.**
Two reasons, one obvious and one not.

The obvious one: seven worktrees writing against a moving contract is seven
merge conflicts in the same file, and the conflicts are semantic rather than
textual. A field renamed in `wt-valuation` and used in `wt-planner` merges
cleanly and fails at runtime.

The non-obvious one: **the contract surface is what makes the worktrees
independent in the first place.** `wt-valuation` can be developed against
recorded C1 fixtures with no Belief store present, and `wt-planner` against
recorded C2 fixtures with no Valuation present, *only because* the seams are
serialisable value types (ADR-013). That property is the entire justification for
parallel work. If the surface is still moving, the fixtures are invalid, the
worktrees are coupled through the moving surface, and the parallelism is
imaginary.

Consequence: W0 is not a workstream that runs alongside others. It runs, it
closes, it is merged to the trunk, and only then does anyone else branch.

---

## 5. The golden test as the gate

The unit of verification is a **recorded week plus a deterministic plan hash**.

**Construction.** Pick a week with real variety — a negative-price day, a
peak-critical day, a high-reserve-price day, and at minimum one DST boundary in
the calendar test set. Record every seam payload for the week (C1…C5) plus the
run manifest. Compute the hash chain. Commit the hashes; store the payloads in
`tests/Golden/weeks/` or by reference if large.

**Use.** The gate is one command:

```
dotnet test tests/Golden -- --week 2025-W07
```

It replays the week from the manifest and compares, seam by seam, against the
recorded hashes.

**Why this makes refactors safe.** A refactor that does not change behaviour
produces an identical hash chain. That is a total statement over every slot,
every scenario, every term and every order in the week — vastly stronger than any
unit test suite, and it costs one command. The practical effect is that refactors
become *cheap to attempt*, which is what determines whether they happen at all.

**Why this makes agent output verifiable without reading every line.** The review
question stops being "is this code correct?" and becomes two much smaller
questions:

1. **Did the hash change?** If no, the change is behaviour-preserving, and review
   is about clarity and maintainability, not correctness.
2. **If yes, is the change the one that was intended, and only that one?** The
   Merkle chain localises the divergence to a specific seam and a specific
   artefact (ADR-013), so the diff to inspect is bounded. "The C2 hash changed at
   the DA gate on Wednesday because `AfrrCapacityView`'s breakpoint count went
   from 8 to 12" is a reviewable statement. "The backtest changed" is a day of
   bisecting.

**Rules.**

- An unexplained hash change blocks the PR. Not "investigate later" — blocks.
- Updating a golden hash is a separate, explicit commit with a stated reason, and
  it never rides along with the change that caused it.
- Keep at least two golden weeks: one on the trunk path and one deliberately
  pathological (missing feeds, degraded quality, a solver timeout, an infeasible
  restoration). The second catches the degradation ladder, which the first never
  exercises.
- The lookahead audit (T6) and the acyclicity audit (T3) run on the same week.
  They are cheap and they catch the two failure modes that no amount of code
  review reliably catches.

---

## 6. Delegate versus decide

**The rule: delegate what the specification fully determines; decide yourself
what the specification leaves open.**

The specification is deliberately complete about *structure* and deliberately
open about *economics* (root `README.md`: layer designs are "normative for
structure, indicative for algorithm choice"). Where a field table, an invariant
register or a formulation fully pins the answer, an agent's output is checkable
against it and the work is mechanical. Where the spec leaves a choice, the choice
*is* the value of the system, and it is not checkable — a plausible wrong answer
looks exactly like a right one.

A useful second test: **if you cannot state the acceptance criterion as a test
before the work starts, you are deciding, not delegating.**

| Delegate | Why it is safe | Acceptance criterion |
|---|---|---|
| Contract DTO generation from the C0–C5 field tables | The tables are complete and closed; the mapping is mechanical | Field-table-to-DTO correspondence test |
| Validators from the invariant registers | Each invariant is a stated predicate with a stated `ViolationAction` | One passing and one failing test per `INV-*` |
| Serialisers, hashers, Merkle-chain plumbing | Fully determined by ADR-013 and C0 §1 | Round-trip and cross-machine hash stability |
| Test scaffolding, fixtures, property-test generators | The properties are enumerated in L2 §7 and L3 §9 | The named properties pass |
| Mechanical MILP constraint construction from C2 terms | L3 §2 gives the constraints algebraically; the five shapes have declared MILP realisations (ADR-008) | Declared `binariesByOrigin` equals built binaries; metamorphic suite |
| Adapters: `IExecutionAdapter`, `IOptimizationBackend`, ingestion connectors | The interface is fixed; the other side is external and documented | Backend conformance (T1); adapter consumes the projection type |
| Calendar mechanics: DST slot counts, block indexing, HLZF table loading | Fully specified in conventions §4 and ADR-002 | `INV-T-*`, `INV-D-04`, DST fixtures |
| Refactors under a green golden test | Verified totally by the hash chain | Identical plan hash |

| Decide yourself | Why it cannot be delegated |
|---|---|
| **Anything touching the term ownership matrix** (L2 §5) | A new owner or a moved effect changes what is priced where. The failure mode — an effect counted twice — produces a plausible number that passes every type check and biases strategy in a direction that looks like profit (ADR-009). No test written after the fact catches it, because the test would be written from the same wrong premise. |
| **The composition order and stage preconditions** | The order encodes an economic argument: peak headroom must be priced on top of a battery whose energy is already marked. Reversing it double-counts. The machine checks *that the declared order is followed*; it cannot check that the declared order is the right one. |
| **The tier ladder's escalation policy** (ADR-010, L3 §3) | This is the risk appetite of the system, expressed as thresholds. Escalating too rarely destroys value in the tail — exactly the days that matter — and escalating too often makes the fast path pointless. It is a judgement about the cost of being wrong, and the cost of being wrong is not in the spec. |
| **The value function's state space** (ADR-007, ADR-011) | Which discrete states `V` is conditioned on determines whether the §19(2) cliff is representable at all. Adding a dimension multiplies the fitting problem; omitting one silently smooths away the most valuable feature of the curve. |
| **Any new `EconomicEffect`** | It is a closed enumeration by design. A new member is a claim that a new thing is being priced, which requires an owner, a base, a settlement counterpart, and an argument that it does not overlap an existing effect. |
| **What counts as a "critical" input** | It sets the `DEFENSIVE`/`SAFE` boundary, and therefore how often the engine stops trading. Too tight and it is unoperable; too loose and it trades on invented data. |
| **Conservative defaults per field** (ADR-014 §4 rung 5) | "Conservative" is field-specific and directional: a missing load forecast defaults *high* (protects peak); a missing reserve price defaults *low* (do not chase invented revenue). Getting the direction wrong makes degradation actively dangerous, and it looks identical in code review. |
| **Risk parameters**: CVaR level, chance level, position scale, safety margins | These are the risk function's decisions expressed as numbers. |
| **Scenario count, breakpoint count** | Accuracy/speed trade-offs with a measured curve (T5) — the measurement is delegable, the choice is not. |

Everything in the left column has a test that fails when it is wrong.
Nothing in the right column does. That is the whole distinction.

---

## 7. Review protocol

What a human must check on agent-authored code **in this codebase specifically**.
General code review still applies; this is the list of things that are specific,
cheap to check, and catastrophic to miss.

Work down the list. It is ordered by (probability × cost).

**1. Sign frames.** For every new arithmetic expression involving power: which
frame is it in? Battery positive is discharge; POI positive is import. Is
`PowerFrames.PoiFromSite` the only place they meet? Search the diff for any other
`load - pv -` or any sign flip on a power quantity. A second implementation of
the bridge is a defect even if it is currently correct.

**2. Units.** EUR/MWh versus EUR/kWh; kW versus MW; kWh versus percent for SOC.
Search the diff for `1000`, `0.001`, `/ 1000.0`, `* 1e-3`, `/ 4.0` and `* 0.25`.
Every one of them must be either inside `MarketUnits`, or a documented
slot-to-hour conversion via `SlotSpan.Hours`. A magic `4` is a quarter-hour
assumption and a quarter-hour assumption is a DST bug.

**3. Invariant coverage.** Did the change add a field, a term, a bound or a
constraint? Then: which invariant covers it? If none does, either the change is
outside the contract (fine) or an invariant is missing (not fine). New code that
is covered by no `INV-*` is the crack that everything else eventually falls
through.

**4. A new term without an ownership entry.** The specific check: does the diff
introduce a term with an `EconomicEffect` that the emitting view does not own in
L2 §5? `INV-V-02` catches it at runtime, but catch it at review, because the fix
is a design conversation and the runtime failure is a `HALT` in a backtest at
2am. Related and subtler: does the diff introduce a *second* term for an effect
the view already owns, on the same base and overlapping slots? That is
`INV-V-01`, and it is the failure this architecture is most concerned about.

**5. Wall-clock creep.** Search the diff for `DateTime`, `DateTimeOffset`,
`Stopwatch`, `TickCount`, `Timer`, `Task.Delay`. The analyzer catches the obvious
cases; it does not catch a clock read inside a third-party call, or a
`Stopwatch` whose value reaches a log line that reaches a hash. Ask: could this
value differ between two runs of the same manifest?

**6. Same-tick backwards reads.** Does the change read something a later layer
produces in the same tick? The tells: a new constructor parameter that is a
service rather than a value; a field on a layer that is written during the tick;
anything named `Current`, `Latest` or `Live`. The acyclicity audit (T3) catches
it on a replay, but the review catch is cheaper and the architectural
conversation is better had at review time.

**7. Lookahead creep.** Any new Belief read: does it take `asOf`, and is the
`asOf` the tick's, not a captured or defaulted one? Any new helper that wraps a
Belief read is suspect by construction — the whole point of ADR-004 is that the
read signature is inconvenient on purpose.

**8. Iteration order.** Any new `Dictionary`, `HashSet` or `GroupBy` whose
enumeration reaches the output. Ordered keys, or a sort, or it is a determinism
bug waiting for a runtime upgrade.

**9. Allocation in the tick loop.** LINQ, `ToList`, `ToArray`, string
interpolation, lambdas capturing locals, `params` arrays, boxing of a quantity
struct into an interface. Fine in calibration; not fine per slot per scenario.

**10. Degradation branches.** Any `if` on a `QualityLevel`, `Provenance` or
`DegradationMode` inside a layer. Quality maps to parameters, not to control flow
(ADR-014 §1). The legitimate exceptions are the central mode computation and the
`SAFE`-mode intent filter (`INV-P-05`); everything else is a branch that should
have been a number.

**11. Did the golden hash change, and was that intended?** Last, because it is
the cheapest and most decisive. If it changed and the PR does not explain why,
nothing else on this list matters yet.
