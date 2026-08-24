# ADR-010 — Cross-market co-optimisation: a three-tier ladder with a measured gap

**Status:** Accepted **Reversibility:** Moderate — the tiers share one interface

## Context

This is the hardest part of the system, and the original framing named the
difficulty precisely: plugging everything into one solver and asking for the
optimum is not doable at operational speed, so we need good proxies to price aFRR
capacity against spot, pre-allocate capacity, narrow the physical constraints,
and then run the MILP.

That instinct is right about the shape of the answer and wrong in one important
detail, which this ADR corrects.

### Why the naive joint problem is hard

- **Timing mismatch.** aFRR capacity is committed for coarse blocks, well before
  the intraday information that determines the spot opportunity cost arrives.
  The capacity decision is a first-stage decision under uncertainty.
- **Resource coupling.** Reserve does not consume energy — it consumes *power
  headroom* and *SOC corridor*. Every MW of upward reserve held is a MW that
  cannot be sold on the spot market and a block of SOC that cannot be traded
  away, for the whole delivery block.
- **Activation is stochastic.** Committed reserve turns into energy at random,
  correlated with exactly the prices that make the energy valuable elsewhere.
- **Scale.** Scenarios × slots × binaries.

### Why hard pre-allocation is the wrong fix

Pre-allocating capacity — "reserve X MW, then let the spot MILP work in what's
left" — is a **primal restriction**: it deletes options from the feasible set. If
X is wrong, the loss is unbounded and, worse, **invisible**: the spot MILP
reports a clean optimum over a feasible set that was silently amputated.

The correct instrument is a **dual price**: charge the spot problem for the
headroom it consumes, at a price that reflects what aFRR would have paid for it.
The optimiser then keeps every option and simply pays for the scarce resource.
Dual pricing weakly dominates primal restriction — it recovers the same solution
when the allocation happens to be right, and does better whenever it is wrong.

That dual price is the object worth building. It is the internal transfer price
of a MW of battery headroom, and it is where the edge over less careful
participants actually lives.

## Decision

**Three tiers behind one interface, `ICoOptimizer`, with a mandatory measured
gap between them.**

### Tier 1 — Joint stochastic MILP (the reference)

One model. Reserve capacity per block `r_up[b]`, `r_dn[b]` are decision
variables alongside spot dispatch. The coupling constraints are explicit:

```
power headroom     p_d[t] + r_up[b(t)]        ≤ P_max_dis          ∀t
                   p_c[t] + r_dn[b(t)]        ≤ P_max_chg          ∀t
POI envelope       p_poi[t] within [−E_exp, E_imp]                 ∀t
SOC corridor       soc[t] − r_up[b(t)]·D/η_d  ≥ soc_min            ∀t
                   soc[t] + r_dn[b(t)]·D·η_c  ≤ soc_max            ∀t
activation path    soc evolves under scenario-s activation, and
                   soc[s,t] ∈ [soc_min, soc_max]  for a weighted
                   fraction ≥ (1−ε) of scenarios       (chance constraint)
peak               z_peak ≥ p_poi[t] ∀t ;  z_peak ≥ pPoiRealisedPeakMw
```

`D` is the sustained-delivery requirement from prequalification, a calendar
parameter (ADR-002), not a constant.

Tier 1 is the **accuracy reference**. It does not need to be fast, because its
primary job is to be right and to serve as the oracle everything else is scored
against. It runs offline on every backtest day regardless of which tier ships.

### Tier 2 — Lagrangian decomposition (the price discovery)

Relax the coupling constraints with multipliers `μ_up[b]`, `μ_dn[b]`. The problem
separates into:

- **Spot subproblem** — a fine-grained MILP over 15-minute slots, charged
  `μ · (headroom consumed)` for the resource it uses.
- **Reserve subproblem** — a small, near-analytic problem over coarse blocks:
  offer reserve where expected capacity revenue plus expected activation value
  exceeds `μ`.

Iterate `μ` with a subgradient or bundle method to primal feasibility.

Three properties make this the centre of the design:

1. **`μ*` is the reservation price**, and it is *provably consistent* with spot
   opportunity cost — it is not a heuristic weighting, it is the marginal value
   of headroom in the spot problem.
2. **The dual value is a certified bound.** At any iteration, the Lagrangian
   gives an upper bound on the true optimum. The gap to the best primal solution
   is a *number you can report*, not a hope.
3. **The decomposition matches the natural structure**: coarse reserve blocks,
   fine spot slots. The subproblems are each far easier than the joint model.

### Tier 3 — Learned reservation-price surface (the fast path)

Fit `μ̂ = f(features)` offline on Tier 1 / Tier 2 solutions across a long
history — features being calendar position, price-level and spread beliefs,
reserve-price beliefs, SOC, peak headroom, qualification state. At runtime,
evaluate `μ̂`, price the headroom, solve **one** spot MILP.

This is the original "pre-allocate, then narrow, then solve" idea — but with the
allocation replaced by a *price*, and with a number attached to what it costs.

`f` is a materialised artefact (ADR-004 §4): versioned, content-hashed,
exportable to a form C# evaluates natively.

### The mandatory discipline

**No tier ships without its gap measured against Tier 1.** `04-compliance/T5`
requires, per release:

- gap distribution over a holdout backtest — reported as **p50 / p90 / p99 and
  worst case**, never as a mean, because the tail is where a fast heuristic
  destroys value;
- gap conditioned on regime (high-spread days, peak-critical days, high reserve
  price days), because a heuristic that is fine on average and terrible on the
  ten days that matter is worse than useless;
- the distance from Tier 1 to a **perfect-foresight upper bound**, which
  separates "our optimiser is weak" from "the world is uncertain".

**Escalation is automatic.** `ICoOptimizer` may escalate at runtime: if Tier 3's
solution is near a coupling constraint boundary, or the state is out of the
training distribution, or the peak-critical flag is set, the tick escalates to
Tier 2 within its time budget. Cheap when it does not matter, careful when it
does.

## Consequences

- The system has a defensible answer to "how good is your approximation?" at all
  times. Most participants do not.
- The three tiers share one interface, so the production tier is a configuration
  choice and a backtest can run all three for comparison on the same data.
- Tier 2's `μ*` is diagnostically valuable in its own right: it is an
  interpretable internal price that a human can sanity-check, unlike a black-box
  policy.
- Tier 1 must exist even though it may never run in production. That is an
  accepted cost; without an oracle there is no gap, and without a gap there is no
  engineering, only hope.
- Subgradient convergence needs care: step-size rule, iteration cap, and a
  guaranteed feasible primal recovery on every exit path.

## Rejected

- **Hard pre-allocation by heuristic.** Primal restriction with invisible,
  unbounded loss. This is the failure mode the ADR exists to prevent.
- **Sequential markets in priority order** (aFRR first, spot with the leftovers).
  A special case of hard pre-allocation, with the allocation set by an arbitrary
  ordering.
- **Reinforcement learning end-to-end.** No feasibility guarantee, no
  certifiable gap, unreplayable, and impossible to explain to a risk function.
  It may have a place *inside* Tier 3's `f`, where its output is a price checked
  against an oracle — not as the decision maker.
- **Tier 1 only.** Correct and probably too slow; and until the latency budget is
  fixed (ADR-015) we cannot claim otherwise. The ladder makes that decision
  deferrable without blocking design.
