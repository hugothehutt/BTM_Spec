# T1 — Invariant Register and Contract Conformance

Normative and consolidated. Every invariant defined anywhere in this
specification, in one register, with its severity, its check site and its test
level.

---

## 1. How to read this register

This document is the single authoritative list. Where an ADR, a layer design or
a contract states an invariant, the statement here is the normative one and the
source is cited. Where a document **references** an ID without fully stating it
— `INV-T-01…04`, `INV-P-07`, `INV-G-05` — the full statement is written here and
marked `(consolidated)`.

An invariant is a predicate over data that must hold at a named point in the
pipeline. It is not a test; it is a runtime check that tests exercise. The tests
prove the check is wired up and that the system does not violate it on inputs
nobody thought of.

### 1.1 Severity semantics

Three severities, and nothing between them.

| Severity | Meaning | Runtime behaviour |
|---|---|---|
| `HALT` | The producing layer is in an unknown state; nothing downstream can be trusted. | Submit nothing. Cancel speculative orders. Alert. **Operator acknowledgement required to resume** (ADR-014). The tick's artefacts are still recorded, because the state at the moment of failure is the evidence. |
| `alert` | The engine can continue safely, but something is wrong that a human must look at within the operating day. | Continue; page the on-call; record in the settlement artefact so P&L can be conditioned on it. |
| `warn` | A tolerance was exceeded or a soft expectation missed. Diagnostic, not operational. | Continue; count it; expose the rate as a monitored series. A rising warn rate is itself an alert condition. |

Two source phrasings map onto these and are not separate severities:

- **"escalate per ladder"** (`INV-D-10`, `INV-V-05`) means the degradation
  ladder of ADR-014 §4 selects the fallback rung and the mode is recomputed
  centrally. The resulting mode may be anything from `DEGRADED` to `HALT`. The
  register records the *floor* severity as `alert`.
- **"structural"** (`INV-X-04`) means the invariant is enforced by the type
  system or an analyzer rule and cannot be violated at runtime. It still carries
  a test — a compile-fail test or an analyzer test — because "cannot happen" is a
  claim about the build, and builds change.

`INV-D-07` and `INV-P-10` carry a compound action in their source (`warn +
DEGRADED`, `warn + block the intent`). The register records the severity plus
the additional action in the statement.

### 1.2 Validation stays on in production

`C0` §3 is normative and is restated here because it is the rule most likely to
be eroded by a latency conversation:

> Validation is on by default in every environment, including production.

Every seam is validated **twice** — the producer validates before emitting, with
its own context available to diagnose its own bug; the consumer validates on
receipt, defending against a producer it does not control (`C0` §4).
Producer-side failure enters the producing layer's degradation ladder.
Consumer-side failure is `HALT`, unconditionally, because a contract violation
means the upstream layer's state is unknown.

The cost is a few microseconds against a solve measured in milliseconds. There
is no configuration flag that disables it, and adding one is an ADR-level
decision, not a tuning decision. The temptation to disable validation in
production is precisely the temptation to stop noticing corruption.

### 1.3 Tolerances

All comparisons reference the single constants file
(`00-overview/02-conventions.md` §6). Tolerances are never re-typed at a call
site.

| Domain | Tolerance |
|---|---|
| Money | `1e-6 EUR` absolute |
| Power | `1e-6 kW` absolute |
| Scenario weights | `1e-9` on the sum |
| Meter energy balance | Meter tolerance, per `meterQuality`, declared per site |
| Objective agreement between backends | Relative `1e-6`, or the MIP gap tolerance, whichever is larger |

---

## 2. `INV-G-*` — Global / universal

Applied at **every** seam by the shared validator, on both sides. Sources:
`C0` §3, `00-overview/02-conventions.md` §6, ADR-003, ADR-013.

| ID | Statement | Where checked | Severity | Test level |
|---|---|---|---|---|
| `INV-G-01` | No numeric field in any payload is `NaN` or `±Infinity`. Missing is expressed by the quality channel (`C0` §6), never by a sentinel value. | Shared validator, both sides of every seam | `HALT` | `T1`, `T6` (fuzz) |
| `INV-G-02` | Every numeric field carries a declared unit and a typed quantity (ADR-003). No bare `double` crosses a seam. | Schema check at serialisation and deserialisation | `HALT` | `T1`, `T0` (compile-fail) |
| `INV-G-03` | `schemaVersion` is recognised by the consumer. An unrecognised version is rejected, never guessed at, never partially read. | Consumer, before any field is read | `HALT` | `T1`, `T6` (version rejection) |
| `INV-G-04` | `contentHash` recomputed over the payload matches the carried value; `inputHashes` is present and non-empty. | Producer at seal, consumer at receipt | `HALT` | `T1`, `T3` |
| `INV-G-05` | *(consolidated)* No layer reads the system clock. Time enters the pipeline only as `tickId`/`SlotId` and `asOf`, supplied by the driver. No payload contains a wall-clock timestamp taken at construction, and no code path in `L1`, `L2`, `L3` or `L5` calls a wall-clock API. Enforced at two points: a **Roslyn analyzer rule** banning clock APIs in the layer assemblies (ADR-013), and a **payload check** that no timestamp field was populated at construction time (`C0` §3). | Analyzer at build; validator at every seam | `HALT` (build failure at the analyzer; `HALT` at the seam) | `T1`, `T3` |
| `INV-G-06` | Every slot-indexed array has exactly the length declared by `horizon` / `slotCount`. | Shared validator | `HALT` | `T1` |
| `INV-G-07` | Every scenario-indexed array shares the scenario axis length **and ordering**. Index `s` means the same thing in every array in the payload. | Shared validator | `HALT` | `T1`, `T2` |
| `INV-G-08` | `asOf` is non-decreasing across successive payloads on the same seam within a run. | Seam recorder / consumer, across ticks | `HALT` | `T1`, `T3` |
| `INV-G-09` | Every field marked `Null = no` in the contract's field table is present. | Shared validator | `HALT` | `T1` |
| `INV-G-10` | Declared ranges hold for **every element** of an array, not merely the first. | Shared validator | `HALT` | `T1`, `T6` (fuzz) |

---

## 3. `INV-D-*` — Belief and data

Sources: `C1` §9, `C1` §4, ADR-004, ADR-005.

| ID | Statement | Where checked | Severity | Test level |
|---|---|---|---|---|
| `INV-D-01` | `socMin ≤ socNow ≤ socMax`. | `C1` producer and consumer | `HALT` | `T1`, `T2` |
| `INV-D-02` | No fact inside a `BeliefSnapshot` has `knowledgeTime > asOf`. Enforced structurally: the only read API is `Get(series, validRange, asOf)` and the filter is applied inside the storage layer against an index, not by the caller (ADR-004 §1). | Belief store read path; re-asserted at `C1` | `HALT` | `T1`, `T2` (structural), `T6` (poisoning audit) |
| `INV-D-03` | Every `[H]` array has length `slotCount`. | `C1` validator | `HALT` | `T1` |
| `INV-D-04` | `blockIndex` is non-decreasing and covers every slot in the window exactly once; `blockSlots` sums to `slotCount`. | `C1` validator | `HALT` | `T1` |
| `INV-D-05` | All `[S,·]` arrays share the scenario axis: index `s` denotes the same coherent state of the world in `load`, `pv`, `daPrice`, `activationUp`, `imbalancePrice` and every other scenario-indexed series (ADR-005). Indexing one series by `s` and another by `s'` is a defect. | `C1` validator; enforced by construction in the ensemble artefact | `HALT` | `T1`, `T2` (axis coherence) |
| `INV-D-06` | `scenarioWeights` are non-negative and sum to `1 ± 1e-9`. | `C1` validator | `HALT` | `T1` |
| `INV-D-07` | The reduced ensemble preserves each marginal's mean, against the full ensemble, within tolerance. | Offline, at reduction-artefact build; re-checked at `C1` | `warn`, and the mode moves to `DEGRADED` | `T1`, `T2` |
| `INV-D-08` | `pMaxCharge` and `pMaxDischarge` are `≥ 0` and finite for every slot. | `C1` validator | `HALT` | `T1` |
| `INV-D-09` | `etaCharge · etaDischarge ≤ 1`. Round-trip efficiency above unity is a physical impossibility and usually a sign that a loss was placed twice or with the wrong sign. | `C1` validator | `HALT` | `T1`, `T0` |
| `INV-D-10` | Every field marked `Null = no` is present, **or** the field appears in `criticalMissing`. A missing field that is neither present nor declared missing is a broken ingestion path. | `C1` validator | `alert`, then escalate per the ADR-014 §4 ladder | `T1`, `T2` (quality matrix) |

---

## 4. `INV-V-*` — Valuation

Sources: `C2` §8, `C2` §3.3, `L2` §2/§4/§5, ADR-008, ADR-009.

| ID | Statement | Where checked | Severity | Test level |
|---|---|---|---|---|
| `INV-V-01` | No `(effect, decision variable, slot)` triple is priced by more than one term. Exclusivity, per the term ownership matrix (`L2` §5). | Composer, ownership check | `HALT` — double count | `T1`, `T2` (duplicate-effect injection) |
| `INV-V-02` | Every term's `originView` is the declared owner of its `effect` in the ownership matrix. | Composer, ownership check | `HALT` | `T1`, `T2` |
| `INV-V-03` | Every term references only decision-variable symbols from the closed vocabulary in `C2` §2. | Composer, then `C2` consumer | `HALT` | `T1` |
| `INV-V-04` | Every term marked `mandatory` is present in the bundle. Peak protection and confirmed-commitment terms are mandatory in every degradation mode (ADR-014 §3). | Composer, stage 5 | `HALT` | `T1`, `T2` (degradation matrix) |
| `INV-V-05` | `unclaimedEffects` is logged. An unclaimed effect is priced at zero and the omission recorded; if the effect is in the configured critical set, escalate. Silently missing revenue is safer than silently double-counting cost (ADR-009). | Composer, coverage check | `warn`; `alert` + escalate if critical | `T1`, `T2` |
| `INV-V-06` | No scenario array and no Belief handle, cursor, delegate or `object` appears anywhere in the bundle. Scenario structure reaches the Planner only through `CouplingConstraint.scenarioScope` and through terms already aggregated by Valuation. | `C2` producer and consumer | `HALT` | `T1`, `T2` (schema closure) |
| `INV-V-07` | `compositionOrder` satisfies the stage precondition table (ADR-009, `L2` §4): Tariff → OppCost+V → ReserveCoupling → Peak → Validate, each stage's `Requires` established by an earlier stage. | Composer, before each stage runs | `HALT` | `T1`, `T2` (out-of-order composition) |
| `INV-V-08` | *Reserved. Not allocated. Do not reuse — see §10.* | — | — | — |
| `INV-V-09` | *Reserved. Not allocated. Do not reuse — see §10.* | — | — | — |
| `INV-V-10` | *Reserved. Not allocated. Do not reuse — see §10.* | — | — | — |
| `INV-V-11` | `vSocSlopes` are strictly decreasing: `V` is concave in SOC. Concavity is what makes the PWL binary-free under maximisation (ADR-007). | Value-function artefact build; `C2` producer and consumer | `HALT` | `T1`, `T2` (concavity) |
| `INV-V-12` | Every `PwlTerm`'s declared `curvature` is verified against its breakpoints. A term declaring `Concave` with a non-concave breakpoint set is a contract violation, not a silently-wrong relaxation, because binary count — and therefore solve time — depends on the declaration being true (ADR-008). | Composer, curvature verification | `HALT` | `T1`, `T2` (curvature honesty) |
| `INV-V-13` | `prorationFactor ∈ [0,1]` and is consistent with the `CivilCalendar`: it equals the fraction of the accounting period covered by the planning horizon. Mis-setting it is the classic route to a pathologically peak-averse engine. | Composer; `C2` consumer | `HALT` | `T1`, `T2`, `T0` (calendar) |
| `INV-V-14` | `binariesByOrigin` sums to `binaryCount`, and `binaryCount` matches the binaries the Planner actually builds. | Composer, then `L3` model build | `warn` | `T1`, `T2` |
| `INV-V-15` | Every `BoundTerm` carries a `reason`; no bound with `reason = Physical` or `reason = Regulatory` is soft (`softPenalty` must be null). Feasibility restoration depends on this tagging (`L3` §6). | Composer; `C2` consumer | `HALT` | `T1`, `T2` (restoration ordering) |
| `INV-V-16` | `FillProbView` emits only `BoundTerm`. It carries no priced term. Fill probability constrains the Planner and never prices for it (ADR-008, ADR-012). | Composer; view schema check | `HALT` | `T1`, `T0` |

---

## 5. `INV-P-*` — Planner

Sources: `C3` §5, `L3` §4/§5, `00-overview/02-conventions.md` §3.

| ID | Statement | Where checked | Severity | Test level |
|---|---|---|---|---|
| `INV-P-01` | Every emitted intent is physically deliverable under the planned trajectory, given SOC bounds, per-slot power bounds and the POI envelope. | `L3`, before emitting `C3`; re-checked by the `C3` consumer | `HALT` | `T1`, `T2` (feasibility under every scenario) |
| `INV-P-02` | Every `Confirmed` commitment in the ledger is covered by the plan: the required SOC corridor and delivery obligation are satisfied as **hard** constraints, in every degradation mode. A plan that cannot honour a confirmed award never reaches `C3`. | `L3`, per solve, before emit | `HALT` | `T2` (commitment safety) |
| `INV-P-03` | No two intents target the same product, slot and side without a `replacesIntentId` chain linking them. | `C3` producer and consumer | `HALT` | `T1`, `T2` |
| `INV-P-04` | Limit prices are tick-aligned and volumes lot-aligned, rounded exactly once at the `C3` boundary (`00-overview/02-conventions.md` §6). Rounding earlier corrupts the optimality-gap measurement. | `C3` producer | `HALT` | `T1`, `T5` |
| `INV-P-05` | In `SAFE` mode, the intent set contains only `CommitmentCover` intents and cancels. | `C3` producer and consumer | `HALT` | `T2` (degradation matrix) |
| `INV-P-06` | Total planned discharge over any window respects energy availability including the reserve corridor: the plan cannot sell the same kWh into spot and hold it as reserve headroom. | `L3` post-solve; `C3` producer | `HALT` | `T2` |
| `INV-P-07` | *(consolidated)* No solution contains simultaneous charge and discharge above tolerance: for every slot `t`, `¬(p_charge[t] > 1e-6 kW ∧ p_discharge[t] > 1e-6 kW)`. With `η_c·η_d < 1` and non-negative energy prices this is automatic, so the complementarity binary is **omitted by default** and enabled by configuration flag. It is *not* automatic under negative prices, an aFRR activation obligation, or a peak-driven incentive to import, where burning energy can be profitable and the LP relaxation will exploit it. When the binary is disabled, this is a post-solve monitor over the returned trajectory; a violation means the omission was not safe for that instance and the tick is flagged (`00-overview/02-conventions.md` §3). | `L3`, post-solve monitor on every solve | `alert` | `T2` (negative-price and activation fixtures), `T5` |
| `INV-P-08` | *Reserved. Not allocated. Do not reuse — see §10.* | — | — | — |
| `INV-P-09` | Every DA bid curve is monotone: quantity non-increasing in price for a buy curve, non-decreasing for a sell curve. Asserted, never sorted into compliance. A non-monotone curve is rejected by the exchange **and** is diagnostic of a formulation error, most often a missing coupling constraint (ADR-012, `L3` §5). | `L3` after the parametric re-solve; `C3` producer | `HALT` | `T2` (DA curve monotonicity) |
| `INV-P-10` | No sell intent is priced below its `shadowValue`; no buy intent above it. The quoting policy never trades through the Planner's own indifference price. A legitimate exception exists — covering a commitment at a loss is sometimes correct — but such an intent must carry the `CommitmentCover` tag. An untagged violation is blocked. | Quoting policy output, before `C3` seal; `L5` re-checks ex post | `warn`, **and the intent is blocked** | `T2`, `T4` (trade-through check) |

---

## 6. `INV-X-*` — Execution boundary

Sources: `C4` §7, `C3` §2.

| ID | Statement | Where checked | Severity | Test level |
|---|---|---|---|---|
| `INV-X-01` | Every `fillId` references an `intentId` that was submitted in this run. A fill with no matching intent is a phantom fill and means the adapter or the ledger has lost identity. | `C4` consumer (`L5`) | `HALT` | `T1`, `T6` (ledger tampering) |
| `INV-X-02` | Filled volume, summed across all fills for an intent, does not exceed the intended volume. | `C4` consumer | `HALT` | `T1`, `T2` |
| `INV-X-03` | Energy balance holds within meter tolerance: `poiImport − poiExport = load − pv + chargeEnergy − dischargeEnergy`, per slot. This is the POI bridge (`00-overview/02-conventions.md` §1) evaluated on realised data. | `L5`, per slot | `warn` while provisional; `HALT` when `isFinal` | `T1`, `T2` (conservation) |
| `INV-X-04` | Execution does not consume `shadowValue` or `urgency`. They cross `C3` for settlement attribution only. Enforced structurally: the adapter interface exposes a projection of `ExecutionIntent` that omits both fields, so Execution cannot read them even by accident. | Structural — adapter projection type | `HALT` if the projection is bypassed | `T0` (compile-fail), `T1` |
| `INV-X-05` | `socMeasured` is consistent with `batteryChargeEnergy`, `batteryDischargeEnergy` and the declared efficiencies, within tolerance. Persistent divergence is the earliest available signal that the efficiency or SOH model has drifted, and it feeds the `modelError` bucket in `C5` §6. | `L5`, per slot and as a rolling statistic | `warn`, escalating to `alert` on persistent drift | `T2`, `T4` |
| `INV-X-06` | Every reserve award in `C4` has a corresponding `Confirmed` entry in the commitment ledger by the end of the tick. | `L5` at `C5` write | `HALT` | `T1`, `T2`, `T6` |

---

## 7. `INV-S-*` — Settlement and state

Sources: `C5` §9, `C4` §7 (`INV-S-04` is mirrored there deliberately).

| ID | Statement | Where checked | Severity | Test level |
|---|---|---|---|---|
| `INV-S-01` | `realisedPeak` is non-decreasing within an accounting period and resets **exactly** at the local-calendar period boundary, which is a local-midnight boundary and therefore not a fixed UTC offset across the year (`INV-T-02`). | `L5` at `C5` write; `L0` on read | `HALT` | `T1`, `T2`, `T0` (calendar) |
| `INV-S-02` | `realisedByEffect`, together with `unexplained`, sums to total realised P&L. The `unexplained` bucket is mandatory and must never be absorbed silently into another effect. | `L5` | `HALT` | `T1`, `T2` (attribution completeness) |
| `INV-S-03` | The four error buckets — `forecastError`, `modelError`, `optimalityGap`, `executionSlippage` — sum to `(planned − realised)` within tolerance; the residual is `unexplained`. | `L5`, after the counterfactual re-runs | `warn` | `T2` (zero-gap test), `T5` |
| `INV-S-04` | `deliveryShortfall = 0` for every slot. A reserve delivery failure is a prequalification risk, not an accounting item. Mirrored at `C4` §7 because it must be caught the moment the outcome arrives, not only at the state write. | `L5` on `C4` receipt, and again at `C5` write | `alert` **and** `HALT` | `T2`, `T6` |
| `INV-S-05` | Every `Confirmed` commitment carries a `feasibilityRequirement` where the product requires one. A confirmed reserve award with no SOC corridor is an obligation the Planner cannot see. | `L5` at `C5` write; `L3` on ledger read | `HALT` | `T1`, `T2`, `T6` (ledger tampering) |
| `INV-S-06` | `fullLoadHours = annualEnergyKwh / annualPeakKw` within tolerance. Battery operation moves both numerator and denominator (ADR-011), so a stale or inconsistent pair silently mis-states qualification. | `L5` at `C5` write | `HALT` | `T1`, `T2` |
| `INV-S-07` | `unexplainedRatio` is below the configured threshold. A rising ratio is the single best early warning that a term definition has drifted between Valuation and Settlement. | `L5`, per settlement period | `warn`, escalating to `alert` | `T2`, `T4` |
| `INV-S-08` | State is never written for a slot earlier than the previous update's `effectiveFrom`, except as an explicit `revisionOf`. Restatements are new artefacts referencing the old one, never in-place edits (ADR-004). | `L0` write path | `HALT` | `T1`, `T2` (restatement idempotency) |

---

## 8. `INV-T-*` — Time and calendar

`00-overview/02-conventions.md` §4.2 names this family and defers the statements
to this register. They are written in full here. Sources: ADR-002, ADR-013,
`00-overview/02-conventions.md` §4.

| ID | Statement | Where checked | Severity | Test level |
|---|---|---|---|---|
| `INV-T-01` | *(consolidated)* The `CivilCalendar` service is the **only** component permitted to map `SlotId ↔ (local date, local time, tariff window)`. No other component performs a timezone conversion, constructs a local time, or derives an HLZF membership. Enforced by an analyzer rule banning timezone APIs outside the calendar assembly, and by the absence of any local-time field in any contract (`00-overview/02-conventions.md` §4.1). | Analyzer at build; contract schema check | `HALT` (build failure at the analyzer) | `T0`, `T1` |
| `INV-T-02` | *(consolidated)* No computation assumes a fixed number of slots per civil day. A civil day has **92, 96 or 100** quarter-hour slots depending on DST transitions, and every per-civil-day span, accounting-period boundary and HLZF window is derived from the calendar rather than from arithmetic on 96. Any code that assumes 96 is wrong twice a year. Checked as: every calendar-derived span is obtained through `CivilCalendar`, and boundary spans are asserted against the calendar in the validator. | `CivilCalendar`; peak-accounting boundary logic in `L5`; `EpigraphTerm.overSlots` construction in `L2` | `HALT` | `T0`, `T1`, `T2` (DST fixtures) |
| `INV-T-03` | *(consolidated)* Local-time ambiguity is resolved structurally, never heuristically. On the autumn transition the repeated local hour is distinguished only by UTC offset, and the calendar returns both occurrences distinctly; on the spring transition the nonexistent local hour is never produced by any mapping. Every calendar-dependent test includes both DST transition days as fixtures, and the canonical golden window (`T0` §7) contains one of them. | `CivilCalendar`; the `T2` DST fixture suite | `HALT` | `T0`, `T2`, `T3` |
| `INV-T-04` | *(consolidated)* The IANA tzdata version and the `MarketCalendar` artefact hash are pinned and recorded in the run manifest, and are replayed with the backtest. A backtest of 2024 uses the 2024 calendar and the tzdata release in force, not today's. tzdata is itself versioned data and a change to it is a manifest change that forces a reviewed golden re-baseline (`T3` §8). | Run manifest assembly; `T3` replay | `HALT` if absent or unpinned | `T3`, `T6` (stale-artefact audit) |

### 8.1 Newly allocated

One invariant is stated in ADR-002 without an ID. It is allocated here and
flagged as new so that the source ADR can be annotated on its next edit.

| ID | Statement | Where checked | Severity | Test level |
|---|---|---|---|---|
| `INV-T-05` | **(new)** The `MarketCalendar` artefact is internally conformant: gate times are strictly monotone within a product and a day; product blocks do not overlap; every delivery slot in the artefact's coverage is covered by exactly **one** product block; tick and lot sizes are positive; the HLZF table covers the whole accounting year for every DSO in scope (ADR-002, consequences). | Artefact build, and again at load | `HALT` | `T1` |

---

## 9. Contract conformance — what `T1` actually runs

The register above is the *what*. `T1` is the level that proves the checks are
wired up and that the payloads satisfy them.

### 9.1 Per-seam conformance suite

Run for `C1`, `C2`, `C3`, `C4`, `C5` and `StateSnapshot`, against both
hand-built fixtures and recorded seams.

| Check | Assertion |
|---|---|
| Schema | The payload's field set equals the contract's field table exactly. Extra fields fail; missing non-nullable fields fail (`INV-G-09`). |
| Types and units | Every field is the declared typed quantity with the declared unit (`INV-G-02`). |
| Cardinality | `1`, `[H]`, `[S,H]`, `[B]`, `0..1` as declared; array lengths against `horizon`/`scenarioCount` (`INV-G-06`, `INV-G-07`). |
| Ranges | Elementwise, every element (`INV-G-10`). A range test that checks only `array[0]` is the bug this invariant exists for. |
| No-NaN | Elementwise across every numeric array (`INV-G-01`). |
| Nullability | Every `Null = yes` field has a declared `Default` or a documented escalation (`C0` §2). |
| Hashing | `contentHash` recomputes; `inputHashes` present and non-empty (`INV-G-04`). |
| Versioning | Known version accepted; unknown major version rejected cleanly; additive minor version accepted by an older consumer with the default applied (`C0` §5). |
| Round trip | Binary and JSON round-trip to an identical payload and an identical `contentHash`. |
| Double validation | The producer-side and consumer-side validators reach the same verdict on every fixture. A disagreement is itself a failure. |

### 9.2 Unit-change conformance

`C0` §5: a unit change is **always** a major version bump. `T1` asserts this
mechanically by comparing the current contract's unit annotations against the
previous released version's; any changed unit under an unchanged major version
fails the build. A field silently moving from `EUR/MWh` to `EUR/kWh` is the
archetypal catastrophic change and it is cheap to make impossible.

### 9.3 Backend agreement

ADR-001: `IOptimizationBackend` must be designed against the union of what the
ADR-010 tiers need. `T1` therefore includes a backend conformance suite.

| Check | Assertion |
|---|---|
| Objective agreement | The same `MilpModel` solved by two backends agrees on objective value within relative `1e-6` or the MIP gap tolerance, whichever is larger |
| Feasibility agreement | Both backends classify the model identically: optimal, infeasible, unbounded, or gap-limited |
| Shape support | Every term shape of ADR-008 — `LinearTerm`, `PwlTerm` (each curvature/sense combination), `EpigraphTerm`, `BoundTerm`, `CouplingConstraint` (each `kind`) — is translated by every backend, with a declared failure for anything unsupported rather than a silent relaxation |
| Binary count | The binaries actually built match `problemClassHint.binaryCount` (`INV-V-14`) on every backend |
| LP-relaxation backend | Present and exercised, because it is required regardless for the Tier-2 bound (ADR-010, ADR-015 OPEN-1) |
| Determinism settings | Each backend's determinism configuration is expressible and recorded in the manifest; a backend that cannot be made deterministic under parallelism is exercised single-threaded in the reproducibility configuration (ADR-013) |

### 9.4 Calendar artefact conformance

`INV-T-05`, run at artefact build and at load, on every calendar version in the
backtest corpus — including versions that span a regulatory change, because a
backtest across a rule change must see the rules that applied at the time
(ADR-002).

---

## 10. Reserved and unallocated IDs

`INV-V-08`, `INV-V-09`, `INV-V-10` and `INV-P-08` are **not allocated** by any
document in this specification. They are recorded here as reserved rather than
recycled, because reusing an ID that once appeared in a review comment, a commit
message or an alert history is a reliable way to make an incident timeline
unreadable.

A new Valuation or Planner invariant takes the next free number above the
highest allocated in its family — `INV-V-17`, `INV-P-11` — and the reserved IDs
stay empty permanently.
