# ADR-007 — Publish V(SOC) as a concave PWL curve, not a scalar λ_SOC

**Status:** Accepted **Reversibility:** Expensive

## Context

`OppCost` was specified to publish `λ_SOC`, the shadow price of state of charge —
the number that makes "cheap MWh now vs. expensive MWh later" tradeable. Two
problems:

1. **It is a dual of the Planner's own problem** (ADR-006). Valuation cannot
   compute it without solving what the Planner is about to solve.
2. **A scalar is a linearisation around an assumed operating point.** The true
   marginal value of stored energy depends on how much is stored: near-empty
   energy is worth much more than near-full energy when a peak event is possible,
   and much less when the next opportunity is a cheap charging window. A single
   λ prices only one point on that curve and misprices every other.

The second point is quantitatively serious in a BTM setting. The peak charge
creates a large, state-dependent kink: the marginal value of the MWh that keeps
you below the peak threshold is enormous; the marginal value of the next one is
close to the spot spread. No scalar represents both.

## Decision

**The horizon is closed by a value function, published as a concave
piecewise-linear curve.**

```
V : SOC → EUR      published as breakpoints {(e_i, v_i)}, i = 0..n
                   with strictly decreasing slopes (concavity)
                   conditioned on (peakState, qualState, calendar context)
```

The Planner adds `V(soc[T_end])` to its objective as a maximisation term. Because
`V` is concave and the objective is a maximisation, the PWL requires **no binary
variables** — it is representable with a simple λ-formulation or as the minimum
of its affine pieces, and the LP relaxation is exact. This is the property that
makes it affordable.

**λ_SOC is then endogenous:** it is the subgradient of `V` at the optimal
terminal SOC, and the Planner can report it for diagnostics. It is an *output*,
not an input. This inverts the original dependency and removes the cycle.

**V is produced by the slow loop** (`C_slow`, ADR-002): a rolling-horizon or
approximate dynamic programming solve over a horizon long enough to see the end
of the accounting period, using the joint ensemble (ADR-005). It is written to
L0 and read by L2/L3 at a lag.

**Concavity is enforced, not assumed.** The fitting step projects onto the
concave hull; the contract validates strictly decreasing slopes (`INV-V-11`). If
the underlying value is genuinely non-concave — which can happen around a §19(2)
qualification cliff — the non-concavity is *not* smoothed away silently. It is
represented explicitly as a separate discrete state dimension (ADR-011), with a
concave `V` conditional on each state. Concavity in `SOC` given the discrete
state; discreteness handled by the discrete state.

## Consequences

- The terminal-value problem, which is the main source of myopia in rolling
  optimisation, is solved properly rather than by a hand-tuned end-of-horizon
  SOC target.
- The Planner's horizon can be shortened — a real solve-time saving — because the
  truncation is priced rather than ignored.
- `V` becomes a first-class artefact: versioned, content-hashed, replayable, and
  independently testable (does a better `V` produce better realised P&L?).
- The slow loop is a real piece of work. It is also the piece with the most
  headroom: a better `V` improves every decision downstream. This is where
  research effort compounds.
- Staleness of `V` must be handled: the degradation ladder (ADR-014) defines
  behaviour when `V` is older than its validity window.

## Rejected

- **Scalar λ from Valuation.** Circular and a poor approximation at exactly the
  states that matter.
- **Fixed terminal SOC target.** The classic hack. Wastes value whenever the
  target is wrong, and the target is always wrong.
- **Long horizon instead of a value function.** Solve time grows, forecast
  quality collapses beyond a few days, and the accounting period boundary can be
  a month away.
- **Convex quadratic approximation of V.** Cheaper to fit but cannot represent
  the peak-threshold kink, which is the most important feature of the curve.
