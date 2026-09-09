# aFRR capacity and aFRR energy — reference

A cross-cutting read of how the two legs of the aFRR product are treated across
the specification: beliefs, valuation, planning, execution, settlement,
invariants and tests. Descriptive only — every normative statement lives in the
document cited beside it.

---

## 1. The governing distinction

aFRR is **one product with two legs that settle by different rules**, and almost
every design choice below follows from that asymmetry.

| | aFRR **capacity** | aFRR **energy** |
|---|---|---|
| What is sold | MW of power headroom held for a coarse block | MWh actually delivered when called |
| Remuneration | **pay-as-bid** — the awarded MW is paid the price offered | **marginal-priced** by PICASSO |
| Decision | chosen: price *and* volume, at gate S1 | not chosen: activation is stochastic and exogenous |
| Unit | MW, EUR/MW/h | MWh, EUR/MWh |
| Gate | S1, `AfrrCapacityGate` | delivered in real time, S5 |

Sources: `00-overview/04-glossary.md` §7 (`pay-as-bid`),
`01-adr/ADR-018-reserve-bid-price-formation.md`.

**Unit suffixes.** `00-overview/02-conventions.md` §2 fixes capacity price as
EUR/MW/h and energy price as EUR/MWh, so capacity fields carry `EurPerMwH`
(capital `H`) and energy fields carry `EurPerMwh`. The distinction is enforced by
`INV-G-02`: a field's unit suffix must equal its declared unit, and a field with
two possible units has no suffix it can carry. This is why the price and volume
fields on `C3` and `C4` are split into exclusive pairs discriminated by `market`
rather than sharing one identifier.

---

## 2. The pipeline, layer by layer

### L1 — belief

Three series cross `C1` §4:

| Field | Card. | Meaning |
|---|---|---|
| `afrrCapPriceEurPerMwH` | `[S,B]` | **The marginal (last accepted) capacity price** per product block — not a revenue price. Default `0` = do not chase |
| `afrrEnergyPriceUpEurPerMwh`, `afrrEnergyPriceDnEurPerMwh` | `[S,H]` | Activation energy prices. Default `0` |
| `activationUp`, `activationDn` | `[S,H]` | A **dimensionless fraction in `[0,1]`** — the share of committed MW actually called. Not a volume, not a cost |

Reserve product structure crosses `C1` §7, all of it from the versioned
`MarketCalendar` (ADR-002): `blockIndex`, `blockSlots`, `sustainDuration` (the
prequalification `D`), `minBidMw`, `bidStepMw`, `symmetricProduct`.

`L1` §4 fixes the reading of `afrrCapPriceEurPerMwH`: because capacity is
pay-as-bid, a bid is awarded exactly when it sits at or below that number, so
award probability is a **tail mass of the ensemble**:

```
P(award | p, b) = Σ_s w_s · 1[ afrrCapPriceEurPerMwH[s,b] ≥ p ]
```

Nothing is materialised for this, deliberately (`L1` §5 artefact table: *reserve
award probability — not materialised*). A separately fitted award surface would
be independent of the price and activation draws by construction and would
discard exactly the dependence ADR-005 exists to preserve.

`MaxStale` for both `afrrCapPriceEurPerMwH` and `daPriceEurPerMwh` is `Gate`, not
a slot count: a price belief from before the gate has been superseded by an
event, not merely aged.

### L2 — valuation

Two independent views, each owning exactly one effect (`L2` §5 ownership matrix):

| View | Effect | Base | Emits |
|---|---|---|---|
| `AfrrCapacityView` | `ReserveCapacityRevenue` | `ReserveCapacity` | `BoundTerm` (MW envelope from prequalification and asset limits) + `PwlTerm` (concave, Maximize) for `B(E)` |
| `AfrrEnergyView` | `ReserveEnergyRevenue` | `MarketVolume` | `LinearTerm`s for expected activation value |

`B(E)` is the envelope of the pay-as-bid trade-off between markup and award
probability:

```
B(E) = max_p [ p · E · P(award | p, b) ]
```

It is a function of offered MW alone, so the Planner's variable is unchanged.
The maximiser `p*` **is computed here and deliberately not published**: it
maximises capacity *revenue* while ignoring what the headroom would have earned
elsewhere, so it is the wrong base for a markup. The right base is `μ`, a dual of
the Planner's own problem that Valuation structurally cannot see.

Concavity of an envelope is not automatic. It is **verified against the
breakpoints, never assumed** (`INV-V-12`); a failure means the term is declared
`General` and pays for its binaries rather than being mis-declared.

### L3 — planner

`rUp[b]`, `rDn[b]` are the quantity **offered**, not awarded. Every feasibility
constraint binds on the whole offer, because an award obliges all of it; only the
*revenue* term is weighted by `P(award | p, b)`.

```
p_d[t] + rUp[b(t)]        ≤ pBattMaxDischargeMw[t]
p_c[t] + rDn[b(t)]        ≤ pBattMaxChargeMw[t]
soc[t] − rUp[b(t)]·D/η_d  ≥ socMinMwh
soc[t] + rDn[b(t)]·D·η_c  ≤ socMaxMwh

Σ_slots∈b  eAfrrUp[t] ≥ awardedUpMw[b],  eAfrrDn[t] ≥ awardedDnMw[b]   ∀ t ∈ b
```

Bid formation, `L3` §5:

1. Extract `μ_up[b]`, `μ_dn[b]` — the duals on the headroom coupling
   constraints, the reservation price of a MW of headroom in EUR/MW/h. Tier 2
   produces these directly; Tier 3 evaluates `μ̂`.
2. `IReserveBidPolicy` maps `(μ[b], P(award | ·, b), envelope)` to one
   `(limitPriceEurPerMwH, volumeMw)` per block and direction — a single point,
   not a curve.
3. `μ[b]` crosses `C3` as `reserveShadowValueEurPerMwH`, audit-only, so
   Settlement can attribute the markup without the policy and the Planner
   sharing a private channel.
4. **Hard rule:** never bid below `μ` (`INV-P-11`).

The gate structure (`L3` §1) places reserve first: S1 freezes `rUp`/`rDn` offers
under reserve price beliefs; S2 (day-ahead) then knows the reserve awards.

**Objective.** The risk functional runs on imbalance cost alone:

```
− cvarWeight · CVaR_α(imbalance cost)
```

Activation carries no cost term (ADR-019). Its euros are revenue, carried by
`ReserveEnergyRevenue`; the energy moved to deliver an activation is priced by
`SpotView`, by `OppCostView` on `BatteryThroughput`, and by `V` on terminal SOC;
and the hazard activation actually presents — deliverability — is carried by
`chanceLevel` on SOC feasibility and by nothing else. `cvarWeight` therefore has
exactly one referent, the imbalance tail.

**The dispatch boundary** (`L3` §7): the Planner publishes a SOC corridor and a
setpoint, and the *corridor* is binding. The controller may deviate from the
setpoint to follow an activation signal but must stay inside the corridor —
which is exactly the guarantee that keeps the reserve commitment deliverable
without the Planner running at control frequency.

### L4 — execution

A capacity submission does not produce a fill. It produces an **award**, emitted
into `C4` §4 and never into `C4` §2, which is energy markets only. The
distinction is load-bearing: an award is per block and per direction, carries
`submittedMw` alongside `awardedMw` so a partial award is legible, and settles at
the price bid. aFRR *energy* does produce ordinary fills.

`shadowValueEurPerMwh`, `reserveShadowValueEurPerMwH` and `urgency` cross `C3`
for settlement attribution only; `INV-X-04` is enforced structurally by the
adapter exposing a projection that omits them.

### L5 — settlement

```
ReserveCapacityRevenue = Σ_b ( awardedUpMw[b]·awardedPriceUpEurPerMwH[b]
                             + awardedDnMw[b]·awardedPriceDnEurPerMwH[b] ) · blockHours[b]
                         − Σ_b feesEur[b]

ReserveEnergyRevenue   = Σ_t ( activatedEnergyUpMwh[t]·activationPriceUpEurPerMwh[t]
                             − activatedEnergyDnMwh[t]·activationPriceDnEurPerMwh[t] )
```

`blockHours` comes from the market calendar, never a constant — block length is a
product attribute and changes. `awardedPrice*` is the price bid. `marginalPrice*`
appears in no revenue formula; it is the benchmark for `reserveBidErrorEur`.

Both effects are **directly attributable** by `StrategyTag` (`L5` §7), each
cashflow descending from one award or fill.

Risk terms are not cashflows: ex post there is no risk, only an outcome, and the
four-bucket decomposition uses the risk-free objective on both sides.

---

## 3. Why capacity is priced the way it is

### Pay-as-bid makes the bid price a policy, not a dual (ADR-018)

Under pay-as-bid, offering at `μ` earns **exactly zero surplus on every award** —
the entire margin is the markup. The decision has nine parts; the load-bearing
ones:

- **`μ` is the reserve reservation price and a Planner output.** Valuation cannot
  own it: `μ` is the dual of a coupling constraint in the Planner's own problem,
  and Valuation never sees the spot side. No price channel is added to `C2`.
- **Award probability comes from the existing scenario ensemble**, so "we were
  not awarded *and* spot was extreme" stays representable. A standalone surface
  would be independent by construction.
- **`IReserveBidPolicy`** is a sibling of `IQuotingPolicy`, on the Planner side
  of the seam, **never inside the MILP** (`stubs/Interfaces.cs`).
- **The policy runs inside Tier 2's subgradient loop, and once in Tier 3.**
  `B(E)` depends on `P(award | p)`, `p` depends on `μ`, and `μ` depends on the
  solve — a circularity on one scalar per block. Tier 2 already iterates `μ`, so
  the fixed point is taken there at no structural cost; Tier 3's fast path prices
  once against `μ̂` and carries the one-pass error into its measured gap (T5).
- **Feasibility binds on the offer; revenue is discounted by `P(award)`.**
  Splitting offered and expected-awarded into separate variables was rejected —
  it doubles the block variables to express what the discount already captures.
- **One point bid per block per direction.** Whether several price/volume pairs
  may be submitted for one block is unconfirmed; until it is, `C3` carries one
  and there is no reserve bid curve.
- **An award obliges an energy offer**: offered MW ≥ awarded MW for every slot of
  the block (`INV-P-12`). The inequality is deliberate — offering *more* than the
  obligation is permitted and occasionally profitable. The energy leg is
  marginal-priced, so bidding true marginal cost there is optimal; it does not
  inherit the shading problem.

### Reserve is priced, not pre-allocated (ADR-010)

Reserve does not consume energy — it consumes **power headroom and SOC
corridor**. Every MW of upward reserve held is a MW that cannot be sold on spot
and a block of SOC that cannot be traded away, for the whole delivery block.

Hard pre-allocation ("reserve X MW, then let the spot MILP work in what's left")
is a *primal restriction*: it deletes options, and if X is wrong the loss is
unbounded and **invisible**, because the spot MILP reports a clean optimum over a
silently amputated feasible set. The correct instrument is a **dual price** —
charge the spot problem for the headroom it consumes. Dual pricing weakly
dominates primal restriction: it recovers the same solution when the allocation
happens to be right and does better whenever it is wrong.

Three tiers behind one `ICoOptimizer` interface, with a mandatory measured gap:

| Tier | What it is | Reserve treatment |
|---|---|---|
| 1 | Joint stochastic MILP (the reference) | `r_up[b]`, `r_dn[b]` decision variables alongside spot dispatch, coupling constraints explicit |
| 2 | Lagrangian decomposition | Reserve subproblem over coarse blocks: offer where `B(E)` exceeds `μ`. `μ*` is a *provably consistent* reservation price and the dual value is a certified bound |
| 3 | Learned reservation-price surface `μ̂ = f(features)` | Price the headroom once, solve one spot MILP. Features include reserve-price beliefs, SOC, peak headroom, qualification state |

No tier ships without its gap measured against Tier 1, reported as
**p50 / p90 / p99 and worst case** — never a mean — and conditioned on regime,
including high reserve-price days.

### Linearizable form (ADR-008)

- `AfrrCapacity` → `PwlTerm` (concave, Max) + `BoundTerm` (MW envelope). Embeds
  with no binaries **when it is concave**, which is checked (`INV-V-12`), not
  assumed.
- `AfrrEnergy` → `LinearTerm` per scenario. The imbalance risk functional is a
  `PwlTerm` (convex, Min); CVaR is linear-programmable (Rockafellar–Uryasev), so
  the risk term does not change the problem class.

### Correlation is the whole point (ADR-005)

Activation is correlated with imbalance prices, which are correlated with
intraday prices, which are correlated with the residual load that also drives the
site's own load and PV. A high-price hour is precisely the hour where the peak
charge bites, the aFRR is activated, and the battery is wanted in three places at
once. Sampling each series from its own marginal destroys exactly the dependence
that determines whether the co-optimisation is worth anything — and it
systematically *overstates* the strategy's value, because it lets the engine
believe it can be lucky in one market and unlucky in another independently.
`INV-D-05` makes the shared scenario axis a contract obligation.

---

## 4. Contract field inventory

| Seam | Capacity | Energy |
|---|---|---|
| **C1** §4, §7 | `afrrCapPriceEurPerMwH` `[S,B]`; `blockIndex`, `blockSlots`, `sustainDuration`, `minBidMw`, `bidStepMw`, `symmetricProduct` | `afrrEnergyPriceUp/DnEurPerMwh` `[S,H]`, `activationUp/Dn` `[S,H]` |
| **C2** §2, §5 | vars `rUp`/`rDn` `[B]`; `rUpMaxMw`/`rDnMaxMw` `[B]`, `capacityValueCurveUp/Dn` (`PwlTerm`, Concave/Maximize, verified) | vars `eAfrrUp`/`eAfrrDn` `[S,H]`; `LinearTerm`s from `AfrrEnergyView` |
| **C2** §3.5 | `CouplingConstraint.kind ∈ { ReserveCorridor, ReservePowerHeadroom, … }` | — |
| **C3** §2 | `market = AfrrCapacity`, `limitPriceEurPerMwH`, `volumeMw`, `reserveShadowValueEurPerMwH` | `market = AfrrEnergy`, `limitPriceEurPerMwh`, `volumeMwh`, `shadowValueEurPerMwh` |
| **C4** §4 | `intentId`, `submittedMw`, `awardedUp/DnMw`, `awardedPriceUp/DnEurPerMwH`, `marginalPriceUp/DnEurPerMwH`, `feesEur` | `activatedEnergyUp/DnMwh`, `activationPriceUp/DnEurPerMwh`, `deliveryShortfallMwh` |
| **C5** §4 | ledger `kind = ReserveAward`, `signedVolumeMw`, `priceEurPerMwH`, `feasibilityRequirement: SocCorridor?` | ledger `kind = SpotPosition`-style rows, `signedVolumeMwh`, `priceEurPerMwh` |
| **C5** §5, §6 | `ReserveCapacityRevenue` in `realisedByEffect`; `reserveBidErrorEur` sub-bucket of `executionSlippageEur` | `ReserveEnergyRevenue` in `realisedByEffect` |

Three points worth holding onto:

- **The price and volume pairs are exclusive, and `market` decides which.**
  Exactly one of each pair is present; populating both, or the wrong one for the
  market, is a contract failure. Same rule for the shadow-value pair.
- **`marginalPrice*EurPerMwH` never enters a revenue formula.** It is the
  published counterfactual Settlement needs to separate "we bid above the margin
  and forfeited an awardable block" from "we bid below it and gave away markup".
- **A capacity award is not a fill.** `C4` §4 is its only representation; an
  unawarded capacity intent appears in `C4` §3 as a disposition, and the
  shortfall on a reserve bid is `submittedMw − awardedMw` on the award record.
  `submittedMw` lives on the award record rather than being read back from `C3`
  so that Phase A accounting stays a function of realised data alone.

The commitment ledger's single `priceEurPerMwH` field needs no split precisely
because capacity is pay-as-bid: on `AfrrCapacity` it carries the submitted limit
price while the entry is an `OpenOrder` and the awarded price once it is a
`ReserveAward`, and those are **the same number**. The transition confirms a
price rather than replacing it. Under a pay-as-cleared market it would need two.

---

## 5. Invariants

| ID | Statement | On failure |
|---|---|---|
| `INV-D-04` | `blockIndex` is non-decreasing and covers every slot exactly once; `blockSlots` sums to `slotCount` | `HALT` |
| `INV-D-05` | Every `[S,·]` array shares the scenario axis — `activationUp` indexed by `s` denotes the same world as `daPriceEurPerMwh` indexed by `s` | `HALT` |
| `INV-V-12` | The capacity curve's declared `Concave` is verified against its breakpoints — the envelope of a maximisation is not concave for free, and binary count depends on the declaration being true | `HALT` |
| `INV-P-06` | Planned discharge respects energy availability including the reserve corridor: the plan cannot sell the same MWh into spot and hold it as reserve headroom | `HALT` |
| `INV-P-07` | No simultaneous charge and discharge above tolerance. **Not** automatic under an activation obligation, so a post-solve monitor runs on every solve | `alert` |
| `INV-P-11` | No `AfrrCapacity` intent is priced below its `reserveShadowValueEurPerMwH`. Bites harder than `INV-P-10`: under pay-as-bid a bid below `μ` is not a thin margin but a certain loss on every MW awarded. Same `CommitmentCover` exception | `warn`, **and the intent is blocked** |
| `INV-P-12` | For every block carrying a confirmed award, an `AfrrEnergy` intent exists in every slot of the block with volume ≥ the awarded MW. The bound is `≥` because offering beyond the obligation is permitted | `HALT` |
| `INV-X-06` | Every award has a `Confirmed` commitment-ledger entry by end of tick | `HALT` |
| `INV-X-07` | The net market position summed across day-ahead, intraday and aFRR equals the physical flow in the same slot — every position is physically backed | `HALT` |
| `INV-X-12` | For every award, `awardedPrice{Up,Dn}EurPerMwH` equals the `limitPriceEurPerMwH` of the intent named by `intentId`. **Price only** — `awardedMw < submittedMw` is a routine partial award | `HALT` |
| `INV-S-04` | `deliveryShortfallMwh = 0` for every slot. A reserve delivery failure is a prequalification risk, not an accounting item. Mirrored at `C4` so it is caught when the outcome arrives, not only at the state write | `alert` **and** `HALT` |
| `INV-S-05` | Every `Confirmed` commitment carries a `feasibilityRequirement` where the product requires one. A confirmed award with no SOC corridor is an obligation the Planner cannot see | `HALT` |

`INV-X-12` is the one to understand. It is an exact cross-seam identity
**available only because the market is pay-as-bid** — under marginal pricing
there would be nothing to compare, since the price paid is set by other
participants' bids. It is `HALT` rather than a warning because a mismatch means
either the adapter mis-mapped the venue response or the remuneration rule is not
what the specification assumes, and the second is a premise failure rather than a
data error. If the engine is ever pointed at a pay-as-cleared reserve market,
this invariant is the thing that fires first, which is the intended alarm.

`INV-P-11` and `INV-P-12` were allocated by ADR-018; `INV-X-12` likewise,
skipping `INV-X-11`, which `TN-03` §3.10 reserves.

---

## 6. Testing

**T2 — property and metamorphic**

- **Monotonicity.** `afrrCapPriceEurPerMwH[s,b] ↑ ∀s` ⇒ `capacityValueCurve[b]`
  pointwise non-decreasing in offered MW. At the Planner: raise it and planned
  reserve MW must be non-decreasing.
- **Scale equivariance.** Both aFRR price series are in the set multiplied by
  `k > 0`; every EUR quantity scales by exactly `k` and every MW bound is
  unchanged, including `muStar` on Tier 2. The single highest-value cheap test in
  the suite.
- **Ensemble coherence.** `AfrrEnergyView` **must** react to a permutation of one
  series' scenario axis, because activation value depends on activation
  co-moving with price. A view that does not react is ignoring the dependence
  structure ADR-005 exists to preserve.
- **Conformance properties on the reserve round trip**, which scale equivariance
  cannot reach because both are identities rather than magnitudes:
  `AwardPaysTheBidPrice` (`INV-X-12`) and `AwardObligesAnEnergyOffer`
  (`INV-P-12`). The first is the cheapest possible check that the market is the
  market this specification assumes.
- **Degradation matrix.** `afrrCapPriceEurPerMwH` degrades to default `0` — do
  not chase revenue on invented prices. Degraded `activationUp`/`activationDn`
  raises `chanceLevel`, escalating to `DEFENSIVE` when absent.

**T4 — calibration and execution quality**

- §2.4 *Activation model*: realised activation fraction vs. predicted by block,
  direction and regime, aggregate bias within `± 0.03`; a reliability diagram on
  the activation probability, where **under-predicting activation is the unsafe
  direction**; chance-constraint calibration — realised frequency of SOC leaving
  the band must be `≤ ε` over the holdout, and above `ε` blocks release; and a
  joint check that activation conditional on price regime shows the correlation
  ADR-005 preserves in the *realised* data too.
- §3.1.1 *Reserve bid policy*: award-rate calibration against the ex-ante
  `P(award)` by price band; realised markup `limitPrice − μ` by block and
  direction (a markup that never varies with `μ` means the policy is a fixed
  offset wearing a model); and `forfeited` and `donated` reported
  **separately, never netted** — they are opposite errors from one policy, and a
  policy that bids too high on some blocks and too low on others nets to zero and
  looks perfect. Cut also by block position in the day and by
  `qualCritical`/`peakCritical`, because reserve competes with peak and
  qualification on exactly the days those flags fire. Reconciles with `C5`'s
  `reserveBidErrorEur` within tolerance.

**T5 — gap and performance.** Tier gap conditioned on regime, including high
reserve-price days; Tier 3's one-pass bid-pricing error shows up here.

**L5 fixtures.** The synthetic day — flat load, one price spike, one aFRR block,
one peak event — is the fixture that catches unit errors on this product.

---

## 7. P&L attribution

`reserveBidErrorEur` is a **signed** declared sub-bucket of
`executionSlippageEur` (`C5` §6, `L5` §6), owned by `IReserveBidPolicy`. Both
halves are counterfactuals against the published marginal price:

- for an unawarded bid, the surplus `(marginalPrice − μ) · submittedMw ·
  blockHours` that a bid at the margin would have earned;
- for an awarded one, the additional `(marginalPrice − awardedPrice)` per MW that
  would still have cleared.

Both signs are real — forfeiting an awardable block and clearing at less than you
could have are opposite errors from the same policy — so they are never netted.

The bucket is computable **only because capacity is pay-as-bid** and the marginal
price is published per block. Under pay-as-cleared, neither half would exist.

---

## 8. Open and unresolved

1. **Multi-bid per block is unconfirmed** (ADR-018 §8). Whether a provider may
   submit several price/volume pairs for the same block is not established;
   until it is, `C3` carries a single point bid and no reserve bid curve. If
   multi-bid is confirmed, the extension is the day-ahead pattern — parametric
   re-solve to a monotone schedule — and it is a further major bump.
2. **The ADR-002 tension, recorded deliberately.** ADR-002 rules that all
   calendar and product structure is data: "a rule change must be a data
   change". Pay-as-bid is a market rule, and it is written into prose and an
   unconditional invariant rather than into the `MarketCalendar`. That is a
   scoping decision — the engine targets Germany — and it is the one assumption
   here that a second jurisdiction would force back open. The remedy is a
   `remunerationRule` product attribute and a guard on `INV-X-12`; the
   `awardedPrice*` field names are already true under either rule.
3. **S1 carries the widest delineation uncertainty of any gate** (`L3` §1, and
   ADR-017). The reserve gate is the one commitment made before the month's
   route is knowable. The measurement is a re-solve on the realised D+1 `(24)¼`
   vector, and the objective delta is the reserve gate's delineation foresight
   cost. A large measured cost there is a direct argument for smaller reserve
   volumes late in an undecided month; a small one retires the question.
4. **Widening the risk functional** to all market-facing EUR loss — imbalance
   plus the spot/intraday leg that offsets it — is left open deliberately
   (ADR-019, `TN-04` §7). It is not rejected on merit; it changes *what is
   risk-weighted* and belongs with a decision about the risk instrument, such as
   a `cvarLevel` per functional.
