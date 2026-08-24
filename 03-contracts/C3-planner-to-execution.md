# C3 — Planner → Execution

**Payload:** `ExecutionIntent` **Version:** 2.0 **Direction:** L3 → L4

Execution is the pre-existing market simulator, treated as an external system.
This contract is deliberately thin and carries **intent only** (ADR-012).

---

## 1. Envelope

C0 §7 fields, plus:

| Field | Type | Notes |
|---|---|---|
| `planId` | `string` | The plan this intent derives from |
| `gate` | `GateId` | Which gate this submission targets |
| `supersedes` | `string?` | Previous `planId` whose open orders this replaces |
| `mode` | `DegradationMode` | Echoed, so Execution can refuse speculative flow in `SAFE` |

## 2. Order intent

One row per order.

| Field | Type | Unit | Range | Null | Notes |
|---|---|---|---|---|---|
| `intentId` | `string` | — | unique | no | Stable across replaces |
| `market` | `Da \| IdContinuous \| IdAuction \| AfrrCapacity \| AfrrEnergy` | — | — | no | |
| `productId` | `string` | — | — | no | From the market calendar |
| `slot` / `block` | `SlotId` / `BlockId` | — | — | no | Delivery period |
| `side` | `Buy \| Sell` | — | — | no | Market frame (sell = discharge) |
| `limitPriceEurPerMwh` | `double` | EUR/MWh | tick-aligned | yes | Energy markets only. Already rounded (C0 rounding rule) |
| `limitPriceEurPerMwH` | `double` | EUR/MW/h | tick-aligned | yes | `AfrrCapacity` only. Already rounded (C0 rounding rule) |
| `volumeMwh` | `double` | MWh | lot-aligned, `>0` | yes | Energy markets only |
| `volumeMw` | `double` | MW | lot-aligned, `>0` | yes | `AfrrCapacity` only |
| `validity` | `Ioc \| Fok \| GtdUntil(SlotId) \| GtcUntilGate` | — | — | no | |
| `replacesIntentId` | `string?` | — | — | yes | Cancel/replace semantics |
| `shadowValueEurPerMwh` | `double` | EUR/MWh | — | no | Planner's indifference price — **audit only**, Execution must ignore it |
| `urgency` | `double` | — | `[0,1]` | no | `fraction`. Audit only |
| `tag` | `StrategyTag` | — | — | no | `Arbitrage \| PeakShave \| ReserveHedge \| Rebalance \| CommitmentCover` |

**The price and volume pairs are exclusive, and `market` decides which.** For
`Da`, `IdContinuous`, `IdAuction` and `AfrrEnergy` the order carries
`limitPriceEurPerMwh` and `volumeMwh`; for `AfrrCapacity` it carries
`limitPriceEurPerMwH` and `volumeMw`. Exactly one of each pair is present and the
other is absent — populating both, or the wrong one for the market, is a contract
failure. This replaces a single `limitPrice`/`volume` pair whose unit depended on
the market, which `INV-G-02` no longer permits: a field's unit suffix must equal
its declared unit, and a field with two possible units has no suffix it can
carry. The unit of a capacity order is MW of committed power, not MWh of energy,
and the seam now says so in the field name.

`shadowValueEurPerMwh` and `urgency` are carried across the seam **for settlement
attribution, not for execution**. They let L5 answer "did the quoting policy
trade through indifference?" without the Planner and the quoting policy having a
private side channel. `INV-X-04` asserts Execution does not read them — enforced
by the adapter interface exposing a projection that omits them.

## 3. Bid curves (DA)

DA requires a monotone schedule, not a point order.

| Field | Type | Unit | Notes |
|---|---|---|---|
| `curvePoints` | `DaCurvePoint[]` | — | Price-ordered. Each point is `(priceEurPerMwh, quantityMwh)`; the pair is a named record rather than an anonymous tuple precisely so each component carries its own unit suffix (`INV-G-02`) |
| `slot` | `SlotId` | — | |
| `monotone` | `bool` | — | Asserted, not declared: `INV-P-09` |

Quantity must be non-increasing in price for a buy curve and non-decreasing for a
sell curve. Violation indicates a Planner formulation error — most often a
missing coupling constraint — and is a `HALT`, because submitting a non-monotone
curve is both rejected by the exchange and diagnostic of a deeper problem.

## 4. Dispatch setpoints

For the physical controller, which is downstream of the Planner and out of scope
(`00-overview/01-system-model.md` §5), but whose interface is fixed here.

| Field | Type | Unit | Card. | Notes |
|---|---|---|---|---|
| `pBattSetpointMw` | `double` | MW | `[H_near]` | Battery frame |
| `socCorridorLowerMwh`,`socCorridorUpperMwh` | `double` | MWh | `[H_near]` | The band the controller must stay in to keep commitments feasible |
| `reserveObligationUpMw`,`reserveObligationDnMw` | `double` | MW | `[H_near]` | Confirmed awards the controller must be able to serve |

`H_near` is short (typically the next few slots). The corridor, not the setpoint,
is the binding instruction: the controller may deviate from the setpoint to
follow an activation signal, but never out of the corridor.

## 5. Invariants

| ID | Invariant | On failure |
|---|---|---|
| `INV-P-01` | Every intent is physically deliverable given SOC, power and POI limits under the planned trajectory | `HALT` |
| `INV-P-02` | Confirmed commitments in L0 are covered by the plan | `HALT` |
| `INV-P-03` | No two intents for the same product and slot on the same side without a `replacesIntentId` chain | `HALT` |
| `INV-P-04` | Prices tick-aligned, volumes lot-aligned | `HALT` |
| `INV-P-05` | In `SAFE`, only `CommitmentCover` and cancel intents are present | `HALT` |
| `INV-P-06` | Total planned discharge over any window respects energy availability including reserve corridor | `HALT` |
| `INV-P-09` | DA curves monotone | `HALT` |
| `INV-P-10` | No sell intent priced below `shadowValueEurPerMwh`; no buy intent priced above it | warn + block the intent |

`INV-P-10` is the economic safety net for the quoting policy. It is a warning
rather than a halt because a legitimate edge case exists — covering a commitment
at a loss is sometimes correct — but such intents must carry the
`CommitmentCover` tag, and an untagged violation is blocked.

## 6. What does *not* cross C3

- Any reason for the order. Execution is strategy-independent; `tag` is for
  settlement attribution only and does not affect routing.
- Belief, valuation or scenario data.
- Any expectation about fills. The Planner's fill beliefs stay upstream; C3 is
  an instruction, not a forecast.
