// -----------------------------------------------------------------------------
//  Flexbid.Btm.Contracts — C3: Planner → Execution
//
//  Payload: ExecutionIntent   Version: 3.0   Direction: L3 → L4
//  Normative source: 03-contracts/C3-planner-to-execution.md §1–§6,
//                    ADR-012 (intent, not routing; quoting is a separate policy),
//                    L3-planner.md §5 (position → orders), §7 (dispatch boundary).
//
//  Execution is the pre-existing market simulator, treated as an external system.
//  This contract is deliberately thin and carries INTENT ONLY.
//
//  SIGNATURES ONLY.
// -----------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;

namespace Flexbid.Btm.Contracts;

// =============================================================================
//  Price and volume carriers
// =============================================================================

// A limit price and a volume used to be carried by two discriminated-union
// structs, `OrderPrice` and `OrderVolume`, each tagging itself Energy or
// Capacity at runtime. Conventions §5.2 moved unit safety into the identifier,
// and a tag is not an identifier: the reader of `order.Price` still cannot see
// which unit they hold without following the tag. The union is therefore
// replaced by two exclusive nullable fields per quantity, named for their unit,
// with `Market` as the discriminator (C3 §2, C4 §2).
//
// Exactly one of each pair is populated:
//
//   Da, IdContinuous, AfrrEnergy -> LimitPriceEurPerMwh, VolumeMwh
//   AfrrCapacity                            -> LimitPriceEurPerMwH, VolumeMw
//
// Both populated, or the wrong one for the market, is a contract failure.
//
// Prices are <b>already rounded to the market tick size</b>: rounding happens
// exactly once, here at the order intent boundary, and the rounded value is what
// Settlement reconciles against (conventions §6, INV-P-04). Rounding earlier
// corrupts the optimality gap measurement.

/// <summary>Order validity (C3 §2). <see cref="ValidityKind.GtdUntil"/> carries
/// the expiry slot; the other kinds carry nothing.</summary>
public readonly record struct OrderValidity(ValidityKind Kind, SlotId? Until)
{
    public static OrderValidity Ioc => throw new NotImplementedException();
    public static OrderValidity Fok => throw new NotImplementedException();
    public static OrderValidity GtdUntil(SlotId slot) => throw new NotImplementedException();
    public static OrderValidity GtcUntilGate => throw new NotImplementedException();
}

// =============================================================================
//  §2 Order intent
// =============================================================================

/// <summary>
/// One order. <c>(market, product, slot, side, limitPriceEurPerMwh, volume, validity,
/// replacesId, tag)</c> — it says nothing about venue mechanics; the Execution
/// adapter translates intent into whatever the simulator or a live venue
/// requires (ADR-012).
/// </summary>
/// <remarks>
/// The Planner decides the <i>target position</i>; the quoting policy decides the
/// <i>orders</i> (ADR-012, L3 §5). The handoff is <see cref="ShadowValueEurPerMwh"/>: the
/// Planner's own indifference price, which gives the quoting policy an
/// economically meaningful bound — never quote worse than it, because at that
/// price the trade destroys value.
/// </remarks>
public sealed record OrderIntent
{
    /// <summary>Stable across replaces (C3 §2). The only key Settlement uses to
    /// link a fill back to an intent (C4 §2).</summary>
    public required string IntentId { get; init; }

    public required MarketId Market { get; init; }

    /// <summary>From the versioned market calendar (ADR-002).</summary>
    public required string ProductId { get; init; }

    /// <summary>Delivery period — a slot for energy products.</summary>
    public SlotId? Slot { get; init; }

    /// <summary>Delivery period — a block for reserve capacity products.</summary>
    public BlockId? Block { get; init; }

    /// <summary>Market frame: <c>Sell = discharge</c> (C3 §2).</summary>
    public required OrderSide Side { get; init; }

    /// <summary>Unit EUR/MWh. Energy markets only; null on
    /// <see cref="MarketId.AfrrCapacity"/>. Tick-aligned (INV-P-04).</summary>
    public double? LimitPriceEurPerMwh { get; init; }

    /// <summary>Unit EUR/MW/h. <see cref="MarketId.AfrrCapacity"/> only; null on
    /// the energy markets. Tick-aligned (INV-P-04).</summary>
    public double? LimitPriceEurPerMwH { get; init; }

    /// <summary>Unit MWh. Energy markets only. Lot-aligned, <c>&gt; 0</c>
    /// (INV-P-04).</summary>
    public double? VolumeMwh { get; init; }

    /// <summary>Unit MW of committed power. <see cref="MarketId.AfrrCapacity"/>
    /// only. Lot-aligned, <c>&gt; 0</c> (INV-P-04).</summary>
    public double? VolumeMw { get; init; }

    public required OrderValidity Validity { get; init; }

    /// <summary>Cancel/replace semantics. No two intents for the same product and
    /// slot on the same side may exist without a chain through this field
    /// (INV-P-03).</summary>
    public string? ReplacesIntentId { get; init; }

    /// <summary>
    /// The Planner's indifference price, in EUR/MWh. <b>Audit only — Execution
    /// must ignore it.</b>
    /// </summary>
    /// <remarks>
    /// Extracted as the dual on the position accounting constraint (L3 §5).
    /// It crosses the seam for <i>settlement attribution</i>, so that L5 can
    /// answer "did the quoting policy trade through indifference?" without the
    /// Planner and the quoting policy having a private side channel (C3 §2).
    /// INV-P-10: no sell intent priced below it, no buy intent priced above it —
    /// a warning rather than a halt, because covering a commitment at a loss is
    /// sometimes correct, but such intents must carry
    /// <see cref="StrategyTag.CommitmentCover"/> and an untagged violation is
    /// blocked.
    /// <para>
    /// That Execution does not read this field is INV-X-04, and it is enforced
    /// structurally, not by discipline: the adapter consumes
    /// <see cref="OrderIntentProjection"/>, which does not have it.
    /// </para>
    /// </remarks>
    public double? ShadowValueEurPerMwh { get; init; }

    /// <summary>Unit EUR/MW/h. <see cref="MarketId.AfrrCapacity"/> only, and
    /// exclusive with <see cref="ShadowValueEurPerMwh"/> — the same economic
    /// object in the two units the markets are denominated in, which INV-G-02
    /// forbids one identifier from carrying.
    /// <para><c>μ[b]</c>, the reservation price of a MW of headroom, taken from
    /// the coupling dual (ADR-010). <see cref="IReserveBidPolicy"/> marks up from
    /// it and INV-P-11 forbids bidding below it — which bites harder than
    /// INV-P-10, because capacity is <b>pay-as-bid</b> (ADR-018): a bid below
    /// <c>μ</c> is a certain loss on every MW awarded, not a thin margin.</para>
    /// <para><b>Audit only.</b> Absent from
    /// <see cref="OrderIntentProjection"/> like its energy-market twin
    /// (INV-X-04).</para></summary>
    public double? ReserveShadowValueEurPerMwH { get; init; }

    /// <summary>Range <c>[0,1]</c>. Objective degradation if the position is not
    /// reached, normalised. <b>Audit only</b>, same treatment as
    /// <see cref="ShadowValueEurPerMwh"/> (INV-X-04).</summary>
    public required double Urgency { get; init; }

    /// <summary>For settlement attribution only; it does not affect routing
    /// (C3 §6). In <c>SAFE</c>, only <see cref="StrategyTag.CommitmentCover"/>
    /// and cancel intents may be present (INV-P-05).</summary>
    public required StrategyTag Tag { get; init; }
}

// =============================================================================
//  §3 Bid curves (DA)
// =============================================================================

/// <summary>One point of a DA bid curve, price-ordered (C3 §3).</summary>
public readonly record struct DaCurvePoint(double PriceEurPerMwh, double QuantityMwh);

/// <summary>
/// A day-ahead bid curve. <b>DA requires a monotone schedule, not a point
/// order</b> (C3 §3, ADR-012).
/// </summary>
/// <remarks>
/// Produced by parametric re-solve over a grid of candidate clearing prices; the
/// resulting price-quantity schedule <i>is</i> the curve (L3 §5).
/// Quantity must be non-increasing in price for a buy curve and non-decreasing
/// for a sell curve. Violation is a <c>HALT</c> (INV-P-09): a non-monotone curve
/// is both rejected by the exchange and diagnostic of a deeper problem — most
/// often a missing coupling constraint. It is never sorted into compliance.
/// </remarks>
public sealed record DaBidCurve
{
    public required SlotId Slot { get; init; }

    public required OrderSide Side { get; init; }

    /// <summary>Price-ordered.</summary>
    public required IReadOnlyList<DaCurvePoint> CurvePoints { get; init; }

    /// <summary><b>Asserted, not declared</b> (C3 §3, INV-P-09). Present so the
    /// producer records its own check; the consumer re-derives it.</summary>
    public required bool Monotone { get; init; }
}

// =============================================================================
//  §4 Dispatch setpoints
// =============================================================================

/// <summary>
/// The interface to the physical controller, which is downstream of the Planner
/// and out of scope (<c>01-system-model.md</c> §5, L3 §7) but whose interface is
/// fixed here (C3 §4).
/// </summary>
/// <remarks>
/// <b>The corridor, not the setpoint, is the binding instruction.</b> The
/// controller may deviate from <see cref="PBattSetpointMw"/> to follow an aFRR
/// activation signal, but never outside the corridor — which is exactly the
/// guarantee that keeps the reserve commitment deliverable without the Planner
/// running at control frequency.
/// </remarks>
public sealed record DispatchSetpoints
{
    /// <summary>Card. <c>[H_near]</c>. Unit MW. <b>Battery frame</b>
    /// (positive = discharge).</summary>
    public required ReadOnlyMemory<double> PBattSetpointMw { get; init; }

    /// <summary>Card. <c>[H_near]</c>. Unit MWh. Lower edge of the band the
    /// controller must stay in to keep commitments feasible.</summary>
    public required ReadOnlyMemory<double> SocCorridorLowerMwh { get; init; }

    /// <summary>Card. <c>[H_near]</c>. Unit MWh.</summary>
    public required ReadOnlyMemory<double> SocCorridorUpperMwh { get; init; }

    /// <summary>Card. <c>[H_near]</c>. Unit MW. Confirmed awards the controller
    /// must be able to serve.</summary>
    public required ReadOnlyMemory<double> ReserveObligationUpMw { get; init; }

    /// <summary>Card. <c>[H_near]</c>. Unit MW.</summary>
    public required ReadOnlyMemory<double> ReserveObligationDnMw { get; init; }

    /// <summary><c>H_near</c> — typically the next few slots (C3 §4).</summary>
    public required SlotSpan NearHorizon { get; init; }
}

// =============================================================================
//  The payload
// =============================================================================

/// <summary>
/// <b>C3 payload.</b> What to submit, at which gate.
/// </summary>
/// <remarks>
/// <b>What does not cross C3</b> (C3 §6): any reason for the order — Execution is
/// strategy-independent, and <c>tag</c> is for settlement attribution only;
/// belief, valuation or scenario data; and any expectation about fills — the
/// Planner's fill beliefs stay upstream. C3 is an instruction, not a forecast.
/// </remarks>
public sealed record ExecutionIntent : ContractEnvelope
{
    /// <summary>The plan this intent derives from. Links C3 to the recorded
    /// <c>PlanResult</c> (L3 §8).</summary>
    public required string PlanId { get; init; }

    /// <summary>Which gate this submission targets (L3 §1).</summary>
    public required GateId Gate { get; init; }

    /// <summary>Previous <c>planId</c> whose open orders this replaces.</summary>
    public string? Supersedes { get; init; }

    /// <summary>Echoed, so Execution can refuse speculative flow in <c>SAFE</c>
    /// (C3 §1, INV-P-05).</summary>
    public required DegradationMode Mode { get; init; }

    /// <summary>C3 §2.</summary>
    public required IReadOnlyList<OrderIntent> Orders { get; init; }

    /// <summary>C3 §3. Non-empty only at <see cref="GateId.DayAheadGate"/>.</summary>
    public required IReadOnlyList<DaBidCurve> DaCurves { get; init; }

    /// <summary>C3 §4.</summary>
    public required DispatchSetpoints Dispatch { get; init; }
}

// =============================================================================
//  The projection — structural enforcement of INV-X-04
// =============================================================================

/// <summary>
/// An <see cref="OrderIntent"/> <b>with <c>shadowValueEurPerMwh</c> and <c>urgency</c>
/// removed</b>.
/// </summary>
/// <remarks>
/// This type exists for one reason: <b>INV-X-04 asserts that Execution did not
/// consume <c>shadowValueEurPerMwh</c> or <c>urgency</c>, and it is enforced structurally
/// rather than by discipline</b> (C3 §2, C4 §7 — "structural — adapter
/// projection"). The two fields are absent from this type, so no implementation
/// of <c>IExecutionAdapter</c> can read them, no test can accidentally depend on
/// them, and no future edit can quietly start using them without first changing
/// this record — which is a visible, reviewable contract change.
/// <para>
/// The fields still cross the seam on <see cref="OrderIntent"/> because
/// Settlement needs them for the execution-slippage bucket (C5 §6). Keeping them
/// on the recorded payload while withholding them from the adapter is the whole
/// design: the audit trail is complete and the side channel is closed.
/// </para>
/// </remarks>
public sealed record OrderIntentProjection
{
    public required string IntentId { get; init; }
    public required MarketId Market { get; init; }
    public required string ProductId { get; init; }
    public SlotId? Slot { get; init; }
    public BlockId? Block { get; init; }
    public required OrderSide Side { get; init; }
    public double? LimitPriceEurPerMwh { get; init; }
    public double? LimitPriceEurPerMwH { get; init; }
    public double? VolumeMwh { get; init; }
    public double? VolumeMw { get; init; }
    public required OrderValidity Validity { get; init; }
    public string? ReplacesIntentId { get; init; }

    /// <summary>Retained: it is an attribution key that Execution echoes back
    /// unread on C4, not a decision input (C3 §6).</summary>
    public required StrategyTag Tag { get; init; }

    // Deliberately absent: ShadowValueEurPerMwh, ReserveShadowValueEurPerMwH,
    // Urgency. See the type remarks.
}

/// <summary>
/// The view of an <see cref="ExecutionIntent"/> that
/// <c>IExecutionAdapter</c> is given. Carries
/// <see cref="OrderIntentProjection"/> rows, so <c>shadowValueEurPerMwh</c> and
/// <c>urgency</c> are structurally unreachable from Execution (INV-X-04).
/// </summary>
public sealed record ExecutionIntentProjection
{
    public required TickId TickId { get; init; }
    public required SlotId AsOf { get; init; }
    public required AssetId AssetId { get; init; }
    public required PoiId PoiId { get; init; }
    public required SchemaVersion SchemaVersion { get; init; }
    public required ContentHash ContentHash { get; init; }
    public required string PlanId { get; init; }
    public required GateId Gate { get; init; }
    public string? Supersedes { get; init; }
    public required DegradationMode Mode { get; init; }
    public required IReadOnlyList<OrderIntentProjection> Orders { get; init; }
    public required IReadOnlyList<DaBidCurve> DaCurves { get; init; }
    public required DispatchSetpoints Dispatch { get; init; }

    /// <summary>The only sanctioned way to build a projection. Total and lossy by
    /// design.</summary>
    public static ExecutionIntentProjection Project(ExecutionIntent intent)
        => throw new NotImplementedException();
}

/// <summary>
/// The C3 invariant register (C3 §5). Checked before the payload leaves the
/// Planner and again on receipt.
/// </summary>
/// <remarks>
/// <list type="table">
///   <item><term>INV-P-01</term><description>Every intent is physically
///     deliverable given SOC, power and POI limits under the planned trajectory →
///     <c>HALT</c>.</description></item>
///   <item><term>INV-P-02</term><description>Confirmed commitments in L0 are
///     covered by the plan → <c>HALT</c>. A plan that cannot honour a confirmed
///     award never reaches C3 (L3 §4).</description></item>
///   <item><term>INV-P-03</term><description>No two intents for the same product
///     and slot on the same side without a <c>replacesIntentId</c> chain →
///     <c>HALT</c>.</description></item>
///   <item><term>INV-P-04</term><description>Prices tick-aligned, volumes
///     lot-aligned → <c>HALT</c>.</description></item>
///   <item><term>INV-P-05</term><description>In <c>SAFE</c>, only
///     <c>CommitmentCover</c> and cancel intents are present → <c>HALT</c>.</description></item>
///   <item><term>INV-P-06</term><description>Total planned discharge over any
///     window respects energy availability including the reserve corridor →
///     <c>HALT</c>.</description></item>
///   <item><term>INV-P-07</term><description>No simultaneous charge and discharge
///     above tolerance. A monitor rather than a constraint: the complementarity
///     binary is omitted by default and enabled by configuration flag
///     (conventions §3).</description></item>
///   <item><term>INV-P-09</term><description>DA curves monotone →
///     <c>HALT</c>.</description></item>
///   <item><term>INV-P-10</term><description>No sell intent priced below
///     <c>shadowValueEurPerMwh</c>; no buy intent priced above it → warn + block the
///     intent, unless tagged <c>CommitmentCover</c>.</description></item>
/// </list>
/// <para>
/// INV-P-08 is unassigned in the source register as of version 2.0 and is left
/// unassigned here; IDs are stable and are not renumbered (conventions §5).
/// </para>
/// </remarks>
public static class C3Invariants
{
    public const string PhysicallyDeliverable = "INV-P-01";
    public const string CommitmentsCovered = "INV-P-02";
    public const string NoUnchainedDuplicateIntents = "INV-P-03";
    public const string TickAndLotAligned = "INV-P-04";
    public const string SafeModeIntentsRestricted = "INV-P-05";
    public const string EnergyAvailabilityRespected = "INV-P-06";
    public const string NoSimultaneousChargeDischarge = "INV-P-07";
    public const string DaCurvesMonotone = "INV-P-09";
    public const string NeverQuoteThroughShadowValue = "INV-P-10";

    public static IReadOnlyList<string> All => throw new NotImplementedException();
}
