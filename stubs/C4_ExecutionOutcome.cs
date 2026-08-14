// -----------------------------------------------------------------------------
//  Flexbid.Btm.Contracts — C4: Execution → Settlement
//
//  Payload: ExecutionOutcome   Version: 1.0   Direction: L4 → L5
//  Normative source: 03-contracts/C4-execution-to-settlement.md §1–§8.
//
//  What actually happened. Settlement must be able to reconstruct the truth from
//  this payload plus metered reality ALONE — it has no access to what the Planner
//  intended, so that ex-post accounting cannot be contaminated by ex-ante belief.
//
//  SIGNATURES ONLY.
// -----------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;

namespace Flexbid.Btm.Contracts;

// =============================================================================
//  §2 Fills
// =============================================================================

/// <summary>C4 §2. One executed trade.</summary>
public sealed record Fill
{
    public required string FillId { get; init; }

    /// <summary>
    /// Links back to C3. <b>The only backward reference permitted, and it is an
    /// identifier, not data</b> (C4 §2). Every <c>fillId</c> must reference an
    /// <c>intentId</c> submitted in this run (INV-X-01 — a phantom fill is a
    /// <c>HALT</c>).
    /// </summary>
    public required string IntentId { get; init; }

    public required MarketId Market { get; init; }

    public required string ProductId { get; init; }

    public SlotId? Slot { get; init; }

    public BlockId? Block { get; init; }

    public required OrderSide Side { get; init; }

    /// <summary>Executed price. Unit EUR/MWh or EUR/MW/h.</summary>
    public required OrderPrice Price { get; init; }

    /// <summary>Executed volume. Unit kWh or MW. Summed across all fills for an
    /// intent, never exceeds the intended volume (INV-X-02).</summary>
    public required OrderVolume Volume { get; init; }

    /// <summary>Unit EUR. <b>Explicit, never netted into the price</b> (C4 §2) —
    /// netting would corrupt the execution-slippage bucket in C5 §6.</summary>
    public required Money Fees { get; init; }

    public required SlotId ExecutedAt { get; init; }
}

// =============================================================================
//  §3 Unfilled and cancelled
// =============================================================================

/// <summary>
/// C4 §3. What did not trade.
/// </summary>
/// <remarks>
/// <b>Unfilled orders are as important as fills.</b> Without them the four-bucket
/// error decomposition (C5 §6) cannot separate "we were wrong about value" from
/// "we could not get the trade" — the first is an L2 problem and the second is a
/// quoting-policy problem, and conflating them makes both unfixable.
/// </remarks>
public sealed record Unfilled
{
    public required string IntentId { get; init; }

    public required Disposition Disposition { get; init; }

    /// <summary>Populated for <see cref="Disposition.Rejected"/>.</summary>
    public string? RejectReason { get; init; }

    /// <summary>What did not trade. Unit kWh or MW.</summary>
    public required OrderVolume ResidualVolume { get; init; }
}

// =============================================================================
//  §4 Reserve awards and activations
// =============================================================================

/// <summary>C4 §4, per block. <b>The binding obligation.</b> Every award must
/// have a corresponding commitment ledger entry by end of tick (INV-X-06).</summary>
public sealed record ReserveAward
{
    public required BlockId Block { get; init; }

    /// <summary>Unit MW.</summary>
    public required ReserveMw AwardedUp { get; init; }

    /// <summary>Unit MW.</summary>
    public required ReserveMw AwardedDn { get; init; }

    /// <summary>Unit EUR/MW/h.</summary>
    public required CapacityPrice ClearingPriceUp { get; init; }

    /// <summary>Unit EUR/MW/h.</summary>
    public required CapacityPrice ClearingPriceDn { get; init; }
}

/// <summary>C4 §4, per slot. Realised activation against an award.</summary>
public sealed record ReserveActivation
{
    public required SlotId Slot { get; init; }

    /// <summary>Realised activated energy. Unit kWh.</summary>
    public required EnergyKwh ActivatedEnergyUp { get; init; }

    /// <summary>Unit kWh.</summary>
    public required EnergyKwh ActivatedEnergyDn { get; init; }

    /// <summary>Unit EUR/MWh.</summary>
    public required EnergyPrice ActivationPriceUp { get; init; }

    /// <summary>Unit EUR/MWh.</summary>
    public required EnergyPrice ActivationPriceDn { get; init; }

    /// <summary>Unit kWh. <b>Non-zero triggers INV-S-04</b>: alert + <c>HALT</c>.
    /// A reserve delivery failure is a prequalification risk, which is why it is
    /// never absorbed as a cost (C4 §4, ADR-014 §3).</summary>
    public required EnergyKwh DeliveryShortfall { get; init; }
}

// =============================================================================
//  §5 Physical reality
// =============================================================================

/// <summary>C4 §5. Metered truth. All series card. <c>[H]</c>.</summary>
public sealed record PhysicalReality
{
    /// <summary>Unit kWh. <b>The billing-relevant series</b> — the basis of the
    /// realised peak in C5 §2.</summary>
    public required ReadOnlyMemory<EnergyKwh> MeteredPoiImport { get; init; }

    /// <summary>Unit kWh.</summary>
    public required ReadOnlyMemory<EnergyKwh> MeteredPoiExport { get; init; }

    /// <summary>Unit kWh. Sub-metered where available.</summary>
    public required ReadOnlyMemory<EnergyKwh> MeteredLoad { get; init; }

    /// <summary>Unit kWh. Sub-metered where available.</summary>
    public required ReadOnlyMemory<EnergyKwh> MeteredPv { get; init; }

    /// <summary>Unit kWh. <b>At the battery terminal</b>, where losses are
    /// charged (conventions §3).</summary>
    public required ReadOnlyMemory<EnergyKwh> BatteryChargeEnergy { get; init; }

    /// <summary>Unit kWh. At the battery terminal.</summary>
    public required ReadOnlyMemory<EnergyKwh> BatteryDischargeEnergy { get; init; }

    /// <summary>End of slot. Unit kWh. Consistency with charge/discharge energy
    /// and η is INV-X-05 (warn): persistent divergence is the earliest available
    /// signal that the efficiency or degradation model has drifted, and it is
    /// cheap to monitor. It feeds the model-error bucket in C5 §6.</summary>
    public required ReadOnlyMemory<EnergyKwh> SocMeasured { get; init; }

    /// <summary>Card. <c>[H]</c>. <b>Provisional meter data is routine</b>
    /// (C4 §5).</summary>
    public required ReadOnlyMemory<QualityStamp> MeterQuality { get; init; }
}

// =============================================================================
//  §6 Imbalance
// =============================================================================

/// <summary>C4 §6. Per slot.</summary>
public sealed record ImbalanceRecord
{
    public required SlotId Slot { get; init; }

    /// <summary>Unit kWh. <b>Signed.</b></summary>
    public required EnergyKwh ImbalanceVolume { get; init; }

    /// <summary>Unit EUR/MWh. <b>Often final only weeks later</b> — the reason
    /// <c>isFinal</c> and <c>revisionOf</c> exist on this payload (C4 §1).</summary>
    public required EnergyPrice ImbalancePrice { get; init; }

    /// <summary>Unit EUR.</summary>
    public required Money ImbalanceCost { get; init; }
}

// =============================================================================
//  The payload
// =============================================================================

/// <summary>
/// <b>C4 payload.</b> Realised market and physical outcomes for a settlement
/// period.
/// </summary>
/// <remarks>
/// <b>Provisional and final are both first-class</b> (C4 §1). The engine cannot
/// wait for final data to keep operating, and it must not treat provisional data
/// as final. Every settlement artefact declares which it is, and a restatement is
/// a <i>new artefact referencing the old one</i>, never an in-place edit —
/// consistent with ADR-004's append-only revisions.
/// <para>
/// <b>What does not cross C4</b> (C4 §8): any Planner reasoning — only
/// <c>intentId</c> as an opaque key; valuation output; and any belief or
/// forecast. Settlement recomputes truth from realised data; if it needed a
/// forecast it would be doing valuation, not settlement.
/// </para>
/// </remarks>
public sealed record ExecutionOutcome : ContractEnvelope
{
    /// <summary>The settlement period this artefact covers.</summary>
    public required SlotRange SettlementPeriod { get; init; }

    /// <summary>Final versus provisional.</summary>
    public required bool IsFinal { get; init; }

    /// <summary>For restatements: the content hash of the artefact this one
    /// supersedes. Meter data and imbalance prices arrive late and get corrected
    /// (C4 §1).</summary>
    public ContentHash? RevisionOf { get; init; }

    /// <summary>C4 §2.</summary>
    public required IReadOnlyList<Fill> Fills { get; init; }

    /// <summary>C4 §3.</summary>
    public required IReadOnlyList<Unfilled> Unfilled { get; init; }

    /// <summary>C4 §4, per block.</summary>
    public required IReadOnlyList<ReserveAward> ReserveAwards { get; init; }

    /// <summary>C4 §4, per slot.</summary>
    public required IReadOnlyList<ReserveActivation> ReserveActivations { get; init; }

    /// <summary>C4 §5.</summary>
    public required PhysicalReality Physical { get; init; }

    /// <summary>C4 §6, per slot.</summary>
    public required IReadOnlyList<ImbalanceRecord> Imbalance { get; init; }
}

/// <summary>
/// The C4 invariant register (C4 §7).
/// </summary>
/// <remarks>
/// <list type="table">
///   <item><term>INV-X-01</term><description>Every <c>fillId</c> references an
///     <c>intentId</c> submitted in this run → <c>HALT</c>: phantom fill.</description></item>
///   <item><term>INV-X-02</term><description>Filled volume ≤ intended volume per
///     intent, across all fills → <c>HALT</c>.</description></item>
///   <item><term>INV-X-03</term><description>Energy balance holds within meter
///     tolerance: <c>poiImport − poiExport = load − pv + chargeEnergy −
///     dischargeEnergy</c> → warn if provisional, <c>HALT</c> if final.</description></item>
///   <item><term>INV-X-04</term><description>Execution did not consume
///     <c>shadowValue</c>/<c>urgency</c> → <b>structural</b>, via
///     <c>ExecutionIntentProjection</c>.</description></item>
///   <item><term>INV-X-05</term><description><c>socMeasured</c> consistent with
///     charge/discharge energy and η within tolerance → warn; drift indicates an
///     η or SOH model error.</description></item>
///   <item><term>INV-X-06</term><description>Every award has a corresponding entry
///     in the commitment ledger by end of tick → <c>HALT</c>.</description></item>
///   <item><term>INV-S-04</term><description><c>deliveryShortfall = 0</c> → alert
///     + <c>HALT</c>. Mirrored in C5 §9 because it is both an execution fact and a
///     state consequence.</description></item>
/// </list>
/// </remarks>
public static class C4Invariants
{
    public const string NoPhantomFills = "INV-X-01";
    public const string FilledNotExceedingIntended = "INV-X-02";
    public const string EnergyBalance = "INV-X-03";
    public const string ExecutionBlindToShadowValue = "INV-X-04";
    public const string SocConsistentWithThroughput = "INV-X-05";
    public const string AwardsLedgered = "INV-X-06";
    public const string NoDeliveryShortfall = "INV-S-04";

    public static IReadOnlyList<string> All => throw new NotImplementedException();
}
