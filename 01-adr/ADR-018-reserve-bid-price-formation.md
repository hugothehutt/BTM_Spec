# ADR-018 — Reserve capacity is pay-as-bid; the bid price is a policy, not a dual

**Status:** Accepted **Reversibility:** Moderate — the seam survives a change of
remuneration rule; the bid policy does not

## Context

German aFRR **capacity** is procured pay-as-bid. The awarded MW is remunerated at
the price the provider offered, not at a market clearing price:

> "Each bid awarded on the BCM is remunerated with the offered balancing capacity
> price." — regelleistung.net, *Tendering and bidding process*

aFRR **energy** is the opposite: activated energy settles at the marginal price
determined by PICASSO. The two legs of the same product use different rules, and
the spec had absorbed neither.

### What the specification assumed instead

Four independent places encoded a pay-as-cleared market:

- **ADR-010** makes `r_up[b]`, `r_dn[b]` pure *volume* decisions priced at the
  dual `μ`, and its reserve subproblem offers "where expected capacity revenue
  plus expected activation value exceeds `μ`". That is the correct formulation
  when the market hands you a clearing price. Under pay-as-bid, offering at `μ`
  earns **exactly zero surplus on every award** — the entire margin is the
  markup, and there was no variable for it.
- **`AfrrCapacityView`** (L2) emits `B(E)` as "the expected value of the capacity
  bid curve", concave in offered MW, justified by "the clearing model". That
  model appeared nowhere in the corpus: two references, no definition. Bid price
  is not a coordinate of `B(E)`, so the curve cannot express the one trade-off
  the market actually poses.
- **The Planner** binds the SOC corridor and power headroom on the *offer*
  variable, with no award-probability discount on revenue — implicitly
  `P(award) = 1`. Conservative on feasibility, optimistic on revenue, and the
  optimism grows with the markup.
- **C4 §4** named the settlement input `clearingPrice{Up,Dn}EurPerMwH`. A
  marginal price genuinely exists in this market and is published per block, but
  it is not what settles. The name denotes a real series that would produce a
  wrong P&L if wired in.

Meanwhile C3 §2 *requires* a `limitPriceEurPerMwH` on every `AfrrCapacity` order,
and nothing on either side of the seam said how that number is chosen. ADR-012's
quoting policy covers a continuous limit-order ladder; DA gets a bid curve by
parametric re-solve. A sealed-bid daily auction got neither.

## Decision

**Reserve remuneration is pay-as-bid, the reservation price is `μ`, and the
markup over `μ` is a declared policy outside the solver.**

1. **Remuneration.** Awarded capacity is paid the submitted limit price.
   `INV-X-12` asserts the identity at C4. Activated energy is marginal-priced and
   is unaffected.

2. **`μ` is the reserve reservation price, and it is a Planner output.**
   ADR-010 Tier 2 already computes it and already calls it the reservation price,
   "provably consistent with spot opportunity cost". It was being discarded. It
   now crosses C3 as `reserveShadowValueEurPerMwH`, audit-only, exactly as
   `shadowValueEurPerMwh` does for the energy markets.

   Valuation does **not** own this number and structurally cannot: `μ` is the dual
   of a coupling constraint in the Planner's own problem, and Valuation never sees
   the spot side. No price channel is added to C2.

3. **Award probability comes from the existing scenario ensemble.** Under
   pay-as-bid a bid is awarded iff it sits at or below the marginal price, so

   ```
   P(award | p, b) = Σ_s w_s · 1[ afrrCapPriceEurPerMwH[s,b] ≥ p ]
   ```

   `afrrCapPriceEurPerMwH` is therefore **defined as the marginal (last accepted)
   capacity price** — C1 §4. No new artefact is fitted. The benefit over a
   separately calibrated surface is that award probability inherits the shared
   scenario axis (ADR-005, `INV-D-05`), so "we were not awarded *and* spot was
   extreme" is representable; a standalone surface would be independent by
   construction and would silently lose that correlation.

4. **`B(E)` is the envelope of that trade-off**, not a curve in an unnamed model:

   ```
   B(E) = max_p [ p · E · P(award | p, b) ]
   ```

   Still a function of offered MW alone, so C2's field shape is unchanged; its
   *meaning* is not, which is a major bump under C0 §5. Concavity remains
   **verified against the breakpoints, never assumed** (`INV-V-12`): the envelope
   of a maximisation is not concave for free, and if it fails on real ensembles
   the term is declared `General` and pays for its binaries rather than being
   mis-declared.

5. **`IReserveBidPolicy` maps `(μ[b], P(award | ·, b), envelope) → (limitPriceEurPerMwH, volumeMw)`.**
   A sibling of `IQuotingPolicy`, on the Planner side of the seam, never inside
   the MILP. It is the auction analogue of ADR-012's split, and it inherits that
   ADR's economic bound: `INV-P-11` forbids bidding below `μ`, because at that
   price the award destroys value.

6. **The policy runs inside Tier 2's loop, and once in Tier 3.** `B(E)` depends on
   `P(award | p)`, `p` depends on `μ`, and `μ` depends on the solve — a circularity
   on one scalar per block. Tier 2 already iterates `μ` by subgradient, so the
   fixed point is taken there at no structural cost. Tier 3's fast path prices
   once against `μ̂` and eats the one-pass error, which T5 reports like every other
   tier gap. This is the ladder doing the job it exists for.

7. **Feasibility binds on the offer; revenue is discounted by `P(award)`.**
   `rUp[b]`, `rDn[b]` remain the *offered* quantity. Every headroom and SOC-corridor
   constraint holds on the full offer, because an award obliges the whole of it.
   Only the revenue term is weighted by award probability. Splitting offered and
   expected-awarded into separate variables was rejected: it doubles the block
   variables to express what the discount already captures.

8. **One point bid per block per direction.** Whether a provider may submit
   several price/volume pairs for the same block is unconfirmed; until it is, C3
   carries a single `(limitPriceEurPerMwH, volumeMw)` and no reserve bid curve.
   If multi-bid is confirmed, the DA §3 pattern — parametric re-solve to a
   monotone schedule — is the extension, and it is a further major bump.

9. **An award obliges an energy offer.** Awarded capacity must be offered into the
   balancing energy market: offered MW ≥ awarded MW for every slot of the block.
   This is a compliance obligation, not a choice, and it is a Planner constraint
   with a `HALT` invariant (`INV-P-12`). The inequality is deliberate — offering
   *more* than the obligation is permitted and occasionally profitable, so the
   constraint is a floor, not an equality. The energy leg is marginal-priced, so
   bidding true marginal cost there is optimal; it does not inherit the shading
   problem.

## Consequences

- The engine has a bid price with a derivation, and the derivation is testable.
  T4 gains the reserve analogue of the fill-rate check: realised award rate
  against predicted, by price band.
- Bid-price error becomes a **measurable** P&L bucket, because the marginal price
  is published per block. "Bid too high and forfeited an awardable block" and
  "bid too low and gave away markup" are now separable in Settlement (C5 §6);
  under pay-as-cleared neither would be.
- `INV-X-12` is a cross-seam identity available only because the market is
  pay-as-bid — a free, exact check on the C3→C4 round trip.
- The bid policy is replaceable and A/B testable with the Planner held fixed,
  which is the same benefit ADR-012 bought for quoting, and its contribution to
  P&L is separately attributable for the same reason.
- Cost: an extra component, a fixed point inside Tier 2, and five seams at a
  major version.
- **Tension with ADR-002, recorded deliberately.** ADR-002 rules that all
  calendar and product structure is data, "a rule change must be a data change".
  Pay-as-bid is a market rule, and this ADR writes it into prose and into an
  unconditional invariant rather than into the `MarketCalendar`. That is a
  scoping decision — the engine targets Germany, where reserve capacity is
  pay-as-bid — and it is the one assumption here that a second jurisdiction would
  force back open. The remedy, should that day come, is a `remunerationRule`
  product attribute and a guard on `INV-X-12`; the field names chosen here
  (`awardedPrice*`) are already true under either rule.

## Rejected

- **Valuation exports the bid price.** `AfrrCapacityView`'s envelope has an
  internal maximiser, and exporting it is tempting because it is already computed.
  It is the wrong number: it maximises capacity *revenue*, ignoring the
  opportunity cost of the headroom sold. The right base for a markup is `μ`, and
  Valuation cannot see it.
- **Bid price as a MILP variable.** Same objection ADR-012 raises for quoting:
  it puts a stochastic award process and a non-convexity inside the solver.
- **A separately fitted award-probability surface.** More machinery than the
  scenario ensemble already provides, and it would break the correlation that
  ADR-005 exists to preserve.
- **Leaving `P(award) = 1`.** It systematically over-values reserve, the bias
  grows with the markup, and it would leave the bid policy nothing to optimise
  against.
- **An equality on the energy-offer obligation.** Simpler to state, but it
  forecloses voluntary offers of unawarded capacity, which are legal and
  sometimes profitable.
