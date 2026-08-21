// -----------------------------------------------------------------------------
//  Flexbid.Btm.Abstractions — layer interfaces
//
//  The seams as code. Each interface cites the ADR it derives from; where the
//  shape of a signature is unusual, the ADR explains why, and the XML comment
//  says so rather than leaving the next reader to guess.
//
//  SIGNATURES ONLY.
// -----------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using Flexbid.Btm.Contracts;

namespace Flexbid.Btm.Abstractions;

// =============================================================================
//  L1 — Belief
// =============================================================================

/// <summary>A read result: a column slice over <c>[validFrom, validTo)</c>, with
/// the quality channel alongside (C0 §6).</summary>
public readonly record struct ColumnSlice<T>(
    SeriesId Series,
    SlotId ValidFrom,
    SlotSpan Length,
    ReadOnlyMemory<T> Values,
    ReadOnlyMemory<QualityStamp> Quality) where T : struct;

/// <summary>
/// The bitemporal Belief store (<b>ADR-004</b>).
/// </summary>
/// <remarks>
/// Every fact is stored with two times: <c>valid_time</c> (the slot the fact
/// describes) and <c>knowledge_time</c> (when the engine could first have known
/// it). Revisions are appended, never updated in place; <c>asOf</c> selects the
/// latest row at or before the read time, which is exactly what the engine saw
/// (ADR-004 §1, conventions §4.4).
/// <para>
/// <b>There is deliberately no overload of <see cref="Get{T}"/> without
/// <c>asOf</c>, and no API that can return a fact with
/// <c>knowledge_time &gt; asOf</c>.</b> The filter is applied inside the storage
/// layer against an index, not by the caller. Lookahead safety is therefore
/// <i>structural</i>, not disciplinary: to leak, a developer or an agent would
/// have to change the storage layer, not merely forget a filter (ADR-004 §1).
/// Adding a convenience overload that defaults <c>asOf</c> to "latest" would
/// silently delete the property this entire ADR exists to guarantee, and would
/// produce an excellent backtest and losing live performance. Do not add one.
/// </para>
/// <para>
/// The lookahead audit (<c>04-compliance/T6</c>) tests this: run a backtest with
/// all facts having <c>knowledge_time &gt; t</c> replaced by garbage and assert
/// bit-identical results. If any component peeks, the run diverges.
/// </para>
/// </remarks>
public interface IBeliefStore
{
    /// <summary>
    /// <b>The only read API.</b> <c>Get(series, validRange, asOf) → column slice</c>.
    /// </summary>
    /// <param name="series">The series to read.</param>
    /// <param name="validFrom">First slot the facts are about.</param>
    /// <param name="length">How many slots.</param>
    /// <param name="asOf">Knowledge-time boundary. Facts learned after this are
    /// not merely filtered out — they are unreachable.</param>
    ColumnSlice<T> Get<T>(SeriesId series, SlotId validFrom, SlotSpan length, SlotId asOf)
        where T : struct;

    /// <summary>Opens a cursor for sequential replay (ADR-004 §2, warm tier).</summary>
    IBeliefCursor OpenCursor(SlotId startSlot, SlotId asOf, SlotSpan hotWindow);
}

/// <summary>
/// A sequential replay position over the Belief store (<b>ADR-004 §2, §3</b>).
/// </summary>
/// <remarks>
/// The cursor owns the hot window: a preallocated struct-of-arrays buffer
/// covering <c>[t, t + H_hot]</c>, refilled in place each tick, holding only the
/// series and slots the current tick's contract requires. Memory is
/// <c>O(#series × H_hot × #scenarios)</c> <b>regardless of backtest length</b> —
/// nothing about a multi-year run grows with time.
/// <para>
/// Hot-path discipline applies here above all (ADR-001): struct-of-arrays,
/// <c>Span&lt;T&gt;</c>, <c>ArrayPool&lt;T&gt;</c>, no LINQ, no per-slot object
/// allocation. A year-long backtest with scenario ensembles is allocation-bound
/// long before it is CPU-bound.
/// </para>
/// </remarks>
public interface IBeliefCursor : IDisposable
{
    /// <summary>Current replay position.</summary>
    SlotId Position { get; }

    /// <summary>Current knowledge-time boundary. Non-decreasing (INV-G-08).</summary>
    SlotId AsOf { get; }

    /// <summary>Advances the position and refills the hot window in place.
    /// Returns false at the end of the replay range.</summary>
    bool Advance(SlotSpan by, SlotId newAsOf);

    /// <summary>Materialises the C1 payload for the current position. Cheap: the
    /// payload's arrays are views over the already-filled hot window
    /// (ADR-004 §3).</summary>
    BeliefSnapshot Snapshot(TickId tickId, ManifestId manifestId);
}

// =============================================================================
//  L2 — Valuation
// =============================================================================

/// <summary>
/// A view's closed published field table, declared <b>as data</b> and enforced
/// (L2 §1). A view that emits a field it did not declare fails: the closure of
/// the field table is a mechanism, not documentation.
/// </summary>
public sealed record FieldSchema
{
    public required ViewId Owner { get; init; }
    public required IReadOnlyList<string> DeclaredTermIds { get; init; }
    public required IReadOnlyList<EconomicEffect> DeclaredEffects { get; init; }
    public required IReadOnlyList<VarSymbol> DeclaredSymbols { get; init; }
}

/// <summary>What one view emits: terms in the five shapes of ADR-008, and nothing
/// else.</summary>
public sealed record ViewOutput
{
    public required ViewId Origin { get; init; }
    public required IReadOnlyList<LinearTerm> LinearTerms { get; init; }
    public required IReadOnlyList<PwlTerm> PwlTerms { get; init; }
    public required IReadOnlyList<EpigraphTerm> EpigraphTerms { get; init; }
    public required IReadOnlyList<BoundTerm> BoundTerms { get; init; }
    public required IReadOnlyList<CouplingConstraint> CouplingConstraints { get; init; }
}

/// <summary>
/// A valuation view (<b>ADR-008</b>, L2 §1).
/// </summary>
/// <remarks>
/// <b>Views are independent.</b> A view may not read another view's output, may
/// not share mutable state, and may not know that another view exists.
/// Composition is the composer's job alone. That independence is what makes each
/// view a small, individually testable pure function of a recorded C1 payload
/// (L2 §1, ADR-013 seam recording).
/// <para>
/// <see cref="Evaluate"/> is a <b>total function</b>: no I/O, no clock, no state,
/// no randomness (L2 preamble). Its two inputs are the whole of what it may know.
/// </para>
/// </remarks>
public interface IValuationView<TConfig>
{
    ViewId Id { get; }

    /// <summary>The closed field table, declared as data. <c>Evaluate</c>'s output
    /// is validated against it (L2 §1).</summary>
    FieldSchema PublishedSchema { get; }

    /// <summary>The effects this view claims. Checked for coverage and
    /// exclusivity against the ownership matrix (ADR-009, INV-V-02).</summary>
    IReadOnlyList<EconomicEffect> ClaimedEffects { get; }

    ViewOutput Evaluate(in BeliefSnapshot belief, in StateSnapshot state, in TConfig config);
}

/// <summary>
/// The composer (<b>ADR-009</b>, L2 §4).
/// </summary>
/// <remarks>
/// <b>The composer performs no economics.</b> It never adds, scales or reconciles
/// a term. If two terms would overlap, it <i>fails</i> rather than netting them —
/// netting would be economics, and economics belongs in a view where it can be
/// tested (L2 §4).
/// <para>
/// Responsibilities, in order (L2 §4): collect terms from every enabled view;
/// ownership check (coverage and exclusivity); stage precondition check;
/// curvature verification (INV-V-12); unit and range validation; risk profile
/// assembly; problem class hint; seal (compute <c>contentHash</c>, emit).
/// </para>
/// <para>
/// Stage ordering — declared by the precondition table in L2 §4, which is its
/// single source — is a <b>machine-checked property, not a convention</b>. Composing in the
/// wrong order raises rather than producing a plausible wrong number, which is
/// the entire point of ADR-009: an economic effect counted twice produces a
/// number that passes every type check and biases the strategy in a direction
/// that looks like profit.
/// </para>
/// </remarks>
public interface IValuationComposer
{
    /// <summary>The declared stage order for this configuration. Recorded in
    /// <c>ValuationBundle.CompositionOrder</c> for audit (INV-V-07).</summary>
    IReadOnlyList<StageId> CompositionOrder { get; }

    /// <summary>The normative term ownership matrix (L2 §5). Each effect has
    /// exactly one owner.</summary>
    IReadOnlyDictionary<EconomicEffect, ViewId> OwnershipMatrix { get; }

    ValuationBundle Compose(
        in BeliefSnapshot belief,
        in StateSnapshot state,
        IReadOnlyList<ViewOutput> viewOutputs,
        TickId tickId,
        ManifestId manifestId);
}

/// <summary>
/// A tariff regime plug-in (<b>ADR-011</b>).
/// </summary>
/// <remarks>
/// <c>PeakView</c> selects regimes from configuration and emits shapes
/// accordingly. <b>Regimes compose</b>: a site can be subject to more than one
/// simultaneously — an HLZF-based charge and a volumetric charge. Each regime
/// emits terms tagged with its own <see cref="EconomicEffect"/>, so the
/// composer's exclusivity check keeps them from overlapping.
/// <para>
/// The interface exists because <c>PeakView</c> must not hardcode <c>max(·)</c>:
/// <see cref="TariffRegimeId.AtypicalHlzf"/> is a max over a slot <i>subset</i>
/// and <see cref="TariffRegimeId.IntensiveUse"/> carries a discrete
/// qualification state. An engine that hardcodes <c>max</c> over the period
/// cannot express either, and a regime change would be a rewrite instead of a
/// configuration date plus a new implementation.
/// </para>
/// <para>
/// All numeric thresholds (7,000 h; 10 GWh; tier percentages; HLZF windows) are
/// configuration sourced from the published tables per DSO and per year, never
/// constants. Regulation here is actively moving.
/// </para>
/// </remarks>
public interface ITariffRegime
{
    TariffRegimeId Id { get; }

    /// <summary>The effects this regime claims (ADR-009).</summary>
    IReadOnlyList<EconomicEffect> ClaimedEffects { get; }

    /// <summary>The accounting period containing <paramref name="slot"/>,
    /// derived from the local civil calendar — never from UTC arithmetic
    /// (conventions §4.2).</summary>
    SlotRange AccountingPeriod(SlotId slot);

    /// <summary>The slot subset the charge applies to: all slots for
    /// <c>AnnualLeistungspreis</c>, HLZF slots only for <c>AtypicalHlzf</c>.
    /// This is what populates <c>EpigraphTerm.OverSlots</c>.</summary>
    ReadOnlyMemory<SlotId> ChargeableSlots(in BeliefSnapshot belief);

    /// <summary>Fraction of the accounting period inside this horizon
    /// (C2 §3.3, INV-V-13).</summary>
    double ProrationFactor(SlotRange horizon, SlotRange accountingPeriod);

    ViewOutput Emit(in BeliefSnapshot belief, in StateSnapshot state);
}

// =============================================================================
//  L3 — Planner
// =============================================================================

/// <summary>
/// The work limit for a solve (L3 §6, ADR-013).
/// </summary>
/// <remarks>
/// The limit is expressed in <b>deterministic work units</b> where the backend
/// supports it, <b>never wall clock</b> — a wall-clock limit makes the result
/// non-reproducible, which destroys the ability to detect small regressions
/// (ADR-013). The wall-clock field exists only as a backstop for backends that
/// cannot express deterministic limits, and using it means the run records that
/// it is not reproducible rather than pretending otherwise.
/// </remarks>
public sealed record SolveBudget
{
    public required long DeterministicWorkUnits { get; init; }
    public required double MipGapTolerance { get; init; }

    /// <summary>Whether this tick may escalate to a higher tier, and by how much
    /// extra work (ADR-010).</summary>
    public required long EscalationAllowanceWorkUnits { get; init; }

    /// <summary>Fixed seed. No runtime randomness anywhere else (ADR-013).</summary>
    public required int Seed { get; init; }

    /// <summary>Fixed thread count. Parallel reductions use a fixed partitioning
    /// scheme so summation order is stable (ADR-013).</summary>
    public required int ThreadCount { get; init; }

    /// <summary>Backstop only. Non-null means the run is not byte-reproducible
    /// and must record that fact.</summary>
    public TimeSpan? WallClockBackstop { get; init; }
}

/// <summary>A solver-independent MILP model (<b>ADR-001</b>): variables, linear
/// terms, PWL segments, epigraph constraints, indicator constraints, SOS2 sets.
/// The Planner constructs it; the backend translates it.</summary>
public sealed record MilpModel
{
    public required ObjectiveSense Sense { get; init; }
    public required int ColumnCount { get; init; }
    public required int RowCount { get; init; }
    public required int BinaryCount { get; init; }
    public required int Sos2SetCount { get; init; }

    /// <summary>Attribution back to the C2 term that caused each binary
    /// (C2 §7). Carried through the backend boundary so that a solve-time
    /// regression stays attributable.</summary>
    public required IReadOnlyDictionary<string, int> BinariesByOrigin { get; init; }
}

/// <summary>What a backend returns. <see cref="DualBound"/> is required, not
/// optional: it is what makes the gap a reported number rather than a hope
/// (ADR-010).</summary>
public sealed record BackendSolution
{
    public required bool IsFeasible { get; init; }
    public required Money ObjectiveValue { get; init; }
    public required Money DualBound { get; init; }
    public required double MipGap { get; init; }
    public required IReadOnlyDictionary<VarSymbol, ReadOnlyMemory<double>> PrimalValues { get; init; }

    /// <summary>Duals on the position accounting constraints — the source of
    /// <c>shadowValue</c> (L3 §5, ADR-012).</summary>
    public required IReadOnlyDictionary<VarSymbol, ReadOnlyMemory<double>> Duals { get; init; }

    /// <summary>The irreducible infeasible subsystem, when infeasible. The model
    /// must never be reported infeasible to the operator without a diagnosis
    /// (L3 §6).</summary>
    public IReadOnlyList<string>? InfeasibleSubsystem { get; init; }
}

/// <summary>
/// The solver abstraction (<b>ADR-001</b>).
/// </summary>
/// <remarks>
/// The solver choice is <b>OPEN-1</b> (ADR-015): Gurobi is intended but
/// deliberately not fixed, because solver choice should follow from a benchmark
/// on the real model rather than precede it. This interface exists precisely so
/// that it stays a configuration decision.
/// <para>
/// It must be designed against the <i>union</i> of what the ADR-010 tiers need,
/// so that switching backends cannot silently change the formulation. Backend
/// conformance is a test level (<c>T1</c>): the same model solved by two backends
/// must agree on objective value within tolerance. An LP-relaxation backend is
/// needed regardless of the solver choice, for the Tier-2 bound.
/// </para>
/// </remarks>
public interface IOptimizationBackend
{
    string BackendId { get; }

    /// <summary>Recorded in the run manifest; a version change is a manifest
    /// change and therefore a different run (ADR-013).</summary>
    string BackendVersion { get; }

    BackendSolution Solve(in MilpModel model, in SolveBudget budget);
}

/// <summary>The result of one co-optimisation, including everything the audit
/// trail needs (L3 §8).</summary>
public sealed record CoOptResult
{
    public required bool IsFeasible { get; init; }
    public required Money ObjectiveValue { get; init; }

    /// <summary>Objective decomposed by effect. This becomes
    /// <c>plannedByEffect</c> in C5 §5 and is what makes planned and realised
    /// value comparable term by term.</summary>
    public required IReadOnlyDictionary<EconomicEffect, Money> ObjectiveByEffect { get; init; }

    /// <summary>Certified dual bound. From Tier 2 the Lagrangian supplies it
    /// directly; from Tier 1 and Tier 3 it is the backend's bound
    /// (ADR-010).</summary>
    public required Money DualBound { get; init; }

    /// <summary>λ_SOC at the optimum — the subgradient of <c>V</c> at the optimal
    /// terminal SOC. <b>An output, not an input</b>: this is the inversion that
    /// removes the cycle (ADR-007, ADR-006). Diagnostics only.</summary>
    public required double LambdaSoc { get; init; }

    /// <summary><c>μ*</c> per block, when Tier 2 ran: the internal reservation
    /// price of a MW of battery headroom. Interpretable and human-checkable,
    /// unlike a black-box policy (ADR-010).</summary>
    public IReadOnlyDictionary<BlockId, double>? MuUp { get; init; }

    /// <summary>See <see cref="MuUp"/>.</summary>
    public IReadOnlyDictionary<BlockId, double>? MuDn { get; init; }

    /// <summary>Which bounds were relaxed, and by how much. Relaxation is done
    /// with explicit slack variables at declared penalties, never by deleting
    /// constraints, so the amount and location is visible and recorded
    /// (L3 §6).</summary>
    public required IReadOnlyList<string> RelaxationsApplied { get; init; }

    /// <summary>Whether the tick escalated, and why. Escalation is logged and
    /// appears in the settlement record (L3 §3).</summary>
    public required IReadOnlyList<string> Escalations { get; init; }
}

/// <summary>
/// The three-tier cross-market co-optimisation ladder (<b>ADR-010</b>, L3 §3).
/// </summary>
/// <remarks>
/// One interface, three tiers, <b>a mandatory measured gap between them</b>.
/// The production tier is a configuration choice, and a backtest can run all
/// three on the same data for comparison.
/// <para>
/// The instrument the ladder is built around is a <b>dual price</b>, not a primal
/// restriction. Pre-allocating capacity deletes options from the feasible set;
/// if the allocation is wrong the loss is unbounded and, worse, <i>invisible</i>,
/// because the spot MILP reports a clean optimum over a feasible set that was
/// silently amputated. Charging the spot problem for the headroom it consumes
/// weakly dominates: it recovers the same solution when the allocation happens to
/// be right and does better whenever it is wrong.
/// </para>
/// <para>
/// <b>No tier ships without its gap measured against Tier 1</b>
/// (<c>04-compliance/T5</c>): the distribution reported as p50/p90/p99 and worst
/// case, never as a mean, because the tail is where a fast heuristic destroys
/// value; conditioned on regime; and with the distance from Tier 1 to a
/// perfect-foresight bound, which separates "our optimiser is weak" from "the
/// world is uncertain".
/// </para>
/// <para>
/// <b>Every exit path returns a feasible primal</b>, including the
/// cap-exhausted path. A decomposition that can return infeasible is not usable
/// in production (L3 §3).
/// </para>
/// </remarks>
public interface ICoOptimizer
{
    /// <summary>Which tier this instance is configured as; it may report a
    /// different <c>TierUsed</c> if it escalates.</summary>
    TierUsed ConfiguredTier { get; }

    /// <summary>
    /// Solves the tick. Escalates to a higher tier within
    /// <see cref="SolveBudget.EscalationAllowanceWorkUnits"/> when the solution
    /// sits within tolerance of a coupling constraint boundary, the state is
    /// outside the training envelope of <c>μ̂</c>, <c>peakCritical</c> or
    /// <c>qualCritical</c> is set, or <c>V</c> is stale (L3 §3).
    /// </summary>
    CoOptResult Solve(in ValuationBundle valuation, in StateSnapshot state, in SolveBudget budget,
                      out TierUsed tier);
}

/// <summary>
/// Recorded but <b>not transmitted</b> (L3 §8): the diagnostic record of a solve,
/// written for settlement and for the counterfactual re-runs in C5 §6.
/// </summary>
public sealed record PlanResult
{
    public required string PlanId { get; init; }
    public required TickId TickId { get; init; }
    public required ContentHash ValuationBundleHash { get; init; }
    public required CoOptResult CoOpt { get; init; }
    public required TierUsed TierUsed { get; init; }
    public required double MipGap { get; init; }

    /// <summary>Deterministic work consumed, not wall-clock elapsed
    /// (ADR-013).</summary>
    public required long WorkUnitsConsumed { get; init; }

    /// <summary>The full planned trajectory, keyed by symbol. Required for the
    /// counterfactual re-runs that produce the four error buckets
    /// (C5 §6).</summary>
    public required IReadOnlyDictionary<VarSymbol, ReadOnlyMemory<double>> PlannedTrajectory { get; init; }
}

// =============================================================================
//  L3/L4 boundary — quoting and execution
// =============================================================================

/// <summary>
/// <c>P(fill | price, time-to-gate)</c> for one product and slot. Available to
/// the quoting policy <b>only</b> (ADR-012).
/// </summary>
/// <remarks>
/// The Planner sees fill probability as a <c>BoundTerm</c> with no price — how
/// much volume may be counted on — while the quoting policy sees the full curve,
/// because the price/probability trade-off is precisely what it exists to make.
/// Two distinct uses, deliberately kept apart, and the separation is enforced by
/// the type system: a <c>BoundTerm</c> has no price field to populate
/// (INV-V-16).
/// <para>
/// <b>OPEN-3</b> (ADR-015): the fidelity of this model is deferred until the
/// existing simulator's behaviour is characterised. Its accuracy target is
/// defined by the simulator, not by the real market in the abstract — a narrower
/// and much more tractable problem. Standing caveat: whatever is calibrated
/// against the simulator inherits the simulator's biases.
/// </para>
/// </remarks>
public sealed record FillProbabilityCurve
{
    public required MarketId Market { get; init; }
    public required string ProductId { get; init; }
    public required SlotId Slot { get; init; }
    public required OrderSide Side { get; init; }
    public required ReadOnlyMemory<EnergyPrice> PriceGrid { get; init; }
    public required ReadOnlyMemory<double> FillProbability { get; init; }
    public required SlotSpan TimeToGate { get; init; }
    public required ContentHash ArtefactHash { get; init; }
}

/// <summary>Microstructure state available to the quoting policy. Never crosses
/// C2 (C2 §9) and never enters the MILP (ADR-012).</summary>
public sealed record MicrostructureState
{
    public required MarketId Market { get; init; }
    public required string ProductId { get; init; }
    public required SlotId Slot { get; init; }
    public required EnergyPrice BestBid { get; init; }
    public required EnergyPrice BestAsk { get; init; }
    public required EnergyKwh DepthAtBest { get; init; }
    public required SlotSpan TimeToGate { get; init; }
}

/// <summary>
/// The quoting policy (<b>ADR-012</b>, L3 §5).
/// </summary>
/// <remarks>
/// Maps <c>(target position, shadowValue, urgency, fill curve, microstructure
/// state) → a ladder of limit orders</c>. The Planner answers "what net position
/// do I want?"; turning that into a limit price involves fill probability,
/// urgency, adverse selection and the shape of the remaining trading window, and
/// is not naturally a MILP variable — price is a control on a stochastic fill
/// process, not a physical quantity. Conflating the two either loads the MILP
/// with non-convexities it cannot handle or buries the quoting logic inside the
/// optimiser where it cannot be tested or tuned.
/// <para>
/// <b>Hard rule: never quote through the shadow value</b>, except for intents
/// tagged <see cref="StrategyTag.CommitmentCover"/> (INV-P-10). At a price worse
/// than the shadow value, the trade destroys value. Everything between the
/// current market and the shadow value is the region this policy is free to work
/// in.
/// </para>
/// <para>
/// Because it is a separate component, it can be replaced, A/B tested and tuned
/// with the Planner held fixed — which is also why its contribution to P&amp;L is
/// separately measurable as the execution-slippage bucket (C5 §6).
/// </para>
/// </remarks>
public interface IQuotingPolicy
{
    IReadOnlyList<OrderIntent> Quote(
        in PlanResult plan,
        IReadOnlyList<FillProbabilityCurve> fillCurves,
        IReadOnlyList<MicrostructureState> microstructure,
        DegradationMode mode);

    /// <summary>DA is different: it requires a monotone bid curve, produced by
    /// parametric re-solve over a grid of candidate clearing prices (ADR-012,
    /// L3 §5). Monotonicity is asserted, never sorted into compliance
    /// (INV-P-09).</summary>
    IReadOnlyList<DaBidCurve> BuildDaCurves(in PlanResult plan);
}

/// <summary>
/// The adapter onto the pre-existing market simulator (<b>ADR-012</b>,
/// <c>01-system-model.md</c> §5).
/// </summary>
/// <remarks>
/// Execution is an <b>external system</b> and is strategy-independent by design.
/// This adapter translates intent into whatever the simulator or a live venue
/// requires; it must stay thin enough that replacing the simulator with a real
/// venue adapter is a local change.
/// <para>
/// <b>It consumes <see cref="ExecutionIntentProjection"/>, not
/// <see cref="ExecutionIntent"/>.</b> That is the structural enforcement of
/// INV-X-04: <c>shadowValue</c> and <c>urgency</c> are absent from the projected
/// type, so Execution cannot read them even by accident. Widening this parameter
/// to the full payload would silently open the side channel the split exists to
/// close.
/// </para>
/// </remarks>
public interface IExecutionAdapter
{
    ExecutionOutcome Submit(in ExecutionIntentProjection intent);
}

// =============================================================================
//  L5 — Settlement, and L0 — State
// =============================================================================

/// <summary>The full settlement artefact: the C5 payload plus the recomputed
/// ex-post accounting it derives from (ADR-013 names it among the
/// content-addressed artefacts).</summary>
public sealed record SettlementResult
{
    public required StateUpdate StateUpdate { get; init; }
    public required Money TotalRealisedPnl { get; init; }
    public required bool IsFinal { get; init; }
    public required ContentHash ContentHash { get; init; }
}

/// <summary>
/// Settlement (<b>ADR-006</b> for the backwards edge, <b>ADR-013</b> for the
/// counterfactuals, C5 §6 for the decomposition).
/// </summary>
/// <remarks>
/// A <b>pure function</b> of a recorded input, which is exactly what makes the
/// four-bucket error decomposition computable: each bucket is a counterfactual
/// re-run.
/// <para>
/// <b>Settlement has no access to what the Planner intended</b> beyond
/// <c>intentId</c> as an opaque key (C4 §2, §8), so that ex-post accounting
/// cannot be contaminated by ex-ante belief. It recomputes truth from realised
/// data; if it needed a forecast it would be doing valuation, not settlement.
/// </para>
/// </remarks>
public interface ISettlementEngine
{
    /// <summary>Recomputes ex-post truth and produces the C5 payload.</summary>
    SettlementResult Settle(in ExecutionOutcome outcome, in StateSnapshot priorState,
                            TickId tickId, ManifestId manifestId);

    /// <summary>The four-bucket decomposition (C5 §6). Requires the recorded
    /// <see cref="PlanResult"/> and the recorded upstream payloads, because each
    /// bucket is a counterfactual re-run over them.</summary>
    ErrorDecomposition Decompose(in ExecutionOutcome outcome, in PlanResult plan,
                                 in ValuationBundle valuation, in BeliefSnapshot belief);
}

/// <summary>
/// L0, the State/Value Store (<b>ADR-006</b>).
/// </summary>
/// <remarks>
/// <b>The only backwards path, and it is always read at a lag of at least one
/// tick.</b> L0 is snapshotted at the top of the tick into an immutable
/// <see cref="StateSnapshot"/> and written only at the end from L5's output.
/// No component reads a value written later in the same tick — which is
/// checkable, and checked (<c>04-compliance/T3</c> acyclicity audit).
/// <para>
/// The read and write methods are deliberately separate and deliberately
/// asymmetric in time: there is no "read the current value" API that could
/// observe a same-tick write. Adding one would make the dependency graph cyclic
/// again and invalidate every claim that rests on the pipeline being a DAG.
/// </para>
/// <para>
/// L0 is durably persisted with the same content-addressing as everything else
/// (ADR-013), because it <i>is</i> the entire restart state.
/// </para>
/// </remarks>
public interface IStateStore
{
    /// <summary>Snapshot at the top of a tick. The returned value is immutable
    /// for the whole tick.</summary>
    StateSnapshot Snapshot(TickId tickId, ManifestId manifestId);

    /// <summary>Write at the end of a tick, from L5's output. Append-only;
    /// restatements are new artefacts carrying <c>revisionOf</c>
    /// (INV-S-08).</summary>
    void Apply(in StateUpdate update);
}

/// <summary>
/// The slow loop (<b>ADR-007</b>, <b>ADR-011</b>; clock <c>C_slow</c>, ADR-002).
/// </summary>
/// <remarks>
/// Produces <c>V(SOC, peakState, qualState)</c> by a rolling-horizon or
/// approximate dynamic programming solve over a horizon long enough to see the
/// end of the accounting period, using the joint ensemble (ADR-005).
/// <para>
/// <b>Its output is an input, never a dependency.</b> If the slow loop has not
/// run, the Planner uses the last valid <c>V</c> with a staleness penalty and
/// never blocks on it (<c>01-system-model.md</c> §3, ADR-014).
/// </para>
/// <para>
/// This is the piece with the most headroom: a better <c>V</c> improves every
/// decision downstream, and it is independently testable — does a better <c>V</c>
/// produce better realised P&amp;L? (ADR-007).
/// </para>
/// </remarks>
public interface ISlowLoop
{
    /// <summary>Fits <c>V</c> conditional on the discrete state. Concavity is
    /// enforced by projecting onto the concave hull, never assumed; genuine
    /// non-concavity around a §19(2) cliff is represented as a separate discrete
    /// state (ADR-007, INV-V-11).</summary>
    ValueFunctionCurve FitValueFunction(in StateSnapshot state, in BeliefSnapshot longHorizonBelief);

    /// <summary>Whether a refresh is due, from <c>vSocStale</c> and
    /// <c>stateDriftSignal</c> (C5 §8). Lets the loop be event-driven rather than
    /// purely periodic.</summary>
    bool ShouldRefresh(in ValueFunctionRefresh trigger);
}

// =============================================================================
//  Run manifest
// =============================================================================

/// <summary>
/// The run manifest (<b>ADR-013</b>). <b>Two runs with the same manifest must
/// produce byte-identical outputs</b>, and that is tested (<c>T3</c>), not
/// asserted.
/// </summary>
public sealed record RunManifest
{
    public required ManifestId Id { get; init; }
    public required string CodeRevision { get; init; }
    public required ContentHash ConfigurationHash { get; init; }

    /// <summary>Gate times, product blocks, HLZF tables, tick and lot sizes — all
    /// data, replayed with the run. A backtest of 2024 uses the 2024 calendar
    /// (ADR-002).</summary>
    public required ContentHash MarketCalendarHash { get; init; }

    /// <summary>Pinned IANA tzdata version. Civil time is itself versioned data
    /// (conventions §4.2).</summary>
    public required string TzdataVersion { get; init; }

    /// <summary>Scenario generator, value function, fill model, tier-3 surface —
    /// every model artefact, by hash (ADR-004 §4).</summary>
    public required IReadOnlyDictionary<string, ContentHash> ModelArtefacts { get; init; }

    /// <summary>The data snapshot boundary: <c>asOf</c> range and the ingestion
    /// watermark.</summary>
    public required SlotRange AsOfRange { get; init; }

    public required string SolverIdentity { get; init; }
    public required string SolverVersion { get; init; }

    /// <summary>Seed, thread count and deterministic work limit. Where a backend
    /// cannot be made deterministic under parallelism, the reproducibility run
    /// uses single-threaded mode and the production run records its
    /// non-determinism explicitly rather than pretending (ADR-013).</summary>
    public required SolveBudget SolverDeterminismSettings { get; init; }
}
