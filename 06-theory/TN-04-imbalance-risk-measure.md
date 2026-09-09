# TN-04 — The CVaR device: what it earns, what is broken, what needs deciding

**Ticket:** [#71](https://github.com/kpfefferlcreatica/BTM_Spec/issues/71) · **Class:** analytic ·
**Method:** derivation only, no data touched · **Branch:** none — written in place

**Reading context.** Assumes `docs/handover-2026-08.md` §08 (the objective, the five `C2`
shapes) and §10 (the RU form, positive homogeneity, time-inconsistency). Nothing from those
is re-derived here. `TN-01` adjudicates CVaR's *properties* and this note takes them as
settled; it asks instead whether CVaR is the right *instrument* for the risks the corpus
names, and whether the corpus can build the instrument it names.

**Two things moved since that handover.** `ADR-019` removed the activation cost term, so
the objective in handover §08 is now one term shorter — `− cvarWeight · CVaR_α(imbalance
cost)` (`L3`:171) — and the effect enumeration is twelve, not thirteen. §6 records that
argument and its disposal. The finding that replaces it as the note's most consequential is
§2, below.

> **The one thing to take into the room.** Handover §08 and §10 cannot both be true. §08
> says five term shapes cross `C2`, no prices among them, and the Planner adds no economics
> of its own. §10 says the risk term is `S` rows in the per-scenario losses `L_s`. There is
> no shape that carries `L_s`. The objective line at `L3`:171 is not assemblable from the
> payload that feeds it — a frozen-contract defect, upstream of every question about which
> measure to use.

**Three questions for the meeting**, in this order:

1. **How does a risk functional cross `C2`?** An exception to `INV-V-06`, or a sixth shape
   (§2, C7). Blocks everything else.
2. **What is `cvarWeight` standing in for?** Tail risk, model risk, or a contract. Three
   different correct instruments, only one of which is a risk penalty (§5b, C4).
3. **Is `(α, S) = (0.95, 64)` viable?** Derivation in §5a says no, and gives the trade
   (C3).

Nothing here is an ADR. §7 lists the acts; they are the owner's.

---

## 1. How CVaR is used

Three functionals take a tail. They agree on nothing except the parameter.

| Carrier | Argument | Orientation | Declared payload shape | Risk weight |
|---|---|---|---|---|
| **Imbalance** (`L2`:117-126, `L3`:171) | imbalance cost — a **cost**, upper tail | correct | `PwlTerm` convex/Min (`ADR-008`:50-52) | `cvarWeight` |
| **Peak** (`L2`:61-66) | `max_t p_poi[s,t]` — a cost **driver** in MW, upper tail | correct | `EpigraphTerm` (`ADR-008`:36-40) | `peakPrice`, outside the functional (`ADR-020`) |
| **Delineation** (`L2`:165-168, `ADR-017`:60-61) | `V_del` — a **benefit**, so the bad tail is the **lower** one | **inverted** unless negated | `LinearTerm` on `aDel` at `λ_j = ∂V_del/∂A_j` | none — tail taken inside Valuation |

The whole parameter surface is `C2`:190-192: `cvarLevel ∈ (0,1)` ("e.g. 0.95; widened when
quality degrades"), `cvarWeight ≥ 0`, `chanceLevel ∈ (0,1)`. Quality turns them rather than
branching (`ADR-014` §1), per `L2`:292-296 and `T2`:236-243: degraded load raises
`cvarLevel`; degraded **price beliefs** raise `cvarWeight`; degraded activation beliefs
raise `chanceLevel`. Note the middle one — a response to *model* risk routed through a
tail-risk knob (§5b).

Since `ADR-019`, `chanceLevel` carries the whole of the engine's aversion to activation.
It is not a tail, and it is the one instrument the corpus never formulates (§4 claim 11).

Settlement is clean and worth saying so: `L5`:192-198 and `C5`:223 hold that risk is a
decision-shaping penalty, never a cashflow; no effect settles a risk premium, the
four-bucket decomposition runs risk-free on both sides, and `PlanResult` reports the
objective gross and net of risk. That prevents a whole class of "the engine loses its risk
premium every day" error.

---

## 2. The payload cannot carry the functional

The RU program in handover §10 needs four things. `C2` provides none of them.

| Requirement | In `C2`? |
|---|---|
| a scalar auxiliary `ζ` | **No.** `C2` §2 is a closed symbol enumeration (`C2`:30-44) and "adding a symbol is a **major version bump**" (`C2`:56) |
| `S` shortfall variables `u_s ≥ L_s(x) − ζ` | **No.** No `[S]`-indexed auxiliary. `eImbalance` is `[S,H]` but it is an exposure, not a shortfall |
| scenario-indexed loss coefficients `L_s(x)` | **No.** `LinearTerm.coefficient` is `double[H]` (`C2`:80); `PwlTerm` carries two `double[n]` vectors (`C2`:88-89); `INV-V-06` HALTs on any scenario array (`C2`:223, `T1`:137); and `C2`:235-236 makes it intent — "The Planner never sees a price series" |
| objective coefficients `(1−α)⁻¹ w_s` | **No.** `CouplingConstraint` is the only shape with a scenario axis, and it carries constraint rows with no objective coefficient — nor an `S` axis on `coef` (`C2`:130-137) |

And the Planner cannot make up the difference: `L3`:165-166 forbids it from adding
economics of its own, and the per-scenario loss coefficients are economics.

**All three carriers fail, each differently.**

*Imbalance* — two readings, and the corpus does not say which. **(a)** A `LinearTerm` at
the mean price: legal, but then the tail over `s` carries only volume dispersion, while
`T2`:214 says price co-movement is the entire quantity. **(b)** A per-slot convex `PwlTerm`
in `eImbalance`: genuinely representable, because positive homogeneity makes
`v ↦ CVaR_α(price·v)` two-piece convex PWL through the origin — Valuation tabulates the two
slopes and exports no scenario array. But the object built is then `Σ_t CVaR_α(price_t·v_t)`,
and by subadditivity that is **192 separately-taken tails, not one tail of the horizon's
loss** — strictly more conservative than `L3`:171 by an unstated amount, and exact only if
the loss is bilinear in one scalar per slot.

*Peak* — `L2`:63-64's argument is `max_t p_poi[s,t]`, and `C2` §2 offers `pPoi : [H]` and
`zPeak` per regime; neither is scenario-indexed, and `EpigraphTerm`'s six fields
(`C2`:104-109) include no scenario index and no risk-averse level. So the tail mean
`PeakView` computes has no field to arrive in — unless it is folded into `pPoiFloorMw`,
which is the *realised* peak from L0 (`C2`:107), mislabelling a forecast as history.
`TN-01` §10 priced the honest repair: `z_s` per scenario, ≈12k rows per regime at
`S=64, H=192`.

*Delineation* — the one carrier that does cross, because `λ_j` is a scalar per accumulator
and the tail is taken inside Valuation. Valid, with one unstated condition: a subgradient
of `CVaR_α` at `A°` is `E_w[∇V_del | tail(A°)]`, exact only while the tail scenario set is
unchanged — and `A_j` is decision-dependent, so the plan moves the tail set. A first-order
model with no stated region of validity.

**The repair is a payload decision, not a measure decision** (C7).

---

## 3. What CVaR earns the repo

Handover §10 already gives two of these — LP-representability and positive homogeneity.
The other four are load-bearing and **nowhere stated in the corpus**.

| Property | Consumed at | Broken if absent | Stated? |
|---|---|---|---|
| LP-representable (RU) on atomic non-uniform `P_w` | `ADR-008`:51-52; `C2`:94's binary-free rule; `C2`:204 | The risk term costs binaries and `problemClassHint` stops predicting solve time | yes |
| Positively homogeneous | `L2`:61-66 takes the peak tail in MW and prices it outside; also what makes §2 reading (b) representable | The EUR-only rule breaks and `peakPrice` must move inside the functional | yes |
| **Coherent** (sub-additive) | The composer sums three separately-taken tails; coherence is what makes that a risk measure rather than three numbers added up | The objective's risk block has no interpretation at all — and it fails today, because a tail conditional expectation on an atomic measure is not subadditive (claim 1) | no |
| **Preserves convexity in `x`** | This, not LP-representability alone, is what keeps the MILP class | The relaxation stops being exact and `INV-V-12` has nothing to verify against | no |
| **`cvarWeight` reads as a probability distortion** — `λ·CVaR + (1−λ)·E` is an expectation under inflated tail probabilities | The only framing in which `cvarWeight` could ever be calibrated against a stated appetite (§5b) | `cvarWeight` stays a dial with no referent, permanently | no |
| **Imbalance cost is time-additive** (`L5`:143) | §5d: handover §10's drift is hours here, not weeks, so this carrier can be decided ahead of the two path-functional ones | The three carriers must be decided as one, and the slowest blocks the rest | no |

Right shape, too, and the corpus says so correctly (`L2`:121-123): a variance penalty would
punish a *favourable* excursion into an extreme imbalance price exactly as hard as an
adverse one.

The probability-distortion reading is the strongest single argument for keeping CVaR. No
alternative in §8 offers it.

---

## 4. The broken claims

| # | Claim | Anchor | Verdict |
|---|---|---|---|
| 1 | The tail statistic is "a weighted empirical mean over the joint ensemble" / "the weighted mean of the worst `(1−α)` mass" | `ADR-005`:61-62, `L2`:61-66 | **Refuted.** That is the naive tail mean: not coherent on an atomic measure, always understating by up to `(tail spread)/n_eff` where `n_eff = (1−α)/max_s w_s`, and not the RU object `ADR-008`:52 depends on. While it stands, CVaR is not what would be built. Seven sites: `ADR-005`:10, `ADR-005`:61-62, `ADR-017`:61, `T2`:212, `C5`:27, `L2`:61-66, `L2`:165 |
| 2 | The risk functional crosses `C2` as a `PwlTerm` / `EpigraphTerm` | `ADR-008`:36-40, `:50-52`, `L2`:119-120 | **Refuted** — §2 |
| 3 | "`LinearTerm` per scenario" is available | `ADR-008`:50 | **Refuted.** `LinearTerm` (`C2`:75-81) has no scenario selector and `slots` is a `SlotRange`. No term can be restricted to one scenario |
| 4 | The terms referencing `eImbalance` price it | `C2`:42 vs `C2`:80, `:235-236` | **Refuted.** `eImbalance` is `[S,H]`; every coefficient reaching it is `[H]`. The buildable cost prices scenario-varying volume at a scenario-**invariant** price |
| 5 | One `cvarLevel` serves all three carriers | `C2`:190 | **Refuted.** `V_del` is a benefit, so a level applied without an orientation reads as `CVaR_α(V_del)` — the tail of the *best* months (`TN-01` §0). A sign defect, invisible to every registered invariant |
| 6 | Summing separately-taken tails is safe because the components co-move | `TN-01` §4 | **Refuted as a reassurance.** Subadditivity holds, so summing errs conservative — that part stands. But the equality condition is misstated (claim 7), and under §2 reading (b) the sum is over 192 per-slot tails, where the discarded diversification is far larger |
| 7 | `CVaR_α(Σ L_k) = Σ CVaR_α(L_k)` **iff** comonotone | `TN-01` §4 pt 3 | **Refuted.** Comonotone-additivity gives sufficiency only. Correct condition: iff the `L_k` admit a common maximising measure in `{Q : 0 ≤ dQ/dP_w ≤ (1−α)⁻¹}` — on a finite ensemble, iff their loss orderings agree *within the α-tail*. Witness: `w=(.2,.25,.3,.25)`, `1−α=0.3`, `L_1=(10,8,0,5)`, `L_2=(10,8,5,0)` — equality holds, yet scenarios 3→4 move oppositely, so the pair is not comonotone |
| 8 | `TN-01` §9's `INV-V-b`, `n_eff = (1−α)/max_s w_s ≥ 10`, is registrable | `TN-01` §9 | **Refuted — arithmetically infeasible.** Needs `max_s w_s ≤ 0.005` at `α=0.95`, while 64 weights summing to 1 force `≥ 1/64 = 0.0156`. Feasibility needs `S ≥ 10/(1−α)`: `S ≥ 200` at `α=0.95`, or `α ≤ 0.844` at `S=64` |
| 9 | `cvarLevel` is live across its declared `(0,1)` | `C2`:190 | **Refuted — it saturates.** §5a. Above the saturation point the measure is an essential supremum and the knob is inert; `M-P9` (`T2`:897) asserts only *weak* monotonicity, which an inert knob satisfies exactly, so the test passes on a dead parameter |
| 10 | ↑`cvarLevel` is a sound response to degraded quality | `L2`:293, `C2`:190, `T2`:236 | **Refuted.** Raising `α` *narrows* the tail mass, so `n_eff` falls — 3.2 → 1.28 → 0.64 as `α` goes 0.95 → 0.98 → 0.99 at `S=64` uniform. The ladder's answer to a worse belief is a more degenerate estimator. Separately, "widened" (`C2`:190) and "↑" (`L2`:293) are opposite readings of one instruction and the corpus never fixes which |
| 11 | `chanceLevel` is a specified instrument | `C2`:192, `:136`, `T2`:665-673 | **Refuted — never formulated.** `ADR-019` moved the whole activation hazard onto the SOC chance constraint, and `L3` formulates none: the word does not appear in the layer that must build it |
| 12 | Some invariant constrains the risk parameters | `T1` §4, `C2` §8 | **Refuted — none does.** `T1`:132-148 registers `INV-V-01`…`17`; not one mentions `cvarLevel`, `cvarWeight` or `chanceLevel`. `TN-01` §9 proposed four and none was registered. A negative `cvarWeight` is silent risk-seeking and nothing catches it. Next free ID: `INV-V-18` |
| 13 | "A single `cvarLevel` would push some accumulators the wrong way" | `L2`:168 | **Refuted, and still present.** `L2`:165 takes the tail of `V_del` itself, not per accumulator, so there are no per-accumulator directions left to push (`TN-01` §16). Deletion was already called for and the sentence survives |
| 14 | `V` and the tick objective have a consistent risk attitude | `L0` §5.3-5.4, `L3`:171 | **Refuted, and still unstated.** All three candidate methods at `L0`:442-444 take weighted **expectations**, and nothing anywhere assigns `V` a risk attitude. So risk aversion is discontinuous at a horizon boundary that moves every tick — handover §10's second mechanism, now with an anchor |

---

## 5. Where CVaR is the wrong instrument here

### 5a. `(α, S) = (0.95, 64)` is the wrong point on the family

Not an argument for a less risk-averse engine — an argument that the declared pair does not
support the estimator the corpus asks for. Three effects, and they compound.

**Saturation.** Order the losses `L_(1) ≥ L_(2) ≥ …` with weights `w_(i)`. The RU value on
an atomic measure splits the straddling atom so the tail mass is exactly `1−α`, averaging
over the atoms down to the first index whose cumulative weight reaches `1−α`. If the worst
atom alone already carries that mass — `1−α ≤ w_(1)` — the average is over that atom only:

```
CVaR_α(L) = (1−α)⁻¹ · (1−α) · L_(1) = L_(1) = max_s L_s ,
```

constant in `α` on all of `[1 − w_(1), 1)`. Uniform weights at `S=64` put that boundary at
`α = 0.984`; but reduction concentrates mass, so a worst-loss atom carrying `w = 0.08`
saturates at `α = 0.92` — **below the declared 0.95**, at which point the risk measure is
silently an essential supremum. The threshold depends on which atom carries the worst loss,
hence on the decision, so it is checkable only per solve (`INV-V-b″`).

**No tail to measure.** `(1−α)·S = 3.2` scenarios, and one or two under the non-uniform
weights a reduction actually produces. Claim 8 gives the honest alternatives: `S ≥ 200`, or
`α ≤ 0.844`.

**The reduction and the measure work against each other.** `CVaR_α` is `(1−α)⁻¹`-Lipschitz
in `W₁` (`TN-01` §5b) — a 20× amplification at `α=0.95` — while `ADR-005`:46-47's
`W₁`-optimal reduction is a mass criterion that drops the rare excursion the tail exists to
price. It also names no metric and no per-series scale, so two conformant `ReduceEnsemble`
implementations may disagree about which scenarios *are* the tail.

Then claims 9-10: the degradation ladder drives `cvarLevel` toward saturation while raising
`cvarWeight` on the same degraded number, and `M-P9` detects neither.

The repair — a level per functional, market-facing one at `α ≈ 0.8–0.9` with a compensating
weight — is nearly the same decision criterion, far better estimated, and half the
amplification. Cheaper than changing measures (C3).

### 5b. What is `cvarWeight` standing in for?

`C2`:191 defines it as a weight and nothing further. No risk appetite, loss budget or risk
limit exists anywhere in the corpus; the only text naming appetite is `P1`:434, which
assigns it to the tier ladder and notes that "the cost of being wrong is not in the spec".
And the corpus's own behaviour says it is being used as a *model-risk* knob: `L2`:295 and
`T2`:243 raise it on degraded price beliefs.

| If the aversion is really… | …the honest instrument is |
|---|---|
| **Belief error** — model risk, not tail risk (and this is what the quality map does today) | `W₁`-DRO with radius equal to the reduction bound §5a already computes. Stays a finite convex program for PWL losses, so the `ADR-008` class survives; unlike CVaR it does not assume the reduced tail *is* the true tail. CVaR is itself a robust expectation over a density-bounded set, so this is an upgrade inside the family. Composes with C3 |
| **A contractual nonlinearity** — a balancing-group markup, a penalty band, a two-price arrangement | Write the convex PWL down and take an **expectation**. A single-price regime prices one ex-post price per period for the whole control area, exogenous to any one balancing group, so the incentive is already in the expected cost; a CVaR on top double-counts unless a contract sits on top |
| **A loss budget** — "never lose more than `B` per period" | `CVaR_α(loss) ≤ B` as a **constraint**. Also LP-representable, `B` is observable where `cvarWeight` is not, and it is the only reading that keeps CVaR while removing `cvarWeight` from the objective |

Swept for `risk appetite`, `loss budget`, `risk limit`, `two-price`, `pass-through`,
imbalance `markup`, `Bilanzkreis` — nothing. Under the regulatory-primitive rule the middle
row's primitive is one the owner supplies, and it is the input that decides the row.

### 5c. What the compliance layer cannot conclude

CVaR admits no strictly consistent scoring function (Gneiting 2011); it is only *jointly*
elicitable with VaR (Fissler–Ziegel 2016). So `TN-01` §3c(c)'s recommended exercise — roll
the policy on held-out paths, compare a terminal realised CVaR against the value the tick
objective claimed, with an acceptance band — is **not a proper scoring rule**, and at a
handful of genuine tail events per accounting period it has almost no power to separate a
well-calibrated risk term from a badly calibrated one. The backtest has to be on the
`(VaR, CVaR)` pair, and the sample is still thin.

A compliance-layer limit, not an `L2`/`L3` defect: elicitability governs evaluating a
forecast, not making a decision, where coherence and convexity buy the properties. It
belongs in `T4`/`T5`, written down *before* the exercise is run and believed (C5).

### 5d. Which carriers time-inconsistency actually blocks

Handover §10 states the finding generically. It does not bite equally.

**Peak and delineation are terminal path functionals** — a running max, and month-to-date
sums feeding a month-end `MAX[AW − MW_month; 0]`. That is exactly what makes tail
re-selection and the horizon discontinuity bite, and claim 14 compounds it.

**Imbalance cost is time-additive and settles per slot** (`L5`:143). No month-long
commitment to unwind; the mechanism still operates through SOC and the intraday position,
so the finding is not void, but the drift is hours rather than weeks.

**So the conditioning-versus-regeneration question** (`L3`:23-24 conditions a fresh ensemble
on the realised state; `L1`:364-365 regenerates daily on `C_slow` — different objects, and
only the first makes time consistency well-posed) blocks the path-functional carriers and
not the imbalance one. Decide them separately; imbalance can move first.

---

## 6. Closed: the activation leg

`ADR-019` accepted this note's earlier refutation, and `L3`:171 now risk-weights imbalance
alone. In short: `C1` carried no activation cost — `activationUp/Dn` is a dimensionless
fraction and the euros attached to activation are *revenue* (`C1`:54-55); the energy moved
to deliver an activation was already owned by `SpotView`, `OppCostView` and `V`; `L5` booked
realised `ActivationRisk = 0` identically, so the penalty had no realised counterpart to
calibrate against; the real hazard is deliverability, already routed to `chanceLevel`
(`L2`:296), so it was modelled twice under two measures, one dimensionless. And the two legs
were weighted in opposite directions — revenue as an expectation, "cost" under CVaR — on a
pair that is close to **anti**-comonotone, which is where `Σ_k ρ(L_k) ≫ ρ(Σ_k L_k)`: the
aggregation discarding the *most* diversification, not the least.

Two things it leaves: the instrument the decision now leans on is never formulated
(claim 11), and widening the argument to all market-facing EUR loss stays open by design
(C2).

---

## 7. Consequences for the spec

Advisory; the acts are the owner's. Labels are stable — `ADR-019` cites C2 and C3 by label —
so new items are appended. **Ranked: C7, C1, C3, C8, C2, C4, C5, C6.**

**C7 — decide how a risk functional crosses `C2`.** The blocking item (§2). Either
`INV-V-06` gains a stated exception for second-stage loss coefficients, or `C2` gains a
sixth shape carrying `α`, the weight, the per-scenario loss row and its declared row/column
cost. `C2` §2 gains the RU auxiliaries either way, and `ADR-008`:36-40 and `:50-52` are
rewritten to name a shape that exists. *Major `C2` bump.*

**C1 — the definition, to the split-atom RU form.** `ADR-005`:61-62, `L2`:61-66 and the
five further sites in claim 1. Buys coherence and LP-exactness together. *Seven text swaps,
no contract change.*

**C3 — a `cvarLevel` per functional, market-facing one lower.** `C2`:190 carries one level
across three carriers. Now derived rather than asserted: claim 8 forces `α ≤ 0.844` at
`S=64`. Also required by claim 5's sign defect, so it pays twice. *`C2` field change,
version bump.*

**C8 — register the risk-parameter invariants** (claim 12), starting at `INV-V-18`; and
formulate the SOC chance constraint in `L3` (claim 11). *Register rows plus one `L3`
subsection.*

**C2 — widen the risk argument to market-facing EUR loss.** The half `ADR-019` left open:
imbalance plus the spot/intraday leg that offsets it. Changes *what* is risk-weighted.
Decide with C3, after C7. *One objective line.*

**C4 — state what `cvarWeight` means, then pick the instrument.** An `ADR-015` entry
(currently a stub) phrased as §5b's three-way question. The one item that could *remove*
work. *A decision, not a change.*

**C5 — record §5c's limit in `T4`/`T5`.** *A paragraph. Prevents a calibration exercise
that would otherwise be run and believed.*

**C6 — fix `TN-01` §4's equality condition** to claim 7 and drop the co-movement
reassurance. Live in a stronger form if §2 reading (b) is what gets built. *One sentence.*

**Also delete** `L2`:168 (claim 13) — one line, arguing for the opposite of what `L2`:165
does.

**Proposed invariants.** `TN-01` §9's `INV-V-b` must not be registered as stated (claim 8).

| Proposed | Statement | Response |
|---|---|---|
| `INV-V-b′` | `n_eff = (1−α)/max_s w_s ≥ 10` per risk functional, checked **against the reduction artefact**, not as a tuning target | warn + `DEGRADED`; `HALT` if the triple is arithmetically infeasible, as `(0.95, 64)` is |
| `INV-V-b″` | `1 − cvarLevel > w_(1)`, computed **per solve** — below this the functional is an essential supremum and `cvarLevel` is inert | warn + `DEGRADED`, naming the saturated functional; the ladder must not report conservatism it did not obtain |
| `INV-V-a` | `cvarLevel ∈ (0,1)`, `cvarWeight ≥ 0`, `chanceLevel ∈ (0,1)` — from `TN-01` §9, still unregistered | `HALT` — a negative weight is silent risk-seeking |
| `INV-V-c` | Every functional runs on a loss under the `TN-01` §0 convention; a tail of a benefit is negated | `HALT` — sign defect, not tuning (claim 5) |
| `INV-D-h` | Reduction preserves `CVaR_α` of each risk-carrying marginal within a stated band — companion to `INV-D-07`, vacuous for path functionals | warn + `DEGRADED` |

**Registrable claims** — new, in the register's format:

```
no C2 shape can carry the RU program; L3:171 is not assemblable        analytic  (§2)
eImbalance is [S,H] but every coefficient reaching it is [H]           analytic  (claim 4)
a per-slot PwlTerm reading gives Σ_t CVaR_α — 192 tails, not one       analytic  (§2)
CVaR_α = ess sup, constant in α, for all α ≥ 1 − w_(1)                 analytic  (§5a)
↑cvarLevel lowers n_eff, so the ladder degrades the estimator          analytic  (claim 10)
M-P9 is satisfied by a saturated, inert cvarLevel                      analytic  (claim 9)
no registered invariant constrains any risk parameter                  analytic  (claim 12)
L3 formulates no SOC chance constraint — the instrument ADR-019 uses   analytic  (claim 11)
n_eff ≥ 10 is infeasible for every ensemble at (α, S) = (0.95, 64)     analytic  (claim 8)
CVaR_α(Σ) = Σ CVaR_α iff tail orderings agree in the tail             analytic  (claim 7)
the delineation λ_j linearises a tail whose tail set moves with x      analytic  (§2)
the Σ_t-vs-Σ_k diversification gap on realised data                    empirical, band TBD
a contract sits on top of the single imbalance price for this site     assumed-declared
```

The last is `assumed-declared` and currently assumed **false** — the corpus is silent
(§5b) — with the consequence pre-committed: if a markup or penalty band exists, C4 resolves
to row 2 and this leg's risk term becomes a PWL rather than being retuned.

---

## 8. Compared to what

None of these repairs §2 — the payload carries none of the first four either.

| Alternative | Why not |
|---|---|
| **Variance / mean-variance** | Punishes a favourable excursion as hard as an adverse one; wrong shape for a skewed one-sided loss, and it costs the MILP class. `L2`:121-123's own reason, and correct |
| **VaR** | Non-convex on atomic `P_w`, needs binaries, blind to how bad the tail is beyond the quantile — which is the whole content of imbalance risk |
| **Entropic / exponential utility** | The one law-invariant family that is genuinely time-consistent, so superficially attractive. But not positively homogeneous and not LP-representable: it breaks the price-outside-the-tail identity `L2`:61-66 depends on, and makes the measure scale-dependent |
| **Worst case / ess sup** | Coherent and time-consistent, but at `S=64` on a `W₁`-reduced ensemble it is the tail of an artefact. Usable only with a calibrated ambiguity radius — which is the DRO row. Note §5a: the corpus may already be here by accident |
| **Expectiles** | Coherent *and* elicitable, so they repair §5c. But no euro interpretation to show an operator, they forfeit the probability-distortion reading, and they would isolate this carrier from the other two. Fallback if §5c's limit ever binds; not now |
| **`W₁`-DRO on a PWL loss** | Not rejected — §5b's recommendation, conditional on C4 resolving to "belief error" |

---

## 9. Sources

Primary only. `TN-01` §10 carries the Rockafellar–Uryasev, Artzner–Delbaen–Eber–Heath,
Kupper–Schachermayer, Ruszczyński, Shapiro, Epstein–Schneider and
Dupačová–Gröwe-Kuska–Römisch results and is not duplicated here.

| Result used | Source |
|---|---|
| CVaR is not elicitable | Gneiting (2011), *Making and evaluating point forecasts*, JASA 106(494) |
| `(VaR, CVaR)` is jointly elicitable | Fissler & Ziegel (2016), *Higher order elicitability and Osband's principle*, Ann. Statist. 44(4) |
| Expectiles are the only coherent elicitable law-invariant measures | Bellini, Klar, Müller & Rosazza Gianin (2014), *Generalized quantiles as risk measures*, Insurance Math. Econom. 54; Ziegel (2016), *Coherence and elicitability*, Math. Finance 26(4) |
| `W₁`-DRO with PWL loss reduces to a finite convex program | Mohajerin Esfahani & Kuhn (2018), *Data-driven distributionally robust optimization using the Wasserstein metric*, Math. Prog. 171 |
