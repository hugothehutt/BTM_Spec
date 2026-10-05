# BTM Engine — The Planner (L3): core concepts and assumptions, for review

> **Reader:** someone who has read `docs/handover-2026-08.md` and nothing else.
> **Scope:** the Planner only — `02-layers/L3-planner.md` and the decisions it
> implements (`ADR-007`, `-010`, `-012`, `-016`, `-017`, `-018`, `-019`, `-020`).
> Belief, Valuation, Execution and Settlement appear only where the Planner
> depends on them.
> **Purpose:** put every load-bearing concept and assumption of the Planner in
> one place, so that each one can be checked on its own. Each item says what is
> assumed, where it lives, and what breaks if it is wrong.
> **Status:** Advisory. Nothing here binds; every normative statement lives in
> the document cited beside it. Checked against the repository at `521bd76`,
> 2026-10-05.

---

## How to use this file

- **Part 1** recaps the Planner's core concepts. These are *decisions*: they
  hold if the assumptions under them hold.
- **Part 2** is the main part: the assumptions, grouped by kind. Each has an ID
  (`A-xx`) so a review comment can cite it.

Each assumption has a **Basis** tag:

| Basis | Meaning |
|---|---|
| **Stated** | The specification states it, normatively, in the cited document |
| **Asserted** | Stated, but the specification says outright that it is asserted and not modelled or proven |
| **Declared guess** | Recorded as a guess fixed before backtesting (`assumed-declared` in the theory notes) |
| **Implicit** | Not stated anywhere I could find, but the formulas depend on it. Treat these as my reading and check them first |

---

## Part 1 — Core concepts

### 1.1 What the Planner is

| # | Concept | Claim | Source |
|---|---|---|---|
| K-01 | **Inputs and outputs** | Input is the `ValuationBundle` (C2) and the lagged `StateSnapshot` from L0. Output is `ExecutionIntent` (C3), plus a `PlanResult` that is recorded but not sent. | `L3` header, §8 |
| K-02 | **Pure function** | Given a fixed solver configuration and seed, the same C2 and manifest produce byte-identical C3. | `L3` header, `ADR-013` |
| K-03 | **No economics of its own** | The objective is assembled only from C2 terms. If the Planner needs a number, a Valuation view must own it. | `L3` §2, `ADR-008` |
| K-04 | **Decides positions, not prices** | The MILP decides target positions and physical quantities. Limit prices come from separate policies outside the solver. | `ADR-012`, `ADR-018` |

### 1.2 Solution method

| # | Concept | Claim | Source |
|---|---|---|---|
| K-10 | **Rolling two-stage SAA over a fan** | At each gate, the Planner conditions a fresh ensemble on the realised state and the ledger, solves one two-stage problem, keeps the first stage and throws away the second. "The policy is multistage. Each solve is two-stage." | `L3` §0, `TN-02` |
| K-11 | **The guarantee, and only this one** | The here-and-now decision is exactly optimal for the `S`-point weighted distribution, given `V`. The spec claims no multistage optimality. | `L3` §0 |
| K-12 | **Non-anticipativity by omission** | First-stage variables have no scenario index. This is equivalent to explicit non-anticipativity constraints and needs no convexity or integrality assumption. | `L3` §2 |
| K-13 | **Six gates, one model** | S0 slow loop, S1 reserve, S2 day-ahead, S3 post-DA rebalance, S4 continuous intraday, S5 dispatch. The same core model is solved at each gate. Only which variables are free and which are fixed by the ledger changes. | `L3` §1 |
| K-14 | **The first stage is what is irreversible** | S1: `rUp[b]`, `rDn[b]`. S2: the DA bid curve. S3/S4: the order sent this tick, one slot wide. S0 and S5 commit nothing: S0 refits `V`, and S5 is a controller, not an optimiser. | `L3` §2 |
| K-15 | **Gate ≠ stage** | Six gates, two stages per solve. The gates are separate two-stage problems, not nested stages of one program. | `L3` §1 |

### 1.3 The core model

| # | Concept | Claim | Source |
|---|---|---|---|
| K-20 | **Physical constraints** | SOC dynamics with separate charge and discharge variables and one-way efficiency; SOC and power bounds; POI envelope; POI bridge `p_poi = load − pv_out − p_batt` with `pv_out = Σ_k (pv_avail − q)`. | `L3` §2, conventions §1, §3 |
| K-21 | **Curtailment is a decision** | `q[t,k]` is a MILP variable priced in the same objective as everything else. It is never a rule or a preprocessing step. | `ADR-016` |
| K-22 | **Every position is physically backed** | The net DA + ID + aFRR position equals the physical flow in the slot (`INV-X-07`). This is stronger than a bound, which a position netting to zero would pass trivially. | `L3` §2 |
| K-23 | **Coupling: price headroom, don't pre-allocate it** | Reserve uses power headroom and SOC corridor, and that is written as explicit coupling rows. Pre-allocating a fixed MW split is rejected because it removes options and hides the loss. | `L3` §2, `ADR-010` |
| K-24 | **Offer, not award** | `rUp[b]`, `rDn[b]` are the MW *offered*. Every feasibility row binds on the full offer. Only revenue is weighted by `P(award)`. | `ADR-018` §7 |
| K-25 | **Energy-offer obligation** | Awarded capacity must be offered into the balancing energy market in every slot of the block. The constraint is `≥`, not `=` (`INV-P-12`). | `ADR-018` §9 |
| K-26 | **Delineation as state equations** | Seven month-to-date accumulators `A_j` are MILP variables with state equations. Valuation publishes only the marginal values `λ_j`. No lever carries a delineation coefficient of its own. | `ADR-017`, `L3` §2 |
| K-27 | **Where the binaries are** | One per slot for import/export complementarity. One per slot for `(1)¼ = MIN[Z1NB; Z2V]`, only when load is present. `(2)¼` needs none because `λ_11 ≥ 0`. `(24)¼` is a parameter. | `L3` §2 |
| K-28 | **The objective** | `max Σ LinearTerms + Σ PwlTerms + V(socTerminal) − Σ peakPriceEurPerMw·zPeak + Σ_j λ_j·A_j − cvarWeight·CVaR_α(imbalance cost)`. | `L3` §2 |
| K-29 | **`V` closes the horizon** | `V` is a concave piecewise-linear curve in terminal SOC. Because it is concave and the objective maximises, it needs no binaries. `λ_SOC` is an output: the subgradient of `V` at the optimum. | `ADR-007` |
| K-30 | **Peak charged at the full rate** | The peak term is an epigraph `zPeak ≥ p_poi[t]`, `zPeak ≥ floor`, priced at the full rate. There is no proration factor. The accounting period is a field. | `ADR-020` |

### 1.4 Co-optimisation tiers

| # | Concept | Claim | Source |
|---|---|---|---|
| K-40 | **Three tiers, one interface** | `ICoOptimizer`. Tier 1 is the joint MILP and the accuracy reference. Tier 2 is a Lagrangian decomposition, which gives `μ` and a certified bound. Tier 3 evaluates a learned `μ̂` and solves one spot MILP. | `L3` §3, `ADR-010` |
| K-41 | **Measured gap** | No tier ships without its gap to Tier 1, reported as p50 / p90 / p99 and worst case, conditioned on regime. Tier 1 runs offline on every backtest day. | `ADR-010` |
| K-42 | **Automatic escalation** | Tier 3 escalates to Tier 2 when the solution is near a coupling boundary, the state is outside `μ̂`'s training range, `peakCritical`/`qualCritical` is set, or `V` is stale. | `L3` §3 |
| K-43 | **Tier 2 always returns a feasible primal** | This holds on every exit path, including when the iteration cap is hit. Bundle method is preferred over plain subgradient. | `L3` §3 |

### 1.5 Commitments, orders and failure

| # | Concept | Claim | Source |
|---|---|---|---|
| K-50 | **Commitment ledger** | `Confirmed` entries are hard constraints. `Pending` entries are exposure that depends on the scenario, weighted by the fill belief. Every plan is checked for commitment feasibility before it is emitted (`INV-P-02`). | `L3` §4 |
| K-51 | **Target → shadow value → quoting** | The Planner extracts the target, the shadow value (its indifference price) and an urgency signal. The quoting policy turns them into a ladder of limit orders and must never quote through the shadow value (`INV-P-10`). | `L3` §5, `ADR-012` |
| K-52 | **DA bid curve by re-solve** | The DA curve comes from a parametric re-solve over a grid of candidate clearing prices. It must be monotone (`INV-P-09`). A non-monotone curve halts the engine; it is not sorted into shape. | `L3` §5 |
| K-53 | **Reserve bid = `μ` + policy markup** | `μ[b]` is the reservation price. `IReserveBidPolicy` maps `(μ, P(award), envelope)` to one `(price, MW)` per block and direction, and must never bid below `μ` (`INV-P-11`). In Tier 2 the policy runs inside the iteration; in Tier 3 it is applied once. | `L3` §5, `ADR-018` |
| K-54 | **Feasibility restoration** | Bounds are relaxed only in the order Risk → Liquidity, using explicit slacks with penalties. Physical, regulatory and confirmed-commitment constraints are never relaxed. If the model is still infeasible, the engine halts and reports the conflicting constraints. | `L3` §6 |
| K-55 | **Solve budget fallback** | Use the best incumbent if it is within the gap. Otherwise project the previous plan forward. Otherwise fall back to a `DEFENSIVE` baseline. Otherwise `HALT`. Every step is recorded. | `L3` §6 |
| K-56 | **Never dropped** | Peak protection and commitment feasibility apply in every mode. | `L3` §6, `ADR-014` §3 |
| K-57 | **Dispatch boundary** | The Planner publishes a SOC corridor and a setpoint. The corridor is binding, and the setpoint is not. | `L3` §7 |

---

## Part 2 — Assumptions

### 2.1 What the Planner takes as given

| ID | Assumption | Basis | Source | If wrong |
|---|---|---|---|---|
| A-01 | The C2 terms are correct and complete. The Planner does not check the economics; it only composes them. | Stated (by design) | `L3` §2, `ADR-009` | Any error in Valuation passes straight into the plan. Settlement's `modelErrorEur` bucket is where it shows up. |
| A-02 | Site load is **fixed**. The battery and curtailment are the only levers on the POI. | Stated | `ADR-016`, `01-system-model` §5 | Flexible load would be a third lever and would change every delineation branch analysis. |
| A-03 | One site, one POI, one battery. | Stated | `01-system-model` §5 | Multi-asset coupling is not designed. |
| A-04 | L0 state (realised peak, qualification, ledger, `V`) is at least one tick old, and the Planner handles the gap conservatively rather than assuming nothing happened in it. | Stated | `ADR-006`, `L0` §6 | If the gap is treated as empty, a new monthly peak can be set by accident. |
| A-05 | Sub-second dispatch and activation response are handled by a controller that stays inside the published SOC corridor. | Stated | `L3` §7 | If the controller leaves the corridor, reserve deliverability is no longer guaranteed. |

### 2.2 Markets

| ID | Assumption | Basis | Source | If wrong |
|---|---|---|---|---|
| A-20 | aFRR **capacity is pay-as-bid**. aFRR **energy is marginal-priced**, so bidding true marginal cost on energy is optimal. | Stated | `ADR-018` | The bid policy has nothing to optimise, or optimises the wrong thing. |
| A-21 | A capacity bid is awarded **if and only if** its price is at or below the marginal accepted price. So `P(award \| p, b) = Σ_s w_s · 1[afrrCapPrice[s,b] ≥ p]`. | Stated | `ADR-018` §3 | Partial awards, indivisible bids and tie-breaks are not modelled. |
| A-22 | **The site is a price-taker.** Its reserve bids do not move the marginal capacity price, and its DA and ID orders do not move prices beyond what `FillProbView` captures. | **Implicit** | follows from A-21; no statement found | For a battery that is large relative to market depth, `P(award)` and expected revenue are biased upward. |
| A-23 | One point bid per block and direction. Whether several price/volume pairs per block are allowed is **unconfirmed**. | Stated (open) | `ADR-018` §8, `L3` §5 | If multi-bid is allowed, value is left on the table. |
| A-24 | Intraday volume is bounded by `FillProbView`'s `BoundTerm`: the Planner only counts on volume it is likely to get, and that term carries no price. | Stated | `L3` §2, `ADR-012` | If the bound is too loose, the plan relies on volume that will not fill. If it is too tight, value is given up. |
| A-25 | `Pending` orders can be treated as scenario-dependent positions **weighted by the fill belief**. | Stated | `L3` §4 | The spec does not say whether fill is correlated with the price scenario. If it is modelled as independent, exposure in extreme scenarios is understated. **Check.** |
| A-26 | The quoting policy can work profitably between the current market price and the shadow value. The shadow value, read from the position accounting constraint, is a meaningful indifference price. | Stated | `ADR-012` | In a MILP, duals are only defined for the LP fixed at the integer solution. The shadow value is then conditional on the binaries chosen. |

### 2.3 Horizon, `V` and the month

| ID | Assumption | Basis | Source | If wrong |
|---|---|---|---|---|
| A-30 | The planning horizon `H_plan` is about **192 slots (about 2 days)**. The month and the year reach the solve **only through `V` and the `λ_j`**. | Stated | `TN-02` §7.1 | Everything about monthly peaks and annual qualification depends on how good `V` is. |
| A-31 | `V` is **concave in terminal SOC given the discrete state**, and is selected *before* the solve via `conditionedOn`. It is not a function of the in-solve `zPeak`. | Stated | `ADR-007`, `C2` §4 | This is why `V` cannot price the peak beyond the horizon (A-34). |
| A-32 | **The method that produces `V` is unsettled.** | Stated (open) | `L0` §5.4 | Every month- and year-scale decision rests on an approximation whose quality is not yet known. |
| A-33 | `V` is **risk-neutral**, while the tick objective is risk-averse. | Implicit; ruled a defect by `TN-01` claim 18 | `TN-01` | Risk attitude jumps at a horizon boundary that moves every tick, so the engine over-hedges early in the period. |
| A-34 | **The part of the peak charge beyond the horizon is priced at zero within a tick.** Charging the full rate above the floor over-charges an excursion that a later one would have dominated, so the engine leans toward being too averse to new peaks. | Stated (known gap) | `ADR-020`, `ADR-015` 015-1 | Too little peak-adjacent value is captured. |
| A-35 | The peak charge is **linear in the level** and `peakPriceEurPerMw` is a **deterministic scalar**. | Declared guess | `TN-01` §9, claim 19 | A rate whose marginal €/MW falls with the level makes the term concave. The relaxation then understates the charge. |
| A-36 | `λ_j` is a first-order linearisation, recomputed every tick, and good enough over the move the plan makes within one horizon. The resulting `linearizationGap` is budgeted, not zero. | Stated | `ADR-017` | `TN-04` §2: the linearisation has no stated region of validity, because the plan moves the set of tail scenarios. |
| A-37 | **`(12) = 0`**: grid-sourced energy stored in the battery (Fremdtankstrom) vanishes above about **8 charge-equivalent cycles a month**. This is what lets `V_soc` and `V_del` be added separately in the objective. | **Asserted, not modelled** | `ADR-017`, `TN-03` §4 | In a low-throughput month the two value terms interact and the objective double-counts or misses value. **High priority to check.** |

### 2.4 Solution method and tiers

| ID | Assumption | Basis | Source | If wrong |
|---|---|---|---|---|
| A-40 | The scenario ensemble's dependence structure is **right in the tail** (high price, peak and activation together). | Stated as requirement | `ADR-005` | The co-optimisation's value is overstated. |
| A-41 | A **reduced** ensemble (order `S = 64`) is accurate enough for the decisions the Planner makes. | Stated | `ADR-005` | `TN-01` §5c: the reduction is not tail-aware and drops the paths that set the peak. `INV-D-07` (marginal means) does not detect this. |
| A-42 | The **stage-aggregation error** (the second stage sees the whole horizon at once) is acceptable and is **measured, not bounded by theory**. It is largest at S1. | Stated | `L3` §0, `TN-02` §5 | Building a scenario tree comes back into scope only if instrument 3 measures more than 5%. |
| A-43 | The Lagrangian iteration in Tier 2 converges well enough within the solve budget, and it holds the bid-policy fixed point at no structural cost. | Stated | `ADR-010`, `ADR-018` §6 | Tier 2 falls back to its best feasible primal, and the gap is recorded. |
| A-44 | The envelope `B(E)` is **concave in offered MW**. This is checked against the breakpoints and not assumed. If the check fails, the term is declared `General` and binaries are added. | Stated | `ADR-018` §4, `INV-V-12` | The cost is solve time, not correctness. |
| A-45 | A solver can be made deterministic (fixed seed, threads, work-unit limits). | Stated | `ADR-013` | Where it cannot, the reproducibility run is single-threaded and production records the difference. |
| A-46 | The choice of solver and the latency budget for each tick can be deferred without affecting the Planner's design. | Stated | `ADR-015`, README | Tier choice and escalation thresholds will eventually need the latency budget. |

### 2.5 Risk

| ID | Assumption | Basis | Source | If wrong |
|---|---|---|---|---|
| A-50 | Risk enters as `− cvarWeight · CVaR_α(imbalance cost)`. CVaR is chosen because its linear (Rockafellar–Uryasev) form adds no binaries. | Stated | `L3` §2, `ADR-019` | — |
| A-51 | **The CVaR term can be assembled from C2.** | **Refuted by `TN-04` §2** | `TN-04` | No C2 shape carries per-scenario losses, the auxiliary `ζ` or the weights, and the Planner may not add economics of its own. The objective line `L3`:171 cannot be built as specified. **Blocks the rest of this section.** |
| A-52 | `cvarWeight` stands for **some** risk appetite. | Undefined | `TN-04` §5b | It is used today as a model-risk dial (raised on degraded price data). Tail risk, model risk and a loss budget each call for a different instrument. |
| A-53 | `(α, S) = (0.95, 64)` gives a usable tail estimate. | **Refuted by `TN-04` claim 8** | `TN-04` §5a | The tail holds 1–3 scenarios. Above saturation, `cvarLevel` has no effect. |
| A-54 | Imbalance is settled at a **single price**, with **no contractual markup** on top. | Declared guess | `TN-04` §5b | If a contract exists, imbalance risk should be a convex cost in expectation, not a CVaR penalty. |
| A-55 | The engine applies a static CVaR afresh at each gate and **accepts time-inconsistency**. | Stated (accepted) | `TN-01` §3 | Over-hedging early in the accounting period is systematic, not noise. |
| A-56 | Activation hazard is carried **only** by the SOC chance constraint (`soc[s,t]` within bounds for weighted scenario mass ≥ 1−ε). | Stated | `ADR-019`, `L3` §2 | `TN-04` claim 11: L3 states it as one line but does not formulate it as MILP rows. A chance constraint generally needs a binary per scenario. |
| A-57 | The peak term is the single `zPeak` epigraph with no scenario index. | Stated, contested | `ADR-008` vs `L2` | `TN-01` claim 10: that is the **deterministic** term. The risk-averse one needs a `z_s` per scenario (about 12k rows per regime). Open as 015-2. |
