// -----------------------------------------------------------------------------
//  Flexbid.Btm — Quantities
//
//  Normative source: ADR-003 (typed quantities, two reference frames),
//                    00-overview/02-conventions.md §1 (signs), §2 (units),
//                    §3 (efficiency), §4.1 (time grid), §6 (numeric policy).
//
//  SIGNATURES ONLY. This file is part of the frozen contract surface and is
//  committed before any implementation exists (05-implementation/P0, W0).
//
//  Rules encoded here, restated because they are the whole point of the file:
//    * There are two reference frames — battery and POI — and they never
//      implicitly convert. INV-G-02.
//    * There is exactly ONE sanctioned frame bridge: PowerFrames.PoiFromSite.
//    * There is exactly ONE kW<->MW conversion per direction:
//      MarketUnits.ToReserveMw / MarketUnits.ToBatteryPowerKw.
//    * The objective is denominated in EUR and nothing else (conventions §2).
// -----------------------------------------------------------------------------

#nullable enable

using System;

namespace Flexbid.Btm;

// =============================================================================
//  1. Power — battery frame
// =============================================================================

/// <summary>
/// Battery terminal power. <b>Unit: kW.</b> <b>Frame: battery (asset-centric).</b>
/// Positive = DISCHARGE (battery delivers), negative = CHARGE (battery absorbs)
/// — <c>00-overview/02-conventions.md</c> §1.
/// </summary>
/// <remarks>
/// Does not convert to <see cref="PoiPowerKw"/> except through
/// <see cref="PowerFrames.PoiFromSite"/>. Losses are charged at this terminal,
/// one-way, per conventions §3; charge and discharge are separate non-negative
/// variables in the Planner (<c>pCharge</c>, <c>pDischarge</c>, C2 §2).
/// </remarks>
public readonly record struct BatteryPowerKw(double Value)
{
    public static BatteryPowerKw Zero => new(0.0);
    public static BatteryPowerKw operator +(BatteryPowerKw a, BatteryPowerKw b) => throw new NotImplementedException();
    public static BatteryPowerKw operator -(BatteryPowerKw a, BatteryPowerKw b) => throw new NotImplementedException();
    public static BatteryPowerKw operator -(BatteryPowerKw a) => throw new NotImplementedException();
    public static BatteryPowerKw operator *(BatteryPowerKw a, double k) => throw new NotImplementedException();

    /// <summary>Energy delivered over <paramref name="span"/> slots. Unit: kWh.</summary>
    public EnergyKwh OverSlots(SlotSpan span) => throw new NotImplementedException();
}

// =============================================================================
//  2. Power — POI frame
// =============================================================================

/// <summary>
/// Net power at the point of interconnection. <b>Unit: kW.</b>
/// <b>Frame: POI (site-centric).</b> Positive = IMPORT from grid,
/// negative = EXPORT (feed-in) — <c>00-overview/02-conventions.md</c> §1.
/// </summary>
/// <remarks>
/// This is the quantity the Leistungspreis, §19(2) StromNEV and every network
/// charge are levied on, which is why "peak" is a maximum in this frame and
/// not a minimum. It is the abscissa of the peak <c>EpigraphTerm</c> (C2 §3.3).
/// </remarks>
public readonly record struct PoiPowerKw(double Value)
{
    public static PoiPowerKw Zero => new(0.0);
    public static PoiPowerKw operator +(PoiPowerKw a, PoiPowerKw b) => throw new NotImplementedException();
    public static PoiPowerKw operator -(PoiPowerKw a, PoiPowerKw b) => throw new NotImplementedException();
    public static PoiPowerKw operator *(PoiPowerKw a, double k) => throw new NotImplementedException();
}

/// <summary>
/// Site electrical load, including explicitly modelled auxiliary load
/// (HVAC, BMS, standby). <b>Unit: kW.</b> <b>Frame: POI-consuming, always ≥ 0.</b>
/// </summary>
/// <remarks>
/// Auxiliary load is a <see cref="LoadKw"/> component and is deliberately NOT
/// folded into <see cref="Efficiency"/>, so that it appears in the POI balance
/// and therefore in the peak (conventions §3).
/// </remarks>
public readonly record struct LoadKw(double Value)
{
    public static LoadKw Zero => new(0.0);
    public static LoadKw operator +(LoadKw a, LoadKw b) => throw new NotImplementedException();
}

/// <summary>
/// On-site PV generation. <b>Unit: kW.</b> <b>Frame: POI-producing, always ≥ 0.</b>
/// Enters the bridge with a negative sign (conventions §1).
/// </summary>
public readonly record struct PvKw(double Value)
{
    public static PvKw Zero => new(0.0);
    public static PvKw operator +(PvKw a, PvKw b) => throw new NotImplementedException();
}

// =============================================================================
//  3. Reserve — market frame, market unit
// =============================================================================

/// <summary>
/// aFRR reserve capacity. <b>Unit: MW.</b> <b>Frame: market (sale positive).</b>
/// </summary>
/// <remarks>
/// Never mixed with the <c>*PowerKw</c> types (conventions §2). Site scale is kW;
/// aFRR is quoted in MW. The only permitted conversions are
/// <see cref="MarketUnits.ToReserveMw"/> and
/// <see cref="MarketUnits.ToBatteryPowerKw"/>, at the market adapter.
/// </remarks>
public readonly record struct ReserveMw(double Value)
{
    public static ReserveMw Zero => new(0.0);
    public static ReserveMw operator +(ReserveMw a, ReserveMw b) => throw new NotImplementedException();
    public static ReserveMw operator -(ReserveMw a, ReserveMw b) => throw new NotImplementedException();
    public static ReserveMw operator *(ReserveMw a, double k) => throw new NotImplementedException();
}

// =============================================================================
//  4. Energy and state of charge
// =============================================================================

/// <summary>
/// Energy. <b>Unit: kWh.</b> Also the internal representation of state of charge:
/// SOC is absolute kWh internally and never a percentage (conventions §2).
/// </summary>
public readonly record struct EnergyKwh(double Value)
{
    public static EnergyKwh Zero => new(0.0);
    public static EnergyKwh operator +(EnergyKwh a, EnergyKwh b) => throw new NotImplementedException();
    public static EnergyKwh operator -(EnergyKwh a, EnergyKwh b) => throw new NotImplementedException();
    public static EnergyKwh operator -(EnergyKwh a) => throw new NotImplementedException();
    public static EnergyKwh operator *(EnergyKwh a, double k) => throw new NotImplementedException();

    /// <summary>Value of this energy at an <see cref="EnergyPrice"/> (EUR/MWh). Unit: EUR.</summary>
    /// <remarks>Contains the kWh→MWh factor. This is a price conversion, not a
    /// power-unit conversion, and is therefore not governed by the single
    /// kW↔MW rule in <see cref="MarketUnits"/>.</remarks>
    public Money At(EnergyPrice price) => throw new NotImplementedException();
}

/// <summary>
/// State of charge as a fraction of usable capacity. <b>Unit: dimensionless, 0..1.</b>
/// <b>Presentation only</b> — never appears in the objective, never in a term
/// (conventions §2). The contract quantity for SOC is <see cref="EnergyKwh"/>.
/// </summary>
public readonly record struct SocFraction(double Value);

// =============================================================================
//  5. Prices and money
// =============================================================================

/// <summary>
/// Energy price. <b>Unit: EUR/MWh.</b> Market convention — <b>not</b> EUR/kWh.
/// </summary>
/// <remarks>
/// Tariff components (volumetric network charge, levies, taxes, surcharges) are
/// normalised to this unit (conventions §2). A field silently moving from
/// EUR/MWh to EUR/kWh is the archetypal catastrophic change and is always a
/// major version bump (C0 §5). Negative values are legal and material (C1 §4).
/// </remarks>
public readonly record struct EnergyPrice(double Value)
{
    public static EnergyPrice operator +(EnergyPrice a, EnergyPrice b) => throw new NotImplementedException();
    public static EnergyPrice operator -(EnergyPrice a, EnergyPrice b) => throw new NotImplementedException();
    public static EnergyPrice operator *(EnergyPrice a, double k) => throw new NotImplementedException();
}

/// <summary>
/// Reserve capacity price. <b>Unit: EUR/MW/h</b> — per hour of the product block.
/// </summary>
public readonly record struct CapacityPrice(double Value)
{
    public static CapacityPrice operator +(CapacityPrice a, CapacityPrice b) => throw new NotImplementedException();
    public static CapacityPrice operator *(CapacityPrice a, double k) => throw new NotImplementedException();
}

/// <summary>
/// Network peak (demand) charge. <b>Unit: EUR/kW/period</b>, where the period is
/// a year or a month depending on the active <see cref="TariffRegimeId"/>
/// (ADR-011).
/// </summary>
/// <remarks>
/// A horizon shorter than the accounting period must apply
/// <c>EpigraphTerm.prorationFactor</c> (C2 §3.3, INV-V-13); the remainder of the
/// period charge is priced by V (ADR-007).
/// </remarks>
public readonly record struct PeakPrice(double Value)
{
    public static PeakPrice operator *(PeakPrice a, double k) => throw new NotImplementedException();
}

/// <summary>
/// Money. <b>Unit: EUR.</b> The objective function is denominated in EUR and in
/// nothing else; a term that emits any other unit is a contract violation
/// (conventions §2).
/// </summary>
/// <remarks>Stored as <c>float64</c> per the numeric policy (conventions §6).
/// Compared with <see cref="Tolerances.MoneyEur"/>, never with <c>==</c>.</remarks>
public readonly record struct Money(double Value)
{
    public static Money Zero => new(0.0);
    public static Money operator +(Money a, Money b) => throw new NotImplementedException();
    public static Money operator -(Money a, Money b) => throw new NotImplementedException();
    public static Money operator -(Money a) => throw new NotImplementedException();
    public static Money operator *(Money a, double k) => throw new NotImplementedException();
}

// =============================================================================
//  6. Efficiency
// =============================================================================

/// <summary>
/// One-way conversion efficiency. <b>Unit: dimensionless, (0,1].</b>
/// Round-trip efficiency is <c>etaCharge · etaDischarge</c> and is never stored
/// as a single number (conventions §3, INV-D-09).
/// </summary>
/// <remarks>
/// Charged at the battery terminal on each direction separately:
/// <c>soc[t+1] = soc[t] + Δt·(η_c·p_charge[t] − p_discharge[t]/η_d)</c>.
/// Also used for <c>sohFraction</c> (C1 §2), which is dimensionally the same
/// kind of quantity — a bounded derating fraction.
/// </remarks>
public readonly record struct Efficiency(double Value)
{
    public static Efficiency operator *(Efficiency a, Efficiency b) => throw new NotImplementedException();
}

// =============================================================================
//  7. Time
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

    /// <summary>Duration in hours, for converting EUR/MW/h and EUR/MWh rates.</summary>
    public double Hours => throw new NotImplementedException();
}

// =============================================================================
//  8. The single sanctioned frame bridge
// =============================================================================

/// <summary>
/// The one place in the codebase where the battery frame and the POI frame meet.
/// </summary>
public static class PowerFrames
{
    /// <summary>
    /// <b>THE ONLY SANCTIONED CONVERSION BETWEEN THE BATTERY FRAME AND THE POI
    /// FRAME.</b> <c>p_poi[t] = load[t] − pv[t] − p_batt[t]</c>, all kW,
    /// slot-average (<c>00-overview/02-conventions.md</c> §1, ADR-003).
    /// </summary>
    /// <remarks>
    /// No other code — no view, no term, no planner constraint builder, no
    /// adapter, no test helper — may construct a <see cref="PoiPowerKw"/> from a
    /// <see cref="BatteryPowerKw"/> by any other route, and no implicit
    /// conversion exists to permit it. Losses are inside <paramref name="battery"/>
    /// (conventions §3), so this identity holds exactly at the terminal.
    /// This function is a review checkpoint: a second implementation of this
    /// arithmetic anywhere in the tree is a defect, not a convenience.
    /// </remarks>
    public static PoiPowerKw PoiFromSite(LoadKw load, PvKw pv, BatteryPowerKw battery)
        => throw new NotImplementedException();
}

/// <summary>
/// The one place in the codebase where kW and MW meet. Lives at the market
/// adapter boundary (conventions §2, ADR-003).
/// </summary>
public static class MarketUnits
{
    /// <summary>
    /// <b>The only kW→MW conversion.</b> There is exactly one <c>1000.0</c> in
    /// the codebase per direction; this is one of the two.
    /// </summary>
    public static ReserveMw ToReserveMw(BatteryPowerKw power) => throw new NotImplementedException();

    /// <summary>
    /// <b>The only MW→kW conversion.</b> Used when a reserve obligation is
    /// projected onto the battery power headroom constraints (ADR-010).
    /// </summary>
    public static BatteryPowerKw ToBatteryPowerKw(ReserveMw reserve) => throw new NotImplementedException();
}

// =============================================================================
//  9. Comparison tolerances — one file, referenced, never re-typed
// =============================================================================

/// <summary>
/// Absolute comparison tolerances (<c>00-overview/02-conventions.md</c> §6).
/// Referenced, never re-typed at a call site.
/// </summary>
public static class Tolerances
{
    /// <summary>Money comparison tolerance. Unit: EUR.</summary>
    public const double MoneyEur = 1e-6;

    /// <summary>Power comparison tolerance. Unit: kW.</summary>
    public const double PowerKw = 1e-6;

    /// <summary>Scenario weight normalisation tolerance (INV-D-06).</summary>
    public const double ScenarioWeightSum = 1e-9;
}
