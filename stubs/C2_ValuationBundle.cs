// -----------------------------------------------------------------------------
//  Flexbid.Btm.Contracts — C2: Valuation → Planner
//
//  Payload: ValuationBundle   Version: 1.0   Direction: L2 → L3
//  Normative source: 03-contracts/C2-valuation-to-planner.md §1–§9,
//                    ADR-008 (linearizable primitives — this file IS that ADR),
//                    ADR-007 (V(SOC), not λ), ADR-009 (term ownership),
//                    ADR-010 (reserve coupling), L2-valuation.md §4–§6.
//
//  The load-bearing seam. Everything the Planner knows about economics arrives
//  here, expressed in the closed algebra of ADR-008. The Planner has NO access
//  to Belief, to prices, or to scenario arrays.
//
//  Five shapes, nothing else. No delegates, no scenario arrays, no Belief
//  handles, no `object`. If a lever cannot be expressed in the five shapes, that
//  is a design conversation, not a quiet Func<> (ADR-008).
//
//  SIGNATURES ONLY.
// -----------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;

namespace Flexbid.Btm.Contracts;

// =============================================================================
//  §3 Common term header
// =============================================================================

/// <summary>
/// The header every term shape carries (C2 §3). It is what makes the ownership
/// check (ADR-009), the exclusivity check (INV-V-01) and settlement attribution
/// (C5 §5) possible at all.
/// </summary>
public readonly record struct TermHeader
{
    /// <summary>Stable, unique within the bundle. The key of
    /// <c>ProblemClassHint.BinariesByOrigin</c> (C2 §7).</summary>
    public required string TermId { get; init; }

    /// <summary>Which view emitted it. Checked against the ownership matrix
    /// (L2 §5) by INV-V-02.</summary>
    public required ViewId OriginView { get; init; }

    /// <summary>From the closed enumeration. <b>The basis of the exclusivity
    /// check</b>: no <c>(effect, variable, slot)</c> triple may be priced by more
    /// than one term (INV-V-01, ADR-009).</summary>
    public required EconomicEffect Effect { get; init; }

    /// <summary>The base the effect applies to. Two effects may share a base —
    /// <c>NetworkPeakCharge</c> and <c>NetworkVolumetricCharge</c> both apply to
    /// <c>PoiImport</c>, legitimately. Two terms claiming the same effect on the
    /// same base and slot may not (ADR-009 §3).</summary>
    public required TermBase Base { get; init; }

    /// <summary>Which slots the term applies to.</summary>
    public required SlotRange Slots { get; init; }

    /// <summary>If true, dropping the term invalidates the plan. <b>Peak
    /// protection is mandatory</b> and is never degraded away, in any mode
    /// (ADR-014 §3). Every mandatory term must be present (INV-V-04).</summary>
    public required bool Mandatory { get; init; }

    /// <summary>Propagated from input quality. Feeds <c>RiskProfile</c>;
    /// <b>never a branch</b> (ADR-014 §1).</summary>
    public required Confidence Confidence { get; init; }
}

// =============================================================================
//  §3.1 – §3.5  The five shapes. Nothing else crosses C2.
// =============================================================================

/// <summary>
/// <b>Shape 1 of 5</b> (C2 §3.1). A coefficient per (decision variable, slot).
/// MILP realisation: an objective coefficient. <b>Introduces no binaries.</b>
/// </summary>
/// <remarks>
/// Emitted by <c>TariffView</c> (volumetric charge, levies and taxes — a closed
/// enumerated list), <c>OppCostView</c> (cycle cost per throughput),
/// <c>AfrrEnergyView</c> (expected activation value) and the thin <c>SpotView</c>
/// (DA energy value on <c>vDaBuy</c>/<c>vDaSell</c>).
/// </remarks>
public sealed record LinearTerm
{
    public required TermHeader Header { get; init; }

    /// <summary>The variable the coefficient applies to. Must be in the C2 §2
    /// vocabulary (INV-V-03).</summary>
    public required VarSymbol Variable { get; init; }

    /// <summary>Card. <c>[H]</c>. <b>Unit: EUR per unit of the variable</b>, per
    /// slot. Not a price — a coefficient. The Planner never sees a price series;
    /// that is what stops it re-deriving economics (C2 §9).</summary>
    public required ReadOnlyMemory<double> Coefficient { get; init; }

    public required ObjectiveSense Sense { get; init; }
}

/// <summary>
/// <b>Shape 2 of 5</b> (C2 §3.2). Breakpoints plus a declared curvature and
/// sense. MILP realisation: λ-formulation; LP-exact if curvature matches sense,
/// SOS2 otherwise.
/// </summary>
/// <remarks>
/// <b>Binary cost rule.</b> <c>(Concave, Maximize)</c> and <c>(Convex, Minimize)</c>
/// require <b>no</b> binaries and are LP-exact. Any other combination, and any
/// <see cref="Curvature.General"/>, requires SOS2 or binaries, and the count must
/// be declared in <see cref="ProblemClassHint"/> so that a solve-time regression
/// is attributable to a specific economic modelling choice rather than mysterious.
/// <para>
/// <b>Curvature is verified against the breakpoints, not trusted</b>
/// (INV-V-12, ADR-008). A term declaring <c>Concave</c> with a non-concave
/// breakpoint set is a contract violation, not a silently-wrong relaxation —
/// whether the solve takes 200 ms or 200 s depends entirely on this property
/// being true. A fitted capacity curve that turns out non-concave must be
/// declared <c>General</c> and pay for its binaries, never mis-declared
/// (L2 §2, AfrrCapacityView).
/// </para>
/// </remarks>
public sealed record PwlTerm
{
    public required TermHeader Header { get; init; }

    /// <summary>The abscissa.</summary>
    public required VarSymbol Variable { get; init; }

    /// <summary>Card. <c>[n]</c>. Unit: the variable's own unit.
    /// <b>Strictly increasing.</b></summary>
    public required ReadOnlyMemory<double> BreakpointsX { get; init; }

    /// <summary>Card. <c>[n]</c>. <b>Unit: EUR.</b></summary>
    public required ReadOnlyMemory<double> BreakpointsYEur { get; init; }

    /// <summary>Declared, and verified against <see cref="BreakpointsYEur"/>
    /// (INV-V-12).</summary>
    public required Curvature Curvature { get; init; }

    public required ObjectiveSense Sense { get; init; }

    /// <summary><see cref="Extrapolation.Forbid"/> adds bounds at the end
    /// breakpoints.</summary>
    public required Extrapolation Extrapolation { get; init; }
}

/// <summary>
/// <b>Shape 3 of 5</b> (C2 §3.3). A variable, a set of slots it must dominate, a
/// floor and a unit price. MILP realisation: <c>z ≥ expr[t] ∀t</c>,
/// <c>z ≥ floor</c>, cost <c>c·z</c>. <b>Introduces no binaries.</b>
/// </summary>
/// <remarks>
/// This is how <c>max(·)</c> becomes linear. <c>PeakView</c> emits one per active
/// tariff regime: <c>z_peak ≥ p_poi[t] ∀t ∈ window</c>,
/// <c>z_peak ≥ pPoiRealisedPeakMw</c>, objective <c>−peakPriceEurPerMw · z_peak</c>. No
/// binaries, no max operator, exact (ADR-008).
/// The floor is precisely <c>peak_to_go</c>, which is why the peak term must be
/// composed <b>last</b> (<see cref="StageId.Peak"/>, ADR-009).
/// </remarks>
public sealed record EpigraphTerm
{
    public required TermHeader Header { get; init; }

    /// <summary>The epigraph variable, e.g. <see cref="VarSymbol.ZPeak"/>.</summary>
    public required VarSymbol EpigraphVar { get; init; }

    /// <summary>The variable it must dominate, e.g. <see cref="VarSymbol.PPoi"/>.</summary>
    public required VarSymbol Dominates { get; init; }

    /// <summary>A <b>subset</b> of slots — this is how HLZF is expressed, and the
    /// reason this field is a slot <i>set</i> rather than a range (L2 §2,
    /// ADR-011 <c>AtypicalHlzf</c>).</summary>
    public required ReadOnlyMemory<SlotId> OverSlots { get; init; }

    /// <summary>Unit: the dominated variable's unit (MW for <c>pPoi</c>).
    /// <b>Realised peak so far, from L0</b> (C5 §2), adjusted upward for the
    /// unsettled gap using the engine's own modelled trajectory, and increased by
    /// <c>RiskProfile.PeakSafetyMarginMw</c> under degraded load quality
    /// (L2 §2).</summary>
    public required double Floor { get; init; }

    /// <summary>Unit: EUR/MW.</summary>
    public required double UnitPriceEurPerMw { get; init; }

    /// <summary>
    /// Range <c>[0,1]</c>. Fraction of the accounting period inside this horizon.
    /// </summary>
    /// <remarks>
    /// <b>This field matters.</b> The horizon is shorter than the accounting
    /// period, so the full period charge must not be applied to a partial window;
    /// the remaining period value is carried by <c>V</c> (ADR-007). Mis-setting
    /// it is the classic overweighting of peak and the most common way to make
    /// the engine pathologically peak-averse (L2 §2). INV-V-13 asserts it is in
    /// <c>[0,1]</c> and consistent with the calendar.
    /// </remarks>
    public required double ProrationFactor { get; init; }
}

/// <summary>What a <see cref="BoundTerm"/> constrains: a single variable or a
/// linear expression over variables (C2 §3.4).</summary>
public enum BoundTargetKind
{
    Variable,
    LinearExpr,
}

/// <summary>One <c>(variable, index, coefficient)</c> triple. <c>Index</c> is the
/// slot offset, block index or scenario index appropriate to the variable's
/// cardinality in C2 §2.</summary>
public readonly record struct LinearCoefficient(VarSymbol Variable, int Index, double Coefficient);

/// <summary>A linear expression <c>Σ coef·var</c> over the C2 §2 vocabulary.</summary>
public readonly record struct LinearExpr(ReadOnlyMemory<LinearCoefficient> Terms);

/// <summary>Discriminated target of a bound (C2 §3.4).</summary>
public readonly record struct BoundTarget
{
    public required BoundTargetKind Kind { get; init; }
    public VarSymbol? Variable { get; init; }
    public LinearExpr? Expression { get; init; }

    public static BoundTarget OfVariable(VarSymbol symbol) => throw new NotImplementedException();
    public static BoundTarget OfExpression(LinearExpr expr) => throw new NotImplementedException();
}

/// <summary>
/// <b>Shape 4 of 5</b> (C2 §3.4). A lower/upper bound on a decision variable or a
/// linear expression, per slot. MILP realisation: a constraint.
/// <b>Introduces no binaries.</b>
/// </summary>
/// <remarks>
/// <b>A <c>BoundTerm</c> has no price field.</b> That absence is load-bearing:
/// it is how the type system — rather than convention — enforces that
/// <c>FillProbView</c> constrains how much intraday volume may be counted on
/// while carrying no price (ADR-008, ADR-012, INV-V-16). There is deliberately
/// nothing here to populate.
/// </remarks>
public sealed record BoundTerm
{
    public required TermHeader Header { get; init; }

    public required BoundTarget Target { get; init; }

    /// <summary>Card. <c>[H]</c>, unit = the target's unit. <b>Null for a
    /// one-sided bound</b> (the whole side is absent, not individual elements).</summary>
    public ReadOnlyMemory<double>? Lower { get; init; }

    /// <summary>Card. <c>[H]</c>, unit = the target's unit. Null for one-sided.</summary>
    public ReadOnlyMemory<double>? Upper { get; init; }

    /// <summary>
    /// <b>Not decoration.</b> Feasibility restoration (L3 §6) relaxes bounds in a
    /// declared order — <c>Risk</c> first, then <c>Liquidity</c>, never
    /// <c>Physical</c> or <c>Regulatory</c>. Without the tag, an infeasible model
    /// can only be relaxed blindly. Every bound has one (INV-V-15).
    /// </summary>
    public required BoundReason Reason { get; init; }

    /// <summary>EUR per unit of violation. <c>null</c> = hard bound.
    /// <b>No <see cref="BoundReason.Physical"/> bound may be soft</b>
    /// (INV-V-15).</summary>
    public double? SoftPenalty { get; init; }
}

/// <summary>One constraint row: <c>Σ coef·var {≤,=,≥} rhs</c>, per slot or per
/// block (C2 §3.5).</summary>
public readonly record struct LinearRow(
    ReadOnlyMemory<LinearCoefficient> Coefficients,
    RowRelation Relation,
    double Rhs,
    SlotId? Slot,
    BlockId? Block);

/// <summary>Scenario scope of a coupling constraint (C2 §3.5).
/// <see cref="ScenarioScopeKind.ChanceLevel"/> carries ε: the constraint must
/// hold for a weighted scenario mass ≥ <c>1 − ε</c>.</summary>
public readonly record struct ScenarioScope(ScenarioScopeKind Kind, double? Epsilon)
{
    public static ScenarioScope All => throw new NotImplementedException();
    public static ScenarioScope PerScenario => throw new NotImplementedException();
    public static ScenarioScope ChanceLevel(double epsilon) => throw new NotImplementedException();
}

/// <summary>
/// <b>Shape 5 of 5</b> (C2 §3.5). A declared structural constraint spanning
/// decision variables. MILP realisation: constraint rows. May introduce
/// binaries — <b>declared</b>, and checked against what is actually built.
/// </summary>
/// <remarks>
/// This is the carrier of the cross-market coupling that ADR-010 is about:
/// power headroom (<c>p_d[t] + rUp[b(t)] ≤ P_max_dis</c>), the SOC corridor
/// (<c>soc[t] − rUp[b(t)]·D/η_d ≥ socMinMwh</c>), the activation-path chance
/// constraint, and confirmed commitments from the ledger.
/// <para>
/// It is also the <i>only</i> route by which scenario structure reaches the
/// Planner: through <see cref="Scope"/>, and through terms Valuation has already
/// aggregated. No scenario array crosses C2 (INV-V-06, C2 §9).
/// </para>
/// </remarks>
public sealed record CouplingConstraint
{
    public required TermHeader Header { get; init; }

    /// <summary>From the closed set.</summary>
    public required CouplingKind Kind { get; init; }

    public required IReadOnlyList<LinearRow> Rows { get; init; }

    public required ScenarioScope Scope { get; init; }

    /// <summary><b>Declared, and checked against what is built.</b> Feeds
    /// <see cref="ProblemClassHint.BinariesByOrigin"/>.</summary>
    public required int IntroducesBinaries { get; init; }
}

// =============================================================================
//  §4 The value function
// =============================================================================

/// <summary>Discrete peak state <c>V</c> was fitted at (C2 §4, ADR-011).</summary>
public readonly record struct PeakStateKey(string Value);

/// <summary>Discrete §19(2) qualification state <c>V</c> was fitted at. The cliff
/// is represented as a discrete state dimension, with a concave <c>V</c>
/// conditional on each state — concavity in SOC <i>given</i> the discrete state
/// (ADR-007, ADR-011).</summary>
public readonly record struct QualificationStateKey(string Value);

/// <summary>Calendar position <c>V</c> was fitted at (distance to period end,
/// season, weekday class), from the versioned calendar (ADR-002).</summary>
public readonly record struct CalendarContextKey(string Value);

/// <summary>What <c>V</c> is conditioned on: <c>(peakState, qualState,
/// calendarContext)</c> (C2 §4).</summary>
public readonly record struct ValueFunctionContext(
    PeakStateKey PeakState,
    QualificationStateKey QualState,
    CalendarContextKey CalendarContext);

/// <summary>
/// The end-of-horizon value function <c>V : SOC → EUR</c>, as a concave PWL curve
/// (C2 §4, ADR-007).
/// </summary>
/// <remarks>
/// Carried separately from the term list because it is structurally distinctive
/// and because its staleness is handled specially (C2 §4).
/// <para>
/// <b>Valuation does not publish a scalar λ_SOC.</b> λ is a dual of the Planner's
/// own problem; publishing it from L2 is the circularity ADR-006 and ADR-007
/// exist to remove. The Planner obtains λ endogenously as the subgradient of
/// <c>V</c> at the optimal terminal SOC and reports it in <c>PlanResult</c> for
/// diagnostics only (L2 §2, L3 §8). A scalar would price one point on a curve
/// whose whole significance is that its slope is state-dependent — near-empty
/// energy is worth much more than near-full energy when a peak event is possible.
/// </para>
/// <para>
/// Because <c>V</c> is concave and the objective is a maximisation, the PWL
/// requires <b>no binary variables</b> and the LP relaxation is exact. That is
/// the property that makes it affordable (ADR-007).
/// </para>
/// </remarks>
public sealed record ValueFunctionCurve
{
    /// <summary>Card. <c>[n]</c>. Unit MWh. <b>Strictly increasing</b>, spanning
    /// <c>[socMinMwh, socMaxMwh]</c>.</summary>
    public required ReadOnlyMemory<double> VSocBreakpointsXMwh { get; init; }

    /// <summary>Card. <c>[n]</c>. Unit EUR.</summary>
    public required ReadOnlyMemory<double> VSocBreakpointsYEur { get; init; }

    /// <summary>Card. <c>[n-1]</c>. Unit EUR/MWh. <b>Strictly decreasing</b> —
    /// concavity, asserted, not assumed (INV-V-11). The fitting step projects
    /// onto the concave hull; genuine non-concavity around a §19(2) cliff is
    /// represented as a separate discrete state, never smoothed away silently
    /// (ADR-007).</summary>
    public required ReadOnlyMemory<double> VSocSlopesEurPerMwh { get; init; }

    public required ValueFunctionContext ConditionedOn { get; init; }

    /// <summary>When the slow loop produced it.</summary>
    public required SlotId ProducedAt { get; init; }

    /// <summary>Beyond this, the staleness penalty applies. <c>V</c> beyond its
    /// validity window is a <c>DEFENSIVE</c> trigger (ADR-014 §2) and forces tier
    /// escalation (L3 §3).</summary>
    public required SlotSpan ValidityHorizon { get; init; }

    /// <summary>Range <c>[0,1]</c>. Shrink factor applied to the slopes when
    /// stale (L2 §6). <c>C_slow</c> output is an input, never a dependency: if
    /// the slow loop has not run, the Planner uses the last valid <c>V</c> with
    /// this penalty and never blocks (<c>01-system-model.md</c> §3).</summary>
    public required double StalenessPenalty { get; init; }

    /// <summary>For the Merkle chain (ADR-013). <c>V</c> is a first-class,
    /// versioned, content-hashed artefact.</summary>
    public required ContentHash ArtefactHash { get; init; }
}

// =============================================================================
//  §5 Reserve envelope
// =============================================================================

/// <summary>C2 §5. The reserve offer envelope and the confirmed obligations
/// inside it.</summary>
public sealed record ReserveEnvelope
{
    /// <summary>Card. <c>[B]</c>. Unit MW. Envelope from prequalification and
    /// asset limits.</summary>
    public required ReadOnlyMemory<double> RUpMaxMw { get; init; }

    /// <summary>Card. <c>[B]</c>. Unit MW.</summary>
    public required ReadOnlyMemory<double> RDnMaxMw { get; init; }

    /// <summary>Card. <c>[B]</c>. Concave in offered MW: the expected value of the
    /// capacity bid curve. Concavity arises from the clearing model — offering
    /// more MW lowers the probability of clearing at a good price — and is
    /// verified against the breakpoints, not assumed (L2 §2, INV-V-12).</summary>
    public required IReadOnlyList<PwlTerm> CapacityValueCurveUp { get; init; }

    /// <summary>Card. <c>[B]</c>. See <see cref="CapacityValueCurveUp"/>.</summary>
    public required IReadOnlyList<PwlTerm> CapacityValueCurveDn { get; init; }

    /// <summary>Prequalification <c>D</c>, echoed from C1 §7 for the corridor
    /// constraints.</summary>
    public required SlotSpan SustainDuration { get; init; }

    /// <summary>
    /// Card. <c>[B]</c>. Unit MW. Already-confirmed awards from L0 — a <b>hard</b>
    /// commitment.
    /// </summary>
    /// <remarks>
    /// Separated from the offer decision deliberately: confirmed awards are
    /// physical obligations that survive every degradation mode (ADR-014 §3),
    /// whereas offers are decisions. If the SOC corridor required to deliver a
    /// confirmed award cannot be met, the engine goes to <c>HALT</c> and alerts
    /// rather than quietly under-delivering.
    /// </remarks>
    public required ReadOnlyMemory<double> DeliveryObligationMw { get; init; }
}

// =============================================================================
//  §6 Risk profile
// =============================================================================

/// <summary>
/// C2 §6. How input quality reaches the optimisation <b>without becoming control
/// flow</b> (ADR-014 §1, L2 §6).
/// </summary>
/// <remarks>
/// Bad data does not take a different code path — it takes the same path with
/// more conservative parameters. There is one code path to test, and the
/// response to degradation is continuous and tunable rather than a cliff.
/// </remarks>
public sealed record RiskProfile
{
    /// <summary>e.g. 0.95. Widened when quality degrades.</summary>
    public required double CvarLevel { get; init; }

    /// <summary>Weight on the CVaR term versus the expectation. CVaR is
    /// linear-programmable (Rockafellar–Uryasev), so the risk term does not change
    /// the problem class — which is why CVaR and not a variance penalty
    /// (L2 §2).</summary>
    public required double CvarWeight { get; init; }

    /// <summary>ε for SOC feasibility chance constraints (ADR-010).</summary>
    public required double ChanceLevel { get; init; }

    /// <summary>Range <c>[0,1]</c>. Multiplier on speculative position bounds.</summary>
    public required double PositionScale { get; init; }

    /// <summary>Unit MW. Added to the epigraph floor under degraded load or PV
    /// quality (L2 §6). This is the degradation response for peak: a margin, not
    /// a branch.</summary>
    public required double PeakSafetyMarginMw { get; init; }

    /// <summary>Echoed from L0 via C1 §8.</summary>
    public required DegradationMode DegradationMode { get; init; }

    /// <summary><b>Which quality issue moved which parameter</b> — the audit
    /// trail. A conservative plan can always be explained by naming the input
    /// that caused it (L2 §6).</summary>
    public required IReadOnlyList<string> DriverSummary { get; init; }
}

// =============================================================================
//  §7 Problem class hint
// =============================================================================

/// <summary>
/// C2 §7. Computed by the composer from the terms present, <b>before the solver
/// sees anything</b>.
/// </summary>
/// <remarks>
/// This is the instrument that keeps solve time explainable. From the shapes
/// present the engine can report, pre-solve, the number of binaries, whether the
/// model is an LP, and which term introduced each binary. Solve-time regressions
/// become attributable to an economic modelling choice rather than mysterious
/// (ADR-008). When a solve slows down, <see cref="BinariesByOrigin"/> names the
/// term responsible.
/// </remarks>
public sealed record ProblemClassHint
{
    /// <summary>True when no term introduces a binary.</summary>
    public required bool IsLinearProgram { get; init; }

    public required int BinaryCount { get; init; }

    /// <summary><b>Attribution</b> — which term costs what. Keyed by
    /// <c>TermHeader.TermId</c>. Sums to <see cref="BinaryCount"/>
    /// (INV-V-14, warn).</summary>
    public required IReadOnlyDictionary<string, int> BinariesByOrigin { get; init; }

    public required int Sos2SetCount { get; init; }

    /// <summary><c>S</c>. A count, not an array — no scenario array crosses C2
    /// (INV-V-06).</summary>
    public required int ScenarioCount { get; init; }

    public required int EstimatedRowCount { get; init; }

    public required int EstimatedColCount { get; init; }
}

// =============================================================================
//  The payload
// =============================================================================

/// <summary>
/// <b>C2 payload.</b> The complete economic description of the tick, in the
/// closed algebra of ADR-008.
/// </summary>
/// <remarks>
/// <b>What deliberately does not cross C2</b> (C2 §9):
/// prices (the Planner sees coefficients on variables, never a price series —
/// this is what stops it re-deriving economics); scenario arrays (scenario
/// structure reaches the Planner only through <see cref="CouplingConstraint"/>
/// rows with a <see cref="ScenarioScope"/>, and through terms Valuation has
/// already aggregated); delegates and expression trees; and anything from Belief
/// that Valuation merely passed through unpriced — if the Planner needs it, a
/// view must own it and price or bound it.
/// <para>
/// The five shapes are carried in five typed collections rather than a
/// polymorphic list. The algebra is closed by construction: adding a sixth
/// collection is a visible, reviewable schema change, whereas adding a subtype to
/// a polymorphic list is not.
/// </para>
/// </remarks>
public sealed record ValuationBundle : ContractEnvelope
{
    // --- §1 envelope extensions ---

    /// <summary>The stages actually applied, in order — recorded for audit.
    /// Must satisfy the stage precondition table (ADR-009, L2 §4; INV-V-07).</summary>
    public required IReadOnlyList<StageId> CompositionOrder { get; init; }

    /// <summary>Which view owned which effect this tick (ADR-009). Every term's
    /// <c>OriginView</c> must be the owner of its effect here (INV-V-02).</summary>
    public required IReadOnlyDictionary<EconomicEffect, ViewId> EffectCoverage { get; init; }

    /// <summary>Effects claimed by no view, therefore <b>priced at zero</b>.
    /// Empty is the expected case. Logged; if any is in the critical set,
    /// escalate (INV-V-05). Unclaimed effects are a warning rather than a failure
    /// because silently missing revenue is safer than silently double-counting
    /// cost (ADR-009 §1).</summary>
    public required IReadOnlyList<EconomicEffect> UnclaimedEffects { get; init; }

    /// <summary>C2 §6.</summary>
    public required RiskProfile RiskProfile { get; init; }

    /// <summary>C2 §7.</summary>
    public required ProblemClassHint ProblemClassHint { get; init; }

    // --- §3 the five shapes ---

    /// <summary>Shape 1 (C2 §3.1).</summary>
    public required IReadOnlyList<LinearTerm> LinearTerms { get; init; }

    /// <summary>Shape 2 (C2 §3.2).</summary>
    public required IReadOnlyList<PwlTerm> PwlTerms { get; init; }

    /// <summary>Shape 3 (C2 §3.3). One per active tariff regime.</summary>
    public required IReadOnlyList<EpigraphTerm> EpigraphTerms { get; init; }

    /// <summary>Shape 4 (C2 §3.4).</summary>
    public required IReadOnlyList<BoundTerm> BoundTerms { get; init; }

    /// <summary>Shape 5 (C2 §3.5).</summary>
    public required IReadOnlyList<CouplingConstraint> CouplingConstraints { get; init; }

    // --- §4, §5 ---

    /// <summary>C2 §4. Carried separately from the term list.</summary>
    public required ValueFunctionCurve ValueFunction { get; init; }

    /// <summary>C2 §5.</summary>
    public required ReserveEnvelope ReserveEnvelope { get; init; }

    /// <summary>Number of aFRR product blocks, for the <c>[B]</c> cardinality
    /// check.</summary>
    public required int BlockCount { get; init; }
}

/// <summary>
/// The C2 invariant register (C2 §8). The most consequential set in the system:
/// these are what make "the economics is right" a machine-checked property
/// rather than a review outcome.
/// </summary>
/// <remarks>
/// <list type="table">
///   <item><term>INV-V-01</term><description>No <c>(effect, variable, slot)</c>
///     triple is priced by more than one term → <c>HALT</c>: double count. The
///     archetypal silent error — it produces a plausible number, passes every
///     type check, and biases strategy in a direction that looks like profit
///     (ADR-009).</description></item>
///   <item><term>INV-V-02</term><description>Every term's <c>originView</c> is the
///     owner of its <c>effect</c> in the ownership matrix → <c>HALT</c>.</description></item>
///   <item><term>INV-V-03</term><description>Every term references only symbols in
///     C2 §2 → <c>HALT</c>.</description></item>
///   <item><term>INV-V-04</term><description>Every mandatory term is present →
///     <c>HALT</c>.</description></item>
///   <item><term>INV-V-05</term><description><c>unclaimedEffects</c> are logged;
///     if any is in the critical set, escalate.</description></item>
///   <item><term>INV-V-06</term><description>No scenario array and no Belief
///     handle appears anywhere in the bundle → <c>HALT</c>.</description></item>
///   <item><term>INV-V-07</term><description><c>compositionOrder</c> satisfies the
///     stage precondition table (ADR-009) → <c>HALT</c>.</description></item>
///   <item><term>INV-V-11</term><description><c>vSocSlopesEurPerMwh</c> strictly decreasing
///     (concavity) → <c>HALT</c>.</description></item>
///   <item><term>INV-V-12</term><description>Declared <c>curvature</c> matches the
///     breakpoints → <c>HALT</c>.</description></item>
///   <item><term>INV-V-13</term><description><c>prorationFactor ∈ [0,1]</c> and
///     consistent with the calendar → <c>HALT</c>.</description></item>
///   <item><term>INV-V-14</term><description><c>binariesByOrigin</c> sums to
///     <c>binaryCount</c> → warn.</description></item>
///   <item><term>INV-V-15</term><description>Every <c>BoundTerm</c> has a
///     <c>reason</c>; no <c>Physical</c> bound is soft → <c>HALT</c>.</description></item>
///   <item><term>INV-V-16</term><description><c>FillProbView</c> emits only
///     <c>BoundTerm</c>; it has no priced term → <c>HALT</c>.</description></item>
/// </list>
/// <para>
/// INV-V-08 … INV-V-10 are unassigned in C2 §8 as of version 1.0. The gap is in
/// the source register and is preserved here rather than silently renumbered:
/// invariant IDs are stable identifiers (conventions §5) and reusing a retired
/// number would break every cross-reference to it.
/// </para>
/// </remarks>
public static class C2Invariants
{
    public const string NoDoubleCount = "INV-V-01";
    public const string OwnershipMatrixRespected = "INV-V-02";
    public const string KnownSymbolsOnly = "INV-V-03";
    public const string MandatoryTermsPresent = "INV-V-04";
    public const string UnclaimedEffectsLogged = "INV-V-05";
    public const string NoScenarioArraysOrHandles = "INV-V-06";
    public const string CompositionOrderValid = "INV-V-07";
    public const string ValueFunctionConcave = "INV-V-11";
    public const string CurvatureMatchesBreakpoints = "INV-V-12";
    public const string ProrationFactorValid = "INV-V-13";
    public const string BinaryAttributionSums = "INV-V-14";
    public const string BoundsTaggedAndPhysicalHard = "INV-V-15";
    public const string FillProbEmitsBoundsOnly = "INV-V-16";

    public static IReadOnlyList<string> All => throw new NotImplementedException();
}
