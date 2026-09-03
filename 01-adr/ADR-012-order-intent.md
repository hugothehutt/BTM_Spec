# ADR-012 — The Planner emits intent; quoting is a separate policy

**Status:** Accepted **Reversibility:** Moderate

## Context

The execution engine already exists and is fixed. The question the engine must
answer at C3 is therefore narrow and concrete: **which limit orders, at what
price and what volume, do we submit?**

A MILP answers a different question. It answers "what net position do I want in
each slot?". Turning a target position into a limit price is a separate problem
involving fill probability, urgency, adverse selection and the shape of the
remaining trading window — and it is *not* naturally a MILP variable, because
price is a control on a stochastic fill process rather than a physical quantity.

Conflating the two produces the worst of both: either the MILP gains price
variables and non-convexities it cannot handle, or the quoting logic gets buried
inside the optimiser where it cannot be tested or tuned.

## Decision

**Split the decision in two, at a declared interface.**

1. **The Planner decides the target position** — per slot, per market, with a
   *shadow value of position change* (the marginal EUR/MWh at which the engine is
   indifferent). It also emits an **urgency** signal derived from how much the
   objective degrades if the position is not reached.
2. **The Quoting Policy maps `(target, shadow value, urgency, FillProbView,
   market microstructure state) → a ladder of limit orders`** — price, volume,
   validity, and replace/cancel behaviour.

The shadow value is the key handoff. It is the Planner's own indifference price,
which means the quoting policy has an economically meaningful bound: **never
quote a price worse than the shadow value**, because at that price the trade
destroys value. Everything between the current market and the shadow value is
the region the quoting policy is free to work in, and its job is to capture as
much of that spread as fill probability allows.

**C3 carries intent, not routing.** An `OrderIntent` is `(market, product, slot,
side, limitPrice, volume, validity, replacesId, tag)` — where the price and volume
pair is `limitPriceEurPerMwh`/`volumeMwh` on the energy markets and
`limitPriceEurPerMwH`/`volumeMw` on `AfrrCapacity` (C3 §2). It says nothing about
venue mechanics. The Execution adapter translates intent into whatever the
simulator or a live venue requires.

**FillProbView informs the quoting policy, and bounds the Planner.** Two distinct
uses, deliberately kept apart:

- To the Planner, fill probability crosses C2 as a **`BoundTerm`** — how much
  volume may be *counted on* (ADR-008). No price. This prevents the Planner from
  planning on volume it will not get.
- To the quoting policy, the full fill-probability curve `P(fill | price,
  time-to-gate)` is available, because that is precisely the trade-off it exists
  to make.

**The auction case has its own policy.** `IQuotingPolicy` maps a target onto a
ladder of limit orders against a continuous book. A sealed-bid auction shares the
economic idea and none of the mechanics, so reserve capacity gets a sibling
interface rather than a second shape inside the same one: `IReserveBidPolicy`
maps `(μ[b], P(award | ·, b), envelope) → (limitPriceEurPerMwH, volumeMw)`
(ADR-018). The handoff is identical in structure — `reserveShadowValueEurPerMwH`
is the Planner's indifference price for a MW of headroom, and `INV-P-11` forbids
bidding below it exactly as `INV-P-10` forbids quoting through it. What differs is
that reserve is **pay-as-bid**, so the markup over indifference is not spread
captured from a counterparty but the whole of the margin.

**Calibration target is the existing simulator.** Since Execution is fixed, the
fill model is calibrated against the simulator's actual behaviour, and re-verified
whenever the simulator changes. `T4` includes a quoting-policy backtest that
measures realised fill rate against predicted fill rate by price band; systematic
divergence is a calibration failure, reported, not absorbed.

## Consequences

- The Planner stays a clean MILP over physical quantities. No price variables, no
  fill-probability non-convexity inside the solver.
- The quoting policy can be replaced, A/B tested and tuned independently, with
  the Planner held fixed — which also means its contribution to P&L is separately
  measurable (this is the "execution slippage" bucket in Settlement's four-way
  decomposition).
- Three markets, three shapes, one principle. Intraday gets a ladder, DA gets a
  bid curve, aFRR capacity gets a single point priced above `μ` — and in each the
  Planner emits an indifference price it does not itself act on.
- The DA case is different and handled explicitly: DA requires a **bid curve**,
  not a single quantity. The Planner produces it by parametric re-solve across a
  grid of DA clearing prices, yielding a monotone price-quantity schedule.
  Monotonicity is required by exchange rules and by economic sense, and is
  asserted (`INV-P-09`); a non-monotone curve indicates a formulation error, most
  often a missing coupling constraint.
- Cost: an extra component, and the shadow value must actually be extracted from
  the solve rather than approximated.

## Rejected

- **Price as a MILP variable.** Introduces the fill process into the solver;
  makes it non-convex and unstable.
- **Fixed offset from mid.** Ignores the shadow value entirely and will trade
  through indifference under pressure.
- **Quoting inside Execution.** Execution is strategy-independent by design and
  must stay so; quoting is strategy.
