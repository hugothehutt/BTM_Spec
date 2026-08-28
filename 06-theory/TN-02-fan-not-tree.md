# TN-02 — Fan, not tree: the declared solution method

**Ticket:** [#34](https://github.com/hugothehutt/BTM_Spec/issues/34) · **Class:** analytic ·
**Method:** derivation only, no data touched · **Branch:** `wf-34-fan-not-tree`

Fixes what the `[S × H × K]` ensemble structure commits the Planner to, declares the
solution method that follows, and defines the instruments that measure its error.
Sources are the five sub-surveys of [#13](https://github.com/hugothehutt/BTM_Spec/issues/13)
(`research/stochastic-methods`) and the practitioner survey of
[#44](https://github.com/hugothehutt/BTM_Spec/issues/44) (`research/benchmark-practice`).

Nothing here is an ADR. §9 lists what the specification gains and loses; the act is Hugo's.

---

## 0. Notation

| Symbol | Meaning |
|---|---|
| `S`, `H`, `K` | reduced scenario count, horizon in slots, series count (`ADR-005`) |
| `F_t` | the σ-algebra of what is known at slot `t` |
| `τ` | a gate instant |
| `π` | the policy — the whole rolling procedure, not one solve |
| `ω` | one realised path over the evaluation window |
| `H_plan` | the Planner's optimisation horizon, ~192 slots |
| `H_slow` | the slow loop's horizon, up to ~35,000 slots |

**Gate** and **stage** are distinct and are used strictly (§2). `S0`…`S5` are gates.
First and second are stages.

---

## 1. The ensemble is a fan

`ADR-005` fixes the ensemble as `[S scenarios] × [H slots] × [K series]` with weights on
the scenario axis. Every scenario is a complete `H`-slot path, and all `S` of them are
distinct at slot 1.

That is a **fan**: `S` chains radiating from a single root, with no interior branching.
Its filtration is flat.

```
F_1 = F_2 = … = F_H
```

A **tree** branches as time passes, so `F_1 ⊂ F_2 ⊂ … ⊂ F_H` strictly. That nesting is
what the word *multistage* denotes.

The array has no field in which "scenarios 7 and 12 are indistinguishable until slot 40"
could be written, because it has no interior nodes. The fan is not a modelling choice
made on top of the artefact; it is a property of the artefact's shape.

**Consequence.** A solve over the fan in which every variable carries `[s, t]` and no
non-anticipativity constraint is imposed is **anticipative**: it may choose a different
action per scenario from slot 1, as though it knew the path. Its value is an upper bound,
biased high, and is not attainable by any policy.

## 2. Gate and stage

The specification used one word for two things, in adjacent sections. They are separated
here and the separation is normative.

| Term | Meaning | Range |
|---|---|---|
| **Gate** | A point at which the engine commits. Driven by `C_gate`, or by `C_slow` / `C_tick` for the non-binding ones | `S0` … `S5` |
| **Stage** | A measurability level inside one solve | first, second |

Six gates. Two stages per solve. The two counts are unrelated, and no gate is a stage.

## 3. What the fan forces

The only correct repair of §1's anticipativity is to remove the scenario index from what
is committed. `#13` A.2.2 establishes that deleting the index is **exactly** equivalent
to imposing explicit non-anticipativity constraints — it is variable elimination on
`x^1 = x^2 = … = x^S`, requiring no convexity, no continuity and no integrality
assumption. `L3` §2 already does this and is correct.

But the repair yields **two** levels of measurability, not six. There is no third. The
statement "at `S2` I will know the day-ahead clearing and may re-decide" cannot be
expressed inside one fan solve, because expressing it needs an interior node.

So the six gates are six **separate** solves. Reality intervenes between them; the
commitment ledger carries the frozen decisions forward as parameters; a terminal `V`
closes each horizon.

## 4. The declared method

> **Rolling two-stage SAA over a scenario fan, glued by the commitment ledger and closed
> by a terminal value function.**

At each gate: condition a fresh ensemble on the realised state and on the ledger; solve
one two-stage problem; keep the first-stage decision; discard the second stage entirely.

The second stage is **not a plan**. It is a valuation of the future whose only job is to
price the first-stage commitment correctly.

**Guarantee.** An exactly optimal here-and-now decision for the `S`-point weighted
distribution, given `V`. Nothing more, and nothing about multistage optimality.

**Why this is a gain, not a concession.** Two-stage SAA theory gives consistency,
exponential-rate sample-size bounds and computable confidence intervals on the optimality
gap. The multistage theory is far weaker and its guarantees require stagewise
independence, which price processes violate — `#13` 1.3 finds cut sharing across the 192
slots invalid outright. `#13` §4.2 records that the fan formulation's guarantee is
strictly stronger than anything the SDDP family can offer on this model.

### 4.1 The first stage at each gate

The first stage is what is **irreversible**. `L3` §1's own Freezes column supplies it at
`S1` and `S2` and does not at `S3` and `S4`.

| Gate | First stage |
|---|---|
| `S0` | none — `C_slow` commits nothing; it refits `V` |
| `S1` | `rUp[b]`, `rDn[b]` for the auction being bid |
| `S2` | the day-ahead bid curve |
| `S3`, `S4` | **the order sent this tick, and nothing else** — one slot wide |
| `S5` | none — a controller, not an optimiser |

At `S3` and `S4` a sent order can fill, and that is the whole of the irreversibility. Any
wider first stage would be a plan asserted as a commitment. This also explains, rather
than merely asserts, `L3` §1's claim that `S1` is the row that matters: `S1`'s first stage
is a month-wide blind commitment, `S4`'s is one slot re-decided every tick, so the
stage-aggregation error is largest at `S1` and smallest at `S4`.

### 4.2 Positions are physically backed

The net market position summed across day-ahead, intraday and aFRR must equal the
physical flow in the same slot. Unbacked positions are outside the feasible set.

This is not a restatement of the POI bridge, which is a physical identity. It is a
separate constraint tying the market position to that identity, and without it the
formulation admits a matched buy and sell in one delivery period — zero flow, captured
spread, no physical involvement. `L2`'s `IntradayView` already intends it ("an infeasible
intraday plan is not a cheap plan, it is an invalid one") but a physical-support **bound**
does not deliver it: a position netting to zero is trivially supportable.

Two consequences beyond feasibility. The delineation accumulators and the peak charge
price *physical* flow, so an unbacked position earns nothing from the `λ_j` while still
perturbing the `A_j`. And it closes the benchmark pathology of §7 at the feasible set
rather than at the information set.

## 5. Two errors, opposite signs

| Error | Direction | Source |
|---|---|---|
| Within-scenario foresight in the second stage | **Optimistic** | The second stage sees all of `[τ, τ+H_plan]`; reality reveals progressively |
| Stage aggregation | **Pessimistic** | Two stages is a restriction of the multistage problem, so `VMS ≥ 0` (`#13` B.1.1) |

Neither `#13` nor this note signs the net. `B.1.1` cannot settle it: it compares two-stage
against multistage **on the same tree**, and on a fan the "multistage" problem is itself
anticipative. The quantity that matters — rolling fan policy against the true optimal
policy — is unsigned by theory and is measurable only by simulation (§8).

**`V` does not close the gap.** `#13` finds "lookahead + exact `V` = optimal" **false**
for `S1`→`S4`: stage-aggregation error survives an exact `V`. `V` prices the state at the
end of the horizon; it cannot repair a wrong information structure inside it.

## 6. Refinement cannot buy the tree back

Heitsch–Römisch–Strugarek bound reduction error by two terms: a scenario distance and a
**filtration distance** `D_f`. Raising `S` and improving the reducer drive the first term
down. For a fan of `S` distinct paths against any real tree, `D_f` is `O(1)` and is
**unaffected by any amount of scenario refinement** (`#13` §E, `05-scenario-reduction`).
There is a floor that no value of `S` reaches past.

Nor can a tree be obtained by reducing the fan. Heitsch and Römisch state that deleting
scenarios per Dupačová "is not appropriate" for multistage input trees. A tree requires
tree **construction** — a conditional-branching generator or a Markov lattice over a
low-dimensional exogenous state — which is a different artefact with a different
generator.

`S` therefore remains a genuine tuning parameter for **discretisation** and solve time,
and its curve is not an accuracy curve.

## 7. The benchmark, and why perfect foresight is rejected

A perfect-foresight solve at orderbook granularity earns most of its value from pure
financial arbitrage across the book — a channel the policy cannot reach.

Under §4.2 the objection is stronger than looseness. A benchmark that buys and sells the
same delivery period **violates the same physical-backing constraint the policy obeys**,
so it is not a bound on the right feasible region at all. The pathology closes through the
feasible set, needing no restriction on foresight.

A granularity cap is still required, for a different reason. The engine trades at
orderbook granularity, so a benchmark with book foresight would select the best resting
order in every slot and absorb **execution** skill into the denominator, hiding it. The
benchmark therefore transacts at `DA`, `IDA` and `ID3`/`ID1` clearing prices only.

`#44` finds the granularity axis real but unnamed in practice: KYOS's legacy battery index
gave its optimiser perfect foresight *of the EPEX `ID1` index* — index substitution, on
these markets — and Modo's German virtual asset replaces `ID3` with a centred rolling
mean. The leading published taxonomy has no rung for the axis, and KYOS have since moved
onto real orderbook data without published reasoning. This is a documented choice between
two published treatments, not an invention, and not a convention that can be cited.

**Capture rate is rejected as the metric.** `#44` finds Modo's numerator includes ancillary
revenue while its denominator is energy-only `TBX` at the node, so it is unbounded above
by construction, and it is defined in none of Modo's six regulated methodology documents.
The metric here is named `PolicyQuality` and the word *capture* is not used for it;
`T4` §3.1's existing `CaptureRatio` keeps its name and its meaning, which is execution
quality.

### 7.1 The horizon mismatch

`H_plan` is ~192 slots, roughly two days, and is truncated well short of the accounting
period. The monthly peak charge and the annual qualification never appear inside a Planner
solve; they reach it only through `V` and the `λ_j`. `#44`'s recommended benchmark is
monthly, and every benchmark it surveyed is built per calendar day with a midnight SoC
reset, with Modo's German index setting grid fees to zero and excluding `Netzentgelte` and
`StromNEV §19` outright. No publisher benchmarks a behind-the-meter asset against a
demand-charge objective.

A monthly benchmark may shape the peak across weeks the Planner cannot see. Its gap
therefore mixes policy quality with the cost of the truncation. The resolution is to
report the same metric at **both** horizons and read the difference:

| Reported | Horizon and terminal condition | Isolates |
|---|---|---|
| `PolicyQuality` @ matched | `H_plan`, terminal `V` as the Planner uses | Policy quality alone |
| `PolicyQuality` @ monthly | one Berlin month, peak term and delineation live | Policy quality plus truncation |
| Difference | — | **The cost of the `H_plan` truncation, i.e. what `V` is repairing** |

The difference is the first direct measurement of `V`'s adequacy anywhere in the
specification.

## 8. The instruments

Three, measuring different things. Only one carries a band.

### 8.1 Instrument 1 — `PolicyQuality`

`realised value / benchmark value`, bounded by 1 by construction. The benchmark is a
sequential re-solve with only the imminent slot binding, full physical constraints,
physically backed per §4.2, transacting at index granularity per §7, respecting `T5` §4.1's
gate ordering. Reported at both horizons of §7.1.

Mixes anticipativity, ensemble misspecification, reduction error and — at the monthly
horizon — `V` error. It is a quality measure, not an attribution.

### 8.2 Instrument 2 — report-versus-realised drift

`D(τ) = E_ω[z_rep − z_real]` per gate kind, in EUR and per cent, where `z_rep` is the
objective the gate solve at `τ` returns over `[τ, τ+H_plan]` including terminal `V`, and
`z_real` is realised settlement over the **same** span plus `V` at the same endpoint.
Window and terminal term must match or the difference is meaningless.

Free — both numbers already exist. Signed: positive means the model over-promises.
**Unbanded**, deliberately: it mixes anticipativity, ensemble misspecification and `V`
error with no way to separate them, so a threshold would fire without saying which moved.
Its value is as a trend, and a persistently positive `D(S1)` is the earliest warning that
the blind reserve commitment is mispriced.

### 8.3 Instrument 3 — isolated stage-aggregation error

The only instrument that isolates anything, and the falsifier for §4.

Build a **synthetic instance** small enough that a genuine multistage tree is enumerable.
That tree is the true law by fiat: no estimation error, no real data, no orderbook, no
financial arbitrage channel. Then compute two numbers on that same tree.

`z_MS` — solve the genuine multistage stochastic program on the tree. Every node carries
its own decision variable; two paths indistinguishable at slot `t` must take the same
action at `t`. This is the best expected value any policy can achieve when information
genuinely arrives progressively.

`z_rolling-fan` — simulate the §4 method on the same tree:

```
for each path ω in the tree:
    for each slot t along ω:
        collect the tree's continuation paths from the current node
        FLATTEN them into a fan            ← the information structure is destroyed here
        solve the two-stage problem on that fan
        keep the first-stage decision only
        advance one slot along ω, observe the branch
    record the realised value on ω
z_rolling-fan = probability-weighted mean over all ω
```

The continuation paths are the tree's own, so the probability law is identical; only the
branching is discarded.

```
A = (z_MS − z_rolling-fan) / z_MS
```

| Error | Why it cancels |
|---|---|
| Sampling | Both use the full tree |
| Ensemble misspecification | The tree **is** the truth |
| Reduction | No reduction; all paths kept |
| `V` error | Horizon is the end of the world; no terminal value needed |
| Financial arbitrage | Synthetic prices, no orderbook |
| **Stage aggregation** | **The only difference between the two solves** |

**Band: `A ≤ 5 %`.** Not derivable — no theorem supplies it. Its meaning is the
stage-aggregation error at which building a tree becomes worth it. Calibration argument:
`T5` records the `S=64` discretisation gap at 0.4 %, so a stage-aggregation error below
roughly ten times that would have a tree repairing the smallest error in the stack at the
cost of the largest artefact in the project. Breach reopens the tree.

### 8.4 Design criteria for the synthetic instance

`A` is measured on a toy and transfers to the real problem by argument, not by proof.
Instance design is where this instrument can be quietly rigged, so the criteria are fixed
here and the numbers are chosen elsewhere (`#47`).

The instance must:

1. **Be exactly solvable as a tree** — total path count enumerable, so `z_MS` is exact
   rather than approximated.
2. **Match the ratio of commitment horizon to information horizon**, per gate kind. This
   is the ratio that drives stage-aggregation error, and `S1` and `S4` sit at opposite
   extremes of it (§4.1). Both extremes must be instanced.
3. **Preserve which constraint binds.** If the real problem is energy-bound, an instance
   tuned to bind on power measures a different mechanism.
4. **Branch at a rate defensible against the fitted joint model**, stated with its
   justification rather than chosen for convenience.
5. **Carry no terminal value function.** The horizon is the end of the world, so `V` error
   cannot leak in.
6. Be committed as a **fixture**, with the design argument in this note and the numbers in
   the register.

The instance need **not** carry the delineation regime or the peak charge. Both are
month-coupled, and instrument 1 at the monthly horizon (§7.1) measures them.

## 9. What the specification gains and loses

Corrections, per `CLAUDE.md` rule 2 — the replaced text is deleted, not annotated.

| Site | Action |
|---|---|
| `L3` §0 | **New.** Declares §4's method, its guarantee, and the three instruments by reference |
| `L3` §1 | The staging table's first column becomes **Gate**; the "six information sets" paragraph is replaced (§2, §3); `S3`'s `Freezes: nothing` is corrected per §4.1 |
| `L3` §2 | The first stage is stated per gate (§4.1); the physical-backing constraint is added (§4.2) |
| `ADR-005` | The `S` consequence bullet is narrowed: `S` controls discretisation and solve time, and its curve is not an accuracy claim (§6) |
| `T5` §6.2 | The objective-versus-`S` row is relabelled a discretisation curve; rows are added for instruments 1–3 and for the `V`-truncation difference |
| `INV-*` | One new invariant for §4.2's backing constraint |

Two assertions the specification carried are **withdrawn**, not reworded: that `S`
has a measurable cost/quality curve, and that the objective-versus-`S` curve shows
where accuracy flattens. Both claim more than §6 supports. A third — one
formulation, one set of tests, six information sets — is a genuine restatement:
the engineering assertion is unchanged and only its framing was false.

**Out of scope, with a re-entry condition.** Tree construction is beyond this map's
destination. It returns as a fresh effort if instrument 3 measures `A > 5 %`.

## 10. What is not settled here

- The **staged denominator** for instrument 1 — delineation, host load, availability as
  separate stages — is month-coupled and downstream of §7.1. Ticketed with the market
  ladder (`#48`).
- **Orderbook depth**, and how a depth restriction is priced, collides with
  `FillProbView`'s deliberate ban on a price field and cannot be phrased precisely until
  the quoting policy of `ADR-012` is settled. Fog.
- **Holdout policy** — instruments 1 and 2 run on held-out paths, and what stops those
  paths leaking into the `V` fit or the reducer is unanswered (`#46`).
- `#44`'s coverage limits: Enverus, Baringa, Cornwall Insight, Flexcity, Statkraft and
  Trayport unresolved; LCP Delta's German index publishes no methodology. Its §A.6
  (rolling intrinsic) is carried from a prior run and is not independently verified.

## 11. Sources

| Claim | Source |
|---|---|
| Index omission ≡ explicit NACs, no assumptions | `#13` `02-nonanticipativity-twostage` A.2.1–A.2.2; SDR §2.4, §3.1.4 |
| Two-stage is a restriction of multistage, `VMS ≥ 0` | `#13` B.1.1; Huang & Ahmed, *Oper. Res.* 57(4):893–904 |
| Fan filtration is flat; `D_f` is `O(1)` and refinement-proof | `#13` `05-scenario-reduction` §E; Heitsch–Römisch–Strugarek 2006 |
| Scenario deletion "not appropriate" for multistage | `#13` §E; Heitsch & Römisch, *Scenario tree reduction for multistage stochastic programs* |
| Lookahead + exact `V` ≠ optimal for `S1`→`S4` | `#13` `03-terminal-value` |
| Cut sharing across slots invalid under stagewise dependence | `#13` `01-methods` 1.3 |
| Fan guarantee strictly stronger than SDDP here | `#13` `01-methods` §4.2, §4.3 |
| Granularity-capped foresight in practice | `#44` A.4.2 (KYOS `ID1`), A.7 (Modo German virtual asset) |
| Capture rate unbounded above, undefined in regulated methodology | `#44` B.2, A.3 |
| No behind-the-meter demand-charge benchmark exists | `#44` C.7, E.5 |
| Policy ladder is published practice | `#44` C.5 (Suena, monthly, German asset) |
