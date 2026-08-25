# ADR-005 — One joint scenario ensemble with a shared scenario axis

**Status:** Accepted **Reversibility:** Very expensive

## Context

The engine is uncertain about several things at once: DA prices, intraday
prices, imbalance prices, aFRR capacity clearing prices, aFRR activation
volumes and direction, site load, and PV. Valuation needs distributions, not
point forecasts — `PeakView` uses a CVaR tail, `AfrrCapacity` needs an expected
bid-curve value, `ImbalanceRisk` is by definition a tail quantity.

The tempting implementation is a per-series distribution: each series carries its
own quantiles or its own sampled paths. This is wrong, and the way it is wrong
matters more here than in most systems.

**The entire cross-market problem is driven by correlation.** aFRR activation is
correlated with imbalance prices, which are correlated with intraday prices,
which are correlated with the residual load that also drives the site's own load
and PV. A high-price hour is precisely the hour where the peak charge bites, the
aFRR is activated, and the battery is wanted in three places at once. Sampling
each series from its own marginal destroys exactly the dependence structure that
determines whether the co-optimisation is worth anything. It will systematically
*overstate* the value of the strategy, because it lets the engine believe it can
be lucky in one market and unlucky in another independently.

## Decision

**One joint ensemble, one scenario axis, shared by every uncertain series.**

```
Ensemble : [S scenarios] × [H slots] × [K series]     (float32, SoA by series)
Weights  : [S]  — non-negative, sum to 1
```

**Notation, fixed here and used throughout:** `K` is the number of *series*,
`S_gen` the number of *generated* scenarios, and `S` the number of *reduced*
scenarios that actually reach the Planner — `S` is C1 §1's `scenarioCount`.
Reduction maps `S_gen → S` with `S ≪ S_gen`.

Scenario `s` is a *coherent state of the world*: `price[s,t]`, `load[s,t]`,
`pv[s,t]`, `activation[s,t]` all belong to the same draw. Any code that indexes
one series by `s` and another by `s'` is a defect (`INV-D-05`).

**Weights are explicit and generally non-uniform**, because the ensemble that
reaches the Planner is a *reduced* one. Scenario reduction (fast-forward
selection / Wasserstein-optimal reduction) maps the generated ensemble to `S`
representatives with weights. The reduction is a materialised artefact
(ADR-004 §4), reproducible and versioned.

**The ensemble is generated in one place, from a joint model.** Whatever the
model is — copula over marginals, multivariate residual bootstrap, conditional
generative model — it is offline (ADR-001) and its output is the artefact. The
engine consumes scenarios; it never assembles them from marginals.

**Two derived forms are permitted to cross C1**, both computed from the joint
ensemble and never independently:

- `QuantileView` — marginal quantiles, for views that genuinely need only a
  marginal (e.g. a bound).
- `TailStatistic` — CVaR-type functionals, computed as a weighted empirical mean
  over the joint ensemble (this is what `PeakView` uses).

## Consequences

- Memory: `S × H × K × 4 bytes`. With `K=8`, `H=192`, `S=64` this is ~400 kB per
  snapshot — trivial. The scenario axis is affordable *because* the hot window is
  bounded (ADR-004).
- Scenario count `S` controls the **discretisation** of the joint law and the solve
  time. `04-compliance/T5` requires reporting objective value and solve time as a
  function of `S`, so the choice is evidenced rather than guessed. The curve measures
  convergence to the ensemble's own large-`S` value; it is not a measure of distance to
  the true optimum, which is bounded separately (`T5` §6.2, `06-theory/TN-02`).
- The Planner's stochastic formulations (chance constraints, CVaR, two-stage
  recourse) all index the same axis, so they are mutually consistent by
  construction.
- The ensemble is a **fan**, not a recombining tree: every scenario is a complete path,
  distinct from slot 1, so `F_1 = F_2 = … = F_H`. This fixes the solution method as
  rolling two-stage SAA glued by `V` (`L3` §0) and makes two-stage SAA theory available.
  No multistage-optimality claim is available from this artefact. A tree cannot be
  obtained by reducing the fan; it would require tree construction, a different artefact.
- Any single-series forecast improvement must be re-integrated into the joint
  model rather than patched in downstream. This is a real workflow cost and it is
  the price of correctness.
- `INV-D-05` (shared axis), `INV-D-06` (weights sum to 1, non-negative),
  `INV-D-07` (reduced ensemble preserves the mean of each marginal within
  tolerance).

## Rejected

- **Independent per-series distributions.** Destroys dependence; biases the
  strategy's apparent value upward. This is the primary reason for this ADR.
- **Point forecast plus a risk add-on.** Cannot express tail co-movement, which
  is where the imbalance and peak risk live.
- **Full ensemble into the MILP without reduction.** Solve time scales with `S`;
  reduction with weights gives most of the accuracy at a fraction of the cost,
  and the loss is measurable against a large-`S` reference run.
