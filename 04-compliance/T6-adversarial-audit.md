# T6 — Adversarial and Lookahead Audit

Normative. The tests that assume the engine is trying to cheat, that the data is
trying to poison it, and that the operator's configuration is wrong.

---

## 1. Why this level exists separately

`T0`–`T5` test the engine against inputs it is supposed to handle. `T6` tests it
against inputs designed to break a specific claim. The claims under attack are
the ones that, if false, invalidate everything else:

| Claim | Attacked by |
|---|---|
| The engine cannot use information it did not have (ADR-004) | §2 lookahead poisoning |
| Unknown configuration is rejected, not ignored (`L2` §2, ADR-011) | §4 config poisoning |
| A malformed payload is rejected cleanly, never partially read (`C0` §4) | §5 contract fuzzing |
| The commitment ledger is authoritative and tamper-evident (ADR-006, `C5` §4) | §6 ledger tampering |
| A stale artefact is refused, not used silently (ADR-014, `C2` §4) | §7 stale-artefact audit |

Every one of these is a claim the architecture makes in prose. `T6` is where the
prose is turned into an executable attack.

---

## 2. The lookahead poisoning audit

### 2.1 The headline test

ADR-004 names it in its consequences, and it is the single most decisive test in
this specification:

> Run a backtest with all facts having `knowledge_time > t` replaced by garbage,
> and assert bit-identical results. If any component peeks, the run diverges.

```
procedure LookaheadPoisoningAudit(corpus, manifest):
    # RUN A — the reference
    runA = Backtest(corpus, manifest)

    # RUN B — identical in every way except that the future is destroyed
    poisoned = PoisonedStore(corpus)
    runB = Backtest(poisoned, manifest)

    assert runA.goldenRunHash == runB.goldenRunHash          # bit-identical
    for tick in ticks:
        assert runA.GoldenTick[tick] == runB.GoldenTick[tick]

    # on failure, the Merkle chain names the first artefact that diverged,
    # which names the layer that peeked (T3 §4.2)
    if divergence: report Localise(runA, runB)
```

`PoisonedStore` wraps the Belief store and, for every read, replaces the value of
any row whose `knowledge_time > tick.asOf` before the row can reach anything.
The poison itself is applied at the storage layer, **below** the `asOf` filter,
so that a correctly-implemented engine never observes it and a peeking one gets
nothing usable.

### 2.2 The four poison modes

Different poisons catch different peeks. All four are run; a component that
survives one may fail another.

| Mode | Replacement for future rows | Catches |
|---|---|---|
| `Garbage` | Deterministic pseudo-random values drawn from the series' own plausible range, seeded from the manifest | A component that reads the future and uses it *quantitatively* — the value changes, so the output changes |
| `NaN` | `NaN` in every numeric field | A component that reads the future at all: `NaN` propagates and either changes the output or trips `INV-G-01`, both of which are detections. This is the loudest mode and the first to run |
| `Shuffled` | The true future values, permuted across `valid_time` within the series | The subtle case: a component that uses only *distributional* properties of the future — a variance, a range, a quantile — which `Garbage` might coincidentally preserve and `NaN` would trip. Shuffling preserves every marginal moment and destroys every temporal alignment |
| `Constant` | A single fixed value | A component whose use of the future is masked by averaging; also a control, since a run that is bit-identical under all four is strong evidence |

`Shuffled` is the mode most likely to find a real defect in a codebase that has
already fixed the obvious leaks, because a "future variance" or "future range"
used to size a bound is easy to introduce accidentally and impossible to see in
review.

### 2.3 Why this is only meaningful because of ADR-013

ADR-013 states it plainly: *the lookahead audit is only meaningful because runs
are deterministic — otherwise "the result changed" proves nothing.*

The audit is a **null test**. Its assertion is that two runs are identical, and
the entire inferential content lies in the fact that identity is achievable at
all. Consider what happens without determinism:

- Runs A and B differ by some amount `δ` even with no poisoning, because of
  thread-scheduling-dependent floating-point reduction order, a wall-clock time
  limit, or hash-order iteration.
- The audit then has to ask whether the observed difference exceeds the noise
  floor `δ`, which requires estimating `δ`, which requires many runs, which turns
  a decisive test into a statistical one.
- A component that peeks *slightly* — using the future to nudge a bound by 0.3 % —
  produces a difference smaller than `δ` and is undetectable. That is exactly the
  size of leak that produces a beautiful backtest.
- And the failure mode ADR-004 exists to prevent is a backtest that looks like
  alpha, which is precisely a small, systematic, hard-to-see improvement.

With determinism, the assertion is `hash(runA) == hash(runB)` and any leak of any
size, anywhere in the pipeline, produces a hash difference that the Merkle chain
then localises to a single artefact in a single tick. The test goes from
"statistically inconclusive" to "decisive and self-diagnosing". This dependency
is why ADR-013 is marked *very expensive* to reverse and why `T3` gates merges
while `T6` gates releases: the cheap, decisive audit is *purchased* by the
determinism discipline, and it is the largest single return that discipline pays.

### 2.4 Scope and the negative control

```
# scope: every source of future information, not just prices
poison(series in {daPriceEurPerMwh, idPriceRefEurPerMwh, idSpreadBeliefEurPerMwh, afrrCapPriceEurPerMwH,
                  afrrEnergyPriceUpEurPerMwh/Dn, activationUp/Dn, imbalancePriceEurPerMwh,
                  load, pv, meter data, settlement revisions,
                  daClearedEurPerMwh, reserve awards, fill outcomes})

# and every artefact with a temporal dimension
poison(model artefacts fitted on data after t)      # see §7.3

# NEGATIVE CONTROL — the test must be capable of failing
inject_deliberate_leak(component = PeakView,
                       leak = "read load at t+96 to set the safety margin")
assert LookaheadPoisoningAudit() FAILS
assert Localise() names ValuationBundle at the expected tick
remove_leak()
```

The negative control runs in the same job. An audit that has never been observed
to fail is an audit nobody knows is wired up, and this one is easy to break
silently — a `PoisonedStore` that is constructed but not installed passes
perfectly.

### 2.5 The partial-poison variant

The full audit says *whether* something peeked. The partial variant says *what*.

```
procedure LocalisePeek(corpus, manifest):
    for series in all_series:
        runS = Backtest(PoisonedStore(corpus, only = series), manifest)
        if runS.goldenRunHash != runA.goldenRunHash:
            report "future values of {series} affect the run"
            report Localise(runA, runS)     # → first divergent artefact → layer
```

`K` series means `K` backtests, which is affordable on the canonical window and
is run on demand after a full-audit failure rather than nightly.

### 2.6 Cadence and severity

| Cadence | Scope |
|---|---|
| Nightly | Canonical 7-day window, `NaN` and `Garbage` modes |
| Pre-release | Full history, all four modes, plus the negative control |
| After any change to `L1` or the Belief store | Full history, all four modes, blocking |

**Severity: stop-ship.** A lookahead divergence is not a bug to be triaged. Every
backtest result produced since the last clean audit is suspect, and the release
does not proceed until the leak is found and the audit is clean on the full
history.

---

## 3. What else the audit covers

The remaining sections are smaller in scope but each defends a specific claim
that other levels assume.

---

## 4. Configuration poisoning

**Claim under attack:** unknown configuration is rejected, not ignored.

`L2` §2 states it for `TariffView`: *"Closed list" is enforced: the configuration
enumerates permitted components and an unrecognised component is a configuration
error, not a silently ignored field.* ADR-011 requires the same of every
numeric threshold — 7,000 h, 10 GWh, tier percentages, HLZF windows — all
configuration, never constants.

The failure mode is specific and expensive: a typo'd key is silently dropped, the
default applies, and the engine runs a configuration nobody intended while every
test passes.

```
property ConfigPoisoning():
    # 1. unknown keys are rejected, not ignored
    for key in {"peakPriec", "cvarLevl", "tariff.unknownComponent",
                "scenarioCount ", "SCENARIOCOUNT"}:
        assert_raises(ConfigurationError, () => LoadConfig(base + {key: value}))
        assert error.names(key) and suggests_nearest_valid_key(key)

    # 2. a valid key with an out-of-range value is rejected at LOAD, not at use
    for (key, bad) in [("cvarLevel", 1.5), ("chanceLevel", -0.1),
                       ("positionScale", 2.0), ("scenarioCount", 0),
                       ("stalenessPenalty", -1)]:
        assert_raises(ConfigurationError, () => LoadConfig(base + {key: bad}))

    # 3. a valid key with the wrong TYPE is rejected, never coerced
    assert_raises(ConfigurationError, () => LoadConfig({scenarioCount: "64"}))
    # "120 EUR/GW" is rejected as a FOREIGN UNIT, not as a wrong denominator on
    # an accepted one: no power scale other than MW is in the unit system at all
    # (conventions §2), and a value carrying one must be normalised at dataload
    # or refused. The same assertion held before the audit, for the opposite
    # reason.
    assert_raises(ConfigurationError, () => LoadConfig({peakPriceEurPerMw: "120 EUR/GW"}))
    # and a unit that IS in the system but is the wrong one for this field is
    # rejected on the suffix, by INV-G-02, before the value is ever parsed
    assert_raises(ConfigurationError, () => LoadConfig({peakPriceEurPerMw: "120 EUR/MWh"}))

    # 4. a tariff component outside the closed enumeration is rejected (L2 §2)
    assert_raises(ConfigurationError,
                  () => LoadConfig({tariff.components: [..., "MysterySurcharge"]}))

    # 5. a required key that is ABSENT is rejected — no silent default for
    #    anything that has no conservative default (C0 §2)
    for key in required_keys_without_default:
        assert_raises(ConfigurationError, () => LoadConfig(base − key))

    # 6. layered config: an override file with an unknown key fails the same way
    assert_raises(ConfigurationError, () => LoadConfig(base, overrides = {typo: 1}))

    # 7. the RESOLVED config is what is hashed into the manifest (T3 §2)
    assert manifest.configHash == hash(resolved_config)
    assert two_different_layerings_producing_the_same_resolved_config
           produce the same configHash
```

Item 7 closes the loop with `T3`: if the manifest hashed the config *files*
rather than the resolved result, two runs with different layering but identical
effective settings would appear different, and — worse — a change that alters
resolution without altering any file would appear identical.

**Regulatory-threshold variant.** ADR-011's thresholds move by regulation. A
dedicated fixture asserts that changing `fullLoadHoursThreshold` from 7000 to a
different value changes behaviour observably, so that a threshold which has
silently become a hardcoded constant is detected:

```
property ThresholdsAreConfiguration():
    for threshold in {fullLoadHoursThreshold, annualEnergyThresholdMwh,
                      hlzfWindows, qualificationMargin}:
        a = Run(config)
        b = Run(config.with(threshold materially changed))
        assert a.goldenRunHash != b.goldenRunHash    # it is actually consulted
```

---

## 5. Contract fuzzing

**Claim under attack:** a malformed payload produces a clean rejection, never a
partial read.

`C0` §4 defines the behaviour: consumer-side validation failure is `HALT`,
because a contract violation means the producer is in an unknown state and
nothing downstream can be trusted. The dangerous outcome is not a crash — it is a
consumer that read the first eight fields, acted on them, and then rejected the
ninth.

```
procedure ContractFuzz<TPayload>(seed_corpus, iterations):
    for i in 1..iterations:
        p = mutate(random_choice(seed_corpus))     # structure-aware mutation
        before = snapshot(consumer_state)

        result = try(() => Consumer.Receive(p))

        # 1. only two outcomes are permitted
        assert result in {Accepted, RejectedWithInvariantId}
        assert never in {Crash, Hang, PartialAccept, SilentTruncation}

        # 2. rejection is atomic
        if result.rejected:
            assert snapshot(consumer_state) == before      # no partial read
            assert result.invariantId in INV_G ∪ INV_D ∪ INV_V ∪ INV_P ∪ INV_X ∪ INV_S
            assert result.message names the field and the violated range
            assert nothing_emitted_downstream()

        # 3. acceptance implies full validity
        if result.accepted:
            assert AllInvariants(p).hold
```

### 5.1 Mutation classes

| Class | Mutation | Expected rejection |
|---|---|---|
| Numeric | `NaN`, `+Inf`, `-Inf`, `-0.0`, denormals, `double.MaxValue` | `INV-G-01`, or range `INV-G-10` |
| Range | One element of a `[H]` or `[S,H]` array out of range, in the **middle** of the array | `INV-G-10` — this is the mutation that catches a validator checking only `array[0]` |
| Length | Array one element short, one long, empty | `INV-G-06`, `INV-D-03` |
| Scenario axis | One `[S,H]` array with a different `S` | `INV-G-07`, `INV-D-05` |
| Weights | Weights summing to `1 ± 1e-3`; a negative weight | `INV-D-06` |
| Version | Unknown major; unknown minor; missing `schemaVersion` | `INV-G-03` |
| Hash | Corrupted `contentHash`; empty `inputHashes` | `INV-G-04` |
| Nullability | A `Null = no` field absent | `INV-G-09`, `INV-D-10` |
| Enum | An out-of-range `EconomicEffect`, `VarSymbol`, `BoundReason`, `DegradationMode` | `INV-V-03`, `INV-V-15` |
| Structural | Duplicate `termId`; a `PwlTerm` with non-increasing `X`; a mis-declared curvature; a `FillProbView` term with a price | `INV-V-12`, `INV-V-16` |
| Encoding | Truncated binary; trailing bytes; deeply nested JSON; a 100 MB string field | Clean rejection, bounded memory, no hang |
| Adversarial encoding | Duplicate JSON keys; integer overflow in a length prefix; a self-referential `inputHashes` | Clean rejection |
| Cross-field | `socMinMwh > socMaxMwh`; `etaCharge · etaDischarge > 1`; `blockIndex` not covering every slot | `INV-D-01`, `INV-D-09`, `INV-D-04` |

### 5.2 Rules

- **Structure-aware, not byte-random.** Random bytes are rejected by the
  deserialiser and test nothing above it. The fuzzer mutates *valid* payloads at
  the field level, which is where the interesting failures are.
- **Corpus-driven.** The seed corpus is the recorded seams of the designated
  audit days (`T3` §7.1), so mutations start from realistic structure.
- **Every crash becomes a fixture.** A payload that crashes rather than rejecting
  is minimised and added to the permanent `T1` corpus in the same commit as the
  fix.
- **Bounded resources.** Each iteration runs under a memory and time cap; a
  payload that causes unbounded allocation is a finding, not a timeout to be
  raised.

---

## 6. Commitment-ledger tampering

**Claim under attack:** the commitment ledger is authoritative, and awarded
capacity is a physical obligation that survives every degradation mode
(ADR-014 §3, ADR-006).

The ledger is the one piece of state where a corruption is both plausible — it is
written by two producers, `L5` for confirmed entries and `L3` for pending ones —
and expensive, because an undetected loss of a confirmed award produces exactly
the delivery shortfall that `INV-S-04` treats as a prequalification risk.

```
property LedgerTampering(base_state):
    for attack in attacks:
        tampered = attack(base_state.ledger)
        r = try(() => Tick(bundle, tampered))
        assert r.detected_with(expected_invariant(attack))
```

| Attack | Expected detection |
|---|---|
| Delete a `Confirmed` reserve award that `C4` reported | `INV-X-06` — every award has a ledger entry by end of tick. `HALT` |
| Flip a `Confirmed` entry to `Cancelled` | `INV-X-06` and the award/ledger reconciliation. `HALT` |
| Flip a `Pending` entry to `Confirmed` | Reconciliation against `C4`: no fill or award backs it. `HALT`. This is the dangerous direction — the Planner would treat phantom exposure as a hard constraint and over-constrain itself, or worse, believe a position it does not hold |
| Strip `feasibilityRequirement` from a `Confirmed` reserve award | `INV-S-05`. `HALT`. Without the corridor the Planner cannot see the obligation |
| Widen a `feasibilityRequirement` corridor so the obligation looks easier | Cross-check against the product's `sustainDuration` and `D` from prequalification (`C1` §7). `HALT` |
| Alter `signedVolumeMwh` on a settled entry | `INV-S-08` — no backward write without an explicit `revisionOf`. `HALT` |
| Alter `signedVolumeMwh` on a `Confirmed` entry | Reconciliation against `C4` fills. `HALT`, `INV-X-02` |
| Introduce a fill with no matching intent | `INV-X-01` — phantom fill. `HALT` |
| Duplicate a ledger entry | Idempotency by `entryId`; the duplicate is rejected, not double-counted |
| Reorder entries | The ledger is keyed and ordered (ADR-013 iteration order); the tick's output must be unchanged |
| Replay a stale ledger from a previous tick | `INV-S-08` plus the `T3` §6.2 assertion that `StateSnapshot(t).contentHash == StateUpdate(t−1).contentHash`. `HALT` |
| Corrupt the ledger's own content hash | `INV-G-04`. `HALT` |

Two further assertions, on the response rather than the detection:

```
# the engine goes to HALT rather than quietly under-delivering (ADR-014 §3)
assert response == HALT
assert not (engine continues with a reduced obligation)
assert alert_raised and operator_acknowledgement_required

# and the ledger is durably content-addressed like everything else (ADR-006)
assert L0.ledger.contentHash is recorded in StateSnapshot
assert restart_from(StateSnapshot) reproduces the identical ledger
```

The restart clause is worth its own fixture. ADR-006 claims `L0` is the entire
state and an engine restart loads a `StateSnapshot` and continues. A tampering
attack that survives a restart — because the ledger was rebuilt from a source
other than the snapshot — breaks that claim.

---

## 7. Stale-artefact audit

**Claim under attack:** the engine refuses an artefact beyond its validity
horizon rather than using it silently.

This is the quietest failure in the system. A value function fitted three weeks
ago on a different peak state still evaluates, still returns plausible euros,
still produces a feasible plan — and prices the entire end of the horizon wrongly.

### 7.1 The value function

`C2` §4 carries `producedAt`, `validityHorizon`, `stalenessPenalty` and
`artefactHash`. ADR-014 makes "value function beyond validity" a `DEFENSIVE`
trigger.

```
property StaleValueFunction():
    for age in {0, 0.5·H, H − 1, H, H + 1, 10·H}:      # H = validityHorizon
        v = value_function.with(producedAt = now − age)
        r = Tick(bundle.with(vSoc = v), state)

        if age <= H:
            assert r.mode == NORMAL
            assert r.vSocSlopesEurPerMwh == v.slopes                 # unshrunk
        else:
            assert r.mode >= DEFENSIVE                      # ADR-014 trigger
            assert r.vSocSlopesEurPerMwh == v.slopes · stalenessPenalty   # shrunk
            assert "V stale" in r.riskProfile.driverSummary      # explainable
            assert r.escalated_tier                         # L3 §3: V stale
                                                            # forces escalation
            assert r.refreshRequested == true               # C5 §8

    # the boundary is not silently crossed
    assert transition_at(age = H) is observable in the artefact record
    # and the ENGINE MUST NOT simply carry on
    assert never (age > H and mode == NORMAL and slopes unshrunk)
```

The final assertion is the whole test. The tempting implementation logs a warning
and proceeds, which is ADR-014's "silent imputation" failure applied to a model
artefact: the engine makes full-confidence decisions on a stale value function
and the failure is invisible until Settlement.

### 7.2 Every other artefact

The same audit runs for every artefact in the manifest. Each declares a validity
policy; an artefact with no declared policy fails the audit by construction.

| Artefact | Staleness signal | Required response |
|---|---|---|
| Value function `V` | `producedAt + validityHorizon`; also `stateDriftSignal` (`C5` §8) | `stalenessPenalty` shrink, `DEFENSIVE`, tier escalation, `refreshRequested` |
| Scenario ensemble / reduction | Generation `asOf` versus tick `asOf`; ensemble horizon versus `H_plan` | Escalate per ladder; an ensemble not covering the horizon is `Missing`, not truncated |
| Fill model | Stamped simulator version versus the configured adapter (`T4` §1.2); fit date versus policy | `HALT` on version mismatch; `alert` on age |
| Activation model | Fit date; realised calibration drift (`T4` §2.4) | `alert`; widen `chanceLevel` |
| Tier-3 surface `μ̂` | Fit date; **out-of-training-envelope detection on the current state** | Escalate to Tier 2 (`L3` §3), logged and visible in the settlement record |
| `MarketCalendar` | Coverage: does it cover the tick's slot and the whole accounting period? | `HALT` — a tick outside calendar coverage cannot be priced |
| tzdata | Pinned version present and matching the manifest (`INV-T-04`) | `HALT` if absent or mismatched |
| Peak state / qualification state | `unsettledGapFrom` (`C5` §2); `peakIsProvisional` | Treat the gap conservatively using the engine's own modelled trajectory — never assume the gap contained nothing |
| Price beliefs, per series | `maxStale` from the contract field table | The ADR-014 §4 ladder |

### 7.3 Artefacts fitted on the future

A model artefact fitted on data later than the backtest position is a lookahead
leak that the `PoisonedStore` of §2 cannot see, because the leak is baked into
the artefact rather than read from the store.

```
property ArtefactTemporalHygiene(run):
    for artefact in manifest.modelArtefacts:
        assert artefact.trainingDataEnd <= run.firstTick.asOf
        # or, for a rolling refit, the artefact in force at tick t satisfies
        assert artefact_in_force(t).trainingDataEnd <= t.asOf
    # an artefact with no declared trainingDataEnd cannot be used in a backtest
    assert every artefact declares trainingDataEnd
```

This is a separate audit from §2 and it is the one people forget. A Tier-3
surface `μ̂` fitted on the whole history and then backtested over that same
history will show an excellent gap and will not reproduce live. The holdout
discipline in `T5` §7 depends on this check being real.

---

## 8. Cadence, severity and reporting

| Audit | Nightly | Pre-release | Severity on failure |
|---|---|---|---|
| Lookahead poisoning (`NaN`, `Garbage`) | canonical window | full history | **Stop-ship** |
| Lookahead poisoning (`Shuffled`, `Constant`) | — | full history | **Stop-ship** |
| Negative control | — | every run | Stop-ship — the audit is not wired up |
| Partial-poison localisation | on demand | on failure | — |
| Config poisoning | full | full | Block release |
| Contract fuzzing | 10⁵ iterations | 10⁷ iterations | Block release on any crash, hang or partial read |
| Ledger tampering | full | full | Block release |
| Stale-artefact audit | full | full | Block release |
| Artefact temporal hygiene | full | full | **Stop-ship** — same class as a lookahead leak |

A `T6` report accompanies every release and records, for each audit, the corpus,
the manifest, the number of iterations, and the negative control's result. An
audit that passed without its negative control passing is reported as **not run**.

### 8.1 What a `T6` failure means operationally

- **A lookahead or temporal-hygiene failure invalidates history.** Every backtest
  result, every `T5` gap, every calibration report produced since the last clean
  audit is suspect and is marked as such in the archive. This is not
  proportionate-response territory; the whole point of ADR-004 is that a
  lookahead leak produces results that look like alpha, so the results cannot be
  trusted merely because they look reasonable.
- **A fuzzing crash is a `HALT` that did not happen.** The consumer was supposed
  to reject and instead did something else. Treat it as a contract defect, fix
  the validator, and add the payload to the `T1` corpus.
- **A ledger-tampering miss is a delivery risk.** It sits upstream of
  `INV-S-04`, and `INV-S-04` is a prequalification risk rather than an accounting
  item.
- **A stale-artefact miss is a slow leak.** It costs money quietly and shows up
  months later as a rising `unexplainedRatio` (`INV-S-07`), by which point the
  attribution is gone.
