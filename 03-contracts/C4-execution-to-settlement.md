# C4 — Execution → Settlement

**Payload:** `ExecutionOutcome` **Version:** 3.0 **Direction:** L4 → L5

What actually happened. Settlement must be able to reconstruct the truth from
this payload plus metered reality **alone** — it has no access to what the
Planner intended, so that ex-post accounting cannot be contaminated by ex-ante
belief.

---

## 1. Envelope

C0 §7 fields, plus `settlementPeriod`, `isFinal` (versus provisional), and
`revisionOf` (for restatements — meter data and imbalance prices arrive late and
get corrected).

**Provisional and final are both first-class.** The engine cannot wait for final
data to keep operating, and it must not treat provisional data as final. Every
settlement artefact declares which it is, and a restatement is a new artefact
referencing the old one, never an in-place edit (consistent with ADR-004's
append-only revisions).

## 2. Fills

| Field | Type | Unit | Null | Notes |
|---|---|---|---|---|
| `fillId` | `string` | — | no | |
| `intentId` | `string` | — | no | Links back to C3 — the only backward reference permitted, and it is an identifier, not data |
| `market`,`productId`,`slot`/`block` | — | — | no | |
| `side` | `Buy \| Sell` | — | no | |
| `priceEurPerMwh` | `double` | EUR/MWh | no | Executed price |
| `volumeMwh` | `double` | MWh | no | Executed volume |
| `feesEur` | `double` | EUR | no | Explicit, never netted into price |
| `executedAt` | `SlotId` | — | no | |

**Fills are energy markets only.** `market` ranges over `Da`, `IdContinuous` and
`AfrrEnergy` here; a fill is MWh at EUR/MWh, and the price and volume fields are
no longer nullable because there is no second unit to be exclusive against.

An `AfrrCapacity` commitment is **an award, not a fill** (§4). Earlier revisions
carried it as both, and the duplication was a live double-count hazard: L5 read
capacity revenue from the award and only fees from the fill, but nothing in this
contract said so, nothing reconciled the two, and a reader of C4 alone who summed
both got the revenue twice. The fill row was also strictly the weaker of the two —
it carries `side: Buy | Sell` with no up/down field, so it could not express
direction except through `productId`, while awards are split `Up`/`Dn`. C5 §3
already stated the resolution this contract omitted: "a capacity award is carried
by the reserve fields, not here". §4 is now the only representation.

## 3. Unfilled and cancelled

| Field | Type | Notes |
|---|---|---|
| `intentId` | `string` | |
| `disposition` | `Expired \| Cancelled \| Rejected \| PartiallyFilled` | |
| `rejectReason` | `string?` | Populated for `Rejected` |
| `residualVolumeMwh` | `double` | What did not trade. Energy markets only |

Unfilled orders are as important as fills. Without them, the four-bucket error
decomposition (L5) cannot separate "we were wrong about value" from "we could not
get the trade".

**A capacity intent that was not awarded appears here as a disposition, and its
volumes come from §4.** `residualVolumeMw` is gone with the capacity fill: the
shortfall on a reserve bid is `submittedMw − awardedMw` on the award record, which
is exact under partial awards and needs no second source of truth. The
`disposition` row still carries the `intentId` linkage, so `INV-X-01` reaches
reserve intents unchanged.

## 4. Reserve awards and activations

| Field | Type | Unit | Notes |
|---|---|---|---|
| `intentId` | `string` | — | The C3 intent this award answers |
| `submittedMw` | `double` | MW | Per block and direction; what was bid |
| `awardedUpMw`,`awardedDnMw` | `double` | MW | Per block; the binding obligation |
| `awardedPriceUpEurPerMwH`,`awardedPriceDnEurPerMwH` | `double` | EUR/MW/h | Per block. **What was paid** — the submitted limit price, capacity being pay-as-bid |
| `marginalPriceUpEurPerMwH`,`marginalPriceDnEurPerMwH` | `double` | EUR/MW/h | Per block. The published last-accepted price. **Benchmark only — never settles** |
| `feesEur` | `double` | EUR | Per block; explicit, never netted into price |
| `activatedEnergyUpMwh`,`activatedEnergyDnMwh` | `double` | MWh | Per slot, realised |
| `activationPriceUpEurPerMwh`,`activationPriceDnEurPerMwh` | `double` | EUR/MWh | Per slot |
| `deliveryShortfallMwh` | `double` | MWh | Per slot; non-zero triggers `INV-S-04` |

**Capacity is pay-as-bid; energy is marginal-priced.** The two legs of the same
product settle by different rules, and this table is where that shows. Awarded MW
is remunerated at the price *offered* — so `awardedPrice*EurPerMwH` equals the
`limitPriceEurPerMwH` on the originating intent, and `INV-X-12` asserts it. The
previous name for this field was `clearingPrice*EurPerMwH`, which was wrong twice
over: nothing clears at a single price under pay-as-bid, and a *marginal* price
genuinely exists and is published per block, so the old name denoted a real series
that is not the one that settles. That series is now carried under its own name.

`marginalPrice*EurPerMwH` never enters a revenue formula. It is the counterfactual
Settlement needs to separate "we bid above the margin and forfeited an awardable
block" from "we bid below it and gave away markup" (L5 §6) — an attribution only a
pay-as-bid market makes possible, and the reason to carry it at all.

`submittedMw` is here rather than derived from C3 so that Phase A accounting stays
a function of realised data alone (§8): the shortfall `submittedMw − awardedMw` is
computable without reading the plan.

## 5. Physical reality

| Field | Type | Unit | Card. | Notes |
|---|---|---|---|---|
| `pPoiMeteredImportMwh`,`pPoiMeteredExportMwh` | `double` | MWh | `[H]` | The billing-relevant series |
| `meteredLoadMwh`,`meteredPvMwh` | `double` | MWh | `[H]` | Sub-metered where available |
| `pBattChargeEnergyMwh`,`pBattDischargeEnergyMwh` | `double` | MWh | `[H]` | At the battery terminal |
| `socMeasuredMwh` | `double` | MWh | `[H]` | End of slot |
| `meterQuality` | `QualityStamp` | — | `[H]` | Provisional meter data is routine |

## 6. Imbalance

| Field | Type | Unit | Notes |
|---|---|---|---|
| `imbalanceVolumeMwh` | `double` | MWh | Per slot, signed. **Market frame**: `> 0` is LONG (the Bilanzkreis is over-delivered and the site is owed), `< 0` is SHORT (`02-conventions.md` §1) |
| `imbalancePriceEurPerMwh` | `double` | EUR/MWh | Per slot; often final only weeks later |
| `imbalanceCostEur` | `double` | EUR | Per slot |

## 7. Invariants

| ID | Invariant | On failure |
|---|---|---|
| `INV-X-01` | Every `fillId` references an `intentId` submitted in this run | `HALT` — phantom fill |
| `INV-X-02` | Filled volume ≤ intended volume per intent, across all fills | `HALT` |
| `INV-X-03` | Energy balance holds within meter tolerance: `pPoiMeteredImportMwh − pPoiMeteredExportMwh = meteredLoadMwh − meteredPvMwh + pBattChargeEnergyMwh − pBattDischargeEnergyMwh`. Metered PV is generation **after** curtailment, so it is `pv_out`, not `pv_avail`. **No efficiency term appears, and that is not an omission** — it is what places the loss boundary at the AC terminal of the storage unit (conventions §3): the battery energies here are metered on the grid side of the inverter, so conversion losses are already inside them and the identity closes without η | warn if provisional, `HALT` if final |
| `INV-X-04` | Execution did not consume `shadowValueEurPerMwh`/`urgency` | structural — adapter projection |
| `INV-S-04` | `deliveryShortfallMwh = 0` | alert + `HALT` — a reserve delivery failure is a prequalification risk |
| `INV-X-05` | `socMeasuredMwh` consistent with charge/discharge energy and η within tolerance | warn — drift indicates an η or SOH model error |
| `INV-X-06` | Every award has a corresponding entry in the commitment ledger by end of tick | `HALT` |
| `INV-X-12` | For every award, `awardedPrice{Up,Dn}EurPerMwH` equals the `limitPriceEurPerMwH` of the intent named by `intentId`. **Price only** — an award is partial whenever `awardedMw < submittedMw`, which is routine and not a violation | `HALT` — the venue paid something other than what was bid |

`INV-X-05` deserves note: persistent divergence between modelled and measured SOC
is the earliest available signal that the efficiency or degradation model has
drifted, and it is cheap to monitor. It feeds the model-error bucket in L5.

`INV-X-12` is an exact cross-seam identity, and it exists only because the market
is pay-as-bid: under marginal pricing there would be nothing to compare, since the
price paid is set by other participants' bids. It is `HALT` rather than a warning
because a mismatch means either the adapter mis-mapped the venue's response or the
remuneration rule is not what this specification assumes — and the second is a
premise failure, not a data error. If the engine is ever pointed at a
pay-as-cleared reserve market, this invariant is the thing that fires first, which
is the intended alarm (ADR-018, Consequences).

## 8. What does *not* cross C4

- Any Planner reasoning. Only `intentId` as an opaque key.
- Valuation output.
- Any belief or forecast. Settlement recomputes truth from realised data; if it
  needed a forecast it would be doing valuation, not settlement.
