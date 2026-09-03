// -----------------------------------------------------------------------------
//  Flexbid.Btm.Contracts — C1: Belief → Valuation
//
//  Payload: BeliefSnapshot   Version: 3.0   Direction: L1 → L2
//  Normative source: 03-contracts/C1-belief-to-valuation.md §1–§10,
//                    ADR-004 (bitemporal store), ADR-005 (joint ensemble),
//                    ADR-014 (quality, defaults).
//
//  Everything Valuation may know about the world, as of a knowledge-time
//  boundary, over the hot window. Nothing else reaches Valuation: it has no
//  access to the Belief store, to the network, or to a clock (C1 preamble).
//
//  SIGNATURES ONLY.
// -----------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;

namespace Flexbid.Btm.Contracts;

// =============================================================================
//  §2 Site and asset state
// =============================================================================

/// <summary>C1 §2. Measured and contracted state of the asset and the connection.</summary>
public sealed record SiteAssetState
{
    /// <summary>Measured SOC. Unit MWh. Card. <c>1</c>. Range <c>[socMinMwh, socMaxMwh]</c>.
    /// <b>No default</b>: if telemetry is lost, escalate — never default (C1 §2,
    /// INV-D-01).</summary>
    public required double SocNowMwh { get; init; }

    /// <summary>Operating band lower bound, including warranty derates. Unit MWh.
    /// Card. <c>1</c>.</summary>
    public required double SocMinMwh { get; init; }

    /// <summary>Operating band upper bound. Unit MWh. Card. <c>1</c>.
    /// <c>socMinMwh &lt; socMaxMwh</c>.</summary>
    public required double SocMaxMwh { get; init; }

    /// <summary>Maximum discharge power. Unit MW. Card. <c>[H]</c>. Range <c>≥0</c>.
    /// Per-slot; may derate on temperature or SOH (INV-D-08).</summary>
    public required ReadOnlyMemory<double> PBattMaxDischargeMw { get; init; }

    /// <summary>Maximum charge power. Unit MW. Card. <c>[H]</c>. Range <c>≥0</c>.</summary>
    public required ReadOnlyMemory<double> PBattMaxChargeMw { get; init; }

    /// <summary>One-way charge efficiency η_c. Card. <c>1</c>. Range <c>(0,1]</c>.
    /// <c>etaCharge · etaDischarge ≤ 1</c> (INV-D-09).</summary>
    public required double EtaCharge { get; init; }

    /// <summary>One-way discharge efficiency η_d. Card. <c>1</c>. Range <c>(0,1]</c>.</summary>
    public required double EtaDischarge { get; init; }

    /// <summary>State of health. Card. <c>1</c>. Range <c>(0,1]</c>. Nullable,
    /// default <c>1.0</c>, MaxStale 96 slots. <b>Informational only</b> — derates
    /// flow through <c>pMax*</c> and <c>socMaxMwh</c>, not through this field
    /// (C1 §2).</summary>
    public double? SohFraction { get; init; }

    /// <summary>Contractual/physical POI import limit. Unit MW. Card. <c>[H]</c>.
    /// Range <c>≥0</c>.</summary>
    public required ReadOnlyMemory<double> PPoiImportLimitMw { get; init; }

    /// <summary>Contractual/physical POI export limit. Unit MW. Card. <c>[H]</c>.
    /// Range <c>≥0</c>.</summary>
    public required ReadOnlyMemory<double> PPoiExportLimitMw { get; init; }

    /// <summary>Auxiliary load (HVAC, BMS, standby). Unit MW. Card. <c>[H]</c>.
    /// Range <c>≥0</c>. Nullable; conservative default is <b>site max</b>,
    /// MaxStale 96. Explicit, never folded into η, so that it appears in the POI
    /// balance and therefore in the peak (conventions §3).</summary>
    public ReadOnlyMemory<double>? AuxLoadMw { get; init; }
}

// =============================================================================
//  §3 Site forecasts (scenario-indexed)
// =============================================================================

/// <summary>C1 §3. Scenario-indexed site forecasts. Both series draw from the
/// shared scenario axis (ADR-005, INV-D-05).</summary>
public sealed record SiteForecasts
{
    /// <summary>Site load. Unit MW. Card. <c>[S,H]</c>. Range <c>≥0</c>. MaxStale 8.
    /// <b>Conservative default is a high quantile</b> — it protects the peak
    /// (C1 §3, ADR-014 §4 rung 5).</summary>
    public required ScenarioSlotMatrix<double> LoadMw { get; init; }

    /// <summary>PV generation. Unit MW. Card. <c>[S,H]</c>. Range <c>≥0</c>.
    /// Nullable, MaxStale 8. <b>Conservative default is zero</b> — a PV shortfall
    /// raises import, so zero protects the peak (C1 §3).</summary>
    public ScenarioSlotMatrix<double>? PvAvailMw { get; init; }

    /// <summary>Per-element load quality. Card. <c>[H]</c>. Measured prefix,
    /// forecast suffix (C0 §6).</summary>
    public required ReadOnlyMemory<QualityStamp> LoadQuality { get; init; }

    /// <summary>Per-element PV quality. Card. <c>[H]</c>.</summary>
    public required ReadOnlyMemory<QualityStamp> PvQuality { get; init; }
}

// =============================================================================
//  §4 Market beliefs (scenario-indexed)
// =============================================================================

/// <summary>
/// C1 §4. Market beliefs on the shared scenario axis.
/// </summary>
/// <remarks>
/// <b>INV-D-05</b>: index <c>s</c> denotes the same state of the world in
/// <see cref="DaPriceEurPerMwh"/>, <see cref="ActivationUp"/>, <c>load</c>, <c>pv</c> and
/// every other <c>[S,·]</c> array. Violation is a hard failure, not a warning —
/// ADR-005 explains why this is the invariant most worth defending: sampling
/// each series from its own marginal destroys the dependence structure that
/// determines whether the co-optimisation is worth anything, and systematically
/// <i>overstates</i> the value of the strategy.
/// </remarks>
public sealed record MarketBeliefs
{
    /// <summary>Day-ahead price belief. Unit EUR/MWh. Card. <c>[S,H]</c>.
    /// Range <c>[-9999, 9999]</c>. MaxStale: gate.
    /// <b>Negative prices are legal and material.</b></summary>
    public required ScenarioSlotMatrix<double> DaPriceEurPerMwh { get; init; }

    /// <summary>Realised DA clearing price. Unit EUR/MWh. Card. <c>[H]</c>.
    /// <b>Present only post-gate; null pre-gate</b> (C1 §4).</summary>
    public ReadOnlyMemory<double>? DaClearedEurPerMwh { get; init; }

    /// <summary>Intraday reference (e.g. ID index) belief. Unit EUR/MWh.
    /// Card. <c>[S,H]</c>. MaxStale 2.</summary>
    public required ScenarioSlotMatrix<double> IdPriceRefEurPerMwh { get; init; }

    /// <summary>Intraday half-spread belief. Unit EUR/MWh. Card. <c>[S,H]</c>.
    /// Range <c>≥0</c>. MaxStale 2. <b>Wide default suppresses trading</b>
    /// (C1 §4).</summary>
    public required ScenarioSlotMatrix<double> IdSpreadBeliefEurPerMwh { get; init; }

    /// <summary><b>Marginal (last accepted) capacity price</b> belief per product
    /// block — not a revenue price. Unit EUR/MW/h. Card. <c>[S,B]</c>.
    /// Range <c>≥0</c>. MaxStale: gate.
    /// <para>Capacity is pay-as-bid (ADR-018), so a bid is awarded exactly when
    /// it sits at or below this, and award probability is a tail mass of the
    /// ensemble rather than a fitted surface:
    /// <c>P(award | p, b) = Σ_s w_s · 1[AfrrCapPriceEurPerMwH[s,b] ≥ p]</c>.
    /// Reading it off the shared scenario axis is what keeps it correlated with
    /// spot prices and activation (ADR-005, INV-D-05).</para>
    /// <b>Zero default = do not chase revenue on invented prices</b>
    /// (ADR-014 §4 rung 5).</summary>
    public required ScenarioBlockMatrix<double> AfrrCapPriceEurPerMwH { get; init; }

    /// <summary>aFRR upward activation energy price. Unit EUR/MWh.
    /// Card. <c>[S,H]</c>. Default <c>0</c>. MaxStale: gate.</summary>
    public required ScenarioSlotMatrix<double> AfrrEnergyPriceUpEurPerMwh { get; init; }

    /// <summary>aFRR downward activation energy price. Unit EUR/MWh.
    /// Card. <c>[S,H]</c>. Default <c>0</c>.</summary>
    public required ScenarioSlotMatrix<double> AfrrEnergyPriceDnEurPerMwh { get; init; }

    /// <summary>Upward activation, as a <b>fraction of committed MW</b>.
    /// Unit: dimensionless. Card. <c>[S,H]</c>. Range <c>[0,1]</c>. MaxStale 96.
    /// Scenario-consistent with prices (ADR-005) — this is what makes
    /// "activated exactly when prices are extreme" representable at all.</summary>
    public required ScenarioSlotMatrix<double> ActivationUp { get; init; }

    /// <summary>Downward activation fraction. Card. <c>[S,H]</c>. Range <c>[0,1]</c>.</summary>
    public required ScenarioSlotMatrix<double> ActivationDn { get; init; }

    /// <summary>reBAP-type imbalance price belief. Unit EUR/MWh.
    /// Card. <c>[S,H]</c>. MaxStale 96.</summary>
    public required ScenarioSlotMatrix<double> ImbalancePriceEurPerMwh { get; init; }
}

// =============================================================================
//  §5 Liquidity beliefs (no prices)
// =============================================================================

/// <summary>
/// C1 §5. <b>Volumes only.</b> There is no price field in this section, by
/// design: it is the C1-side enforcement of the rule that fill probability
/// constrains the Planner but never prices for it (ADR-008, ADR-012, INV-V-16).
/// Prices for quoting reach the quoting policy by a different route, outside C2.
/// </summary>
public sealed record LiquidityBeliefs
{
    /// <summary>Intraday buy volume that may be <i>counted on</i>. Unit MWh.
    /// Card. <c>[H]</c>. Range <c>≥0</c>. Default <c>0</c>. MaxStale 2.</summary>
    public required ReadOnlyMemory<double> IdReliableVolumeBuyMwh { get; init; }

    /// <summary>Intraday sell volume that may be counted on. Unit MWh.
    /// Card. <c>[H]</c>. Range <c>≥0</c>. Default <c>0</c>. MaxStale 2.</summary>
    public required ReadOnlyMemory<double> IdReliableVolumeSellMwh { get; init; }

    /// <summary>Optional per-price-band refinement. Unit MWh.
    /// Card. <c>[H,Band]</c>, row-major (<c>h * bandCount + band</c>).
    /// Nullable, default <c>0</c>, MaxStale 2. Its representation is
    /// <b>OPEN-3</b> (ADR-015): scalar cap versus per-band cap. Either resolution
    /// is a <c>BoundTerm</c> at C2, so the contract shape does not change.</summary>
    public ReadOnlyMemory<double>? IdVolumeByPriceBandMwh { get; init; }

    /// <summary>Band count for <see cref="IdVolumeByPriceBandMwh"/>; zero when absent.</summary>
    public int PriceBandCount { get; init; }
}

// =============================================================================
//  §6 Tariff and network
// =============================================================================

/// <summary>C1 §6. Active tariff regimes and their rates (ADR-011).</summary>
public sealed record TariffAndNetwork
{
    /// <summary>Active regimes, from the versioned calendar. Card. <c>1..n</c>,
    /// non-empty. Regimes compose (ADR-011).</summary>
    public required IReadOnlyList<TariffRegimeId> ActiveRegimes { get; init; }

    /// <summary>Peak charge <b>per regime</b>, keyed by <see cref="ActiveRegimes"/>.
    /// Unit EUR/MW. Range <c>≥0</c>.</summary>
    public required IReadOnlyDictionary<TariffRegimeId, double> PeakPriceEurPerMw { get; init; }

    /// <summary>Network volumetric component. Unit EUR/MWh. Card. <c>[H]</c>.
    /// Range <c>≥0</c>.</summary>
    public required ReadOnlyMemory<double> VolumetricChargeEurPerMwh { get; init; }

    /// <summary>Levies, taxes and surcharges — a <b>closed enumerated list</b>,
    /// summed and normalised to EUR/MWh. Card. <c>[H]</c>. An unrecognised
    /// component is a configuration error, not a silently ignored field
    /// (L2 §2, TariffView).</summary>
    public required ReadOnlyMemory<double> LeviesAndTaxesEurPerMwh { get; init; }

    /// <summary>Whether each slot falls inside a Hochlastzeitfenster, from the
    /// DSO HLZF table. Card. <c>[H]</c>. <b>Conservative default is <c>true</c></b>
    /// — inside the window (C1 §6). Drives <c>EpigraphTerm.overSlots</c> for the
    /// <c>AtypicalHlzf</c> regime.</summary>
    public required ReadOnlyMemory<bool> IsHlzf { get; init; }
}

// =============================================================================
//  §7 Reserve product structure
// =============================================================================

/// <summary>C1 §7. Product structure from the versioned market calendar (ADR-002).</summary>
public sealed record ReserveProductStructure
{
    /// <summary>Maps each slot to its aFRR product block. Card. <c>[H]</c>.
    /// Range <c>≥0</c>. Non-decreasing, and covers every slot exactly once
    /// (INV-D-04).</summary>
    public required ReadOnlyMemory<int> BlockIndex { get; init; }

    /// <summary>Block lengths, from the calendar. Card. <c>[B]</c>. Range <c>&gt;0</c>.</summary>
    public required ReadOnlyMemory<SlotSpan> BlockSlots { get; init; }

    /// <summary>Prequalification sustained-delivery requirement <c>D</c>.
    /// Card. <c>1</c>. Range <c>&gt;0</c>. A calendar parameter, not a constant
    /// (ADR-010). Enters the SOC corridor constraints
    /// <c>soc[t] − rUp·D/η_d ≥ socMinMwh</c>.</summary>
    public required SlotSpan SustainDuration { get; init; }

    /// <summary>Minimum bid size. Unit MW. Card. <c>1</c>. Range <c>&gt;0</c>.</summary>
    public required double MinBidMw { get; init; }

    /// <summary>Bid granularity. Unit MW. Card. <c>1</c>. Range <c>&gt;0</c>.</summary>
    public required double BidStepMw { get; init; }

    /// <summary>Whether up and down capacity must be offered equal. Card. <c>1</c>.</summary>
    public required bool SymmetricProduct { get; init; }
}

// =============================================================================
//  §8 Quality summary
// =============================================================================

/// <summary>C1 §8. The basis of the risk multiplier (L2 §6, ADR-014 §1).</summary>
public sealed record QualitySummary
{
    /// <summary>One stamp per series.</summary>
    public required IReadOnlyDictionary<SeriesId, QualityStamp> SeriesQuality { get; init; }

    /// <summary>Read from L0 and <b>echoed here so Valuation need not consult L0
    /// separately</b> (C1 §8) — which is what keeps L2 a pure function of its two
    /// declared inputs.</summary>
    public required DegradationMode DegradationMode { get; init; }

    /// <summary>Empty in <c>NORMAL</c>; non-empty implies mode ≥ <c>DEFENSIVE</c>.
    /// A field marked <c>Null = no</c> may be absent only if it is listed here
    /// (INV-D-10).</summary>
    public required IReadOnlyList<SeriesId> CriticalMissing { get; init; }
}

// =============================================================================
//  The payload
// =============================================================================

/// <summary>
/// <b>C1 payload.</b> Everything Valuation may know about the world, as of
/// <c>asOf</c>, over the hot window.
/// </summary>
/// <remarks>
/// <b>What deliberately does not cross C1</b> (C1 §10), stated to keep it out:
/// any handle, cursor or reference into the Belief store; raw order-book data
/// (only the derived volume beliefs in §5); any Planner or Settlement output
/// (that arrives via L0, at a lag — ADR-006); wall-clock time; model objects
/// (only their evaluated outputs and their version hashes).
/// <para>
/// The arrays here are <see cref="ReadOnlyMemory{T}"/> views over the
/// preallocated struct-of-arrays hot window (ADR-004 §2), refilled in place each
/// tick. Constructing a snapshot therefore costs no copy — which is the
/// reconciliation of "a snapshot is a cheap immutable handle" (ADR-004 §3) with
/// "a contract is a value carrying no handle into another layer's storage"
/// (C0 §1): the memory is owned by L1 and is immutable for the lifetime of the
/// snapshot, and nothing in the payload can be used to reach back into the store.
/// </para>
/// </remarks>
public sealed record BeliefSnapshot : ContractEnvelope
{
    // --- §1 envelope extensions ---

    /// <summary><c>S</c>, the shared scenario axis length (ADR-005).</summary>
    public required int ScenarioCount { get; init; }

    /// <summary>Card. <c>[S]</c>. Non-negative, sums to 1 ± <c>1e-9</c>
    /// (INV-D-06). Generally <b>non-uniform</b>, because the ensemble that
    /// reaches Valuation is a reduced one (ADR-005).</summary>
    public required ReadOnlyMemory<double> ScenarioWeights { get; init; }

    /// <summary>First slot of the window.</summary>
    public required SlotId SlotStart { get; init; }

    /// <summary><c>H_hot</c>. Every <c>[H]</c> array has this length (INV-D-03).</summary>
    public required SlotSpan SlotCount { get; init; }

    /// <summary><c>B</c>, the number of aFRR product blocks in the window.</summary>
    public required int BlockCount { get; init; }

    // --- §2–§8 sections ---

    /// <summary>C1 §2.</summary>
    public required SiteAssetState Site { get; init; }

    /// <summary>C1 §3.</summary>
    public required SiteForecasts Forecasts { get; init; }

    /// <summary>C1 §4.</summary>
    public required MarketBeliefs Market { get; init; }

    /// <summary>C1 §5.</summary>
    public required LiquidityBeliefs Liquidity { get; init; }

    /// <summary>C1 §6.</summary>
    public required TariffAndNetwork Tariff { get; init; }

    /// <summary>C1 §7.</summary>
    public required ReserveProductStructure ReserveProducts { get; init; }

    /// <summary>C1 §8.</summary>
    public required QualitySummary Quality { get; init; }
}

/// <summary>
/// The C1 invariant register (C1 §9). Applied in addition to
/// <see cref="UniversalInvariants"/>, on both sides of the seam (C0 §4).
/// </summary>
/// <remarks>
/// <list type="table">
///   <item><term>INV-D-01</term><description><c>socMinMwh ≤ socNowMwh ≤ socMaxMwh</c> →
///     <c>HALT</c>: telemetry or model is wrong.</description></item>
///   <item><term>INV-D-02</term><description>No fact inside has
///     <c>knowledgeTime &gt; asOf</c> → <c>HALT</c>: lookahead. This is the
///     invariant ADR-004 makes structural rather than disciplinary.</description></item>
///   <item><term>INV-D-03</term><description>Every <c>[H]</c> array has length
///     <c>slotCount</c> → <c>HALT</c>.</description></item>
///   <item><term>INV-D-04</term><description><c>blockIndex</c> is non-decreasing
///     and covers every slot exactly once → <c>HALT</c>.</description></item>
///   <item><term>INV-D-05</term><description>Shared scenario axis across all
///     <c>[S,·]</c> arrays → <c>HALT</c> (ADR-005).</description></item>
///   <item><term>INV-D-06</term><description><c>scenarioWeights</c> non-negative,
///     sums to 1 ± 1e-9 → <c>HALT</c>.</description></item>
///   <item><term>INV-D-07</term><description>Reduced-ensemble marginal means match
///     the full ensemble within tolerance → warn + <c>DEGRADED</c>.</description></item>
///   <item><term>INV-D-08</term><description><c>pBattMaxChargeMw</c>,
///     <c>pBattMaxDischargeMw</c> ≥ 0 and finite for every slot → <c>HALT</c>.</description></item>
///   <item><term>INV-D-09</term><description><c>etaCharge · etaDischarge ≤ 1</c>
///     → <c>HALT</c>.</description></item>
///   <item><term>INV-D-10</term><description>Every field marked <c>Null = no</c>
///     present, or listed in <c>criticalMissing</c> → escalate per the
///     ladder.</description></item>
/// </list>
/// </remarks>
public static class C1Invariants
{
    public const string SocWithinBand = "INV-D-01";
    public const string NoLookahead = "INV-D-02";
    public const string SlotArrayLength = "INV-D-03";
    public const string BlockIndexCoversSlots = "INV-D-04";
    public const string SharedScenarioAxis = "INV-D-05";
    public const string ScenarioWeightsNormalised = "INV-D-06";
    public const string ReducedEnsembleMeansPreserved = "INV-D-07";
    public const string PowerLimitsFinite = "INV-D-08";
    public const string RoundTripEfficiencyAtMostOne = "INV-D-09";
    public const string RequiredOrDeclaredMissing = "INV-D-10";

    public static IReadOnlyList<string> All => throw new NotImplementedException();
}
