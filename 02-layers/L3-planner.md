# L3 — Planner

Owns the whole decision. Composes the primitives from C2 into a MILP family,
solves it at gates carrying commitments forward, and emits order
intent.

**Input:** `ValuationBundle` (C2) + `StateSnapshot` (L0, lagged)
**Output:** `ExecutionIntent` (C3), `PlanResult` (internal, recorded)
**Purity:** pure given a fixed solver configuration and seed (ADR-013).

---

## 0. The declared solution method

**Rolling two-stage SAA over a scenario fan, glued by the commitment ledger and closed by
a terminal value function.**

The `ADR-005` ensemble is a **fan**: `S` complete paths, all distinct at slot 1, so
`F_1 = F_2 = … = F_H`. It has no interior nodes and therefore encodes no progressive
branching. The only correct treatment is to drop the scenario index from what is
committed, which yields **two** measurability levels per solve and no third.

So the engine does not solve one multistage stochastic program. At each gate it conditions
a fresh ensemble on the realised state and on the commitment ledger, solves one two-stage
problem, keeps the first-stage decision and discards the second stage entirely. The second
stage is not a plan; it is a valuation whose only job is to price the first-stage
commitment. Reality supplies the new information between gates, and the ledger carries the
frozen decisions forward.

**The policy is multistage. Each solve is two-stage.**

**Guarantee.** An exactly optimal here-and-now decision for the `S`-point weighted
distribution, given `V`. No claim of multistage optimality is available, and none is made.

**Declared error.** The second stage sees the whole of `[τ, τ+H_plan]` while reality
reveals progressively, so the model treats its future self as better informed than it will
be. Stage-aggregation error survives an exact `V`. It is bounded by `T5`'s three
instruments, of which the isolated measurement carries the acceptance band. Derivation,
instrument definitions and the synthetic-instance design criteria are in
`06-theory/TN-02-fan-not-tree.md`.

---

## 1. Decisions at gates, not one solve

The Planner does not run once per day. It runs at gates, and each run has a
different information set and a different irreversibility. Conflating them is
the mistake that makes the problem look intractable.

| Gate | Clock | Decides | Freezes | Information added since the last gate |
|---|---|---|---|---|
| S0 Slow loop | `C_slow` | `V(SOC, peakState, qualState)`| nothing | new realised state, refreshed long-horizon scenarios |
| S1 Reserve | `C_gate` (aFRR capacity gate) | `rUp[b]`, `rDn[b]` offers | reserve offers once submitted | reserve price beliefs |
| S2 Day-ahead | `C_gate` (DA gate) | DA bid curve per slot | DA position at clearing | reserve awards from S1 |
| S3 Post-DA rebalance | `C_gate` | initial intraday target position | the orders it sends | realised DA clearing |
| S4 Continuous intraday | `C_tick` | intraday target position, re-optimised | progressively, as fills occur | book state, updated forecasts |
| S5 Dispatch | real time | setpoint within the SOC corridor | — | activation signal |

### Delineation information set per stage

The delineation machinery is monthly, so each gate commits under a different amount of
knowledge about the month it is committing into (ADR-017).

| Gate | Delineation information | Measurement of what not knowing it cost |
|---|---|---|
| S0 | MTD accumulators; `MW_month` distribution; AW>0 forecast for the remaining month | `V_del` projection drift against the previous slow tick |
| S1 | Same, **without** D+1 certainty — the widest delineation uncertainty of any stage | Re-solve on the realised D+1 `(24)¼` vector; the objective delta is the reserve gate's delineation foresight cost |
| S2 | Realised D+1 `(24)¼`, since § 51 / § 51b make it a function of day-ahead price signs | Value of the S1-forecast versus realised indicator |
| S3–S4 | Elapsed-slot `(2)¼`, `(23)¼` realised; `(30)` firming as the month proceeds | Intraday drift of the `λ_j` within the day |

S1 is the row that matters. The reserve gate is the one commitment made before the
month's route is knowable, so a large measured cost there is a direct argument for
smaller reserve volumes late in an undecided month — and a small one retires the
question.

**The same core model is solved at every gate.** What changes is which variables are free
and which are fixed by the commitment ledger. One formulation, one set of tests, six
gates. Writing six models would be six times the surface area and six chances to disagree
about the physics.

Each gate solve is a **separate two-stage problem** (§0): the variables it commits carry no
scenario index, everything downstream carries one. The gates are not nested stages of a
single stochastic program — each solve conditions a fresh ensemble on the realised state
and on the ledger.

**Gate** and **stage** are distinct throughout this document. `S0`–`S5` are gates. First
and second are stages. Six gates, two stages per solve; the counts are unrelated.

S5 is a controller, not an optimiser, and is out of scope (§7).

---

## 2. Core model

Decision variables are the C2 §2 vocabulary. Constraints in three groups.

**Physical.** SOC dynamics with split charge/discharge and one-way efficiency
(`00-overview/02-conventions.md` §3); SOC bounds; power bounds per slot; POI
envelope; the POI bridge `p_poi = load − pv_out − p_batt`, where `pv_out = Σ_k (pv_avail − q)` is post-curtailment (`02-conventions.md` §1, ADR-016).

**Market.** Position accounting per market and slot; DA position fixed after
clearing; intraday volume bounded by `FillProbView`'s `BoundTerm`; reserve
offers on the product grid (`minBidMw`, `bidStepMw`, symmetry if required).

**Every position is physically backed.** The net market position summed across day-ahead,
intraday and aFRR equals the physical flow in the same slot (`INV-X-07`). This is not the
POI bridge restated — the bridge is a physical identity, this ties the market position to
it. Without it the formulation admits a matched buy and sell in one delivery period: zero
flow, captured spread, no physical involvement. A physical-support *bound* does not
deliver it, because a position netting to zero is trivially supportable. The delineation
accumulators and the peak charge price physical flow, so an unbacked position would earn
nothing from the `λ_j` while still perturbing the `A_j`.

**Coupling** (from C2 `CouplingConstraint`, and the heart of the cross-market
problem — ADR-010):

```
p_d[t] + rUp[b(t)]        ≤ pBattMaxDischargeMw[t]
p_c[t] + rDn[b(t)]        ≤ pBattMaxChargeMw[t]
soc[t] − rUp[b(t)]·D/η_d  ≥ socMinMwh
soc[t] + rDn[b(t)]·D·η_c  ≤ socMaxMwh
soc[s,t] ∈ [socMinMwh, socMaxMwh]  for weighted scenario mass ≥ 1−ε

Σ_slots∈b  eAfrrUp[t] ≥ awardedUpMw[b],  eAfrrDn[t] ≥ awardedDnMw[b]   ∀ t ∈ b
```

`rUp[b]`, `rDn[b]` are the quantity **offered**, and every constraint above binds
on the whole of it: an award obliges the full offer, so feasibility may not be
discounted by the chance of not winning. Only the *revenue* term is weighted, by
`P(award | p, b)` (ADR-018 §7). Planning feasibility against an expected award
would leave the battery unable to serve the blocks it actually won.

The last pair is the **energy-offer obligation** (`INV-P-12`): awarded capacity
must be offered into the balancing energy market in every slot of its block. It is
`≥`, not `=` — offering beyond the obligation is permitted and sometimes
profitable. The energy leg is marginal-priced, so bidding true marginal cost there
is optimal and it does not inherit the capacity leg's shading problem.

**Delineation** (ADR-017). The seven month-to-date accumulators of C6 §3.1 are carried
forward as variables over the horizon, and their state equations are constraints:

```
A_j = A_j^MTD + Σ_{t ∈ H} contribution_j(t)          j ∈ {(3),(5),(6),(9),(11),(26),(29)}

Z1NB[t] − Z1NE[t] = (load + p_c − P_net − p_d)·Δt    Z1NB, Z1NE ≥ 0
(1)¼ = MIN[Z1NB; Z2V]    (2)¼ = MIN[Z1NE; Z2E]    (23)¼ = Z1NE − (2)¼
(25)¼ = (24)¼·(23)¼      (27)¼ = (24)¼·(2)¼
```

Linearisation, and where the binaries actually go:

| Construct | Binary | Why |
|---|---|---|
| `Z1NB · Z1NE = 0` | one per slot | Levies penalise import while the premium rewards export, so adding the same δ to both pays whenever the premium exceeds the levy. Unbounded without complementarity |
| `(2)¼ = MIN[Z1NE; Z2E]` | **none** | `λ_11 ≥ 0` — `(11)` feeds `(13)`, the base of *both* routes — so the objective pushes `(2)` up and `(2) ≤ Z1NE, (2) ≤ Z2E` is tight |
| `(1)¼ = MIN[Z1NB; Z2V]` | one per slot, when load is present | `λ_9` carries the sign of `(30)·premium − levy_rate` and flips with the route. With `load = 0`, `Z1NB ≤ Z2V` always and the `MIN` is linear |
| `(24)¼` | none | A parameter from the day-ahead price series (L1 §4), not a variable |

**No lever carries a delineation coefficient.** Curtailment `q`, charge source,
discharge routing and reserve headroom are priced through these constraints against the
`λ_j` from C2. Writing a coefficient on any of them would require the `MIN`-branch case
analysis of `00-overview/03` §5, evaluated at a state this solve has not yet chosen —
which is ADR-016's rejected rule one level up.

**Objective.** Assembled entirely from C2 terms. The Planner adds no economics
of its own — if it needs a number, a view must own it.

```
max  Σ LinearTerms + Σ PwlTerms + V(socTerminal) − Σ peakPriceEurPerMw·zPeak·proration
     + Σ_j λ_j · A_j
     − cvarWeight · CVaR_α(imbalance + activation cost)
```

### Two-stage structure

Every gate solve is two-stage. First-stage variables are scenario-independent;
second-stage variables carry a scenario index. Non-anticipativity is structural:
first-stage variables simply have no `s` index. That is exactly equivalent to imposing
explicit non-anticipativity constraints — variable elimination on `x^1 = … = x^S`,
needing no convexity, continuity or integrality assumption — and it is both the cheapest
and the least error-prone way to enforce it.

**The first stage is what is irreversible**, and nothing else.

| Gate | First stage |
|---|---|
| S0 | none — `C_slow` commits nothing; it refits `V` |
| S1 | `rUp[b]`, `rDn[b]` for the auction being bid |
| S2 | the day-ahead bid curve |
| S3, S4 | **the order sent this tick, and nothing else** — one slot wide |
| S5 | none — a controller, not an optimiser |

At S3 and S4 a sent order can fill, and that is the whole of the irreversibility. A wider
first stage would assert a plan as a commitment. This is also why S1 is the row that
matters: its first stage is a month-wide blind commitment, S4's is one slot re-decided
every tick, so the stage-aggregation error is largest at S1 and smallest at S4.

---

## 3. Cross-market co-optimisation

The three tiers of ADR-010 behind `ICoOptimizer`:

```csharp
interface ICoOptimizer {
    CoOptResult Solve(in ValuationBundle v, in StateSnapshot s,
                      in SolveBudget budget, out TierUsed tier);
}
```

- **Tier 1** — the joint model above, solved directly. Reference accuracy.
- **Tier 2** — Lagrangian decomposition on the coupling block. Multipliers
  `μ_up[b]`, `μ_dn[b]` are the internal reservation price of headroom. Returns a
  feasible primal **and** a dual bound, so the gap is certified per solve.
- **Tier 3** — evaluate the learned surface `μ̂`, price the headroom, solve one
  spot MILP.

**Escalation policy.** Tier 3 escalates to Tier 2 within budget when: the
solution sits within a tolerance of a coupling constraint boundary; the state is
outside the training envelope of `μ̂`; `peakCritical` or `qualCritical` is set
(ADR-011); or `V` is stale. Escalation is logged and appears in the settlement
record, so "we were on the fast path that day" is always visible.

**Tier 2 numerics.** Bundle method preferred over plain subgradient for
convergence stability; iteration cap and time cap from `SolveBudget`; **every
exit path returns a feasible primal**, including the cap-exhausted path. A
decomposition that can return infeasible is not usable in production.

---

## 4. The commitment ledger

Read from L0 (C5 §4), it is what makes the staged design safe.

- **`Confirmed`** entries are hard constraints. Awarded reserve fixes
  `deliveryObligationMw` and its SOC corridor for the block. Filled spot positions
  fix the position variable.
- **`Pending`** entries are probabilistic exposure. An open order that may or may
  not fill is modelled as a scenario-dependent position, weighted by the fill
  belief. This is what lets S4 re-optimise continuously without either
  double-selling the same MWh or ignoring live exposure.
- **Every solve is checked for commitment feasibility before it is emitted**
  (`INV-P-02`). A plan that cannot honour a confirmed award never reaches C3.

---

## 5. From position to orders

The MILP produces target positions. The **quoting policy** produces orders
(ADR-012).

1. Extract the target position per market and slot.
2. Extract the **shadow value** — the dual on the position accounting constraint,
   which is the Planner's indifference price in EUR/MWh.
3. Extract **urgency** — the objective degradation if the position is not
   reached, normalised.
4. The quoting policy maps `(target, shadowValueEurPerMwh, urgency, fill curve,
   microstructure state)` to a limit-order ladder.
5. **Hard rule:** never quote through the shadow value, except for intents tagged
   `CommitmentCover`. Enforcement is `INV-P-10`: severity `warn`, but the
   offending intent is **blocked** and never reaches the venue. It is not a
   `HALT`, because covering a confirmed commitment at a loss is sometimes
   correct — but such an intent must carry the tag, and an untagged one is a
   quoting-policy defect.

**DA is different.** DA needs a monotone bid curve, produced by parametric
re-solve over a grid of candidate clearing prices; the resulting price-quantity
schedule *is* the curve. Monotonicity is asserted (`INV-P-09`); a non-monotone
result signals a formulation error, most often a missing coupling constraint, and
halts rather than being sorted into compliance.

**aFRR capacity is different again**, and for a different reason: it is
**pay-as-bid** (ADR-018), so the price offered is the price paid and the markup
over indifference is the entire margin rather than spread captured from a
counterparty.

1. Extract `μ_up[b]`, `μ_dn[b]` — the duals on the headroom coupling constraints,
   which are the reservation price of a MW of headroom in EUR/MW/h. Tier 2
   produces these directly; Tier 3 evaluates `μ̂`.
2. `IReserveBidPolicy` maps `(μ[b], P(award | ·, b), envelope)` to one
   `(limitPriceEurPerMwH, volumeMw)` per block and direction. A single point, not
   a curve: whether several price/volume pairs may be submitted for one block is
   unconfirmed, and until it is, C3 carries one.
3. `μ[b]` crosses C3 as `reserveShadowValueEurPerMwH`, audit-only, so Settlement
   can attribute the markup without the policy and the Planner sharing a private
   channel.
4. **Hard rule:** never bid below `μ`. `INV-P-11`, same shape and same
   `CommitmentCover` exception as `INV-P-10`, and it bites harder — under
   pay-as-bid a bid below `μ` is not a thin margin but a certain loss on every MW
   awarded.

**The bid policy runs inside the Tier 2 loop.** `B(E)` depends on `P(award | p)`,
`p` depends on `μ`, and `μ` depends on the solve. The circularity is one scalar
per block, and Tier 2 already iterates `μ` by subgradient, so the fixed point
costs nothing structural. Tier 3 prices once against `μ̂` and carries the one-pass
error into its measured gap — the ladder making the same trade-off it exists to
make everywhere else.

---

## 6. Feasibility, budget and failure

### Feasibility restoration

The model must never be reported infeasible to the operator without a diagnosis.
Bounds carry a `BoundReason` (C2 §3.4) and relaxation proceeds in a fixed order:

```
Risk  →  Liquidity  →  (stop)
```

`Physical`, `Regulatory` and confirmed-commitment constraints are **never**
relaxed. If the model is infeasible with only those remaining, the state is
genuinely infeasible — the world has moved outside what the asset can deliver —
and the engine goes to `HALT` with the irreducible infeasible subsystem reported.

Relaxation is done with explicit slack variables at declared penalties, not by
deleting constraints, so the amount and location of relaxation is visible in the
solution and recorded.

### Solve budget

`SolveBudget` carries a deterministic work limit (ADR-013), a MIP gap tolerance,
and the escalation allowance. On budget exhaustion:

1. Best incumbent, if feasible and within an acceptable gap → use it, record the
   gap.
2. Else, previous tick's plan projected forward and re-checked for feasibility.
3. Else, `DEFENSIVE` baseline: honour commitments, protect peak, no new exposure.
4. Else, `HALT`.

Every step is recorded. Repeated use of step 2 or 3 is itself a mode trigger
(ADR-014).

### What is never dropped

Peak protection and commitment feasibility, in every mode (ADR-014 §3).

---

## 7. The dispatch boundary

S5 is a controller and is out of scope for this specification, but its interface
is fixed at C3 §4: the Planner publishes a **SOC corridor** and a setpoint. The
corridor, not the setpoint, is binding. The controller may deviate from the
setpoint to follow an aFRR activation signal but must remain inside the corridor,
which is exactly the guarantee that keeps the reserve commitment deliverable
without the Planner needing to run at control frequency.

---

## 8. Outputs recorded but not transmitted

`PlanResult` is recorded for settlement and diagnostics (ADR-013), not sent to
Execution:

- objective value and its decomposition by `EconomicEffect` — this is
  `plannedByEffect` in C5, and it is what makes planned-vs-realised comparable;
- λ_SOC at the optimum (the subgradient of `V`) — the diagnostic that the
  original design wanted as an input and now gets as an output;
- `μ*` reservation prices, when Tier 2 ran — interpretable, human-checkable;
- MIP gap, tier used, escalations, solve time, relaxations applied;
- the full planned trajectory, for the counterfactual re-runs in C5 §6.

---

## 9. Testing this layer

Detail in `04-compliance/T2`. The properties that matter most:

- **Feasibility** — the planned trajectory satisfies every physical constraint,
  and satisfies confirmed commitments under **every** scenario in the ensemble,
  not just the mean.
- **Determinism** — same C2 and same manifest, byte-identical C3.
- **Metamorphic** — raise the Marktprämie `MAX[AW − MW_month; 0]`: planned PV charging
  must be non-decreasing, since PV charging is what moves `(15)` and converts grey to
  green. Raise the EnFG rate in a slack regime: planned grid charging must not decrease.
  Raise `afrrCapPriceEurPerMwH`: planned reserve MW must be
  non-decreasing. Shift all prices up uniformly: planned discharge must not
  decrease. Raise `peakPriceEurPerMw`: planned peak must not increase. These are cheap to
  state and catch formulation errors that feasibility tests cannot.
- **Tier agreement** — on small instances, Tiers 1, 2 and 3 solved on identical
  input; Tier 2's dual bound must dominate Tier 1's objective; Tier 3's gap
  recorded.
- **Commitment safety** — inject a confirmed award that the current SOC cannot
  serve; assert `HALT`, never a quiet under-delivery.
- **Budget exhaustion** — force a time-out at each fallback rung and assert the
  correct rung is taken and recorded.
