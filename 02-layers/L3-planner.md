# L3 — Planner

Owns the whole decision. Composes the primitives from C2 into a MILP family,
solves it at staged market gates carrying commitments forward, and emits order
intent.

**Input:** `ValuationBundle` (C2) + `StateSnapshot` (L0, lagged)
**Output:** `ExecutionIntent` (C3), `PlanResult` (internal, recorded)
**Purity:** pure given a fixed solver configuration and seed (ADR-013).

---

## 1. Staged decisions, not one solve

The Planner does not run once per day. It runs at gates, and each run has a
different information set and a different irreversibility. Conflating them is
the mistake that makes the problem look intractable.

| Stage | Clock | Decides | Freezes | Information added since last stage |
|---|---|---|---|---|
| S0 Slow loop | `C_slow` | `V(SOC, peakState, qualState)`| nothing | new realised state, refreshed long-horizon scenarios |
| S1 Reserve | `C_gate` (aFRR capacity gate) | `rUp[b]`, `rDn[b]` offers | reserve offers once submitted | reserve price beliefs |
| S2 Day-ahead | `C_gate` (DA gate) | DA bid curve per slot | DA position at clearing | reserve awards from S1 |
| S3 Post-DA rebalance | `C_gate` | initial intraday target position | nothing | realised DA clearing |
| S4 Continuous intraday | `C_tick` | intraday target position, re-optimised | progressively, as fills occur | book state, updated forecasts |
| S5 Dispatch | real time | setpoint within the SOC corridor | — | activation signal |

### Delineation information set per stage

The delineation machinery is monthly, so each gate commits under a different amount of
knowledge about the month it is committing into (ADR-017).

| Stage | Delineation information | Measurement of what not knowing it cost |
|---|---|---|
| S0 | MTD accumulators; `MW_month` distribution; AW>0 forecast for the remaining month | `V_del` projection drift against the previous slow tick |
| S1 | Same, **without** D+1 certainty — the widest delineation uncertainty of any stage | Re-solve on the realised D+1 `(24)¼` vector; the objective delta is the reserve gate's delineation foresight cost |
| S2 | Realised D+1 `(24)¼`, since § 51 / § 51b make it a function of day-ahead price signs | Value of the S1-forecast versus realised indicator |
| S3–S4 | Elapsed-slot `(2)¼`, `(23)¼` realised; `(30)` firming as the month proceeds | Intraday drift of the `λ_j` within the day |

S1 is the row that matters. The reserve gate is the one commitment made before the
month's route is knowable, so a large measured cost there is a direct argument for
smaller reserve volumes late in an undecided month — and a small one retires the
question.

**The same core model is solved at every stage.** What changes is which variables
are free and which are fixed by the commitment ledger. One formulation, one set
of tests, six information sets. Writing six models would be six times the surface
area and six chances to disagree about the physics.

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

**Coupling** (from C2 `CouplingConstraint`, and the heart of the cross-market
problem — ADR-010):

```
p_d[t] + rUp[b(t)]        ≤ pBattMaxDischargeMw[t]
p_c[t] + rDn[b(t)]        ≤ pBattMaxChargeMw[t]
soc[t] − rUp[b(t)]·D/η_d  ≥ socMinMwh
soc[t] + rDn[b(t)]·D·η_c  ≤ socMaxMwh
soc[s,t] ∈ [socMinMwh, socMaxMwh]  for weighted scenario mass ≥ 1−ε
```

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

Where the stage has genuine recourse — reserve committed before intraday
information — the model is two-stage stochastic: first-stage variables are
scenario-independent (`rUp`, `rDn`, DA position), second-stage variables carry a
scenario index. Non-anticipativity is structural: first-stage variables simply
have no `s` index, which is both the cheapest and the least error-prone way to
enforce it.

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
