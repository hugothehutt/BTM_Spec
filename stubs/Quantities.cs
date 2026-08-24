// -----------------------------------------------------------------------------
//  Flexbid.Btm — Quantities
//
//  Normative source: 00-overview/02-conventions.md §1 (signs), §2 (units),
//                    §3 (efficiency), §4.1 (time grid), §5.2 (naming),
//                    §6 (numeric policy).
//
//  SIGNATURES ONLY. This file is part of the frozen contract surface and is
//  committed before any implementation exists (05-implementation/P0, W0).
//
//  This file used to hold twelve wrapper structs — BatteryPowerKw, PoiPowerKw,
//  LoadKw, PvKw, ReserveMw, EnergyKwh, SocFraction, EnergyPrice, CapacityPrice,
//  PeakPrice, Money, Efficiency — one per dimensioned quantity, under ADR-003.
//  ADR-003 was deleted by the conventions audit. Dimensioned quantities are now
//  plain `double`, and the unit and frame live in the identifier instead:
//
//      * unit suffix, mandatory: Mw, Mwh, EurPerMwh, EurPerMw, EurPerMwH, Eur
//      * frame token, mandatory for power and energy: pBatt*/soc* (battery),
//        pPoi* (POI), the product's own name (market), load*/pv*/aux* (site),
//        mtd* and the MiSpel register names (delineation)
//
//  INV-G-02 checks both mechanically, off the contract field tables, in
//  07-verification/check_units.py. Nothing is enforced by review.
//
//  Two types survive, because neither carries a unit in the sense above: SlotId
//  is an index and SlotSpan is a count. Replacing them with `long` and `int`
//  would buy nothing on units and would lose the DST-safe arithmetic that
//  conventions §4 depends on.
//
//  Two things are deliberately gone rather than ported:
//
//    * MarketUnits, which held the only sanctioned kW<->MW conversion in each
//      direction. There is no kW and therefore no seam. Normalisation to MW/MWh
//      happens once, at dataload, alongside UTC (conventions §2, §4.1). Any
//      factor of 1000 in this codebase is now a defect.
//    * EnergyKwh.At(EnergyPrice), which existed to hide the kWh->MWh factor
//      inside a price conversion. Energy is MWh and price is EUR/MWh, so the
//      product is EUR and needs no helper.
// -----------------------------------------------------------------------------

#nullable enable

using System;

namespace Flexbid.Btm.Contracts;

// =============================================================================
//  1. Time
// =============================================================================

/// <summary>
/// A quarter-hour slot. <b>Unit: count of 15-minute slots since the Unix epoch,
/// UTC.</b> Monotone, gap-free, DST-independent (conventions §4.1).
/// </summary>
/// <remarks>
/// There are no local-time fields in any contract. Mapping
/// <c>SlotId ↔ (local date, local time, tariff window)</c> is the exclusive
/// privilege of the <c>CivilCalendar</c> service, backed by a pinned IANA tzdata
/// version recorded in the run manifest (conventions §4.2, ADR-013).
/// A civil day has 92, 96 or 100 slots; code that assumes 96 is wrong twice a
/// year (INV-T-01 … INV-T-04).
/// </remarks>
public readonly record struct SlotId(long Value) : IComparable<SlotId>
{
    public int CompareTo(SlotId other) => throw new NotImplementedException();
    public static SlotId operator +(SlotId t, SlotSpan span) => throw new NotImplementedException();
    public static SlotId operator -(SlotId t, SlotSpan span) => throw new NotImplementedException();
    public static SlotSpan operator -(SlotId a, SlotId b) => throw new NotImplementedException();
    public static bool operator <(SlotId a, SlotId b) => throw new NotImplementedException();
    public static bool operator >(SlotId a, SlotId b) => throw new NotImplementedException();
    public static bool operator <=(SlotId a, SlotId b) => throw new NotImplementedException();
    public static bool operator >=(SlotId a, SlotId b) => throw new NotImplementedException();
}

/// <summary>
/// A duration. <b>Unit: count of 15-minute slots.</b> Never seconds in business
/// logic (conventions §2). Horizons (<c>H_plan</c>, <c>H_hot</c>), aFRR block
/// lengths, the prequalification sustain duration <c>D</c> and every
/// <c>MaxStale</c> threshold are expressed in this type.
/// </summary>
public readonly record struct SlotSpan(int Slots)
{
    public static SlotSpan Zero => new(0);
    public static SlotSpan operator +(SlotSpan a, SlotSpan b) => throw new NotImplementedException();
    public static SlotSpan operator -(SlotSpan a, SlotSpan b) => throw new NotImplementedException();

    /// <summary>
    /// Duration in hours, for converting an EUR/MW/h rate into EUR. The only
    /// legitimate numeric conversion left in the contract surface, and the reason
    /// a bare <c>/ 4.0</c> or <c>* 0.25</c> is a defect: a civil day has 92, 96 or
    /// 100 slots, so the slot-to-hour factor is a property of the span, not a
    /// constant (conventions §4.3).
    /// </summary>
    public double Hours => throw new NotImplementedException();
}

// =============================================================================
//  2. The single sanctioned frame bridge
// =============================================================================

/// <summary>
/// The one place in the codebase where the battery frame and the POI frame meet.
/// </summary>
/// <remarks>
/// Market quantities are sign-aligned with the battery frame (conventions §1), so
/// this is the <b>only sign flip in the system</b>.
/// </remarks>
public static class PowerFrames
{
    /// <summary>
    /// <b>THE ONLY SANCTIONED CONVERSION BETWEEN THE BATTERY FRAME AND THE POI
    /// FRAME.</b>
    /// <c>p_poi[t] = load[t] − pv_out[t] − p_batt[t]</c>, all MW, slot-average
    /// (<c>00-overview/02-conventions.md</c> §1).
    /// </summary>
    /// <remarks>
    /// <paramref name="pvOutMw"/> is <b>post-curtailment</b>:
    /// <c>pv_out[t] = Σ_k (pv_avail[t,k] − q[t,k])</c> with
    /// <c>0 ≤ q[t,k] ≤ pv_avail[t,k]</c> (ADR-016). Passing available PV here
    /// instead of PV out is the specific defect the audit found restated at four
    /// sites: it silently asserts that curtailment is free and never chosen.
    /// <para>
    /// No other code — no view, no term, no planner constraint builder, no
    /// adapter, no test helper — may compute a POI-frame power from a
    /// battery-frame power by any other route. Losses are inside
    /// <paramref name="pBattMw"/> at the AC terminal (conventions §3), so this
    /// identity holds exactly at that terminal and carries no η of its own —
    /// which is what fixes the efficiency boundary at the inverter's grid side
    /// rather than at the cell.
    /// </para>
    /// This function is a review checkpoint: a second implementation of this
    /// arithmetic anywhere in the tree is a defect, not a convenience.
    /// </remarks>
    public static double PoiFromSite(double loadMw, double pvOutMw, double pBattMw)
        => throw new NotImplementedException();
}

// =============================================================================
//  3. Comparison tolerances — one file, referenced, never re-typed
// =============================================================================

/// <summary>
/// Absolute comparison tolerances (<c>00-overview/02-conventions.md</c> §6).
/// One number across money, power and energy, each in the quantity's own unit.
/// Referenced, never re-typed at a call site.
/// </summary>
public static class Tolerances
{
    /// <summary>Money comparison tolerance. Unit: EUR.</summary>
    public const double MoneyEur = 1e-6;

    /// <summary>Power comparison tolerance. Unit: MW.</summary>
    public const double PowerMw = 1e-6;

    /// <summary>Energy comparison tolerance. Unit: MWh.</summary>
    public const double EnergyMwh = 1e-6;

    /// <summary>Scenario weight normalisation tolerance (INV-D-06).</summary>
    public const double ScenarioWeightSum = 1e-9;
}
