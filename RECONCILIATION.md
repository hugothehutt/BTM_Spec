# Reconciliation Log

Findings from the cross-consistency pass over the specification, and how each was
resolved. Kept in the repository because several of these are the kind of
inconsistency that reappears under editing pressure, and because the *reasoning*
matters more than the fix.

---

## Resolved — normative changes made

| # | Finding | Resolution |
|---|---|---|
| 1 | **`SpotEnergyValue` was owned by nobody.** The ownership matrix listed the owner as "the Planner objective", contradicting ADR-009 (every effect maps to exactly one *view*), `INV-V-02` (a term's `originView` must be the effect's owner — unsatisfiable if the owner is a layer), and L3 §2 ("the Planner adds no economics of its own"). The largest term in the objective had no owner. | A thin **`SpotView`** is now the normative owner (L2 §5). ADR-009 annotated to state the matrix in L2 §5 is authoritative and that no effect may be owned by the Planner. |
| 2 | **`TermBase` enumeration mismatch.** ADR-009 listed four bases; C2 and L2 use five (adding `ReserveCapacity`). | ADR-009 corrected. Five bases. |
| 3 | **`StateSnapshot` had no contract.** ADR-006 asserted it "is a contract with a field table, a version and invariants like every other seam" — and no such document existed. Four documents read fields from it. | **`03-contracts/C6-state-to-layers.md`** written. Numbered separately from C5 because it has a different direction, different consumers and a different failure policy. |
| 4 | **`peakCritical` was read but never defined.** L3 §3 and ADR-011 both use it as an escalation trigger; C5 defined only `qualCritical`. | Defined in C5 §2 and C6 §2, alongside a continuous `headroomToPeakKw`. Computed by Settlement, never by the Planner, so the trigger cannot be influenced by the thing it guards. |
| 5 | **`INV-G-02` ("no bare `double`") was violated by the contracts themselves** — fractions, weights, ratios, probabilities and full-load hours are all legitimately dimensionless or scalar. As written, the invariant was unsatisfiable and would have been quietly ignored, which is worse than not having it. | Reworded: dimensioned quantities must use a typed quantity; dimensionless fields may be primitive but must declare `—` as their unit and carry a range constraint. |
| 6 | **`BeliefSnapshot`: handle or value?** ADR-004 §3 called it a "handle" with a "hot window reference"; C0 §1 and C1 §10 forbid handles into another layer's storage. | ADR-004 §3 rewritten. A read-only view over a buffer frozen for the tick is permitted; anything advanceable or aliasing mutable state is not. Two forms defined — the hot form (`ReadOnlyMemory<T>` views, avoids copying) and `BeliefSnapshotRecord` (materialised, satisfies C0 serialisability for seam recording). Identical content, identical hash. |
| 7 | **`unexplained` was defined twice with different meanings** — the residual of the *cash* decomposition (C5 §5) and the residual of the *error* decomposition (C5 §6). Netting them would have concealed both. | The second is renamed `unexplainedError`. C5 §5 and §6 both state explicitly that the two are never netted, and each keeps its own invariant (`INV-S-02`, `INV-S-03`). |
| 8 | **`optimalityGap` could not be a component of `planned − realised`.** A suboptimal tier lowers planned *and* realised value, so it nets out of that difference — the bucket had nowhere to live, and optimiser weakness would have been silently attributed to forecast error. | The decomposition now telescopes from the **Tier-1 reference value `Ĵ₁`**, not the used-tier value. `planned` in `INV-S-03` means `Ĵ₁`. The two coincide when Tier 1 was used. Written out as a chain in C5 §6. |
| 9 | **`forecastError`'s stated counterfactual did not telescope.** "Re-run Valuation and Planner on realised data" yields the value of perfect foresight, which is a different quantity from the chain link. | C5 §6 now books the telescoping form (plan re-scored on outturn) and reports the re-plan form separately as `valueOfForesight` — which is also ADR-010's perfect-foresight upper bound. The two must not be conflated: one is a link, one is a bound. |
| 10 | **Risk terms would have booked a phantom daily loss.** `ActivationRisk` and the CVaR weighting are objective penalties, not cashflows. Comparing a risk-loaded planned value against a risk-free realised one books the risk premium as a loss every day. Not addressed anywhere in the original design. | C5 §6 mandates the **risk-free** objective on both sides; `PlanResult` reports gross and net, and only gross enters the chain. |
| 11 | **`INV-P-10` severity differed by document** — "hard rule" in L3 §5, "warn + block" in C3 §5. | Reconciled to: severity `warn`, intent **blocked**, no `HALT`. Covering a confirmed commitment at a loss is sometimes correct, but must carry the `CommitmentCover` tag; an untagged violation is a quoting-policy defect. |
| 12 | **`S` and `K` used in two senses** in adjacent paragraphs of ADR-005 (`K` as series count and as reduced scenario count). | Notation fixed in ADR-005: `K` = series, `S_gen` = generated scenarios, `S` = reduced scenarios reaching the Planner (= C1 §1's `scenarioCount`). |

---

## Resolved — resolutions recorded in the layer documents

These were resolved in place by the layer designs rather than by changing a
normative document. Recorded here so the reasoning is findable.

| # | Finding | Where resolved |
|---|---|---|
| 13 | **`criticalMissing` vs. the one-tick mode lag.** C1 §8 requires non-empty `criticalMissing` to imply mode ≥ `DEFENSIVE`, but `degradationMode` is read from L0 and is one tick old. In the transition tick these contradict. | L1 §7.3 and C6 §6: L1 applies a **monotone escalation floor** — it may raise the mode from its own same-tick inputs, never lower it. De-escalation stays central and hysteretic. Not an ADR-006 violation, because raising a floor from one's own inputs is not a read of another component's same-tick write. |
| 14 | **Two writers to the commitment ledger.** ADR-006 says pending entries are written by L3; it also says L0 is written only at end of tick from L5 — and C4 carries only terminal dispositions, so L5 cannot see a still-live open order, while C4 §8 forbids L5 from seeing Planner reasoning. | L0 §2.8: exactly one writer, the driver's `Commit`. The `StateUpdate` is assembled from L5's settled output plus a driver-computed ledger delta reconciling C3 intent against C4 dispositions. "One backwards edge" stays literally true. |
| 15 | **"L0 is the entire state" needed a qualifier.** A crash between order submission and commit leaves orders live at the venue that the restored ledger does not know about; blind replay would double-submit. | L0 §4.4: entire *engine* state, not entire *world* state. A mandatory reconciliation pass emits a recorded `Reconciliation` update before replay resumes, so `INV-X-06` stays checkable across a restart. `isColdStart` in C6 §1 gates speculative intent until it completes. |
| 16 | **Settlement's isolation contradicted its own required inputs.** C4 §8 denies Settlement access to Planner reasoning; C5 §5–§7 require `plannedByEffect`, counterfactual re-runs against `PlanResult`, and `StrategyTag` (which lives on C3). | L5 §1: two-phase split. Phase A (accounting) is structurally denied the plan, content-hashed and sealed. Phase B (attribution) reads the sealed output plus recorded artefacts. Non-contamination survives because Phase B cannot alter the ledger side of the comparison. |

---

## Open — decide before implementation starts

Small, but each will otherwise be decided by whoever writes the code first.

| # | Item | Needs |
|---|---|---|
| A | `INV-D-07` (reduced ensemble preserves marginal means) has **no numeric tolerance**, unlike its sibling `INV-D-06` (±1e-9). | A number, ideally derived from the scenario-reduction study in `T5`. |
| B | `BoundTerm.lower/upper` — "either may be null" does not say whole-array or per-element. The stubs assume whole-array (a one-sided bound). | Confirm, or make it per-element. |
| C | `TermHeader.slots` is a contiguous `SlotRange`; `EpigraphTerm.overSlots` is a **set** (this is how HLZF is expressed). Their relationship is unstated — is the header range the hull? | State it. Affects the HLZF regime directly. |
| D | C1 §5 `idVolumeByPriceBand[H, Band]` — `Band` is never defined (count, ordering, boundaries). | Depends on ADR-015 OPEN-3; defer with it, but record the dependency. |
| E | `ValueFunctionContext` — the types of `peakState`, `qualState`, `calendarContext` are not given. The stubs use opaque string keys. | This is the **value function's state space**, so it is a modelling decision, not a typing one. Belongs to the slow-loop workstream (W7). |
| F | Invariant IDs `INV-V-08`, `INV-V-09`, `INV-V-10`, `INV-P-08` are referenced by nothing. | Recorded as permanently **Reserved** in `04-compliance/T1` §10 rather than recycled, so alert history stays readable. Confirm that policy. |
| G | `INV-T-05` (MarketCalendar artefact conformance) was newly allocated during the compliance pass; ADR-002 states the invariant in prose without an ID. | Annotate ADR-002. |
| H | `SelfConsumptionView` is absent from the valuation catalog (L2 §3). In many BTM cases the avoided retail energy price is the **largest** term. | Decide whether it is in scope for v1, and if so which `EconomicEffect` it claims and whether it overlaps `TariffView`. This is the largest open economic question in the spec. |

---

## Standing note

Items 1, 3, 8 and 10 are the ones worth re-reading. Each was a case where the
design was internally plausible and quietly wrong: an unowned term, a missing
contract, a decomposition that could not sum, and a risk premium booked as a
daily loss. None would have thrown an exception. All four would have produced
numbers that looked reasonable — which is the failure mode this architecture's
enforcement machinery exists to make impossible.
