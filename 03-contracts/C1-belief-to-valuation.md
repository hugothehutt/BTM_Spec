# C1 — Belief → Valuation

**Payload:** `BeliefSnapshot` **Version:** 3.0 **Direction:** L1 → L2

Carries everything Valuation may know about the world, as of a knowledge-time
boundary, over the hot window. Nothing else reaches Valuation. In particular
Valuation has **no** access to the Belief store, to the network, or to a clock.

---

## 1. Envelope

Per C0 §7: `tickId`, `asOf`, `horizon`, `assetId`, `poiId`, `manifestId`,
`schemaVersion`, `contentHash`, `inputHashes`.

Plus:

| Field | Type | Notes |
|---|---|---|
| `scenarioCount` | `int` | `S`, the shared scenario axis length (ADR-005) |
| `scenarioWeights` | `double[S]` | Non-negative, sums to 1 |
| `slotStart` | `SlotId` | First slot of the window |
| `slotCount` | `SlotSpan` | `H_hot` |

## 2. Site and asset state

| Field | Type | Unit | Card. | Range | Null | Default | MaxStale | Notes |
|---|---|---|---|---|---|---|---|---|
| `socNowMwh` | `double` | MWh | 1 | `[socMinMwh, socMaxMwh]` | no | — | 1 | Measured; if telemetry lost, escalate — never default |
| `socMinMwh`,`socMaxMwh` | `double` | MWh | 1 | `≥0`, `min<max` | no | — | — | Operating band, incl. warranty derates |
| `pBattMaxDischargeMw`,`pBattMaxChargeMw` | `double` | MW | `[H]` | `≥0` | no | — | — | Per-slot; may derate on temperature/SOH |
| `etaCharge`,`etaDischarge` | `double` | — | 1 | `(0,1]` | no | — | — | One-way |
| `sohFraction` | `double` | — | 1 | `(0,1]` | yes | `1.0` | 96 | Informational; derates flow via `pBattMax*Mw`/`socMaxMwh` |
| `pPoiImportLimitMw`,`pPoiExportLimitMw` | `double` | MW | `[H]` | `≥0` | no | — | — | Contractual/physical POI envelope |
| `auxLoadMw` | `double` | MW | `[H]` | `≥0` | yes | site max | 96 | Explicit, not folded into η |

## 3. Site forecasts (scenario-indexed)

| Field | Type | Unit | Card. | Range | Null | Default | MaxStale | Notes |
|---|---|---|---|---|---|---|---|---|
| `loadMw` | `double` | MW | `[S,H]` | `≥0` | no | **high quantile** | 8 | Conservative default is *high* — protects peak |
| `pvAvailMw` | `double` | MW | `[S,H]` | `≥0` | yes | `0` | 8 | Conservative default is *zero* — protects peak |
| `loadQuality`,`pvQuality` | `QualityStamp` | — | `[H]` | — | no | — | — | Per-element; measured prefix, forecast suffix |

## 4. Market beliefs (scenario-indexed)

| Field | Type | Unit | Card. | Range | Null | Default | MaxStale | Notes |
|---|---|---|---|---|---|---|---|---|
| `daPriceEurPerMwh` | `double` | EUR/MWh | `[S,H]` | `[-9999, 9999]` | no | — | gate | Negative prices are legal and material |
| `daClearedEurPerMwh` | `double` | EUR/MWh | `[H]` | as above | yes | — | — | Present only post-gate; null pre-gate |
| `idPriceRefEurPerMwh` | `double` | EUR/MWh | `[S,H]` | as above | no | — | 2 | Reference (e.g. ID index) belief |
| `idSpreadBeliefEurPerMwh` | `double` | EUR/MWh | `[S,H]` | `≥0` | no | wide | 2 | Half-spread belief; wide default suppresses trading |
| `afrrCapPriceEurPerMwH` | `double` | EUR/MW/h | `[S,B]` | `≥0` | no | `0` | gate | **The marginal (last accepted) capacity price** per product block — not a revenue price. Capacity is pay-as-bid, so a bid is awarded iff it sits at or below this; `P(award \| p, b)` is read off the scenario axis (ADR-018 §3). Zero default = do not chase |
| `afrrEnergyPriceUpEurPerMwh`,`afrrEnergyPriceDnEurPerMwh` | `double` | EUR/MWh | `[S,H]` | — | no | `0` | gate | |
| `activationUp`,`activationDn` | `double` | — | `[S,H]` | `[0,1]` | no | — | 96 | `fraction` of committed MW actually called. Scenario-consistent with prices (ADR-005) |
| `imbalancePriceEurPerMwh` | `double` | EUR/MWh | `[S,H]` | — | no | — | 96 | reBAP-type belief |

**`INV-D-05`** every scenario-indexed array shares the axis: index `s` denotes
the same state of the world in `loadMw`, `pvAvailMw`, `daPriceEurPerMwh`, `activationUp`, and all
others. Violation is a hard failure, not a warning — see ADR-005 for why this is
the invariant most worth defending.

## 5. Liquidity beliefs (no prices)

| Field | Type | Unit | Card. | Range | Null | Default | MaxStale | Notes |
|---|---|---|---|---|---|---|---|---|
| `idReliableVolumeBuyMwh` | `double` | MWh | `[H]` | `≥0` | no | `0` | 2 | Volume that may be *counted on* |
| `idReliableVolumeSellMwh` | `double` | MWh | `[H]` | `≥0` | no | `0` | 2 | |
| `idVolumeByPriceBandMwh` | `double` | MWh | `[H,Band]` | `≥0` | yes | `0` | 2 | Optional refinement (OPEN-3) |

These are **volumes only**. There is no price field in this section, by design:
it is the C1-side enforcement of the rule that fill probability constrains the
Planner but never prices for it (ADR-008, ADR-012). Prices for quoting reach the
quoting policy by a different route, outside C2.

## 6. Tariff and network

| Field | Type | Unit | Card. | Range | Null | Default | MaxStale | Notes |
|---|---|---|---|---|---|---|---|---|
| `activeRegimes` | `TariffRegimeId[]` | — | `1..n` | non-empty | no | — | — | From the calendar (ADR-011) |
| `peakPriceEurPerMw` | `double` | EUR/MW | per regime | `≥0` | no | — | — | |
| `volumetricChargeEurPerMwh` | `double` | EUR/MWh | `[H]` | `≥0` | no | — | — | Network volumetric component |
| `leviesAndTaxesEurPerMwh` | `double` | EUR/MWh | `[H]` | — | no | — | — | Closed enumerated list of the **non-reducible** components, summed (ADR-017) |
| `enfgLeviesEurPerMwh` | `double` | EUR/MWh | 1 | `≥0` | no | — | — | The reducible EnFG components, summed. Charged on `(21)`, never on metered import |
| `isHlzf` | `bool` | — | `[H]` | — | no | `true` | — | From the DSO HLZF table; conservative default is *inside* the window |

## 6.1 Delineation (ADR-017)

Both fields are **derived** in L1 §4.2.1, not ingested.

| Field | Type | Unit | Card. | Range | Null | Default | Notes |
|---|---|---|---|---|---|---|---|
| `awPositive` | `bool` | — | `[H]` | — | no | `false` | `(24)¼`. A realised parameter within the settled day-ahead horizon; scenario-indexed `[S, H]` beyond it. Conservative default forfeits the premium |
| `anzulegenderWertEurPerMwh` | `double` | EUR/MWh | per plant | `≥0` | no | — | `AW`, static per plant and EEG vintage |
| `zuordnungsFaktor` | `double` | — | per plant | `0..1` | no | — | `ZF`, power-weighted; sums to 1 across plants |
| `marktwertMonthEurPerMwh` | `double` | EUR/MWh | `[S]` | — | no | — | `MW_month`, derived **per scenario** from national spot and national solar generation on the shared axis (ADR-005). Never an independent series |
| `commonEegVintage` | `bool` | — | 1 | — | no | `false` | True only when every plant shares a § 51 / § 51b threshold, which is what makes the AW forfeit simultaneous and collapses A5 to A1. False forbids the collapse |

The premium is `MAX[anzulegenderWertEurPerMwh − marktwertMonthEurPerMwh; 0]`, which is **convex** in
`marktwertMonthEurPerMwh`. That is why the latter crosses as `[S]` and not as a scalar: a point
forecast understates the premium, most severely when the two are close.

## 7. Reserve product structure

| Field | Type | Unit | Card. | Range | Null | Notes |
|---|---|---|---|---|---|---|
| `blockIndex` | `int` | — | `[H]` | `≥0` | no | Maps each slot to its aFRR product block |
| `blockSlots` | `SlotSpan` | — | `[B]` | `>0` | no | Block lengths; from the calendar |
| `sustainDuration` | `SlotSpan` | — | 1 | `>0` | no | Prequalification `D` (ADR-010) |
| `minBidMw`,`bidStepMw` | `double` | MW | 1 | `>0` | no | Product granularity |
| `symmetricProduct` | `bool` | — | 1 | — | no | Whether up and down must be equal |

## 8. Quality summary

| Field | Type | Notes |
|---|---|---|
| `seriesQuality` | `map<SeriesId, QualityStamp>` | One per series; the basis of the risk multiplier |
| `degradationMode` | `DegradationMode` | Read from L0, echoed here so Valuation need not consult L0 separately |
| `criticalMissing` | `SeriesId[]` | Empty in `NORMAL`; non-empty implies mode ≥ `DEFENSIVE` |

## 9. Contract-specific invariants

| ID | Invariant | On failure |
|---|---|---|
| `INV-D-01` | `socMinMwh ≤ socNowMwh ≤ socMaxMwh` | `HALT` — telemetry or model is wrong |
| `INV-D-02` | No fact inside has `knowledgeTime > asOf` | `HALT` — lookahead |
| `INV-D-03` | Every `[H]` array has length `slotCount` | `HALT` |
| `INV-D-04` | `blockIndex` is non-decreasing and covers every slot exactly once | `HALT` |
| `INV-D-05` | Shared scenario axis across all `[S,·]` arrays | `HALT` |
| `INV-D-06` | `scenarioWeights` non-negative, sums to 1 ± 1e-9 | `HALT` |
| `INV-D-07` | Reduced-ensemble marginal means match the full ensemble within tolerance | warn + `DEGRADED` |
| `INV-D-08` | `pBattMaxChargeMw`, `pBattMaxDischargeMw` ≥ 0 and finite for every slot | `HALT` |
| `INV-D-09` | `etaCharge · etaDischarge ≤ 1` | `HALT` |
| `INV-D-10` | Every field marked `Null = no` present, or the field is listed in `criticalMissing` | escalate per ladder |

## 10. What deliberately does *not* cross C1

Stated to keep it out.

- Any handle, cursor or reference into the Belief store.
- Raw order-book data. Only the derived volume beliefs in §5.
- Any Planner or Settlement output (that arrives via L0, at a lag — ADR-006).
- Wall-clock time.
- Model objects. Only their evaluated outputs and their version hashes.
