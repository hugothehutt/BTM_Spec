// -----------------------------------------------------------------------------
//  Flexbid.Btm.Contracts — Envelope, identity, quality, validation
//
//  Normative source: C0-contract-conventions.md (§1 what a contract is,
//  §3 universal invariants, §4 validation placement, §6 quality metadata,
//  §7 identity and keying), ADR-013 (content addressing), ADR-014 (quality).
//
//  SIGNATURES ONLY.
// -----------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;

namespace Flexbid.Btm.Contracts;

// =============================================================================
//  1. Identity (C0 §7)
// =============================================================================

/// <summary>
/// Identifies a tick uniquely: <c>(SlotId, clock, sequence)</c> (C0 §7).
/// The clock is part of the identity because two solves at the same wall time
/// from different clocks are different artefacts
/// (<c>00-overview/01-system-model.md</c> §3).
/// </summary>
public readonly record struct TickId(SlotId Slot, ClockId Clock, int Sequence);

/// <summary>The battery. Present in every contract from the start so that adding
/// a second asset is not a schema break (C0 §7); multi-asset is out of scope for
/// v1 (<c>00-overview/01-system-model.md</c> §5).</summary>
public readonly record struct AssetId(string Value);

/// <summary>The connection point. See <see cref="AssetId"/>.</summary>
public readonly record struct PoiId(string Value);

/// <summary>Hash of the run manifest (ADR-013): code revision, configuration,
/// market calendar artefact, tzdata version, every model artefact, the data
/// snapshot boundary, solver identity, version and determinism settings.
/// Two runs with the same manifest must produce byte-identical outputs.</summary>
public readonly record struct ManifestId(string Value);

/// <summary>Contract schema version. A consumer rejects a version it does not
/// understand rather than guessing (C0 §1, INV-G-03).</summary>
public readonly record struct SchemaVersion(int Major, int Minor);

/// <summary>Content hash of a payload or artefact (ADR-013). Forms a Merkle
/// chain over the tick together with <c>inputHashes</c>, which localises which
/// upstream input diverged when a downstream artefact differs between runs.</summary>
public readonly record struct ContentHash(string Value);

/// <summary>An aFRR product block index. Blocks are defined by the versioned
/// market calendar (ADR-002); every delivery slot is covered by exactly one
/// block (INV-D-04).</summary>
public readonly record struct BlockId(int Value);

/// <summary>A named data series. The key of the quality map (C1 §8, C5 §7).</summary>
public readonly record struct SeriesId(string Value);

/// <summary>A valuation view. The unit of ownership in the term ownership matrix
/// (ADR-009, L2 §5).</summary>
public readonly record struct ViewId(string Value);

/// <summary>
/// Confidence propagated from input quality into a term header (C2 §3).
/// <b>Unit: dimensionless, 0..1.</b> It feeds <c>riskProfile</c>; it is never a
/// branch (ADR-014 §1).
/// </summary>
public readonly record struct Confidence(double Value);

// =============================================================================
//  2. Shape carriers (C0 §2 cardinalities)
// =============================================================================

/// <summary>
/// A contiguous slot range, half-open <c>[Start, Start + Length)</c>.
/// Used by <c>TermHeader.slots</c> (C2 §3). Where a term applies to a
/// non-contiguous <i>set</i> of slots — HLZF windows — an explicit
/// <c>SlotId[]</c> is used instead (C2 §3.3 <c>overSlots</c>).
/// </summary>
public readonly record struct SlotRange(SlotId Start, SlotSpan Length)
{
    public bool Contains(SlotId slot) => throw new NotImplementedException();
}

/// <summary>
/// A scenario × slot array, cardinality <c>[S,H]</c>, stored row-major:
/// element <c>(s, h)</c> is at flat index <c>s * H + h</c>.
/// </summary>
/// <remarks>
/// The scenario axis is <b>shared</b>: index <c>s</c> denotes the same state of
/// the world in every series in the payload (ADR-005, INV-D-05, INV-G-07).
/// Any code that indexes one series by <c>s</c> and another by <c>s'</c> is a
/// defect. This carrier exists so that the shape is checkable in one place
/// rather than by convention at every call site.
/// Storage is <c>float32</c> for ensembles where source precision does not
/// justify more, <c>float64</c> for anything entering the objective
/// (conventions §6) — the choice is made by the instantiating type argument.
/// </remarks>
public readonly record struct ScenarioSlotMatrix<T>(ReadOnlyMemory<T> Flat, int ScenarioCount, int SlotCount)
    where T : struct
{
    /// <summary>Row <c>s</c>: the <c>[H]</c> slice for one coherent state of the world.</summary>
    public ReadOnlySpan<T> Scenario(int s) => throw new NotImplementedException();

    public T this[int s, int h] => throw new NotImplementedException();
}

/// <summary>
/// A scenario × block array, cardinality <c>[S,B]</c>, row-major
/// (<c>s * B + b</c>). Same shared-axis rule as <see cref="ScenarioSlotMatrix{T}"/>.
/// </summary>
public readonly record struct ScenarioBlockMatrix<T>(ReadOnlyMemory<T> Flat, int ScenarioCount, int BlockCount)
    where T : struct
{
    public T this[int s, int b] => throw new NotImplementedException();
}

// =============================================================================
//  3. Quality metadata (C0 §6, ADR-014 §1)
// =============================================================================

/// <summary>
/// The parallel quality channel (C0 §6). Quality is carried <i>alongside</i> the
/// value, never embedded in it: there is no sentinel, and
/// <b>NaN is never a valid value in a contract</b> (conventions §6, INV-G-01).
/// </summary>
/// <remarks>
/// Carried per series where quality is uniform across the array, and per element
/// where it is not — a forecast with a measured prefix and a forecast suffix is
/// the routine case (C1 §3).
/// Consumers map quality to risk parameters, never to branches (ADR-014 §1);
/// the mapping is the table in L2 §6 and its output lands in
/// <c>RiskProfile.driverSummary</c>.
/// </remarks>
/// <param name="Provenance">Measured | Forecast | Revised | Imputed | Default.</param>
/// <param name="Quality">Good | Degraded | Stale | Imputed | Missing.</param>
/// <param name="KnowledgeTime">When the engine could first have known this fact
/// (conventions §4.4). Never greater than the payload's <c>asOf</c> (INV-D-02).</param>
/// <param name="SourceId">Which source supplied it; the fallback rung is
/// recoverable from <paramref name="Provenance"/> plus this (ADR-014 §4).</param>
/// <param name="MaxStaleBreached">True when the value is older than the field's
/// declared <c>MaxStale</c> (C0 §2).</param>
public readonly record struct QualityStamp(
    Provenance Provenance,
    QualityLevel Quality,
    SlotId KnowledgeTime,
    SeriesId SourceId,
    bool MaxStaleBreached);

/// <summary>Per-series quality statistics over a settlement period; feeds
/// data-source SLAs (C5 §7).</summary>
public readonly record struct QualityStats(
    int TotalObservations,
    int GoodCount,
    int DegradedCount,
    int StaleCount,
    int ImputedCount,
    int MissingCount,
    SlotSpan MaxObservedStaleness);

// =============================================================================
//  4. The envelope (C0 §7)
// =============================================================================

/// <summary>
/// The identity fields every payload on every seam carries (C0 §7), plus the
/// versioning and content-addressing fields from C0 §1.
/// </summary>
/// <remarks>
/// A contract is a <b>value</b>: immutable, serialisable, versioned,
/// content-addressed. No references to mutable state, no handles into another
/// layer's storage, no delegates, no <c>object</c>, no dictionary of extras
/// (C0 §1). If it is not in the field table, it does not cross.
/// <para>
/// <b>No wall-clock field.</b> There is deliberately no construction timestamp:
/// INV-G-05 asserts that no payload contains a wall-clock timestamp taken at
/// construction, and ADR-013 forbids any component reading the system clock.
/// Time enters through <see cref="TickId"/> and <see cref="AsOf"/>, both supplied
/// by the driver.
/// </para>
/// </remarks>
public abstract record ContractEnvelope
{
    /// <summary>Identifies the tick uniquely (C0 §7).</summary>
    public required TickId TickId { get; init; }

    /// <summary>Knowledge-time boundary for every fact inside. No fact in the
    /// payload has <c>knowledgeTime &gt; asOf</c> (INV-D-02). Non-decreasing
    /// across successive payloads on the same seam (INV-G-08).</summary>
    public required SlotId AsOf { get; init; }

    /// <summary>Slot span covered. Every <c>[H]</c> array in the payload has this
    /// length (INV-G-06).</summary>
    public required SlotSpan Horizon { get; init; }

    /// <summary>The battery.</summary>
    public required AssetId AssetId { get; init; }

    /// <summary>The connection point.</summary>
    public required PoiId PoiId { get; init; }

    /// <summary>The run manifest hash (ADR-013).</summary>
    public required ManifestId ManifestId { get; init; }

    /// <summary>Rejected by the consumer if unrecognised (INV-G-03).</summary>
    public required SchemaVersion SchemaVersion { get; init; }

    /// <summary>Hash of this payload's own contents (INV-G-04).</summary>
    public required ContentHash ContentHash { get; init; }

    /// <summary>Hashes of the payloads that produced this one. Present and
    /// non-empty (INV-G-04); this is the Merkle chain edge.</summary>
    public required IReadOnlyList<ContentHash> InputHashes { get; init; }
}

// =============================================================================
//  5. Validation (C0 §3, §4)
// =============================================================================

/// <summary>A single invariant failure, with enough context to act on it.</summary>
/// <param name="InvariantId">Invariant ID, e.g. <c>INV-G-01</c>, <c>INV-V-12</c>
/// (naming: conventions §5).</param>
/// <param name="FieldPath">Payload path of the offending field, e.g.
/// <c>pwlTerms[3].breakpointsYEur[7]</c>.</param>
/// <param name="Message">Human-readable diagnosis.</param>
/// <param name="Action">What the failure costs. Consumer-side failures are
/// always <see cref="ViolationAction.Halt"/> (C0 §4).</param>
public readonly record struct ContractViolation(
    string InvariantId,
    string FieldPath,
    string Message,
    ViolationAction Action);

/// <summary>Result of a validation pass. Empty violations means the payload is
/// admissible on this seam.</summary>
public readonly record struct ContractValidation(
    bool IsValid,
    IReadOnlyList<ContractViolation> Violations)
{
    /// <summary>The most severe action among the violations, or none.</summary>
    public ViolationAction? RequiredAction => throw new NotImplementedException();
}

/// <summary>
/// The shared seam validator (C0 §3, §4).
/// </summary>
/// <remarks>
/// <b>Invoked twice per seam.</b> The producer validates before emitting —
/// catching its own bugs at the source, with its own context available — and the
/// consumer validates on receipt, defending against a producer it does not
/// control (C0 §4).
/// <para>
/// <b>Producer-side failure</b> enters the producing layer's degradation ladder.
/// <b>Consumer-side failure is <c>HALT</c></b>, unconditionally, because a
/// contract violation means the producer is in an unknown state and nothing
/// downstream can be trusted (C0 §4, ADR-014 §2).
/// </para>
/// <para>
/// <b>Validation is on by default in every environment, including production.</b>
/// It is a few microseconds against a solve measured in milliseconds. The
/// temptation to disable it in production is precisely the temptation to stop
/// noticing corruption (C0 §3). There is deliberately no <c>enabled</c> flag on
/// this interface.
/// </para>
/// </remarks>
public interface IContractValidator
{
    /// <summary>Applies the universal invariants (<see cref="UniversalInvariants"/>)
    /// plus the seam-specific register for the payload's concrete type.</summary>
    ContractValidation Validate<TPayload>(in TPayload payload) where TPayload : ContractEnvelope;
}

/// <summary>
/// The universal invariants applied at <b>every</b> seam by
/// <see cref="IContractValidator"/> (C0 §3). Seam-specific registers are in
/// C1 §9 (<c>INV-D-*</c>), C2 §8 (<c>INV-V-*</c>), C3 §5 (<c>INV-P-*</c>),
/// C4 §7 (<c>INV-X-*</c>) and C5 §9 (<c>INV-S-*</c>).
/// </summary>
/// <remarks>
/// <list type="table">
///   <item><term>INV-G-01</term><description>No <c>NaN</c>, no infinity, in any
///     numeric field. Missing is expressed by an explicit quality/provenance
///     field, never by a sentinel (conventions §6).</description></item>
///   <item><term>INV-G-02</term><description>Every dimensioned field's unit
///     suffix equals its declared unit, every power and energy field names its
///     frame, and every dimensionless field declares a kind and a closed range
///     (conventions §5.2, §2.3). Checked off the contract field tables by
///     <c>07-verification/check_units.py</c>.</description></item>
///   <item><term>INV-G-03</term><description><c>schemaVersion</c> is recognised
///     by the consumer.</description></item>
///   <item><term>INV-G-04</term><description><c>contentHash</c> matches the
///     payload; <c>inputHashes</c> are present and non-empty.</description></item>
///   <item><term>INV-G-05</term><description>No payload contains a wall-clock
///     timestamp taken at construction. Enforced additionally by an analyzer rule
///     (ADR-013).</description></item>
///   <item><term>INV-G-06</term><description>All slot-indexed arrays have the
///     length declared by <c>horizon</c>.</description></item>
///   <item><term>INV-G-07</term><description>All scenario-indexed arrays share the
///     scenario axis length <i>and ordering</i> (ADR-005).</description></item>
///   <item><term>INV-G-08</term><description><c>asOf</c> is non-decreasing across
///     successive payloads on the same seam.</description></item>
///   <item><term>INV-G-09</term><description>Every field marked <c>Null = no</c>
///     is present.</description></item>
///   <item><term>INV-G-10</term><description>Ranges hold for <i>every</i> element,
///     not merely the first.</description></item>
/// </list>
/// </remarks>
public static class UniversalInvariants
{
    public const string NoNaNOrInfinity = "INV-G-01";
    public const string DeclaredUnits = "INV-G-02";
    public const string RecognisedSchemaVersion = "INV-G-03";
    public const string ContentHashChain = "INV-G-04";
    public const string NoWallClockTimestamp = "INV-G-05";
    public const string SlotArrayLength = "INV-G-06";
    public const string SharedScenarioAxis = "INV-G-07";
    public const string AsOfNonDecreasing = "INV-G-08";
    public const string RequiredFieldsPresent = "INV-G-09";
    public const string RangesHoldElementwise = "INV-G-10";

    /// <summary>All ten, in register order, for exhaustiveness tests over the
    /// validator (T0/T1 conformance).</summary>
    public static IReadOnlyList<string> All => throw new NotImplementedException();
}
