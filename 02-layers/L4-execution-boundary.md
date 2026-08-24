# L4 — Execution Boundary

Execution is a pre-existing, strategy-independent market simulation engine —
fixed, stable, and out of scope for design. This document specifies only the
**seam**: the adapter between C3 and C4, which is where the boundary either holds
or erodes. The adapter performs **no economics**; it translates, records and
reconciles. A number it needs but does not receive belongs upstream in a view
(ADR-009) or in the quoting policy (ADR-012).

**Input:** `ExecutionIntent` (C3), projected **Output:** `ExecutionOutcome` (C4)
**Purity:** impure by nature — the only I/O boundary in the pipeline

---

## 1. `IExecutionAdapter`

```csharp
// The projection: shadowValueEurPerMwh and urgency are absent from the TYPE, so Execution
// cannot read them. INV-X-04 is structural, not a rule anyone must remember.
readonly record struct OrderInstruction(
    string IntentId,                      // idempotency key, stable across replaces
    MarketId Market, string ProductId, DeliveryKey Delivery, Side Side,
    LimitPrice LimitPrice, OrderVolume Volume,  // tick-/lot-aligned at C3; never re-rounded
    Validity Validity, string? ReplacesIntentId);

readonly record struct ExecutionInstructionSet(
    GateId Gate, DegradationMode Mode, string ManifestId,
    IReadOnlyList<OrderInstruction> Orders,
    IReadOnlyList<BidCurveInstruction> Curves);     // DA, monotone (C3 §3)

interface IExecutionAdapter {
    SubmissionReceipt         Submit(in ExecutionInstructionSet set);
    SubmissionReceipt         CancelReplace(in ExecutionInstructionSet set);
    IReadOnlyList<VenueEvent> Poll(SlotId asOf);        // pull venues / simulator
    IDisposable               Subscribe(Action<VenueEvent> sink);  // push venues
    OpenOrderBook             Reconcile(SlotId asOf);
    ExecutionOutcome          Seal(SettlementPeriod period);       // emits C4
}
```

`Poll` and `Subscribe` are alternatives, normalised into one `VenueEvent` stream.
`Seal` is the only producer of C4 and validates before returning (C0 §4).

## 2. The projection rule

C3 §2 carries `shadowValueEurPerMwh` and `urgency` for settlement attribution and states
that Execution must ignore them. `Project(ExecutionIntent) → ExecutionInstructionSet`
is the only crossing, lives in exactly one file, and the target type has no field
to hold them. The full intent, audit fields included, is recorded to the seam log
(ADR-013) on the Planner side of the projection, so L5 still receives them without
Execution ever having seen them. A comment saying "do not read this" is not a
boundary; a type that cannot express the value is.

## 3. Quoting sits on the Planner side of the seam

Stated flatly, because this is the boundary most likely to erode: **the quoting
policy is strategy and belongs to L3** (ADR-012, `L3-planner.md` §5). The adapter
receives limit prices; it does not choose, improve, peg, or re-price a residual
after a partial fill. A re-price is a new `OrderIntent` carrying
`replacesIntentId`, produced by the quoting policy from the current fill curve and
the unchanged `shadowValueEurPerMwh`, re-entering through C3. Let the adapter acquire a
price rule and three things break at once: `INV-P-10` becomes unenforceable; L5's
`executionSlippageEur` bucket stops measuring the quoting policy and starts measuring
an untested piece of the adapter; and Execution stops being strategy-independent,
and therefore replaceable.

## 4. Simulator versus live venue

One interface, two implementations. What differs:

| Dimension | Simulator | Live venue |
|---|---|---|
| Latency | deterministic or zero | variable; a source of adverse selection |
| Partial fills | per the simulator's fill model | per the real book |
| Rejections | a narrow modelled set | risk limits, credit, product state, malformed |
| Fill-model calibration target | the simulator's behaviour | the venue's behaviour |

**Standing risk (ADR-015 OPEN-3).** A fill model fitted to the simulator inherits
the simulator's biases, and so does everything derived from it — `FillProbView`'s
`BoundTerm`, the quoting ladder, and therefore L5's `executionSlippageEur` bucket.
Recalibration against the live venue is a **precondition for live operation**, not
a follow-up, and the gap between the two is reported (`04-compliance/T4`), never
absorbed.

## 5. Idempotency and reconciliation

**`intentId` is the idempotency key**: a repeated submission leaves at most one
live order (`INV-X-07`), which makes retry-after-timeout safe and reconnect logic
simple enough to be correct. Cancel/replace is a chain keyed by `replacesIntentId`
(C3 §2); the adapter never invents an identifier, so Settlement can follow it. On
reconnect the adapter calls `Reconcile` and diffs live venue orders against the
commitment ledger (C5 §4): matched → no action; ledger entry with no venue order →
re-submit under the same `intentId`; **venue order with no ledger entry → orphan →
`HALT`** (`INV-X-08`). Live exposure the engine does not know it has makes every
ledger-derived plan untrustworthy, and no mode short of `HALT` is honest about
that (ADR-014 §3).

| ID | Invariant | On failure |
|---|---|---|
| `INV-X-07` | Re-submitting an `intentId` yields at most one live order | `HALT` |
| `INV-X-08` | No orphaned venue order at reconciliation | `HALT` |
| `INV-X-09` | Simulator byte-reproducible given the same instruction stream and seed | fail the run |
| `INV-X-10` | `limitPriceEurPerMwh` and `volumeMwh` reach the venue exactly as received | `HALT` |

## 6. Determinism in backtest

ADR-013 requires two runs with the same manifest to produce byte-identical output,
and the simulator sits inside that chain — so **it must be deterministic under a
fixed seed for ADR-013 to hold at all**; its seed and version are manifest fields.
If it is not — hash-ordered iteration, wall-clock reads, an unseeded RNG,
thread-count-dependent matching — **that is a named gap**, and a gap in the replay
guarantee for the whole pipeline rather than a local defect in L4. Closing it is a
prerequisite for `04-compliance/T3` and `T6`; until then any comparison downstream
of C3 must state the non-determinism explicitly, as ADR-013 requires of
non-deterministic solver backends.

## 7. Testing this layer

Detail in `04-compliance/T1` (adapter conformance) and `T4` (fill calibration).

- **Conformance** — one suite run against the simulator and a recorded venue stub;
  both satisfy the same postconditions. A divergence is a finding about the
  simulator, not an exemption.
- **Projection closure** — reflection test: no field of `ExecutionInstructionSet`
  is reachable from `shadowValueEurPerMwh`/`urgency`, and `Project` is the sole construction
  path. The machine check behind `INV-X-04`.
- **Idempotency and reconnect** — submit, duplicate, submit-after-timeout; then
  kill the connection mid-gate and assert the three reconciliation outcomes are
  classified correctly and an injected orphan halts.
- **Fault injection** — rejection, partial fill, out-of-order and duplicate events,
  and a fill for an unknown `intentId` (must trip `INV-X-01`).
- **Determinism and fidelity** — same instruction stream and seed twice, byte-
  compared (`INV-X-09`); property test that every price and volume reaching the
  venue equals the one that left C3 (`INV-X-10`).
