# C4 — Execution → Settlement

**Payload:** `ExecutionOutcome` **Version:** 1.0 **Direction:** L4 → L5

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
| `price` | `EnergyPrice`/`CapacityPrice` | EUR/MWh, EUR/MW/h | no | Executed price |
| `volume` | `EnergyKwh`/`ReserveMw` | kWh, MW | no | Executed volume |
| `fees` | `Money` | EUR | no | Explicit, never netted into price |
| `executedAt` | `SlotId` | — | no | |

## 3. Unfilled and cancelled

| Field | Type | Notes |
|---|---|---|
| `intentId` | `string` | |
| `disposition` | `Expired \| Cancelled \| Rejected \| PartiallyFilled` | |
| `rejectReason` | `string?` | Populated for `Rejected` |
| `residualVolume` | `EnergyKwh`/`ReserveMw` | What did not trade |

Unfilled orders are as important as fills. Without them, the four-bucket error
decomposition (L5) cannot separate "we were wrong about value" from "we could not
get the trade".

## 4. Reserve awards and activations

| Field | Type | Unit | Notes |
|---|---|---|---|
| `awardedUp`,`awardedDn` | `ReserveMw` | MW | Per block; the binding obligation |
| `clearingPriceUp`,`…Dn` | `CapacityPrice` | EUR/MW/h | Per block |
| `activatedEnergyUp`,`…Dn` | `EnergyKwh` | kWh | Per slot, realised |
| `activationPriceUp`,`…Dn` | `EnergyPrice` | EUR/MWh | Per slot |
| `deliveryShortfall` | `EnergyKwh` | kWh | Per slot; non-zero triggers `INV-S-04` |

## 5. Physical reality

| Field | Type | Unit | Card. | Notes |
|---|---|---|---|---|
| `meteredPoiImport`,`…Export` | `EnergyKwh` | kWh | `[H]` | The billing-relevant series |
| `meteredLoad`,`meteredPv` | `EnergyKwh` | kWh | `[H]` | Sub-metered where available |
| `batteryChargeEnergy`,`…Discharge` | `EnergyKwh` | kWh | `[H]` | At the battery terminal |
| `socMeasured` | `EnergyKwh` | kWh | `[H]` | End of slot |
| `meterQuality` | `QualityStamp` | — | `[H]` | Provisional meter data is routine |

## 6. Imbalance

| Field | Type | Unit | Notes |
|---|---|---|---|
| `imbalanceVolume` | `EnergyKwh` | kWh | Per slot, signed |
| `imbalancePrice` | `EnergyPrice` | EUR/MWh | Per slot; often final only weeks later |
| `imbalanceCost` | `Money` | EUR | Per slot |

## 7. Invariants

| ID | Invariant | On failure |
|---|---|---|
| `INV-X-01` | Every `fillId` references an `intentId` submitted in this run | `HALT` — phantom fill |
| `INV-X-02` | Filled volume ≤ intended volume per intent, across all fills | `HALT` |
| `INV-X-03` | Energy balance holds within meter tolerance: `poiImport − poiExport = load − pv + chargeEnergy − dischargeEnergy` | warn if provisional, `HALT` if final |
| `INV-X-04` | Execution did not consume `shadowValue`/`urgency` | structural — adapter projection |
| `INV-S-04` | `deliveryShortfall = 0` | alert + `HALT` — a reserve delivery failure is a prequalification risk |
| `INV-X-05` | `socMeasured` consistent with charge/discharge energy and η within tolerance | warn — drift indicates an η or SOH model error |
| `INV-X-06` | Every award has a corresponding entry in the commitment ledger by end of tick | `HALT` |

`INV-X-05` deserves note: persistent divergence between modelled and measured SOC
is the earliest available signal that the efficiency or degradation model has
drifted, and it is cheap to monitor. It feeds the model-error bucket in L5.

## 8. What does *not* cross C4

- Any Planner reasoning. Only `intentId` as an opaque key.
- Valuation output.
- Any belief or forecast. Settlement recomputes truth from realised data; if it
  needed a forecast it would be doing valuation, not settlement.
