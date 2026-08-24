# C4 — Execution → Settlement

**Payload:** `ExecutionOutcome` **Version:** 2.0 **Direction:** L4 → L5

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
| `priceEurPerMwh` | `double` | EUR/MWh | yes | Executed price, energy markets only |
| `priceEurPerMwH` | `double` | EUR/MW/h | yes | Executed price, `AfrrCapacity` only |
| `volumeMwh` | `double` | MWh | yes | Executed volume, energy markets only |
| `volumeMw` | `double` | MW | yes | Executed volume, `AfrrCapacity` only |
| `feesEur` | `double` | EUR | no | Explicit, never netted into price |
| `executedAt` | `SlotId` | — | no | |

The price and volume pairs are exclusive and `market` decides which, exactly as on
C3 §2. A fill on `AfrrCapacity` is MW of committed power at EUR/MW/h; a fill on
any energy market is MWh at EUR/MWh. Carrying both in one field with a
market-dependent unit is what `INV-G-02` now rejects.

## 3. Unfilled and cancelled

| Field | Type | Notes |
|---|---|---|
| `intentId` | `string` | |
| `disposition` | `Expired \| Cancelled \| Rejected \| PartiallyFilled` | |
| `rejectReason` | `string?` | Populated for `Rejected` |
| `residualVolumeMwh` | `double` | What did not trade. Energy markets only |
| `residualVolumeMw` | `double` | What did not trade. `AfrrCapacity` only |

Unfilled orders are as important as fills. Without them, the four-bucket error
decomposition (L5) cannot separate "we were wrong about value" from "we could not
get the trade".

## 4. Reserve awards and activations

| Field | Type | Unit | Notes |
|---|---|---|---|
| `awardedUpMw`,`awardedDnMw` | `double` | MW | Per block; the binding obligation |
| `clearingPriceUpEurPerMwH`,`clearingPriceDnEurPerMwH` | `double` | EUR/MW/h | Per block |
| `activatedEnergyUpMwh`,`activatedEnergyDnMwh` | `double` | MWh | Per slot, realised |
| `activationPriceUpEurPerMwh`,`activationPriceDnEurPerMwh` | `double` | EUR/MWh | Per slot |
| `deliveryShortfallMwh` | `double` | MWh | Per slot; non-zero triggers `INV-S-04` |

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

`INV-X-05` deserves note: persistent divergence between modelled and measured SOC
is the earliest available signal that the efficiency or degradation model has
drifted, and it is cheap to monitor. It feeds the model-error bucket in L5.

## 8. What does *not* cross C4

- Any Planner reasoning. Only `intentId` as an opaque key.
- Valuation output.
- Any belief or forecast. Settlement recomputes truth from realised data; if it
  needed a forecast it would be doing valuation, not settlement.
