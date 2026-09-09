# ADR-020 — The peak charge carries no proration; the accounting period is a field

**Status:** Accepted **Reversibility:** Moderate — removes a required field from
`C2` and adds one, hence a major bump on `C2`

## Context

The Planner's objective (`02-layers/L3-planner.md` §2) priced peak as
`− peakPriceEurPerMw · zPeak · proration`, where `prorationFactor` is "the
fraction of the accounting period covered by the planning horizon" (`C2` §3.3,
`INV-V-13`). No ADR ever authorised that factor, and every document that prices
the same term prices it without one:

- `ADR-008`:27 gives the `EpigraphTerm`'s MILP realisation as
  `z ≥ expr[t] ∀t`, `z ≥ floor`, cost **`c·z`**, and its field list as four
  items — variable, slot set, floor, unit price. `ADR-008`:36-40 writes the
  `PeakView` objective as `−peakPriceEurPerMw · z_peak`. The factor is in
  neither.
- `ADR-010`:68 states the peak rows and no price at all. `ADR-011`:38-45 gives
  all four regimes by objective shape, every one an `EpigraphTerm`, none with a
  scalar.
- `L5`:225-228 settles the charge as `peakPriceEurPerMw · pPoiRealisedPeakMw`
  per regime: the whole period charge on the period max, no proration on any
  regime row.
- The stub's own doc comment on the record that carried the field
  (`stubs/C2_ValuationBundle.cs`:150,155) states cost `c·z` and objective
  `−peakPriceEurPerMw · z_peak`.

So `C2` §3.3 carried a sixth field its governing ADR does not have, and priced
with it a charge Settlement books without it.

**The factor is also wrong on its own terms.** Mid-period, with the floor
`F = pPoiFloorMw`, a plan either stays at `F` or reaches `F + Δ`. Exceeding costs
`peakPriceEurPerMw · Δ` for the whole accounting period, because the charge is
levied on the period max and the period does not care when that max occurred. The
objective charged `peakPriceEurPerMw · Δ · θ`. On the case `P0`:253 names — a
one-week horizon inside an annual period — `θ ≈ 1/52`, so the marginal cost of
setting a new annual peak was understated about fiftyfold. The factor is a
calendar constant standing in for the probability that this horizon's excursion
binds the period max. That probability rises to 1 at `periodEnd`; `θ` never
moves.

`06-theory/TN-01-cvar-across-stages.md` §3b(ii) reaches the same field by another
route and rules it **refuted** (claim 8): `max(M_H, M_rest) ≠ θ·M_H + (1−θ)·M_rest`,
so a proration is exact for a time-additive charge and only for that. Its claim 9
confirmed `L2`'s warning that proration is "the most common way to make the engine
pathologically peak-averse". That direction is wrong for the `θ < 1` the spec
specified: understating the cost of a new peak errs toward tolerance. The
over-aversion hazard is real and belongs elsewhere — see Consequences.

## Decision

**The peak term prices the level at the full rate.**

```
max  … − Σ peakPriceEurPerMw · zPeak
```

**`prorationFactor` leaves `EpigraphTerm`; `accountingPeriod` joins it.** The
period remains what `00-overview/02-conventions.md` §2.1 requires — a mandatory
explicit input to the peak term, carried as its own field, never a unit
denominator — but it is carried as the `SlotRange` it is rather than as a
coefficient derived from it. `ITariffRegime.AccountingPeriod(SlotId)` already
computes it.

**`INV-V-13` is retired and its id reserved. `INV-V-18` replaces it:** the floor
is `pPoiRealisedPeakMw` for the declared `accountingPeriod`, adjusted for the
unsettled gap, and no scalar stands between `unitPriceEurPerMw` and the epigraph
variable. The new field is what makes that checkable at the seam — a floor with
no declared period cannot be validated against anything, which is the
invalidation hazard `L0`:381 already names.

This applies to all four `ADR-011` regimes uniformly. The argument is about the
shape of a max, not about a rate.

## Consequences

- `C2` goes to **5.0**. Removing a required field and adding a required field are
  each major under `C0` §5.
- `L2` and `L5` now carry one definition of the charge. `T2` §5.3's zero-gap test
  asserts `realisedByEffect ≈ plannedByEffect` term by term; for the peak bucket
  that assertion could not have held at any tick after the period's first peak,
  and now can. That is this decision's only mechanical proof, and it is stated
  there.
- **The out-of-horizon remainder is priced at zero within the tick, and this is a
  known gap.** Charging the full rate above the floor is right when the excursion
  becomes the period max and over-charges when a later one would have dominated
  it. The residual hazard is therefore over-aversion: `L2`'s warning had the right
  word and the wrong cause. The remedy is `V`, which `ADR-007` conditions on
  `peakState`, and it is not available — `C2` §4 ships `V` as one curve in SOC
  with `conditionedOn` selecting *which* curve before the solve, so `V` is not a
  function of the in-solve `zPeak`; `peakBuckets` (`L0`:132-133, its only
  occurrence in the corpus) has no domain or discretisation; and `W7` delivers
  conditioning on the qualification state only (`P0`:178). Registered in
  `ADR-015`.
- `ADR-007`, `ADR-008`, `ADR-010` and `ADR-011` are untouched. None carried the
  factor, and `ADR-008` is the document this decision restores `C2` to.
- Unlike `ADR-019`, the compliance layer is **not** generic over this change:
  `T0`:182, `T1`:144 and `T2`:51,845 name the field, so all three move in this
  commit under `README.md`'s change discipline.
- Glossary §14 gains a retirement whose word was never contested — a concept the
  specification removed, not a spelling that lost a contest. The section's scope
  widens by exactly that, and `TN-01` joins its named allowances, because a note
  that argues a term away must be able to name it.

## Rejected

- **Recalibrate the factor as a probability.** Replace the calendar fraction with
  an ensemble-derived `P(this excursion binds the period max)`. That is the
  quantity `θ` was reaching for, and it is worse than nothing here: a coefficient
  no bill can be reconciled against, on the one seam whose mechanical check *is*
  reconciliation against a bill. It also still prorates a max.
- **Redefine the factor over the *remaining* period.** Rises toward `periodEnd`,
  which is directionally right, and still never reaches 1, still prorates a max,
  and is a semantic change — equally major under `C0` §5. A smaller error at the
  same price.
- **Price the increment `max(M_H, floor) − floor`**, which is `TN-01`:292's
  literal prescription. Identical argmin: the two forms differ by
  `peakPriceEurPerMw · pPoiFloorMw`, constant within any one solve. So nothing is
  gained in behaviour, while `L2`'s reported peak bucket would differ from `L5`'s
  by that constant at every tick after the period's first peak — trading one
  definitional split for another. `TN-01`'s argument is about decomposition and is
  discharged in full by deleting `θ`; the increment framing was incidental to it.
  Level pricing is also what `ADR-008`:27 already says.
- **Block on `V`.** Defer the removal until `C2` carries `V` in a form that can
  price the remainder. This puts a two-line correction behind the most-deferred
  thing in the plan — `W7` is eighth of ten, and `P0`:9-17 names the value
  function as the wrong place to start — and leaves `C2` contradicting `ADR-008`
  meanwhile. The gap is smaller than the contradiction.
- **Emit a second term for the remainder.** A view guessing at the out-of-horizon
  max is `θ`'s error in a new shape, and `TN-01`:292 is the reason it cannot be
  done term-wise.
- **Resolve the scenario-indexed epigraph at the same time.** `TN-01` claim 10
  rules the single unindexed `z_peak` of `ADR-008`:36-41 the *deterministic* term
  rather than the risk-averse one, and its §9 asks for one of `ADR-008`:36-41 /
  `L2`:55-66 to go. Untouched here. Not rejected on merit: it changes the term's
  row count and variable set where this decision changes only its coefficient, and
  it needs the cost `TN-01` puts at ≈12k rows per regime at `S=64, H=192`. Left
  open deliberately, and registered in `ADR-015` beside the remainder.
