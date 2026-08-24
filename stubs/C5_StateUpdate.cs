// -----------------------------------------------------------------------------
//  Flexbid.Btm.Contracts — C5: Settlement → State/Value Store, and the read side
//
//  Payload:  StateUpdate    Version: 2.0   Direction: L5 → L0   (write)
//            StateSnapshot  Version: 2.0   Direction: L0 → L2/L3 (read, lagged)
//  Normative source: 03-contracts/C5-settlement-to-state.md §1–§9,
//                    ADR-006 (the only backwards edge), ADR-007, ADR-011, ADR-014.
//
//  The ONLY backwards edge in the system. Written at the end of a tick, read at
//  the top of the next. Everything here is therefore, by construction, at least
//  one tick old when it is used — and that lag is explicit and priceable rather
//  than hidden.
//
//  SIGNATURES ONLY.
// -----------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;

namespace Flexbid.Btm.Contracts;

// =============================================================================
//  §2 Peak state
// =============================================================================

/// <summary>C5 §2. Realised peak accounting, <b>per tariff regime</b>.</summary>
public sealed record PeakState
{
    public required TariffRegimeId Regime { get; init; }

    /// <summary>Unit MW. <b>The epigraph floor for the next tick</b>
    /// (C2 §3.3 <c>floor</c>). Non-decreasing within an accounting period, and
    /// resets exactly at the local-calendar boundary (INV-S-01).</summary>
    public required double PPoiRealisedPeakMw { get; init; }

    /// <summary>When it occurred — <b>diagnostically important</b>: a peak set in
    /// an HLZF window and one set outside it have different implications
    /// (C5 §2).</summary>
    public required SlotId RealisedPeakSlot { get; init; }

    /// <summary>Accounting period start, <b>local-calendar derived</b>. Month
    /// boundaries are local-midnight boundaries, which are not fixed UTC offsets
    /// across the year (conventions §4.2).</summary>
    public required SlotId PeriodStart { get; init; }

    /// <summary>Accounting period end, local-calendar derived.</summary>
    public required SlotId PeriodEnd { get; init; }

    /// <summary>True while meter data is unfinal.</summary>
    public required bool PeakIsProvisional { get; init; }

    /// <summary>
    /// Earliest slot not yet settled.
    /// </summary>
    /// <remarks>
    /// <b>This is what makes the lag safe.</b> The Planner knows the realised peak
    /// is authoritative only up to this slot, and treats the gap conservatively —
    /// using its own modelled POI trajectory as a provisional peak contribution
    /// rather than assuming the gap contained nothing (C5 §2, L2 §2 PeakView
    /// floor).
    /// </remarks>
    public required SlotId UnsettledGapFrom { get; init; }
}

// =============================================================================
//  §3 Tariff qualification state
// =============================================================================

/// <summary>
/// C5 §3. §19(2) StromNEV qualification state (ADR-011).
/// </summary>
/// <remarks>
/// <b>The margin, not just the status, is carried.</b> A binary "qualified" flag
/// tells the engine nothing until the moment it is too late; the margin lets the
/// value function price the approach to the cliff continuously (C5 §3).
/// <para>
/// Battery operation moves <i>both</i> the numerator and the denominator of
/// full-load hours, so it moves qualification directly. Falling below the
/// threshold loses the reduction for the entire year — the single largest
/// downside event in the BTM business case, and it must not be reachable through
/// an approximation error (ADR-011).
/// </para>
/// </remarks>
public sealed record QualificationState
{
    /// <summary>Accumulated in the qualification year. Unit MWh.</summary>
    public required double PPoiAnnualEnergyMwh { get; init; }

    /// <summary>Denominator of full-load hours. Unit MW.</summary>
    public required double PPoiAnnualPeakMw { get; init; }

    /// <summary>Unit h. <c>annualEnergy / annualPeak</c>, within tolerance
    /// (INV-S-06).</summary>
    public required double FullLoadHours { get; init; }

    /// <summary>Peak within HLZF windows only. Unit MW.</summary>
    public required double PPoiHlzfPeakMw { get; init; }

    /// <summary>Per applicable §19(2) path.</summary>
    public required QualificationStatus QualificationStatus { get; init; }

    /// <summary>Unit h. Distance to the threshold. Drives
    /// <see cref="QualCritical"/>.</summary>
    public required double QualificationMarginHours { get; init; }

    /// <summary>Unit h. Forward projection from the slow loop.</summary>
    public required double ProjectedYearEndFlh { get; init; }

    /// <summary>Within the configured margin. <b>Forces tier escalation</b>
    /// (ADR-010, L3 §3) <b>and applies the protective bound</b> restricting
    /// actions that would worsen the projection (ADR-011). All thresholds are
    /// configuration, never constants.</summary>
    public required bool QualCritical { get; init; }
}

// =============================================================================
//  §4 Commitment ledger
// =============================================================================

/// <summary>The SOC corridor a confirmed reserve award requires. Becomes a
/// <b>hard constraint in every degradation mode</b> (C5 §4, ADR-014 §3).</summary>
public readonly record struct SocCorridor(double Lower, double Upper, SlotRange Over);

/// <summary>
/// C5 §4. One entry in the commitment ledger.
/// </summary>
/// <remarks>
/// <b><see cref="CommitmentStatus.Pending"/> versus
/// <see cref="CommitmentStatus.Confirmed"/> is load-bearing</b> (ADR-006).
/// Confirmed entries are hard constraints — an awarded aFRR block fixes
/// <c>deliveryObligationMw</c> and its SOC corridor; a filled spot position fixes
/// the position variable. Pending entries are <i>exposure, not obligation</i>:
/// the Planner models an open order as a scenario-dependent position weighted by
/// the fill belief. That distinction is what lets the continuous intraday loop
/// re-optimise without either double-selling the same MWh or ignoring live
/// exposure (L3 §4).
/// </remarks>
public sealed record CommitmentEntry
{
    public required string EntryId { get; init; }

    public required CommitmentKind Kind { get; init; }

    public required CommitmentStatus Status { get; init; }

    public required MarketId Market { get; init; }

    public required string ProductId { get; init; }

    public SlotId? Slot { get; init; }

    public BlockId? Block { get; init; }

    /// <summary><b>Market frame</b> (sale positive). Unit MWh. Energy markets
    /// only; a capacity award is carried by the reserve fields, not here.</summary>
    public double? SignedVolumeMwh { get; init; }

    /// <summary><b>Market frame</b> (sale positive). Unit MW of committed power.
    /// <see cref="MarketId.AfrrCapacity"/> only.</summary>
    public double? SignedVolumeMw { get; init; }

    /// <summary>Unit EUR/MWh. Energy markets only.</summary>
    public double? PriceEurPerMwh { get; init; }

    /// <summary>Unit EUR/MW/h. <see cref="MarketId.AfrrCapacity"/> only.</summary>
    public double? PriceEurPerMwH { get; init; }

    /// <summary>For reserve awards: the corridor that must be maintained. Every
    /// <c>Confirmed</c> commitment has one where the product requires it
    /// (INV-S-05).</summary>
    public SocCorridor? FeasibilityRequirement { get; init; }
}

// =============================================================================
//  §5 Realised P&L attribution
// =============================================================================

/// <summary>
/// C5 §5. Realised P&amp;L decomposed into the <b>same</b>
/// <see cref="EconomicEffect"/> enumeration used by Valuation (ADR-009), so
/// planned and realised value are comparable term by term.
/// </summary>
public sealed record PnlAttribution
{
    /// <summary>Unit EUR, per effect.</summary>
    public required IReadOnlyDictionary<EconomicEffect, double> RealisedByEffect { get; init; }

    /// <summary>What Valuation expected. Unit EUR, per effect. Sourced from
    /// <c>PlanResult</c>'s objective decomposition (L3 §8).</summary>
    public required IReadOnlyDictionary<EconomicEffect, double> PlannedByEffect { get; init; }

    /// <summary>
    /// Unit EUR. <b>Mandatory bucket.</b>
    /// </summary>
    /// <remarks>
    /// Not optional, and it must not be allowed to be silently absorbed
    /// elsewhere. A rising unexplained ratio is the single best early warning
    /// that a term definition has drifted between Valuation and Settlement
    /// (C5 §5). <see cref="RealisedByEffect"/> plus this sums to total realised
    /// P&amp;L (INV-S-02).
    /// </remarks>
    public required double UnexplainedEur { get; init; }

    /// <summary>Alerted above a threshold (INV-S-07: warn, escalating to
    /// alert).</summary>
    public required double UnexplainedRatio { get; init; }
}

// =============================================================================
//  §6 Error decomposition
// =============================================================================

/// <summary>
/// C5 §6. The mechanism that makes the system <b>improvable rather than merely
/// operable</b>: the gap between planned and realised value, split into four
/// attributable buckets.
/// </summary>
/// <remarks>
/// Each is computed by a counterfactual re-run, which is only possible because
/// every layer is a pure function of a recorded input (ADR-013). The four buckets
/// should sum to the total gap within tolerance; the residual is
/// <c>unexplained</c> and is alerted on (INV-S-03, warn).
/// <para>
/// This four-way split is what turns "we lost money last week" into "the load
/// forecast degraded on Tuesdays", which is actionable.
/// </para>
/// </remarks>
public sealed record ErrorDecomposition
{
    /// <summary>Unit EUR. Value lost because the world differed from the belief.
    /// <b>Fix lives in L1 / the scenario model.</b> Computed by re-running
    /// Valuation and Planner on <i>realised</i> data instead of beliefs, keeping
    /// everything else fixed.</summary>
    public required double ForecastErrorEur { get; init; }

    /// <summary>Unit EUR. Value lost because the valuation was wrong <i>given</i>
    /// the belief. <b>Fix lives in L2.</b> Computed by re-running Settlement's
    /// valuation of the actual trajectory using Valuation's terms and comparing
    /// with Settlement's own accounting.</summary>
    public required double ModelErrorEur { get; init; }

    /// <summary>Unit EUR. Value lost because the chosen tier was not Tier 1.
    /// <b>Fix lives in L3 / ADR-010.</b> Computed by re-running the tick at
    /// Tier 1 and comparing objective values — this is ADR-010's measured gap,
    /// and it is why Tier 1 must exist even if it never ships.</summary>
    public required double OptimalityGapEur { get; init; }

    /// <summary>Unit EUR. Value lost between intent and fill. <b>Fix lives in the
    /// quoting policy.</b> Computed by valuing the fills at intent prices versus
    /// executed prices — which requires <c>shadowValueEurPerMwh</c> to have crossed C3
    /// (C3 §2).</summary>
    public required double ExecutionSlippageEur { get; init; }

    /// <summary>Which tier actually ran, for conditioning the gap
    /// (ADR-010).</summary>
    public required TierUsed TierUsed { get; init; }
}

// =============================================================================
//  §7 Quality and mode
// =============================================================================

/// <summary>One logged degradation-mode transition (C5 §7, ADR-014 §2).</summary>
public readonly record struct ModeTransition(
    SlotId At,
    DegradationMode From,
    DegradationMode To,
    string Cause);

/// <summary>
/// C5 §7. The operating-mode audit trail, so that a period of poor P&amp;L can be
/// attributed to degraded operation rather than to strategy (ADR-014 §2).
/// </summary>
public sealed record QualityAndMode
{
    /// <summary>Computed centrally, written here (ADR-014 §2).</summary>
    public required DegradationMode DegradationMode { get; init; }

    /// <summary>Full audit trail for the period, with causes.</summary>
    public required IReadOnlyList<ModeTransition> ModeTransitions { get; init; }

    /// <summary>Lets P&amp;L be conditioned on operating mode.</summary>
    public required IReadOnlyDictionary<DegradationMode, int> TicksByMode { get; init; }

    /// <summary>Feeds data-source SLAs.</summary>
    public required IReadOnlyDictionary<SeriesId, QualityStats> SeriesQualitySummary { get; init; }
}

// =============================================================================
//  §8 Value function refresh trigger
// =============================================================================

/// <summary>C5 §8. Whether the slow loop should re-run.</summary>
public sealed record ValueFunctionRefresh
{
    /// <summary>Beyond <c>validityHorizon</c> (C2 §4).</summary>
    public required bool VSocStale { get; init; }

    /// <summary>
    /// How far the current state is from where <c>V</c> was fitted.
    /// </summary>
    /// <remarks>
    /// Allows the slow loop to be <b>event-driven rather than purely
    /// periodic</b>: a large peak event or a qualification status change
    /// invalidates <c>V</c> faster than the calendar does (C5 §8).
    /// </remarks>
    public required double StateDriftSignal { get; init; }

    public required bool RefreshRequested { get; init; }
}

// =============================================================================
//  The write-side payload
// =============================================================================

/// <summary>
/// <b>C5 payload.</b> Everything Settlement writes back to L0 — the only
/// backwards edge in the system (ADR-006).
/// </summary>
public sealed record StateUpdate : ContractEnvelope
{
    public required bool IsFinal { get; init; }

    /// <summary>For restatements. Append-only revisions, never an in-place edit
    /// (ADR-004).</summary>
    public ContentHash? RevisionOf { get; init; }

    /// <summary>The slot from which this state applies. State is never written
    /// for a slot earlier than the previous update's <c>effectiveFrom</c>, except
    /// as an explicit <c>revisionOf</c> (INV-S-08).</summary>
    public required SlotId EffectiveFrom { get; init; }

    /// <summary>C5 §2, per regime.</summary>
    public required IReadOnlyList<PeakState> PeakStates { get; init; }

    /// <summary>C5 §3.</summary>
    public required QualificationState Qualification { get; init; }

    /// <summary>C5 §4.</summary>
    public required IReadOnlyList<CommitmentEntry> Commitments { get; init; }

    /// <summary>C5 §5.</summary>
    public required PnlAttribution Pnl { get; init; }

    /// <summary>C5 §6.</summary>
    public required ErrorDecomposition Errors { get; init; }

    /// <summary>C5 §7.</summary>
    public required QualityAndMode Quality { get; init; }

    /// <summary>C5 §8.</summary>
    public required ValueFunctionRefresh ValueFunctionRefresh { get; init; }
}

// =============================================================================
//  The read side
// =============================================================================

/// <summary>
/// <b>The read-side mirror of C5</b> (ADR-006): L0 snapshotted at the top of the
/// tick into an immutable value, consumed by L2 (<c>PeakView</c>,
/// <c>TariffView</c>, <c>OppCostView</c>) and L3 (commitment ledger).
/// </summary>
/// <remarks>
/// <b>Read at a lag of at least one tick, always.</b> No component reads a value
/// written later in the same tick; within a tick every arrow points right. This
/// is mechanically verifiable — <c>04-compliance/T3</c> instruments each layer's
/// reads and writes during a replay and asserts no read of a same-tick write
/// (ADR-006, <c>01-system-model.md</c> §2).
/// <para>
/// It is a contract like every other seam: field table, version, invariants
/// (ADR-006). It is also the entire restart state — an engine restart loads a
/// <see cref="StateSnapshot"/> and continues.
/// </para>
/// </remarks>
public sealed record StateSnapshot : ContractEnvelope
{
    /// <summary>Content hash of the <see cref="StateUpdate"/> this snapshot was
    /// materialised from. The lag is explicit and therefore priceable.</summary>
    public required ContentHash SourceUpdate { get; init; }

    /// <summary>Realised peak per regime, including <c>unsettledGapFrom</c>
    /// (C5 §2). The <c>EpigraphTerm.pPoiFloorMw</c> comes from here.</summary>
    public required IReadOnlyList<PeakState> PeakStates { get; init; }

    /// <summary>§19(2) state and margin (C5 §3).</summary>
    public required QualificationState Qualification { get; init; }

    /// <summary>The ledger, both <c>Pending</c> and <c>Confirmed</c> (C5 §4).</summary>
    public required IReadOnlyList<CommitmentEntry> Commitments { get; init; }

    /// <summary>The last valid <c>V</c>, with its staleness metadata. <b>Never a
    /// blocking dependency</b>: if the slow loop has not run, the Planner uses
    /// this with the staleness penalty applied (<c>01-system-model.md</c> §3,
    /// ADR-014).</summary>
    public required ValueFunctionCurve ValueFunction { get; init; }

    /// <summary>Current mode; echoed into C1 §8 so Valuation need not consult L0
    /// separately.</summary>
    public required DegradationMode DegradationMode { get; init; }

    /// <summary>Peak-critical flag: forces tier escalation and the protective
    /// bound (ADR-011, L3 §3).</summary>
    public required bool PeakCritical { get; init; }

    /// <summary>Model artefact versions in force, for the Merkle chain
    /// (ADR-006 table, ADR-013).</summary>
    public required IReadOnlyDictionary<string, ContentHash> ModelArtefactVersions { get; init; }
}

/// <summary>
/// The C5 invariant register (C5 §9).
/// </summary>
/// <remarks>
/// <list type="table">
///   <item><term>INV-S-01</term><description><c>pPoiRealisedPeakMw</c> is non-decreasing
///     within an accounting period, and resets exactly at the local-calendar
///     boundary → <c>HALT</c>. The reset is calendar-derived, not
///     UTC-arithmetic (conventions §4.2).</description></item>
///   <item><term>INV-S-02</term><description><c>realisedByEffect</c> sums, with
///     <c>unexplained</c>, to total realised P&amp;L → <c>HALT</c>.</description></item>
///   <item><term>INV-S-03</term><description>The four error buckets sum to
///     (planned − realised) within tolerance → warn.</description></item>
///   <item><term>INV-S-04</term><description>No reserve delivery shortfall
///     (mirrors C4) → alert + <c>HALT</c>.</description></item>
///   <item><term>INV-S-05</term><description>Every <c>Confirmed</c> commitment has
///     a <c>feasibilityRequirement</c> where the product requires one →
///     <c>HALT</c>.</description></item>
///   <item><term>INV-S-06</term><description><c>fullLoadHours =
///     pPoiAnnualEnergyMwh / pPoiAnnualPeakMw</c> within tolerance → <c>HALT</c>.</description></item>
///   <item><term>INV-S-07</term><description><c>unexplainedRatio</c> below
///     threshold → warn, escalating to alert.</description></item>
///   <item><term>INV-S-08</term><description>State is never written for a slot
///     earlier than <c>effectiveFrom</c> of the previous update, except as an
///     explicit <c>revisionOf</c> → <c>HALT</c>.</description></item>
/// </list>
/// </remarks>
public static class C5Invariants
{
    public const string PeakMonotoneWithinPeriod = "INV-S-01";
    public const string PnlAttributionSums = "INV-S-02";
    public const string ErrorBucketsSum = "INV-S-03";
    public const string NoDeliveryShortfall = "INV-S-04";
    public const string ConfirmedCommitmentsHaveCorridors = "INV-S-05";
    public const string FullLoadHoursConsistent = "INV-S-06";
    public const string UnexplainedRatioBounded = "INV-S-07";
    public const string StateWritesMonotone = "INV-S-08";

    public static IReadOnlyList<string> All => throw new NotImplementedException();
}
