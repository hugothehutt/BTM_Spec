# ADR-016 — Curtailment is a priced decision, not a rule

**Status:** Accepted **Reversibility:** Moderate — changes the POI bridge and every
term that reads generation

## Context

Available PV routinely exceeds load plus charge headroom plus the export limit, so a
bridge without a curtailment term is an equality over non-negative quantities that is
infeasible under an ordinary condition. Hence `q ≥ 0` in the POI bridge
(`02-conventions.md` § 1).

`q` is not merely a feasibility slack. Under the Abgrenzungsoption it moves `Z1NE¼`,
therefore `(2)¼`, `(23)¼` and every monthly aggregate downstream — `(11)`, `(13)`,
`(16)`, `(30)`, `(32)` (`00-overview/03-mispel-reference.md`). Whether curtailing a
quarter-hour pays depends jointly on the spot price, on `AW` that hour and on the
state of the calendar-month aggregates. 

## Decision

**`q[t,k]` is a decision variable of the Planner's MILP, priced against the same
objective as every other lever.** No rule, no heuristic and no preprocessing step
may fix it. Valuation may publish `BoundTerm`s constraining `q` to the physically
available generation, never a schedule for it.

## Consequences

- The generation allocation is always feasible, and the export limit's shadow price
  becomes readable instead of implicit in an infeasibility.
- Curtailment couples to the monthly aggregates `(16)` and `(32)`, so horizon
  truncation is economically load-bearing: a quarter-hour of `q` is valued only
  through the calendar month it falls in, and `V(SOC)` plus the carried monthly state
  (ADR-007, C6) must span that boundary.
- Off-take is not a control (`load` fixed), so `q` and the battery are the only levers
  on `Z1NB¼`/`Z1NE¼` — their joint treatment is mandatory, not optional.

## Rejected

- **"Curtail only when the export limit binds."** Ignores price and `AW`: forgoes
  curtailment that pays at negative prices, and forces curtailment that does not
  whenever charging could absorb the excess more cheaply.
- **"Curtailment is dominated under the delineation regime."** False — it assumes
  off-take is a control and that AW>0 accounting is local. Neither holds.
- **Curtailment as a Belief-side adjustment to `pv_avail`.** Hides a decision inside
  a forecast, makes it unattributable in Settlement, and breaks replay.
