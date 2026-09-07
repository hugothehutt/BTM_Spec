# TN-04 — The imbalance risk device: is CVaR the right measure?

**Ticket:** [#71](https://github.com/kpfefferlcreatica/BTM_Spec/issues/71) · **Class:** analytic ·
**Method:** derivation only, no data touched · **Branch:** none — written in place

Asks of one term what `TN-01` never asks of it: not whether CVaR has the properties the
corpus claims, but whether CVaR is the right *instrument* for the risk the term names.
The term is `L3`:171's `− cvarWeight · CVaR_α(imbalance + activation cost)`.

`TN-01` takes the choice of measure as given and adjudicates its properties. This note
takes the properties as settled — they are, and `TN-01` §§1-2 are why — and attacks the
choice. It bears directly on
[#71](https://github.com/kpfefferlcreatica/BTM_Spec/issues/71)'s second unwritten
load-bearing hypothesis, *"aggregate EUR loss is the right argument"*, and on its fourth,
*"the `S`-atom measure resolves the `(1−α)` tail"*.

Self-contained: every fact it rests on is either derived here or cited to a line of this
corpus. All anchors were re-read at the time of writing.

Nothing here is an ADR. §11 lists what the specification must gain or lose; the act is the
owner's.

---

## 0. Notation

Follows `TN-01` §0 exactly, and that section is the reference for the loss convention.
The three symbols used below:

| Symbol | Meaning |
|---|---|
| `L` | a loss in EUR — positive is bad, `L = −(contribution to the objective)` |
| `P_w` | the reduced ensemble's induced measure: `S` atoms, weights `w_s ≥ 0`, `Σ_s w_s = 1` (`ADR-005`, `INV-D-06`) |
| `α` | the confidence level; the tail is the worst `(1−α)` mass |

```
CVaR_α(L) = min_ζ { ζ + (1−α)⁻¹ · E_w[(L − ζ)⁺] }
```

---

## 1. The term, and where it lives

The Planner's objective (`L3`:169-171) closes with

```
     − cvarWeight · CVaR_α(imbalance + activation cost)
```

and `cvarWeight` is granted to that term alone. The surrounding text:

| Site | What it says |
|---|---|
| `L2`:117-126 | `AfrrEnergyView` / `ImbalanceRiskView` emit `LinearTerm`s for **expected** activation value plus a `PwlTerm` (convex, Minimize) for the CVaR functional on imbalance cost. Stated reason for CVaR over a variance penalty: "same risk intent, no quadratic term, no loss of MILP structure" |
| `ADR-008`:50-52 | The same at ADR level: CVaR is LP-representable (Rockafellar–Uryasev), "so a CVaR-penalised objective stays a MILP" |
| `C2`:190-192 | One `cvarLevel` (`(0,1)`, e.g. 0.95), one `cvarWeight` (`≥0`, `0` recovers the risk-neutral objective), one `chanceLevel` (ε for SOC feasibility chance constraints) |
| `C1`:53-56 | `afrrEnergyPriceUp/DnEurPerMwh` in EUR/MWh; `activationUp`/`activationDn` **dimensionless**, `[0,1]`, "fraction of committed MW actually called"; `imbalancePriceEurPerMwh` (reBAP-type belief) |
| `L2`:248, 256 | Ownership: `ImbalanceCost` → `ImbalanceRiskView`; `ActivationRisk` → `AfrrEnergyView`. Both on base `MarketVolume` |
| `L2`:292 | Quality→risk mapping: degraded `activation` beliefs → **↑ `chanceLevel` for SOC feasibility** |
| `L5`:143 | `ImbalanceCost = Σ_t imbalanceCostEur[t]` — time-additive, settling per slot |
| `L5`:180, 194-198 | Realised `ActivationRisk = 0` identically. "Risk terms are not cashflows… ex post there is no risk, only an outcome, which has already been booked to `ReserveEnergyRevenue` and `ImbalanceCost`" |

---

## 2. Verdict

**CONFIRMED for the imbalance leg. REFUTED for the activation leg. And the choice of
measure is not this term's binding defect — four of the six findings below are upstream of
it and survive replacing CVaR with anything else.**

CVaR is the right *class* for imbalance: a monetary, one-sided, skewed loss that settles
per slot. §10 shows it dominates every alternative on the axes this engine has already
committed to. It is the wrong *instrument* for activation, because the corpus defines no
activation cost to take a tail of (§3), and the hazard activation actually presents is
already carried elsewhere by the right instrument.

---

## 3. The activation leg has no loss variable — REFUTED

`CVaR_α(imbalance + activation cost)` risk-adjusts a quantity that is defined nowhere in
this corpus. Four independent confirmations, each from a different layer:

**(i) `C1` carries no activation cost.** `activationUp`/`Dn` (`C1`:55) is a dimensionless
fraction in `[0,1]`. The euros attached to activation are `afrrEnergyPriceUp/Dn`
(`C1`:53), which is **revenue**. The energy cost of delivering an activation is already
owned, under `L2`'s ownership matrix, by `SpotView` and by `OppCostView`/`V`. There is no
third quantity, and adding a fraction to euros does not create one.

**(ii) `L5` already concedes the term is a shadow.** `ActivationRisk = 0` identically
(`L5`:180), because "ex post there is no risk, only an outcome" (`L5`:194-198). `L5`
therefore runs the four-bucket decomposition on the **risk-free** objective on both sides.
So `cvarWeight`'s effect on this leg has no realised counterpart against which it could be
scored: it is unfalsifiable by construction, and no amount of settlement data will
calibrate it.

**(iii) The hazard is feasibility, not a euro tail.** What hurts when activation runs
against the plan is SOC exhaustion, failure to deliver committed reserve, and loss of
qualification. `L2`:292 already routes degraded activation beliefs to **`chanceLevel` on
SOC feasibility** — a chance constraint, which is the correct instrument for a
deliverability risk. The same hazard is therefore modelled twice, under two different
measures, one of them dimensionless. That is how an engine acquires a risk aversion no
diagnostic can attribute to a cause.

**(iv) `ADR-005`'s guarantee is spent on the wrong pairing here.** The stated payoff of the
shared scenario axis (`L2`:124-126) is that "activated when prices are extreme" stays
representable. True, and valuable — but it is realised through the *revenue* channel and
the *SOC* channel, neither of which needs a cost term that does not exist.

---

## 4. The two legs are risk-weighted in opposite directions — and this is the worst pair to sum

Activation revenue enters as an **expectation** `LinearTerm` (`L2`:119). Activation "cost"
enters under **CVaR**. One underlying event, two measures, opposite signs. Even granting
§3 a cost variable, the term would be mis-signed.

This also inverts `TN-01` §4's own reassurance, and the inversion matters more than the
sign. `TN-01` §4 establishes that

```
Σ_k CVaR_α(L_k)  ≥  CVaR_α( Σ_k L_k )
```

so summing separately-taken tails is a conservative upper bound, and the gap is exactly
the diversification benefit the objective discards. `TN-01` then argues the gap is probably
small "because `ADR-005` exists precisely because these components co-move in the tail".

**For this pair the argument runs backwards.** Activation is called when imbalance prices
are extreme — that is the correlation `ADR-005` exists to preserve — so activation revenue
and imbalance cost are close to **anti**-comonotone. That is precisely the configuration
in which `Σ_k ρ(L_k) ≫ ρ(Σ_k L_k)`. Of every aggregation in the objective, this is the one
where the discarded diversification is *largest*, not smallest.

**A correction to `TN-01` §4 that this section depends on.** `TN-01` §4 point 3 states
equality "**iff** the `L_k` are comonotone". Comonotone-additivity gives sufficiency only;
necessity was read off it without proof and is false. The correct condition: equality iff
the `L_k` admit a common maximising measure in `{Q : 0 ≤ dQ/dP_w ≤ (1−α)⁻¹, Q(Ω)=1}` —
on a finite ensemble, iff they agree on the loss ordering *restricted to the α-tail*.
Witness, on the `TN-01` §0 definition:

> `S = 4`, `w = (0.2, 0.25, 0.3, 0.25)`, `1−α = 0.3`,
> `L_1 = (10, 8, 0, 5)`, `L_2 = (10, 8, 5, 0)`.
> `CVaR(L_1) = CVaR(L_2) = 2.8/0.3 = 9.333`;
> `L_1 + L_2 = (20, 16, 5, 5)`, `CVaR = 5.6/0.3 = 18.667 = 9.333 + 9.333`.
> Equality holds, yet scenarios 3→4 carry opposite increments, so the pair is not
> comonotone.

The correction is in `TN-01`'s favour on safety — subadditivity is untouched, so summing
still errs conservative — but it does not rescue the activation/imbalance pair, which is
the pair least likely to satisfy the tail-ordering condition. `TN-01` §4's text is the
place to fix the condition; it is recorded here because §4 above rests on it.

---

## 5. Sensitivity to the reduction dominates the choice of measure

`TN-01` §5b establishes that `CVaR_α` is `(1−α)⁻¹`-Lipschitz with respect to `W₁`. At
`α = 0.95` that is a **20× amplification** of scenario-reduction error. Two consequences
for this term specifically:

**The reduction optimises the bulk and discards the tail.** `ADR-005`:47 specifies
fast-forward selection / "Wasserstein-optimal reduction". `W₁` is a mean-transport
criterion, so what it drops first is the rare price excursion this term exists to price.
The reduction and the measure are working against each other, and the measure amplifies
the disagreement twentyfold. Note also that `ADR-005`:47 and `T5`:374 name no metric and
no per-series scale, so two conformant implementations of `ReduceEnsemble` may disagree
about which scenarios *are* the tail.

**At `S = 64` there is no tail to measure.** `(1−α)·S = 3.2` scenarios at `α = 0.95`. And
`TN-01` §9's proposed `INV-V-b` — `n_eff = (1−α)/max_s w_s ≥ 10` — is **infeasible at the
declared `S`**: it requires `max_s w_s ≤ 0.005`, while 64 non-negative weights summing to
1 force `max_s w_s ≥ 1/64 = 0.0156`. No ensemble at `S = 64` can satisfy it, at any
weighting. So the imbalance tail at the declared `S` and `α` is an estimate of nothing in
particular, with a bias whose sign is set by the reduction artefact rather than by the
world.

This is **not** an argument for a less risk-averse engine. It is an argument that
`α = 0.95` is the wrong point on the CVaR family for this ensemble size, and the repair
(§11, `C3`) is cheaper than changing measures.

---

## 6. The corpus does not currently specify CVaR

`ADR-005`:61-62 defines `TailStatistic` as "CVaR-type functionals, computed as a weighted
empirical mean over the joint ensemble (this is what `PeakView` uses)". `L2`:61-66 defines
the peak level as "the weighted mean of the worst `(1−α)` mass". Both are the tail
conditional expectation `E[L | L ≥ VaR_α]`, which on an atomic weighted measure is **not
coherent** — subadditivity fails — and which **understates**. It is also not the
Rockafellar–Uryasev object that `ADR-008`:52 relies on for LP-exactness.

Recorded here because it is load-bearing for this note: while the text specifies a tail
mean, "is CVaR the right measure" is moot, because CVaR is not what would be built. Both
the coherence the composer's summing implicitly assumes (`TN-01` §2) and the MILP-class
guarantee require the split-atom RU form.

Seven sites carry the risk-averse form and must be checked together for the same defect:
`ADR-005`:10, `ADR-005`:61-62, `ADR-017`:61, `T2`:212, `C5`:27, `L2`:289, `L2`:61-66.

---

## 7. `cvarWeight` has no referent

`C2`:191 defines `cvarWeight` as a weight `≥0` whose zero "recovers the risk-neutral
objective", and nothing further. No statement of risk appetite, loss budget or risk limit
exists anywhere in the corpus: the only text naming risk appetite is `P1`:434, which
assigns it to the *tier ladder's escalation policy* and observes that "the cost of being
wrong is not in the spec".

So the engine's risk aversion currently lives in three levers with no common unit and no
calibration tying any of them to a stated appetite:

- `cvarWeight`, on the imbalance/activation term;
- `peakPrice`, which by `TN-01` §0's positive-homogeneity pass-through is the peak term's
  **only** risk weight, since `L3`:171 grants `cvarWeight` to the imbalance term alone;
- `chanceLevel`, on SOC feasibility.

**The question to force before the measure is touched: what is `cvarWeight` standing in
for?** Three candidates, each with a *different* correct instrument, only one of which is
a risk measure:

| If the aversion is really… | …then the honest instrument is |
|---|---|
| **Belief error on the imbalance price** — model risk, not tail risk | A distributionally-robust formulation: a `W₁` ball whose radius is the reduction bound already computed. Still LP for piecewise-linear losses, so the `ADR-008` class survives; and unlike CVaR it does not assume the reduced tail *is* the true tail. CVaR is itself a robust expectation over a density-bounded set, so this is an upgrade inside the same family, not a change of philosophy |
| **A contractual nonlinearity** — a balancing-group pass-through markup, a penalty band, a two-price arrangement | Write the convex PWL down and take an **expectation**. No risk measure needed. A single-price imbalance regime prices one ex-post price per settlement period for the whole control area, exogenous to any one balancing group's position, so the regulator's incentive is *already in the expected cost*; a CVaR on top of it double-counts unless a contract sits on top of it |
| **A loss budget** — "never lose more than `B` in an accounting period" | `CVaR_α(loss) ≤ B` as a **constraint**, not a weighted penalty. Also LP-representable, and `B` is an observable business quantity where `cvarWeight` is not |

**The corpus is silent on the middle row's primitive.** Swept for `risk appetite`,
`loss budget`, `risk limit`, `two-price`, `pass-through`, imbalance `markup` and
`Bilanzkreis`; nothing. Under the regulatory-primitive rule that is a primitive the owner
supplies, not one this note researches — and it is the input that decides the row.

---

## 8. Non-elicitability, and what it costs the compliance layer

CVaR admits no strictly consistent scoring function (Gneiting 2011); it is only *jointly*
elicitable with VaR (Fissler–Ziegel 2016). Expectiles are the only law-invariant family
that is both coherent and elicitable (Bellini et al. 2014; Ziegel 2016).

This binds on `TN-01` §3c(c), which `TN-01` recommends adopting: roll the policy on
held-out paths, evaluate one terminal static CVaR of realised EUR, and compare it against
the value the tick objective claimed, with an acceptance band. That comparison is **not a
proper scoring rule**, and with a handful of genuine tail events per accounting period it
has almost no power to separate a well-calibrated risk term from a badly calibrated one.
The backtest has to be on the `(VaR, CVaR)` pair, and even then the sample is thin.

This is a **compliance-layer limitation, not an `L2`/`L3` defect**. Elicitability governs
the evaluation of a forecast, not the making of a decision, where coherence and convexity
are what buy the optimisation properties. It belongs in `T4`/`T5` as a stated limit on what
the calibration can conclude — written down before the exercise is run and believed, not
after.

---

## 9. Where the corpus is right: imbalance is the defensible static CVaR

`TN-01` §3b states its time-consistency attack generically, and it reads as though it
applies equally to all three functionals carrying `cvarLevel`. It does not, and the
difference is worth recording because it decouples two decisions the corpus currently
treats as one.

Peak is a running max, and the delineation accumulators are month-to-date sums feeding a
month-end `MAX[AW − MW_month; 0]`. Both are **terminal path functionals**, which is
precisely what makes `TN-01` §3b(i)'s tail re-selection and §3b(ii)'s horizon-discontinuity
bite: a hedge established at tick `k` is unwound at `k+1` because uncertainty resolved
favourably, and the risk attitude is discontinuous at a truncation boundary that moves.

Imbalance cost is **time-additive and settles per slot** (`L5`:143). There is no
month-long commitment to unwind. The re-selection mechanism still operates through SOC and
through the intraday position, so the finding is not void — but the drift is over hours
rather than weeks, and the magnitude is correspondingly smaller.

**Consequence.** Whatever is decided about conditioning-versus-regeneration across ticks
(`L3`:24 conditions a fresh ensemble on the realised state; `L1`:364-365 regenerates it
daily on `C_slow` — different objects, and only the first makes time consistency a
well-posed predicate) blocks the *path-functional* terms. It does not equally block this
one. The two can be decided separately, and this one can move first.

---

## 10. The alternatives, and why each is rejected

Since "is CVaR right" invites "compared to what":

| Alternative | Why not, for this term |
|---|---|
| **Variance / mean-variance** | Penalises a *favourable* position into an extreme imbalance price exactly as hard as an adverse one. Wrong shape for a skewed one-sided loss, and it costs the MILP class. This is `L2`:121-123's own stated reason, and it is correct |
| **VaR** | Non-convex on the atomic `P_w`, needs binaries, and blind to how bad the tail is beyond the quantile — which is the entire content of imbalance risk |
| **Entropic / exponential utility** | The one law-invariant family that is genuinely time-consistent, so superficially attractive given `TN-01` §3. But not positively homogeneous and not LP-representable. Losing positive homogeneity specifically breaks the price-outside-the-tail identity `TN-01` §0 depends on for `L2`:61-66, and it makes the measure depend on the site's scale |
| **Worst case / essential supremum** | Coherent and time-consistent, but at `S = 64` on a `W₁`-reduced ensemble it is the tail of an artefact (§5). Usable only with a calibrated ambiguity radius — at which point it is §7's DRO row, which is the recommendation there |
| **Expectiles** | Coherent *and* elicitable, so they repair §8. But no interpretation as a euro amount an operator can be shown, and adopting them would isolate this term from the other two `cvarLevel` carriers. Name them as the fallback if §8's calibration limit ever becomes binding; do not adopt now |

**What CVaR uniquely gives**, and the strongest single argument for keeping it:
`λ·CVaR_α + (1−λ)·E` is coherent, and equals an expectation under *inflated tail
probabilities*. That makes `cvarWeight` interpretable as a probability distortion — the one
framing under which it could actually be calibrated against a stated appetite (§7). No
other candidate offers it.

---

## 11. Consequences for the spec

Advisory. ADRs are the owner's act. Ordered cheapest and most decisive first; each is
stated as an act on named text, per `CLAUDE.md` rule 2 — replaced, not annotated.

**C1 — the definition, to the split-atom RU form.** `ADR-005`:61-62 and `L2`:61-66, plus
the five further carriers listed in §6. This is `TN-01` §9's own first item; it is repeated
here because §6 makes it a precondition for everything else on this list. Buys coherence
and LP-exactness together. *Cost: a definition swap in seven places. No contract change.*

**C2 — drop "activation cost" from the CVaR argument.** Amend `L3`:171 so the risk
functional runs on realised market-facing EUR loss — imbalance plus the spot/intraday leg
that offsets it — and leave activation's deliverability hazard where `L2`:292 already
correctly puts it, on `chanceLevel`. Consequences to carry: `ActivationRisk` (`L2`:256)
either acquires a defined base or leaves the ownership matrix, and `L5`:180's
identically-zero row goes with it. *Cost: one objective line, one matrix row, one `L5`
row. No `C2` payload change.* **Highest-value item on this list**: it removes an ownerless
dimensionless quantity from the objective and eliminates a doubly-modelled hazard.

**C3 — separate `cvarLevel` per functional, and lower it for the market tail.** `C2`:190
carries one `cvarLevel` across three functionals. Split it, and set the market-facing one
to `α ≈ 0.8–0.9` with a compensating `cvarWeight`: nearly the same decision criterion, far
better estimated, and roughly half the `(1−α)⁻¹` amplification of reduction error. *Cost: a
`C2` field change, hence a version bump.* This is **also** required by `TN-01` §0's sign
finding — one `cvarLevel` across three functionals, one of which (`V_del`) is a benefit —
so the change pays for itself twice.

**C4 — state what `cvarWeight` means, then pick the instrument.** An `ADR-015` entry,
phrased as §7's three-way question. The one item that could *remove* work: if the answer is
"contractual nonlinearity", the correct change is to write the PWL down and delete the risk
term for this leg. If it is "belief error", the target is a `W₁`-DRO form with radius equal
to the reduction bound — same LP class, and it composes with `C3` rather than competing
with it. If it is "loss budget", the penalty becomes a constraint. *Cost: a decision, not a
change. Blocks nothing, but determines whether `C3`'s tuning is worth doing at all.*

**C5 — record §8's limitation in the compliance layer.** `T4`/`T5`, wherever `TN-01`
§3c(c)'s rolled-policy comparison lands: the acceptance band is not a proper scoring rule,
the backtest is on the `(VaR, CVaR)` pair, and the power at the realised tail-event count
must be stated. *Cost: a paragraph. Prevents a calibration exercise that would otherwise be
run and believed.*

**C6 — fix `TN-01` §4's equality condition** to §4 above, and remove the reassurance that
the diversification gap is small because the components co-move. If `C2` is adopted the
point is moot for this pair; if `C2` slips, the reassurance must not be relied on. *Cost:
one sentence, but it must not be skipped if `C2` slips.*

**Proposed invariant.** `TN-01` §9's `INV-V-b` is infeasible as stated (§5) and should not
be registered in that form. Its replacement:

| Proposed | Statement | Response |
|---|---|---|
| `INV-V-b′` | `n_eff = (1−α)/max_s w_s ≥ 10` for every risk functional, checked **against the reduction artefact** rather than as a tuning target — the reduction must deliver weights that resolve the declared `α` at the declared `S`, or one of the three must change | warn + `DEGRADED`; and a `HALT` if the triple is arithmetically infeasible, as `(α, S) = (0.95, 64)` is |

**Registrable claims** — new, from this note, in the register's format:

```
activation carries no cost primitive in C1; the term's argument is undefined   analytic
E[activation revenue] and CVaR(activation cost) are oppositely signed          analytic
imbalance and activation revenue are anti-comonotone in the tail               empirical, band TBD  (§4)
CVaR_α(Σ) = Σ CVaR_α iff tail orderings agree; comonotone sufficient only      analytic
n_eff ≥ 10 is infeasible for every ensemble at (α, S) = (0.95, 64)             analytic
CVaR admits no strictly consistent scoring function                            analytic
imbalance cost is time-additive, so §3b's drift is hours not weeks             analytic
a contract sits on top of the single imbalance price for this site             assumed-declared
```

The last is registered **`assumed-declared`** and currently assumed **false** — the corpus
is silent (§7), and the consequence is pre-committed: if a balancing-group markup or
penalty band exists, `C4` resolves to the middle row and the risk term for this leg is
replaced by a PWL rather than retuned.

---

## 12. Sources

Primary only; each supports a specific line above. `TN-01` §10 carries the
Rockafellar–Uryasev, Artzner–Delbaen–Eber–Heath, Kupper–Schachermayer, Ruszczyński,
Shapiro, Epstein–Schneider and Dupačová–Gröwe-Kuska–Römisch results and is not duplicated
here.

| Result used | Source |
|---|---|
| CVaR is not elicitable | Gneiting (2011), *Making and evaluating point forecasts*, JASA 106(494) |
| `(VaR, CVaR)` is jointly elicitable | Fissler & Ziegel (2016), *Higher order elicitability and Osband's principle*, Ann. Statist. 44(4) |
| Expectiles are the only coherent elicitable law-invariant measures | Bellini, Klar, Müller & Rosazza Gianin (2014), *Generalized quantiles as risk measures*, Insurance Math. Econom. 54; Ziegel (2016), *Coherence and elicitability*, Math. Finance 26(4) |
| `W₁`-DRO with piecewise-linear loss reduces to a finite convex program | Mohajerin Esfahani & Kuhn (2018), *Data-driven distributionally robust optimization using the Wasserstein metric*, Math. Prog. 171 |
