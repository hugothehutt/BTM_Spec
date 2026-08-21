# ADR-011 — Tariff regime is a plug-in; qualification state is a state variable

**Status:** Accepted **Reversibility:** Expensive if PeakView hardcodes `max`

## Context

The obvious implementation of `PeakView` prices the maximum POI import over the
accounting period. That is correct for today's Leistungspreis and it is about to
stop being the only regime that matters.

Verified August 2026 (sources at the end):

- **§19(2) S.1 StromNEV — atypische Netznutzung.** The charge is based on the
  peak within published **Hochlastzeitfenster**, not the annual maximum. HLZF
  windows are published per DSO per year. The optimisation target is therefore a
  max over a *subset* of slots defined by a data table.
- **§19(2) S.2 StromNEV — stromintensive Netznutzung.** Qualification requires
  ≥ 7,000 full-load hours *and* > 10 GWh annual consumption at the metering
  point, with tiered reductions. Full-load hours are annual energy divided by
  annual peak power — so **battery operation moves both the numerator and the
  denominator**, and therefore moves qualification directly.

One structural consequence:

1. **§19(2) S.2 qualification is a cliff.** Falling below 7,000 full-load hours
   loses the reduction for the entire year. A myopic optimiser that shaves a peak
   in December can raise full-load hours and gain; one that discharges to capture
   a spread can lower annual consumption and lose far more than the spread. This
   is a discrete state with a large terminal value and it belongs in the value
   function, not in the per-tick objective.

## Decision

**`ITariffRegime` is a plug-in interface. `PeakView` selects a regime from
configuration and emits shapes accordingly.**

Regimes in scope:

| Regime | Objective shape | Notes |
|---|---|---|
| `AnnualLeistungspreis` | `EpigraphTerm` over all slots in the year, floored by realised peak | today's baseline |
| `MonthlyLeistungspreis` | `EpigraphTerm` per calendar month | |
| `AtypicalHlzf` (§19(2) S.1) | `EpigraphTerm` over HLZF slots only, from the calendar table | window table is data (ADR-002) |
| `IntensiveUse` (§19(2) S.2) | `EpigraphTerm` + discrete qualification state | cliff — see below |

**Regimes compose.** A site can be subject to more than one simultaneously (an
HLZF-based charge and a volumetric charge).
Each regime emits terms tagged with its own `EconomicEffect` (ADR-009), so the
composer's exclusivity check keeps them from overlapping.

**Qualification state is a state variable in L0 and a dimension of V.**
`V(SOC, peakState, qualState)` — ADR-007 — where `qualState` carries accumulated
annual energy, accumulated full-load hours, current HLZF exposure and a
projected qualification probability. The value function is fitted conditional on
the discrete qualification state, which is exactly how the cliff is represented
without breaking concavity in SOC.

**Cliff protection is a hard guard, not only an economic term.** When projected
qualification is within a configurable margin of a threshold, the engine raises a
`peakCritical` / `qualCritical` flag which (a) forces escalation to a higher
co-optimisation tier (ADR-010), and (b) applies a bound restricting actions that
would worsen the projection. Losing a year of network-charge reduction for a
day's spread is the single largest downside event in the BTM business case and it
must not be reachable through an approximation error.

## Consequences

- A regime change is a configuration date plus a new regime implementation, not a
  rewrite.
- Backtests spanning a regime change are honest, because the regime is selected
  from the versioned calendar (ADR-002).
- All numeric thresholds (7,000 h; 10 GWh; tier percentages; HLZF windows) are
  **configuration**, sourced from the current published tables per DSO and per
  year, never constants. Regulation in this area is actively moving.

## Rejected

- **Hardcode `max` over the month.** Cannot express HLZF and silently mis-prices
  the cliff.
- **Treat qualification as a post-hoc report.** Guarantees the engine will
  eventually trade through a cliff for a small gain.

## Sources

- [BNetzA — Individuelle Netzentgelte Strom gemäß § 19 StromNEV](https://www.bundesnetzagentur.de/DE/Beschlusskammern/BK04/BK4_71_NetzE/BK4_71_Ind_NetzE_Strom/BK4_Ind_NetzEntg_Strom.html)
- [BNetzA — Orientierungspunkte Speichernetzentgelte, Jan 2026 (PDF)](https://www.bundesnetzagentur.de/DE/Beschlusskammern/GBK/GBK_Termine/Downloads/2026/01_2026/30_01/Orientierungspunkte_Speichernetzentgelte.pdf?__blob=publicationFile&v=4)
- [EHA — Individuelle Netzentgelte nach § 19 StromNEV](https://www.eha.net/blog/details/stromnev-individuelle-netzentgelte.html)
- [Avacon Netz — Hochlastzeitfenster 2026 (PDF, example DSO table)](https://www.avacon-netz.de/content/dam/revu-global/avacon-netz/documents/netzentgelte-strom/2026/Hochlastzeitfenster_2026_reg.Methode.pdf)
