// -----------------------------------------------------------------------------
//  Flexbid.Btm — Closed enumerations
//
//  Every enumeration in this file is CLOSED. Adding a member is a contract
//  change: it requires the doc update, the version bump and the conformance
//  tests in the same commit (README "Change discipline", C0 §5).
//
//  Adding an EconomicEffect additionally requires an entry in the term ownership
//  matrix (02-layers/L2-valuation.md §5) or INV-V-02 will reject every term that
//  carries it. Adding a VarSymbol is a MAJOR version bump on C2 (C2 §2).
// -----------------------------------------------------------------------------

#nullable enable

namespace Flexbid.Btm;

// =============================================================================
//  Valuation vocabulary
// =============================================================================

/// <summary>
/// The closed set of economic effects (ADR-009, L2 §5). Each effect has exactly
/// one owning view; no <c>(effect, variable, slot)</c> triple may be priced
/// twice (INV-V-01). Settlement decomposes realised P&amp;L into this <i>same</i>
/// enumeration (C5 §5), which is what makes planned and realised value
/// comparable term by term.
/// </summary>
public enum EconomicEffect
{
    /// <summary>DA energy value on <c>vDaBuy</c>/<c>vDaSell</c>. Owner: <c>SpotView</c>
    /// (L2 §5 note — called out explicitly because the largest term in the
    /// objective must not be owned by nobody).</summary>
    SpotEnergyValue,

    /// <summary>Intraday energy value. Owner: <c>IdOptionView</c>.</summary>
    IdEnergyValue,

    /// <summary>aFRR capacity revenue. Owner: <c>AfrrCapacityView</c>.</summary>
    ReserveCapacityRevenue,

    /// <summary>aFRR activation energy revenue. Owner: <c>AfrrEnergyView</c>.</summary>
    ReserveEnergyRevenue,

    /// <summary>Imbalance cost. Owner: <c>ImbalanceRiskView</c>.</summary>
    ImbalanceCost,

    /// <summary>Network demand charge. Owner: <c>PeakView</c>. Base <c>PoiImport</c>.</summary>
    NetworkPeakCharge,

    /// <summary>Volumetric network charge. Owner: <c>TariffView</c>. Base <c>PoiImport</c>
    /// — legitimately the same base as <see cref="NetworkPeakCharge"/>, a
    /// different effect (ADR-009 §Decision 3).</summary>
    NetworkVolumetricCharge,

    /// <summary>Levies, taxes and surcharges a delineation regime cannot reduce —
    /// Stromsteuer, Konzessionsabgabe — normalised to EUR/MWh.
    /// Owner: <c>TariffView</c>. Base <c>PoiImport</c>.</summary>
    LeviesAndTaxes,

    /// <summary>The reducible EnFG components, charged on the umlagebelasteter
    /// Netzbezug (21) rather than on metered import. Owner: <c>DelineationView</c>.
    /// Base <c>Delineation</c> (ADR-017). Charging these on (3) overstates the
    /// charge by the whole relief.</summary>
    EnfgLevies,

    /// <summary>Marktprämie MAX[AW - MW_month; 0] on the insgesamt förderfähige
    /// Netzeinspeisung (32). Owner: <c>DelineationView</c>. Base <c>Delineation</c>.
    /// Spot revenue on the same MWh stays in <see cref="SpotEnergyValue"/>.</summary>
    SubsidyRevenue,

    /// <summary>Cycle/throughput degradation cost, charged once.
    /// Owner: <c>OppCostView</c>. Base <c>BatteryThroughput</c>.</summary>
    CycleDegradation,

    /// <summary>Terminal value of stored energy, carried by V(SOC).
    /// Owner: <c>OppCostView</c> via <c>V</c>. Base <c>TerminalSoc</c>.</summary>
    StoredEnergyContinuation,
}

/// <summary>
/// The base an economic effect applies to (C2 §3 common header, ADR-009 §3).
/// Two effects may share a base; two terms claiming the same effect on the same
/// base and slot may not (INV-V-01).
/// </summary>
public enum TermBase
{
    PoiImport,
    BatteryThroughput,
    MarketVolume,
    TerminalSoc,
    ReserveCapacity,

    /// <summary>The Abgrenzungsoption monthly aggregates (ADR-017). Distinct from
    /// <see cref="PoiImport"/> because (21) is not metered import.</summary>
    Delineation,
}

/// <summary>
/// The closed decision-variable vocabulary (C2 §2). The Planner owns the
/// variables; Valuation may only name them. A term referencing a symbol not in
/// this enumeration is rejected (INV-V-03). <b>Adding a symbol is a major
/// version bump on C2.</b>
/// </summary>
public enum VarSymbol
{
    /// <summary>Battery charge power. Frame: battery. Index <c>[H]</c>. Unit MW.</summary>
    PCharge,

    /// <summary>Battery discharge power. Frame: battery. Index <c>[H]</c>. Unit MW.</summary>
    PDischarge,

    /// <summary>State of charge, end of slot. Index <c>[H]</c>. Unit MWh.</summary>
    Soc,

    /// <summary>SOC at end of horizon. Index <c>1</c>. Unit MWh. Abscissa of V (ADR-007).</summary>
    SocTerminal,

    /// <summary>POI net power. Frame: POI. Index <c>[H]</c>. Unit MW.</summary>
    PPoi,

    /// <summary>Peak epigraph variable, per regime. Frame: POI. Index: per regime. Unit MW.</summary>
    ZPeak,

    /// <summary>Day-ahead purchase volume. Frame: market. Index <c>[H]</c>.</summary>
    VDaBuy,

    /// <summary>Day-ahead sale volume. Frame: market. Index <c>[H]</c>.</summary>
    VDaSell,

    /// <summary>Intraday purchase volume. Frame: market. Index <c>[H]</c>.</summary>
    VIdBuy,

    /// <summary>Intraday sale volume. Frame: market. Index <c>[H]</c>.</summary>
    VIdSell,

    /// <summary>aFRR upward capacity offered. Frame: market. Index <c>[B]</c>. Unit MW.</summary>
    RUp,

    /// <summary>aFRR downward capacity offered. Frame: market. Index <c>[B]</c>. Unit MW.</summary>
    RDn,

    /// <summary>aFRR upward activated energy. Frame: market. Index <c>[S,H]</c>.</summary>
    EAfrrUp,

    /// <summary>aFRR downward activated energy. Frame: market. Index <c>[S,H]</c>.</summary>
    EAfrrDn,

    /// <summary>Imbalance exposure. Frame: market. Index <c>[S,H]</c>.</summary>
    EImbalance,

}

/// <summary>
/// Why a bound exists (C2 §3.4). <b>Not decoration.</b> Feasibility restoration
/// (L3 §6) relaxes in the declared order <c>Risk → Liquidity → stop</c>;
/// <see cref="Physical"/> and <see cref="Regulatory"/> are never relaxed.
/// Without the tag an infeasible model can only be relaxed blindly.
/// No <see cref="Physical"/> bound may be soft (INV-V-15).
/// </summary>
public enum BoundReason
{
    Physical,
    Contractual,
    Liquidity,
    Risk,
    Regulatory,
}

/// <summary>Declared structural coupling spanning decision variables (C2 §3.5).</summary>
public enum CouplingKind
{
    /// <summary><c>soc[t] − rUp·D/η_d ≥ socMinMwh</c>, <c>soc[t] + rDn·D·η_c ≤ socMaxMwh</c> (ADR-010).</summary>
    ReserveCorridor,

    /// <summary><c>p_d[t] + rUp[b(t)] ≤ P_max_dis</c>, <c>p_c[t] + rDn[b(t)] ≤ P_max_chg</c>.</summary>
    ReservePowerHeadroom,

    /// <summary>Confirmed ledger entries fixed as hard constraints (C5 §4, L3 §4).</summary>
    Commitment,

    /// <summary>SOC feasibility over a weighted scenario mass ≥ 1−ε.</summary>
    ChanceConstraint,

    /// <summary>Escape hatch. Use requires a design conversation, not a quiet addition.</summary>
    Custom,
}

/// <summary>
/// PWL curvature (C2 §3.2, ADR-008). <b>Verified against the breakpoints, never
/// trusted</b> (INV-V-12). <c>(Concave, Maximize)</c> and <c>(Convex, Minimize)</c>
/// are LP-exact and need no binaries; everything else costs SOS2 or binaries and
/// must be declared in <c>problemClassHint</c>.
/// </summary>
public enum Curvature
{
    Concave,
    Convex,
    General,
}

/// <summary>Direction of a term in the objective (C2 §3.1, §3.2).</summary>
public enum ObjectiveSense
{
    Maximize,
    Minimize,
}

/// <summary>Behaviour outside the PWL breakpoint range (C2 §3.2).
/// <see cref="Forbid"/> adds bounds at the end breakpoints.</summary>
public enum Extrapolation
{
    Clamp,
    Forbid,
}

/// <summary>Relation of a <c>LinearRow</c> (C2 §3.5): <c>Σ coef·var {≤,=,≥} rhs</c>.</summary>
public enum RowRelation
{
    LessOrEqual,
    Equal,
    GreaterOrEqual,
}

/// <summary>Scenario scope of a coupling constraint (C2 §3.5).</summary>
public enum ScenarioScopeKind
{
    All,
    PerScenario,
    /// <summary>Holds for a weighted scenario mass ≥ 1−ε; ε carried alongside.</summary>
    ChanceLevel,
}

/// <summary>
/// Composition steps (L2 §4, ADR-009). Preconditions are declared and checked;
/// composing out of order raises rather than producing a plausible wrong number
/// (INV-V-07). Recorded in <c>ValuationBundle.compositionOrder</c> for audit.
/// </summary>
public enum StageId
{
    /// <summary>Stage 1. Requires nothing. Establishes: POI energy fully marked.</summary>
    Tariff,

    /// <summary>Stage 2 (<c>OppCost + V</c>). Requires POI energy marked.
    /// Establishes: battery energy marked, terminal value attached.</summary>
    OppCost,

    /// <summary>Stage 3. Requires battery energy marked. Establishes: <c>(21)</c>
    /// established, delineation <c>λ</c> attached. The reducible EnFG components are
    /// charged here, not at stage 1: their base <c>(21)</c> depends on the month's
    /// PV-versus-grid charging mix (ADR-017).</summary>
    Delineation,

    /// <summary>Stage 4. Requires battery energy marked.
    /// Establishes: SOC corridor and headroom constraints present.</summary>
    ReserveCoupling,

    /// <summary>Stage 5. Requires POI <b>and</b> battery energy marked.
    /// Establishes: <c>zPeak</c> bounded below by realised peak. Reversing this
    /// with stage 2 double-counts.</summary>
    Peak,

    /// <summary>Stage 6. Requires all above. Establishes: bundle well-formed and complete.</summary>
    Validate,
}

// =============================================================================
//  Data quality and operating mode
// =============================================================================

/// <summary>Where a value came from (ADR-014 §1, carried in <c>QualityStamp</c>).</summary>
public enum Provenance
{
    Measured,
    Forecast,
    Revised,
    Imputed,
    Default,
}

/// <summary>
/// How good a value is (ADR-014 §1). Consumers map this onto <b>risk
/// parameters</b>, never onto a branch (ADR-014 §1, L2 §6). One code path,
/// parameterised.
/// </summary>
public enum QualityLevel
{
    /// <summary>Rung 1: primary source, fresh.</summary>
    Good,

    /// <summary>Rung 3: secondary source.</summary>
    Degraded,

    /// <summary>Rung 2: primary source, stale within <c>maxStale</c>.</summary>
    Stale,

    /// <summary>Rung 4: model imputation (persistence, climatology, ensemble mean).</summary>
    Imputed,

    /// <summary>Rung 6: no safe default exists; the mode escalates.</summary>
    Missing,
}

/// <summary>
/// The five global degradation modes (ADR-014 §2). Computed centrally from the
/// quality state, written to L0, echoed into C1/C2/C3. Transitions are
/// hysteretic (enter after N bad ticks, leave after M &gt; N good ones) and every
/// transition is logged with its cause into the settlement record (C5 §7).
/// </summary>
/// <remarks>
/// UPPERCASE member names are mandated by <c>00-overview/02-conventions.md</c>
/// §5 ("Degradation modes | UPPERCASE"), overriding the usual C# casing rule.
/// Two things are never degraded away in any mode: peak protection and
/// commitment feasibility (ADR-014 §3).
/// </remarks>
public enum DegradationMode
{
    /// <summary>All critical inputs Good. Full strategy.</summary>
    NORMAL,

    /// <summary>Non-critical inputs stale or imputed. Full strategy, widened risk,
    /// reduced position limits.</summary>
    DEGRADED,

    /// <summary>Critical input stale, V beyond validity, or solver repeatedly at
    /// its time limit. Honour commitments, maintain peak protection, no new
    /// speculative exposure.</summary>
    DEFENSIVE,

    /// <summary>Critical input missing or commitment ledger inconsistent. Honour
    /// existing commitments only; cancel unfilled speculative orders; hold SOC in
    /// a safe band. Only <c>CommitmentCover</c> and cancel intents may cross C3
    /// (INV-P-05).</summary>
    SAFE,

    /// <summary>Physical constraint violated, state store unreadable, or contract
    /// validation failure. Submit nothing; alert; operator acknowledgement
    /// required to resume.</summary>
    HALT,
}

/// <summary>Action taken when an invariant fails (C0 §4, C1 §9, C2 §8, C3 §5,
/// C4 §7, C5 §9). Producer-side failure enters the producing layer's degradation
/// ladder; consumer-side failure is always <see cref="Halt"/>.</summary>
public enum ViolationAction
{
    Warn,
    Escalate,
    Alert,
    Halt,
}

// =============================================================================
//  Markets, orders and gates
// =============================================================================

/// <summary>Market a term, intent, fill or ledger entry belongs to (C3 §2).</summary>
public enum MarketId
{
    Da,
    IdContinuous,
    AfrrCapacity,
    AfrrEnergy,
}

/// <summary>Reserve product direction. Upward and downward capacity are separate
/// products with separate auctions, merit orders and marginal prices — which is
/// why an award is split <c>Up</c>/<c>Dn</c> and a fill, carrying only
/// <see cref="OrderSide"/>, could never express it (C4 §4).</summary>
public enum ReserveDirection
{
    Up,
    Dn,
}

/// <summary>Order side, in the <b>market frame</b>: <c>Sell = discharge</c> (C3 §2).</summary>
public enum OrderSide
{
    Buy,
    Sell,
}

/// <summary>
/// Order validity (C3 §2). <see cref="GtdUntil"/> carries a
/// <see cref="SlotId"/> payload; see <c>OrderValidity</c> in
/// <c>C3_ExecutionIntent.cs</c> for the discriminated carrier.
/// </summary>
public enum ValidityKind
{
    /// <summary>Immediate-or-cancel.</summary>
    Ioc,

    /// <summary>Fill-or-kill.</summary>
    Fok,

    /// <summary>Good-till-date; the date is a <see cref="SlotId"/>.</summary>
    GtdUntil,

    /// <summary>Good-till-cancel, expiring at the gate.</summary>
    GtcUntilGate,
}

/// <summary>
/// Why the Planner wants this order. <b>Settlement attribution only</b> — it does
/// not affect routing (C3 §6). <see cref="CommitmentCover"/> is load-bearing: it
/// is the only tag under which INV-P-10 (never quote through the shadow value)
/// may legitimately be violated, and the only speculative-flow exemption in
/// <c>SAFE</c> (INV-P-05).
/// </summary>
public enum StrategyTag
{
    Arbitrage,
    PeakShave,
    ReserveHedge,
    Rebalance,
    CommitmentCover,
}

/// <summary>What happened to an intent that did not fully fill (C4 §3).</summary>
public enum Disposition
{
    Expired,
    Cancelled,
    Rejected,
    PartiallyFilled,
}

/// <summary>
/// The staged decision points of L3 §1. A <see cref="GateId"/> identifies which
/// submission a C3 payload targets and, with the clock, stamps the solve
/// (<c>00-overview/01-system-model.md</c> §3: "every solve is stamped with the
/// clock that triggered it").
/// </summary>
/// <remarks>
/// Concrete gate <i>times</i> are never code — they are loaded from the versioned
/// <c>MarketCalendar</c> artefact and replayed with the backtest (ADR-002).
/// This enumeration names the stages; the calendar supplies the instants.
/// </remarks>
public enum GateId
{
    /// <summary>S0, <c>C_slow</c>. Produces V. Submits nothing.</summary>
    SlowLoop,

    /// <summary>S1, <c>C_gate</c>. aFRR capacity auction. Freezes <c>rUp</c>/<c>rDn</c> offers.</summary>
    AfrrCapacityGate,

    /// <summary>S2, <c>C_gate</c>. Day-ahead auction. Requires a monotone bid curve (INV-P-09).</summary>
    DayAheadGate,

    /// <summary>S3, <c>C_gate</c>. Post-DA rebalance on realised clearing. Freezes nothing.</summary>
    PostDaRebalance,

    /// <summary>S4, <c>C_tick</c>. Continuous intraday re-optimisation.</summary>
    IntradayContinuous,

    /// <summary>S4, <c>C_gate</c>. Intraday auction gate closure.</summary>
    IntradayAuctionGate,

    /// <summary>S5, real time. Setpoint and SOC corridor to the controller; out of
    /// scope as an optimiser (L3 §7), but its interface is fixed at C3 §4.</summary>
    Dispatch,
}

// =============================================================================
//  Tariff, co-optimisation, state
// =============================================================================

/// <summary>
/// Tariff regimes behind <c>ITariffRegime</c> (ADR-011). Regimes <b>compose</b>:
/// a site may be subject to several simultaneously, each emitting terms tagged
/// with its own <see cref="EconomicEffect"/> so the composer's exclusivity check
/// keeps them from overlapping. All numeric thresholds (7,000 h; 10 GWh; tier
/// percentages; HLZF windows) are configuration, never constants.
/// </summary>
public enum TariffRegimeId
{
    /// <summary><c>EpigraphTerm</c> over all slots in the year, floored by realised peak.</summary>
    AnnualLeistungspreis,

    /// <summary><c>EpigraphTerm</c> per calendar month.</summary>
    MonthlyLeistungspreis,

    /// <summary>§19(2) S.1 StromNEV. <c>EpigraphTerm</c> over <b>HLZF slots only</b>,
    /// from the DSO calendar table — this is why <c>EpigraphTerm.overSlots</c> is a
    /// slot <i>set</i> and not a range.</summary>
    AtypicalHlzf,

    /// <summary>§19(2) S.2 StromNEV. <c>EpigraphTerm</c> plus a discrete
    /// qualification state. A cliff: battery operation moves both the numerator
    /// and the denominator of full-load hours.</summary>
    IntensiveUse,
}

/// <summary>
/// Which tier of the co-optimisation ladder produced a plan (ADR-010, L3 §3).
/// Reported out of <c>ICoOptimizer.Solve</c>, recorded in <c>PlanResult</c> and
/// visible in the settlement record, so "we were on the fast path that day" is
/// never invisible.
/// </summary>
public enum TierUsed
{
    /// <summary>Joint stochastic MILP. The accuracy reference and the oracle every
    /// other tier is scored against. Runs offline on every backtest day
    /// regardless of which tier ships.</summary>
    Tier1Joint,

    /// <summary>Lagrangian decomposition. Returns a feasible primal <b>and</b> a
    /// certified dual bound, so the gap is a number per solve, not a hope.
    /// <c>μ*</c> is the reservation price of headroom.</summary>
    Tier2Lagrangian,

    /// <summary>Learned reservation-price surface <c>μ̂</c>, then one spot MILP.
    /// Escalates to <see cref="Tier2Lagrangian"/> per L3 §3.</summary>
    Tier3Learned,
}

/// <summary>Kind of commitment ledger entry (C5 §4).</summary>
public enum CommitmentKind
{
    SpotPosition,
    ReserveAward,
    OpenOrder,
}

/// <summary>
/// Commitment ledger status (C5 §4). <see cref="Pending"/> versus
/// <see cref="Confirmed"/> is load-bearing (ADR-006): confirmed entries are hard
/// constraints, pending entries are probabilistic exposure. This distinction is
/// what lets S4 re-optimise continuously without double-selling the same MWh.
/// </summary>
public enum CommitmentStatus
{
    Pending,
    Confirmed,
    Settled,
    Cancelled,
}

/// <summary>§19(2) qualification status per applicable path (C5 §3).
/// The <i>margin</i> matters more than the status: a binary flag tells the engine
/// nothing until it is too late.</summary>
public enum QualificationStatus
{
    Qualified,
    AtRisk,
    Lost,
    NotApplicable,
}

/// <summary>The four clocks (<c>00-overview/01-system-model.md</c> §3, ADR-002).
/// Part of <c>TickId</c> and therefore of the content hash of the resulting plan:
/// two solves at the same wall time from different clocks are different artefacts.</summary>
public enum ClockId
{
    /// <summary>Month/year boundary. Peak reset, §19(2) evaluation. Irreversibility: absolute.</summary>
    Accounting,

    /// <summary>Daily or on material state change. Recomputes V and recalibrates
    /// fill/activation models. Irreversibility: cheap to redo.</summary>
    Slow,

    /// <summary>Market-defined. Binding submissions. Irreversibility: binding at gate.</summary>
    Gate,

    /// <summary>Event-driven / sub-gate. Irreversibility: reversible until the ID gate.</summary>
    Tick,
}
