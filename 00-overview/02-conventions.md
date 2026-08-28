# 02 — Conventions: Units, Signs, Time, Naming

Normative and non-negotiable. Every contract, layer and test depends on these.
Ambiguity here is the single most productive source of silent errors in energy
optimisation systems, and it is almost always a sign convention or a DST bug.

---

## 1. Sign conventions

There are **three reference frames** and they must never be conflated.

### Battery frame (asset-centric)

```
p_batt > 0   →  DISCHARGE (battery delivers energy)
p_batt < 0   →  CHARGE   (battery absorbs energy)
```

### POI frame (site-centric)

```
p_poi > 0   →  IMPORT from grid
p_poi < 0   →  EXPORT to grid (feed-in)
```

Rationale: the Leistungspreis, §19(2) StromNEV and all network charges are
levied on **import** peak. The quantity the tariff measures must have the sign
that makes "peak" a maximum rather than a minimum.

### Market frame (position-centric)

```
market quantity > 0   →  SALE / delivery to the market
market quantity < 0   →  PURCHASE / offtake from the market
```

The market frame is **sign-aligned with the battery frame**: discharge is a
sale, and both are positive. Therefore **battery ↔ POI is the only sign flip in
the system**, and it happens in exactly one place (the bridge, below).

Most market quantities never carry a sign at all: direction rides an enum
(`OrderSide`) or a pair of non-negative variables (`rUp`/`rDn`,
`eAfrrUp`/`eAfrrDn`). The one signed market quantity is the imbalance position:

```
imbalanceVolumeMwh > 0   →  LONG  (delivered more than scheduled)
imbalanceVolumeMwh < 0   →  SHORT (delivered less than scheduled)
```

### The bridge

```
p_poi[t] = load[t] − pv_out[t] − p_batt[t]                    (MW, slot-average)
```

`pv_out` is **delivered** generation — post-curtailment, a decision:

```
pv_out[t] = Σ_k ( pv_avail[t,k] − q[t,k] )
0 ≤ q[t,k] ≤ pv_avail[t,k]
```

`pv_avail` is a **belief** about available generation; `q ≥ 0` is curtailed
generation, a decision (ADR-016). The bridge is written against `pv_out` and
nothing else, so curtailment appears in exactly one constraint and the bridge
stays a three-term identity that cannot be restated wrongly.

Curtailment is not optional in the model. Without `q`, the generation allocation
is an equality over non-negative terms and is infeasible whenever available PV
exceeds load plus charge headroom plus the export limit — an ordinary condition,
not an edge case.

Losses are inside `p_batt`: see §3.

**Enforcement.** Frame and unit are carried in the identifier and checked
mechanically — see §5.2 and `INV-G-02`. The bridge above is the only sanctioned
conversion between frames and lives in exactly one place.

---

## 2. Units

**Everything is MW and MWh.** Site scale, market scale and reserve scale share
one unit system, matching market convention. No smaller power or energy unit
appears anywhere in the specification, in any spelling, and there is therefore
no unit adapter and no factor of a thousand to misplace.

| Quantity | Unit | Note |
|---|---|---|
| Power | MW | All frames: battery, POI, market, reserve |
| Energy | MWh | |
| Price, energy | EUR/MWh | Market convention |
| Price, capacity (reserve) | EUR/MW/h | Per hour of the product block |
| Price, network peak | EUR/MW | The accounting period is a separate mandatory input, never a denominator |
| Marginal value of a delineation accumulator (`λ_j`) | EUR/MWh | All seven accumulators are energy sums — see §2.2 |
| Tariff components | EUR/MWh | Levies, taxes, surcharges normalised to EUR/MWh |
| SOC | MWh | Absolute, never percent, internally |
| SOC (display) | fraction, `[0,1]` | Presentation only, never in the objective |
| Efficiency | fraction, `(0,1]` | One-way; round-trip is η_c · η_d |
| Duration | slot count (int) | Never seconds in business logic |

Source data is not required to arrive in these units. **Normalisation to MW/MWh
happens at dataload**, in the same pass as UTC normalisation (§4.1), and it is
the only place a scale factor exists. A regulatory primitive is normalised with
everything else: the Leistungspreis, for instance, is quoted per year and is
carried as EUR/MW/a. What a primitive **means** is never rewritten to suit this
table — its definition, its accounting period and its slot set are the
regulation's — but the unit it is carried in always is, because a second unit
system is a defect this specification does not accept anywhere.

### 2.1 The accounting period is an input, not a dimension

`PeakPrice` is EUR/MW: a charge per MW of peak, for one accounting period. The
period (year or month, per regime) is a **mandatory explicit input** to the peak
term, carried as its own field. It is not a unit denominator.

This is what makes the objective's unit algebra close. The peak contribution is

```
peakPriceEurPerMw · zPeak · proration    (EUR/MW) · MW · [0,1]  =  EUR
```

with `proration` dimensionless and exactly one period in scope per term.

### 2.2 Marginal values of the delineation accumulators

The seven accumulators are `A_j`, `j ∈ {(3), (5), (6), (9), (11), (26), (29)}`
(ADR-017). Every one is a monthly sum of quarter-hour meter registers, so every
one is an **energy** in MWh. Consequently

```
λ_j = ∂V_del/∂A_j        is EUR/MWh for every j
λ_j · A_j                is (EUR/MWh) · MWh = EUR
```

There is no per-`j` unit family. A future accumulator that is not an energy may
not be added to `Σ_j λ_j · A_j` without declaring its own unit here.

### 2.3 Dimensionless fields

A dimensionless field declares its **kind** and a **closed range**, or it is not
a contract field. The kinds are:

| Kind | Range | Meaning |
|---|---|---|
| `fraction` | `[0,1]` | Part of a whole |
| `ratio` | declared per field | Quotient of two like quantities; may exceed 1 |
| `probability` | `[0,1]` | Measure of an event |
| `weight` | declared per field | Multiplier in an objective or a blend |

The Unit column of a contract field table carries `—` for these, and the range
constraint is mandatory. A dimensionless field with no declared range is a
violation of `INV-G-02`, not a field awaiting documentation.

---

## 3. Efficiency and loss placement

Losses are charged at the **AC terminal of the storage unit** — the grid side of
the inverter — one-way, on each direction:

```
soc[t+1] = soc[t] + Δt · ( η_c · p_charge[t] − p_discharge[t] / η_d )
p_batt[t] = p_discharge[t] − p_charge[t]
p_charge[t] ≥ 0,  p_discharge[t] ≥ 0
```

`η_c` is AC→cell and `η_d` is cell→AC, so `η_c · η_d` is an **AC-to-AC round
trip** — the quantity vendor guarantees and acceptance tests are quoted against,
so a calibrated number is directly comparable to a datasheet.

The AC boundary is forced, not chosen. The bridge (§1) subtracts `p_batt`
straight from POI power with no other loss term, and the energy balance
`INV-X-03` carries no efficiency at all. Both are exact only if `p_batt`,
`p_charge` and `p_discharge` are AC quantities. A DC-terminal reading would
leave the inverter loss unmodelled and bias every energy claim in the
specification.

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

**Auxiliary load** (HVAC, BMS, standby) is modelled explicitly as a load
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
- **Inputs are forced to UTC at dataload**, in the same pass as unit
  normalisation (§2). Nothing downstream of ingest handles a local timestamp.

A `SlotId` is therefore well-defined across the autumn DST transition without
special handling: the repeated local hour maps to two distinct `SlotId`s,
distinguished by UTC offset, and the nonexistent spring local hour maps to no
`SlotId` at all.

### 4.2 The local calendar problem

Tariff logic is *not* UTC. Hochlastzeitfenster, monthly and annual peak
accounting, and §19(2) qualification are defined on the **Europe/Berlin civil
calendar**. A civil boundary *is* a UTC instant, but it is not **derivable** as
one without tzdata. Consequences that must be handled, not discovered:

- A civil day has **92, 96, or 100 quarter-hour slots** depending on DST
  transitions. Any code that assumes 96 is wrong twice a year.
- Month and year boundaries for accounting are local-midnight boundaries, which
  are not fixed UTC offsets across the year.

**Design:** a `CivilCalendar` service is the only component permitted to resolve
a civil boundary or tariff window into a `SlotId`. It runs **at dataload**,
alongside UTC and unit normalisation, and emits `SlotId` boundaries that no
downstream component recomputes. It is backed by a pinned IANA tzdata version
recorded in the run manifest, because tzdata itself changes and a backtest must
be reproducible. Every calendar-dependent test includes both DST transition days
as fixtures. This is `INV-T-01` … `INV-T-04`.

**All accumulator resets are stated as the resolved UTC instant of the
Europe/Berlin local boundary** — monthly for the seven delineation accumulators
and the realised peak, annual for `fullLoadHours` and
`qualificationMarginHours`. A reset written against an unqualified "month
boundary" is a defect: the boundary is local, the stored instant is UTC, and the
resolution belongs to `CivilCalendar`.

### 4.3 Slot-counted state coordinates are DST-varying by construction

`slotsToMonthEnd` is a **true count of remaining slots**, not a proxy for
elapsed civil time. In a month containing a DST transition the same local
instant carries a coordinate four slots away from its non-DST equivalent, and
this is correct: the month genuinely has four more or fewer slots of opportunity
to act in, which is what the value function needs to see.

A DST month is therefore a **required fixture** in the value-function fitting
set, not an outlier to be smoothed. Normalising the coordinate to a civil
fraction would erase a real difference in available decisions.

### 4.4 Horizon

- `H_plan` — the Planner's optimisation horizon, must span at least to the end of
  the current accounting period *in value terms*. It does not: it is truncated,
  and the truncation is priced by V(SOC) and by the peak state carried in L0.
  This is why the value function is not optional.
- `H_hot` — the Belief hot window, `≥ H_plan`. Bounds memory (see ADR-004).

### 4.5 Bitemporality

Every fact carries two times:

- **valid_time** — the slot the fact is *about*.
- **knowledge_time** — when the engine *learned* it.

A read is always `Get(series, valid_time, as_of: knowledge_time)`. There is no
API that returns a fact with `knowledge_time > as_of`. This is structural, not
disciplinary (ADR-004), and it is what makes backtests trustworthy.

---

## 5. Naming

### 5.1 Structural names

| Concept | Convention | Example |
|---|---|---|
| Contract DTOs | `<Seam><Payload>` record | `ValuationBundle`, `OrderIntentSet` |
| Valuation views | `<Lever>View` | `PeakView`, `AfrrCapacityView` |
| Emitted terms | `<Shape>Term` | `PwlTerm`, `EpigraphTerm` |
| Invariants | `INV-<layer>-<nn>` | `INV-V-03` |
| Test levels | `T0` … `T6` | |
| Decisions | `ADR-nnn` | |
| Contracts | `C0` … `C6` | |
| Degradation modes | UPPERCASE | `DEFENSIVE` |

Layer letters for invariants: `D` Belief/data, `V` Valuation, `P` Planner,
`X` Execution boundary, `S` Settlement, `T` Time/calendar, `G` Global.

### 5.2 Quantity names carry unit and frame

Dimensioned quantities are plain numerics. Correctness is carried by the
identifier and enforced by a check, not by a type.

- **Unit suffix, mandatory.** Every dimensioned identifier ends in its unit:
  `Mw`, `Mwh`, `EurPerMwh`, `EurPerMw`, `EurPerMwH`, `Eur`.
  Examples: `pBattMw`, `socMwh`, `peakPriceEurPerMw`.
- **Frame token, mandatory for power and energy.** Every power or energy
  identifier names the frame it is measured in. There are **five** naming
  groups, and the extra two are not sign frames in the sense of §1 — they are
  quantities whose sign is fixed by construction, so §1's three frames do not
  reach them:

  | Group | Token | Example | Sign |
  |---|---|---|---|
  | Battery | `pBatt*`, `soc*` | `pBattMw`, `socMwh` | §1 battery frame |
  | POI | `pPoi*` | `pPoiRealisedPeakMw` | §1 POI frame |
  | Market | the product's own name | `rUpMw`, `imbalanceVolumeMwh` | §1 market frame |
  | Site | `load*`, `pv*`, `aux*` | `loadMw`, `pvAvailMw` | Non-negative by construction |
  | Delineation | `mtd*` and the MiSpel register names | `mtdStorageExportMwh`, `saldierungsfaehigMwh` | Non-negative accumulation |

  Site quantities are consumption and generation *before* the bridge assigns
  them to a frame; they are non-negative and the bridge's signs do the work
  (§1). Delineation accumulators are sign-defined by the register arithmetic of
  `03-mispel-reference.md`, never by battery or POI. A power or energy
  identifier naming none of the five is a violation.
- **Beliefs and decisions are distinguished.** `pvAvailMw` is a belief;
  `pvOutMw` is post-decision. A name that could be either is a defect.

`INV-G-02` enforces this: a field's suffix unit equals its declared unit, every
MW or MWh field carries one of the five frame tokens, and a dimensionless field
carries `—` plus a range (§2.3). It is a `HALT` schema check at serialisation and
deserialisation. In this repository the contract field tables are held to it by
review; the engine repository is where it becomes executable.

A field whose unit depends on another field's value has no suffix it can carry
and is therefore forbidden. Where one existed — an order volume that was MWh on
the energy markets and MW on `AfrrCapacity` — it is split into two exclusive
fields discriminated by `market` (C3 §2, C4 §2).

---

## 6. Numeric policy

- Storage and transport: `float32` for forecast ensembles and price series where
  the source precision does not justify more; `float64` for money, SOC, and
  anything entering the objective.
- **NaN is never a valid value in a contract.** Missing is expressed by an
  explicit quality/provenance field, never by a sentinel. `INV-G-01` asserts
  no NaN or infinity crosses any seam.
- Comparison tolerances are `1e-6` in the quantity's own unit: `1e-6 EUR`,
  `1e-6 MW`, `1e-6 MWh`. One number across money, power and energy. These live
  in one constants file and are referenced, never re-typed.
- Efficiency is in `(0,1]`. Zero is excluded: `η_d = 0` divides by zero in the
  SOC dynamics of §3.
- Rounding to market tick sizes and lot sizes happens **once**, at the order
  intent boundary (C3), and the rounded value is what Settlement reconciles
  against. Rounding earlier corrupts the optimality gap measurement.
