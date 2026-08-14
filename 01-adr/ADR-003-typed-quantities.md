# ADR-003 — Typed quantities and two explicit reference frames

**Status:** Accepted **Reversibility:** Expensive once written as `double`

## Context

Energy optimisation code is dominated by two silent bug classes: sign-frame
confusion (battery discharge positive vs. POI import positive) and unit
confusion (kW/MW, EUR/MWh vs EUR/kWh, SOC in kWh vs percent). Both produce
plausible numbers. Neither throws. Both survive code review. In a system where
the peak term is a `max` and the reserve term is a curve, a sign error does not
merely scale the answer — it inverts the strategy.

The problem is sharpened here because the two frames genuinely coexist: markets
price the battery's export, while §19 StromNEV prices the site's import.

## Decision

**Quantities are distinct value types, not `double`.** Implemented as readonly
record structs wrapping a `double`, with no implicit conversions between frames
or units, and arithmetic defined only where it is dimensionally meaningful.

Minimum set: `BatteryPowerKw`, `PoiPowerKw`, `LoadKw`, `PvKw`, `ReserveMw`,
`EnergyKwh`, `EnergyPrice` (EUR/MWh), `CapacityPrice` (EUR/MW/h), `PeakPrice`
(EUR/kW/period), `Money` (EUR), `Efficiency`, `SocFraction`, `SlotId`,
`SlotSpan`.

**Exactly one sanctioned conversion between frames**, in one place:
`p_poi = load − pv − p_batt` (`00-overview/02-conventions.md` §1).

**Exactly one kW↔MW conversion per direction**, at the market adapter.

## Consequences

- The bridge between frames becomes a single, heavily tested function rather
  than a sign flip scattered through the code.
- Serialisation must handle the wrappers; contracts declare the underlying
  primitive plus a `unit` field so that a mismatch is a schema failure, not a
  runtime surprise.
- Some ceremony in arithmetic-heavy inner loops. Mitigation: the hot numeric
  kernels (scenario reduction, PWL evaluation) operate on raw
  `ReadOnlySpan<double>` inside a typed façade — the type safety is at the
  boundary where errors actually occur, not inside a loop the JIT must vectorise.
- `INV-G-02`: no contract field is a bare `double` without a declared unit.

## Rejected

- **Naming discipline instead of types** (`powerKw_battery`). Relies on
  reviewers, and does not survive refactoring or agent-authored code.
- **Units library.** Generality costs allocation and readability; the fixed set
  above is small and closed.
- **A single signed POI-frame variable everywhere.** Makes market-facing code
  read backwards and makes the efficiency split (`00-overview/02-conventions.md`
  §3) awkward.
