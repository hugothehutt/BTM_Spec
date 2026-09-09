# ADR-019 — Activation carries no cost term; the risk functional runs on imbalance alone

**Status:** Accepted **Reversibility:** Moderate — removes a member of a closed
enumeration, hence a major bump on `C2` and `C5`

## Context

The Planner's objective (`02-layers/L3-planner.md` §2) weighted a risk functional
whose argument was `imbalance + activation cost`. No such cost existed anywhere in
the corpus, and four independent places said so:

- `C1`'s `activationUp`/`activationDn` is a **dimensionless fraction** in `[0,1]` —
  the share of committed MW called. The euros attached to activation are
  `afrrEnergyPriceUp/DnEurPerMwh`, which is **revenue**. Adding a fraction to euros
  does not produce a loss variable.
- The energy moved to deliver an activation is already priced, under the ownership
  matrix (`02-layers/L2-valuation.md` §5), by `SpotView`, by `OppCostView` on
  `BatteryThroughput`, and by `V` on terminal SOC. There is no third quantity.
- `ActivationRisk` sat in the closed `EconomicEffect` enumeration with an owner and
  a base, but no view emitted a term for it, and `L5` booked realised
  `ActivationRisk = 0` identically. A penalty with no realised counterpart cannot be
  scored against settlement data, so no amount of it would ever calibrate
  `cvarWeight` on that leg.
- The hazard activation actually presents is **deliverability** — SOC exhaustion,
  failure to deliver committed reserve, loss of qualification — and `L2` §6 already
  routes degraded activation beliefs to `chanceLevel` on SOC feasibility. The same
  hazard was therefore modelled twice under two different measures, one of them
  dimensionless.

The argument is set out in full in `06-theory/TN-04-imbalance-risk-measure.md` §6,
which rules the activation leg **refuted** while confirming CVaR for the imbalance
leg. Its fourth point is the sharper one: activation revenue enters as an expectation and
activation "cost" entered under CVaR, so one underlying event was carried under two
measures with opposite signs — and because activation is called when imbalance
prices are extreme, this is the one aggregation in the objective where the
discarded diversification is largest, not smallest.

## Decision

**The risk functional runs on imbalance cost alone:**

```
− cvarWeight · CVaR_α(imbalance cost)
```

**`ActivationRisk` leaves the `EconomicEffect` enumeration and the ownership
matrix.** The enumeration is twelve members. Activation's euros are carried by
`ReserveEnergyRevenue` and by the effects that already own the energy moved to
deliver it; activation's hazard is carried by `chanceLevel` on SOC feasibility, and
by nothing else.

## Consequences

- `cvarWeight` now has exactly one referent: the imbalance tail. Risk aversion to
  activation has exactly one instrument, a chance constraint, and a diagnostic can
  attribute conservatism to a cause.
- `AfrrEnergyView` claims one effect, `ReserveEnergyRevenue`. `INV-V-01`, `INV-V-02`
  and the composer's coverage check are unchanged in form; there is one fewer effect
  to claim.
- `L5`'s identically-zero `ActivationRisk` row is gone. The four-bucket
  decomposition's risk-free basis on both sides is unchanged and is now stated
  against the CVaR weighting alone (`C5` §6, `L5` §5).
- `C2` and `C5` go to **4.0**. Removing a member narrows the value set of `effect`,
  `effectCoverage`, `unclaimedEffects`, `realisedByEffect` and `plannedByEffect`,
  which is major under `C0` §5 even though no field table changed. `C1`, `C3`, `C4`
  and `C6` never carried the enumeration and are untouched.
- The `T0`–`T6` conformance tests are generic over the enumeration — none names
  `ActivationRisk` — so the discipline in `README.md` is satisfied by the parity
  check between `stubs/Enums.cs` and `L2` §5, which this commit keeps green.

## Rejected

- **Give `ActivationRisk` a defined base and a formula.** The other branch `TN-04`
  §7 leaves open. It requires a euro loss variable `C1` does not carry and cannot
  cheaply acquire, and even granted one it would model deliverability twice — once
  as a chance constraint, once as a tail — with no realised counterpart to calibrate
  either. A definition is not worth inventing for a term whose hazard is already
  correctly instrumented.
- **Read `activationUp`/`activationDn` as a cost proxy.** Sums a dimensionless
  fraction with euros, and the fraction's monetary sign is revenue. It is the defect,
  not a repair of it.
- **Keep the term and set `cvarWeight = 0` when activation is uncertain.** One weight
  serves both legs, so switching it off to silence activation also switches off the
  imbalance tail — the leg CVaR is confirmed for.
- **Widen the argument to all market-facing EUR loss** — imbalance plus the
  spot/intraday leg that offsets it, the second half of `TN-04` §7's C2. Not
  rejected on merit; it changes *what is risk-weighted* rather than removing an
  undefined quantity, and it belongs with `TN-04`'s C3 (a `cvarLevel` per functional)
  in a decision about the risk instrument. Left open deliberately.
