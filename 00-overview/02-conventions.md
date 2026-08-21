# 02 — Conventions: Units, Signs, Time, Naming

Normative and non-negotiable. Every contract, layer and test depends on these.
Ambiguity here is the single most productive source of silent errors in energy
optimisation systems, and it is almost always a sign convention or a DST bug.

---

## 1. Sign conventions

There are **two distinct reference frames** and they must never be conflated.
The engine uses both, and the type system separates them.

### Battery frame (asset-centric)

```
p_batt > 0   →  DISCHARGE (battery delivers energy)
p_batt < 0   →  CHARGE   (battery absorbs energy)
```

Rationale: aligns with the market convention that a positive traded quantity is
a sale. Every market-facing quantity therefore shares the sign of `p_batt`.

### POI frame (site-centric)

```
p_poi > 0   →  IMPORT from grid
p_poi < 0   →  EXPORT to grid (feed-in)
```

Rationale: the Leistungspreis, §19(2) StromNEV and all network charges are
levied on **import** peak. The quantity the tariff measures must have the sign
that makes "peak" a maximum rather than a minimum.

### The bridge

```
p_poi[t] = load[t] − Σ_k ( pv_avail[t,k] − q[t,k] ) − p_batt[t]     (kW, slot-average)
```

where `pv_avail` is a **belief** about available generation and `q ≥ 0` is
**curtailed** generation, a decision (ADR-016). Writing the bridge without `q`
makes the generation allocation an equality over non-negative terms, which is
infeasible whenever available PV exceeds load plus charge headroom plus the
export limit — an ordinary condition, not an edge case.

Losses are inside `p_batt`: see §3.

**Enforcement.** These are distinct C# value types, not `double`:
`BatteryPowerKw`, `PoiPowerKw`, `LoadKw`, `PvKw`. They do not implicitly convert.
The bridge above is the only sanctioned conversion and lives in exactly one
place. This costs a little ceremony and eliminates an entire bug class.

---

## 2. Units

| Quantity | Unit | Type | Note |
|---|---|---|---|
| Power | kW | `*PowerKw` | Site scale is kW; aFRR is quoted in MW — convert at the market adapter only |
| Energy | kWh | `EnergyKwh` | |
| Reserve capacity | MW | `ReserveMw` | Market unit; never mixed with `kW` types |
| Price, energy | EUR/MWh | `EnergyPrice` | Market convention. Not EUR/kWh. |
| Price, capacity (reserve) | EUR/MW/h | `CapacityPrice` | Per hour of the product block |
| Price, network peak | EUR/kW/period | `PeakPrice` | Period = year or month per regime |
| Tariff components | EUR/MWh | `EnergyPrice` | Levies, taxes, surcharges normalised to EUR/MWh |
| SOC | kWh | `EnergyKwh` | Absolute, never percent, internally |
| SOC (display) | fraction 0..1 | `SocFraction` | Presentation only, never in the objective |
| Efficiency | fraction 0..1 | `Efficiency` | One-way; round-trip is η_c · η_d |
| Duration | slot count (int) | `SlotSpan` | Never seconds in business logic |

**Rule:** the objective function is denominated in **EUR** and nothing else.
Every term converts to EUR at its own boundary. A term that emits anything else
is a contract violation.

**Rule:** kW↔MW conversion happens only at market adapters and is a typed
operation. There is exactly one `1000.0` in the codebase per direction.

---

## 3. Efficiency and loss placement

Losses are charged **at the battery terminal**, one-way, on each direction:

```
soc[t+1] = soc[t] + Δt · ( η_c · p_charge[t] − p_discharge[t] / η_d )
p_batt[t] = p_discharge[t] − p_charge[t]
p_charge[t] ≥ 0,  p_discharge[t] ≥ 0
```

Charge and discharge are separate non-negative variables. This is required for
correctness whenever η < 1: a single signed variable lets the optimiser
"round-trip for free" through the loss term.

**Simultaneity.** With η_c·η_d < 1 and non-negative energy prices, simultaneous
charge and discharge is never optimal, so the complementarity constraint is
usually redundant and can be omitted. It is **not** redundant when negative
prices, an aFRR activation obligation, or a peak-driven incentive to import make
burning energy profitable. Policy: omit the binary by default, enable it under a
configuration flag, and run an invariant monitor (`INV-P-07`) that alerts
whenever a solution contains simultaneous charge and discharge above tolerance.
This gives LP-speed in the common case and correctness detection in the rare one.

**Auxiliary load** (HVAC, BMS, standby) is modelled explicitly as a `LoadKw`
component, not folded into η, so it appears in the POI balance and therefore in
the peak.

---

## 4. Time

### 4.1 The grid

- Canonical resolution: **15 minutes** (the market time unit).
- `SlotId` is an `int64` count of quarter-hours since the Unix epoch, **UTC**.
  It is monotone, gap-free, and DST-independent.
- All internal timestamps are UTC. There are no local-time fields in any
  contract.

### 4.2 The local calendar problem

Tariff logic is *not* UTC. Hochlastzeitfenster, monthly and annual peak
accounting, and §19(2) qualification are defined on the **Europe/Berlin civil
calendar**. Consequences that must be handled, not discovered:

- A civil day has **92, 96, or 100 quarter-hour slots** depending on DST
  transitions. Any code that assumes 96 is wrong twice a year.
- The DST spring-forward day has a nonexistent local hour; autumn has a repeated
  local hour. Repeated local times are distinguished only by UTC offset.
- Month boundaries for peak accounting are local-midnight boundaries, which are
  not fixed UTC offsets across the year.

**Design:** a `CivilCalendar` service is the only component permitted to map
`SlotId ↔ (local date, local time, tariff window)`. It is backed by a pinned IANA
tzdata version recorded in the run manifest, because tzdata itself changes and a
backtest must be reproducible. Every calendar-dependent test includes both DST
transition days as fixtures. This is `INV-T-01` … `INV-T-04`.

### 4.3 Horizon

- `H_plan` — the Planner's optimisation horizon, must span at least to the end of
  the current accounting period *in value terms*. It does not: it is truncated,
  and the truncation is priced by V(SOC) and by the peak state carried in L0.
  This is why the value function is not optional.
- `H_hot` — the Belief hot window, `≥ H_plan`. Bounds memory (see ADR-004).

### 4.4 Bitemporality

Every fact carries two times:

- **valid_time** — the slot the fact is *about*.
- **knowledge_time** — when the engine *learned* it.

A read is always `Get(series, valid_time, as_of: knowledge_time)`. There is no
API that returns a fact with `knowledge_time > as_of`. This is structural, not
disciplinary (ADR-004), and it is what makes backtests trustworthy.

---

## 5. Naming

| Concept | Convention | Example |
|---|---|---|
| Contract DTOs | `<Seam><Payload>` record | `ValuationBundle`, `OrderIntentSet` |
| Valuation views | `<Lever>View` | `PeakView`, `AfrrCapacityView` |
| Emitted terms | `<Shape>Term` | `PwlTerm`, `EpigraphTerm` |
| Invariants | `INV-<layer>-<nn>` | `INV-V-03` |
| Test levels | `T0` … `T6` | |
| Decisions | `ADR-nnn` | |
| Contracts | `C0` … `C5` | |
| Degradation modes | UPPERCASE | `DEFENSIVE` |

Layer letters for invariants: `D` Belief/data, `V` Valuation, `P` Planner,
`X` Execution boundary, `S` Settlement, `T` Time/calendar, `G` Global.

---

## 6. Numeric policy

- Storage and transport: `float32` for forecast ensembles and price series where
  the source precision does not justify more; `float64` for money, SOC, and
  anything entering the objective.
- **NaN is never a valid value in a contract.** Missing is expressed by an
  explicit quality/provenance field, never by a sentinel. `INV-G-01` asserts
  no NaN or infinity crosses any seam.
- Money comparisons use an absolute tolerance of `1e-6 EUR`; power `1e-6 kW`.
  These live in one constants file and are referenced, never re-typed.
- Rounding to market tick sizes and lot sizes happens **once**, at the order
  intent boundary (C3), and the rounded value is what Settlement reconciles
  against. Rounding earlier corrupts the optimality gap measurement.
