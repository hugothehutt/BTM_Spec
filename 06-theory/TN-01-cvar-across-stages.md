# TN-01 — CVaR across stages

**Ticket:** [#14](https://github.com/hugothehutt/BTM_Spec/issues/14) · **Class:** analytic ·
**Method:** derivation only, no data touched · **Branch:** `research/risk-measures`

Adjudicates the specification's risk treatment against the theory of coherent and
dynamic risk measures. Every spec claim below is marked **confirmed**,
**conditional** (with the condition stated) or **refuted**. The five absences
recorded on [#5](https://github.com/hugothehutt/BTM_Spec/issues/5) are resolved in §8.

Nothing here is an ADR. §9 lists what the spec must gain or lose; the act is Hugo's.

---

## 0. Notation, and the one convention that has to be fixed first

Reduced ensemble: scenarios `s = 1…S`, weights `w_s ≥ 0`, `Σ_s w_s = 1` (ADR-005,
`INV-D-06`). `P_w` is the induced discrete measure — **atomic and non-uniform**;
every result below is stated for that measure, never for a uniform sample.

`L : Ω_S → ℝ` is a **loss in EUR**: positive is bad, `L = −(contribution to the
objective)`. For `α ∈ (0,1)`:

```
VaR_α(L)  = inf{ ζ : P_w(L ≤ ζ) ≥ α }
CVaR_α(L) = min_ζ { ζ + (1−α)⁻¹ · E_w[(L − ζ)⁺] }
```

so `α` is the **confidence level** and the tail is the worst `(1−α)` mass. This
matches `L2` §2 ("the worst `(1−α)` mass") and `C2` §6 (`e.g. 0.95`), and it is
the only orientation used in this note.

**The loss convention is not currently in the spec and it is not cosmetic.** Three
functionals carry `cvarLevel` and they do not share a sign:

| Functional | Underlying quantity | Bad tail | Required form |
|---|---|---|---|
| Imbalance / activation | a cost | upper | `CVaR_α(imb + act)` |
| Peak | `max_t p_poi[s,t]`, a cost driver | upper | `CVaR_α(peakPrice · proration · max_t p_poi)` |
| Delineation | `V_del`, a **benefit** | **lower** | `CVaR_α(−V_del)` |

A single `cvarLevel` applied without a stated orientation reads as `CVaR_α(V_del)`
on the third row, which takes the tail of the *best* months. This is a sign defect,
not a tuning question, and it is invisible to every current invariant.

**Why the spec's kW-denominated notation is nonetheless sound.** `L2`:61-66 writes the
peak risk term over `max_t p_poi[s,t]` — a quantity in kW, apparently in breach of the
EUR-only rule. It is not, and the reason is an axiom: `CVaR_α` is **positively
homogeneous**, so for any deterministic `c ≥ 0`, `c · CVaR_α(X) = CVaR_α(c · X)`. With
`peakPrice` and `prorationFactor` deterministic and non-negative, taking the tail in kW
and pricing outside is *identical* to taking the tail of the EUR loss. The two forms are
the same number.

The pass-through is what fixes `peakPrice` as a **scalar deterministic input**. It fails
if the rate is scenario-dependent (an uncertain tariff), and it fails if the rate is
non-linear in the level (§1a). Both cases force the EUR form, where the tail must be
taken after pricing.

Two things the identity does **not** buy. It moves a scalar in and out of *one* `CVaR`;
it does not merge the peak tail with the imbalance or delineation tails, which remain
§4's sum-of-CVaRs. And since `L3`:109 grants `cvarWeight` to the imbalance term alone,
under a scalar rate **`peakPrice` is the peak term's only risk weight** — an error in it
is not merely a mispriced kW, it is a shift in risk aversion, on the same lever `L2`:69
already warns about.

---

## 1. Rockafellar–Uryasev on a reduced, non-uniformly weighted ensemble

> **Claim** (`L2`:100-107, `ADR-008`:44-46) — CVaR is linear-programmable
> (Rockafellar–Uryasev), so the risk term does not change the problem class.

**CONFIRMED, unconditionally, and specifically for the weighted atomic case.**

Rockafellar–Uryasev (1999) proved the minimisation formula for continuous loss
distributions; Rockafellar–Uryasev (2002), *Conditional value-at-risk for general
loss distributions*, extends it to **arbitrary distributions including those with
atoms**, which is exactly `P_w`. Requirements are `E|L| < ∞` and `α ∈ (0,1)`. There
is no uniformity, atomlessness or continuity condition to fail. The weights enter
only as objective coefficients:

```
min  ζ + (1−α)⁻¹ Σ_s w_s u_s
s.t. u_s ≥ L_s − ζ ,  u_s ≥ 0 ,  ζ free
```

`S + 1` continuous variables, `S` rows, **no binaries**. Problem class preserved.

Three conditions that are *not* in the spec and do bind:

**1a. `L_s` must be a linear expression in the model's variables.** For the peak term
`L_s = max_t p_poi[s,t]`, that requires a **per-scenario** epigraph variable
`z_s ≥ p_poi[s,t] ∀ t ∈ overSlots`. Since `CVaR_α` is non-decreasing in `L` and the
objective minimises it, `z_s` is pushed down to the true max: **the relaxation is
exact, no binaries.** But `C2`'s `EpigraphTerm` and `ADR-008`:36-41 / `ADR-010`:65
carry **one** `z_peak`, unindexed by `s`. That is a deterministic epigraph on a single
trajectory. §5 treats the consequence.

**The convexity here is on the *level*, not on the price — and the distinction binds.**
`max_t p_poi` is convex in `x` unconditionally. What the exactness argument additionally
needs is that the **charge is convex and non-decreasing in the level**. The spec supplies
that by pricing at a single scalar rate — `−peakPrice · zPeak · proration` (`L3`:107),
settled as `peakPrice · realisedPeakKw` (`L5`:216) — which is linear, hence convex. All
four `ADR-011` regimes are of this shape. **On the spec as written the condition holds.**

It fails the moment the rate is piecewise with a *decreasing* marginal €/kW — a first
slope steeper than the second. Then `c(·)` is concave non-decreasing and
`c(max_t p_poi)` is **not convex**. Consequences, kept separate because they fail
differently:

- The epigraph still pins the level: `c` non-decreasing drives `z` down to `max_t p_poi`.
- Representing a **concave** PWL under minimisation requires SOS2 / binaries. The
  convex-combination LP relaxation is not tight — it returns the lower convex envelope,
  i.e. a **cheaper peak than the tariff charges**. A *third* downward bias on this term,
  stacking with the two in §5b.
- `CVaR_α` preserves convexity only for per-scenario convex `L` (§2). With `c` concave
  the composed risk term is non-convex in `x`. `ADR-008`:46 ("a CVaR-penalised objective
  stays a MILP") survives; `ADR-008`:37 ("no binaries, exact") does not.
- **RU is untouched.** It requires integrability, not convexity of `L`. The LP block of
  §1 stands. What is lost is convexity and tightness *in the decision variables* — a
  different failure mode from the one this section is about, and worth not conflating.

The German utilisation-hour bands (`<2500 h/a` vs `≥2500 h/a`) are the live instance and
are **worse than a kink**: two price sheets, and crossing reprices all energy, so total
cost is *discontinuous*, not merely concave. No such band appears anywhere in this
repository. Under the regulatory-primitive rule it is a primitive the repo is silent on —
supplied by the owner on request, not researched here.

**If it applies, it does not belong in the tick as a PWL.** Band membership is settled by
realised annual energy and peak; the horizon is two days. Carry the band as a further
discrete conditioning dimension of `V`, exactly as `ADR-011` already carries `qualState`
for the §19(2) S.2 cliff. `peakPrice` is then a parameter within the tick, the term is
linear in the level again, and convexity and the no-binaries claim are both restored —
with the non-convexity living where `L0` §5.3 says it should. The alternative costs
`S·(bands−1)` binaries, because band membership is a scenario-dependent second-stage
quantity: **64 per regime at `S = 64`**, for a term currently priced with none.

**1b. Joint minimisation is valid, and valid under integrality.** Minimising over
`(x, ζ)` jointly equals minimising `CVaR_α(L(x))` over `x` (RU 2002, Thm 10),
and the joint problem is convex when `L(·, ω)` is convex for each `ω`. With binaries
present the equivalence still holds — it holds for each fixed integer assignment,
hence at the MILP optimum.

**1c. `ζ*` is only a VaR at proven optimality.** At a suboptimal incumbent, `ζ` is a
free variable with no interpretation. `T5` reports an optimality gap, so any
diagnostic that reads `ζ` back as "the VaR" is valid only on gap-zero solves. Cheap
to state, expensive to discover later.

### 1d. The estimator the spec actually describes is the wrong one

> **Claim** (`L2`:61-66) — take the weighted mean of the worst `(1−α)` mass.

**REFUTED as written; confirmed only for the split-atom form.**

Order losses `L_(1) ≥ L_(2) ≥ …`, let `W_k = Σ_{i≤k} w_(i)` and `k` the least index
with `W_k ≥ 1−α`. The natural reading of "weighted mean of the worst `(1−α)` mass"
is the **naive tail mean**

```
naive = (1/W_k) Σ_{i≤k} w_(i) L_(i)
```

The RU value instead splits the straddling atom so the tail mass is *exactly* `1−α`:

```
CVaR_α = (1−α)⁻¹ [ Σ_{i<k} w_(i) L_(i) + (1−α − W_{k−1}) L_(k) ]
```

Both are averages over the same `k` scenarios, but the naive form puts weight
`w_(k)/W_k` on `L_(k)` — the *smallest* loss in the tail — where the correct form puts
`(1−α−W_{k−1})/(1−α)`. Since `x ↦ x/(W_{k−1}+x)` is increasing and
`1−α−W_{k−1} ≤ w_(k)`, the naive weight is the larger. Therefore

```
0 ≤ CVaR_α − naive ≤ (δ / (1−α)) · (L_(1) − L_(k)) ,    δ = W_k − (1−α) < w_(k)
```

**The naive estimator always understates.** Define the **effective tail count**

```
n_eff := (1−α) / max_s w_s
```

(with uniform weights, `n_eff = S(1−α)`, the expected number of tail scenarios).
Then the bound reads `CVaR_α − naive ≤ (tail spread) / n_eff`. One quantity controls
the error, and §5 shows it controls the estimator's degeneracy too.

Under a **reduced** ensemble this is not a rounding concern: reduction concentrates
mass, so a single `w_s` can exceed `1−α` outright, at which point the naive estimator
collapses to one scenario's value and the bound is the whole tail spread.

---

## 2. Coherence, and where subadditivity is used implicitly

> **Absence** (#5) — no spec text anywhere states a coherence or subadditivity property.

CVaR is **coherent** in the Artzner–Delbaen–Eber–Heath sense — monotone,
translation-equivariant, positively homogeneous, subadditive — on any probability
space, for `α ∈ (0,1)` and integrable `L` (Pflug 2000; Acerbi–Tasche 2002).

**CONDITIONAL, on the same condition as §1d.** Coherence holds for the
superquantile / RU form. It does **not** hold for the tail conditional expectation
`E[L | L ≥ VaR_α]` on distributions with atoms — the standard counterexample kills
subadditivity there. A reduced weighted ensemble is maximally atomic. So:

> **One implementation choice — the split-atom RU form — buys LP-exactness and
> coherence together. The naive tail mean forfeits both.**

**Where subadditivity is used implicitly.** Nowhere in a way that fails, once the
form is fixed — but it is used, and unstated. The composer sums CVaR-flavoured terms
from `ImbalanceRiskView`, `PeakView` and `DelineationView`. What makes that sum
meaningful at all is §4's inequality, which *is* subadditivity. Without coherence the
sum is an arbitrary aggregation of three numbers.

**Positive homogeneity is used implicitly too, and more often.** Every time the spec
takes a tail in physical units and prices it outside — `L2`:61-66's peak term is the
live case — it is invoking `c · CVaR_α(X) = CVaR_α(c · X)`. That step is legal only for
a deterministic non-negative `c` (§0). It is the axiom that lets `peakPrice` stay a
scalar input outside the risk functional, and the one that fails first if the tariff is
ever made uncertain or non-linear.

Additional property the spec relies on without naming it: **CVaR preserves convexity
in the decision variables.** If `L(·, ω)` is convex for every `ω`, `CVaR_α(L(x))` is
convex in `x`, because `CVaR_α` is convex and non-decreasing. This is what keeps the
MILP class, not LP-representability alone.

---

## 3. Time consistency

> **Absence** (#5) — no spec text on time consistency, nor on nested versus terminal CVaR.

### 3a. The theory, stated exactly

A dynamic risk measure `{ρ_t}` is **time-consistent** if `ρ_{t+1}(X) ≤ ρ_{t+1}(Y)`
a.s. implies `ρ_t(X) ≤ ρ_t(Y)`. Facts:

1. **Static CVaR applied afresh at each re-solve is time-inconsistent.** Not a corner
   case: Kupper–Schachermayer (2009) show the only law-invariant, time-consistent,
   relevant dynamic monetary utility functions are the entropic family; within the
   *coherent* subclass only expectation and essential supremum survive. There is no
   repair inside the coherent law-invariant class. CVaR is not on that list and cannot
   be put on it.
2. **Nested CVaR is time-consistent by construction** (Ruszczyński 2010), via
   `ρ_t(·) = CVaR_α(· + ρ_{t+1}(·))`.
3. **Nested CVaR is not the static CVaR of the terminal loss, and dominates it.**
   Time consistency is equivalent to *rectangularity* of the underlying ambiguity set
   (Epstein–Schneider). Static `CVaR_α` is a sup over a non-rectangular set of
   measures; nesting takes the rectangular hull, which contains it — hence the nested
   measure is uniformly more conservative, and the gap compounds with stage count
   (Shapiro 2009, 2012). Nesting at a naive `α` over many stages drifts toward
   worst-case behaviour.

So the choice is not *consistent vs inconsistent*; it is **which** inconsistency is
paid for.

### 3b. What the failure looks like in *this* engine

Three concrete mechanisms, each derivable from the spec as written.

**(i) Tail re-selection across ticks.** The `α`-tail is re-determined every tick from
a fresh conditional reduced ensemble. A scenario in the tail at tick `k` can leave it
at `k+1` *because uncertainty resolved favourably*, not because anything happened.
The engine then unwinds a hedge it paid to establish. The cost is realised in spread
and cycle cost with no offsetting risk reduction. This is systematic, not noise: the
conditional tail shrinks as the horizon shortens.

**(ii) Risk attitude is discontinuous at the horizon truncation — the sharpest finding.**
The tick objective applies CVaR to in-horizon quantities. The continuation value `V`
is a plain expectation (`L0` §5.4: all three candidate fits are risk-neutral; the
recommended rolling-horizon deterministic equivalent cannot represent a nested measure
at all). So the engine is **risk-averse inside `H_plan` and risk-neutral beyond it**,
with the boundary moving every tick.

`PeakView`'s `prorationFactor` sits exactly on that boundary. Worse, it prorates a
**max**, and a max does not decompose additively:

```
max(M_H, M_rest) ≠ θ·M_H + (1−θ)·M_rest
```

A proration is exact for a time-additive charge and only for that. The correct split
prices the in-horizon **increment** `max(M_H, floor) − floor` and lets `V`, conditioned
on `peakState`, carry the remainder — which `L0` already lists as a state dimension.
As written, the horizon-inside part of the period max is priced under `CVaR_α` and the
horizon-outside part under `E`, and the weight between them slides each tick. This is
the precise mechanism behind `L2`'s own warning that proration is "the most common way
to make the engine pathologically peak-averse". The warning is **confirmed**, and now
has a cause.

**(iii) Path-functional terms plan what the engine will not execute.** `peakState` is a
running max; the delineation accumulators `A_j` are month-to-date sums feeding a
month-end `MAX[AW − MW_month; 0]`. Both are terminal functionals of a path. Under a
per-tick static CVaR the engine can plan at tick `k` to accept a continuation that at
`k+1` lands in the new tail and is refused. The objective value reported at `k` is then
not achievable by the policy the engine implements — and `T5`'s optimality gap is
bounding distance to the optimum of a problem whose solution is never executed. The
gap is measured against the wrong reference.

**Signed conclusion.** Mechanisms (i) and (ii) both point the same way: the engine
**over-hedges early in an accounting period relative to the policy it will actually
follow**, and pays for protection it later abandons.

### 3c. Repairs, ranked

| Repair | What it costs | Verdict |
|---|---|---|
| **(a) One terminal CVaR on one aggregate EUR loss**, taken at the accounting boundary that settles — month for peak and delineation, settlement period for imbalance | Every view's per-scenario EUR contribution must reach the composer as a `[S]` vector; `C2` does not carry that uniformly today (peak is a single epigraph) | **Adopt.** Re-conditioning drift remains, but the object risked is the object settled, and §4's aggregation defect disappears |
| **(b) Nested / recursive CVaR**, with `V` as a risk-averse value-to-go (ρ-DP) | `V`'s fitting method changes: SDDP and backward recursion admit it, the rolling-horizon deterministic equivalent does not. Per-stage `α` must be calibrated against an intended terminal aversion or conservatism compounds | **Target**, once `V`'s state space stops moving. Do not adopt before then |
| **(c) Accept the inconsistency with a stated bound** | A simulation study: roll the policy on held-out paths, evaluate **one** terminal static CVaR of realised EUR, compare to the value the tick objective claimed | **Adopt alongside (a).** This is the empirical claim, with an acceptance band, that #2's protocol wants |

Recommendation: **(a) now, (c) as the measured claim, (b) named as the target.**

---

## 4. CVaR of a sum versus the sum of CVaRs

> **Absence** (#5) — the separately-taken tail functionals are never reconciled into
> one risk measure. **Ticket question** — is summing them a valid risk measure at all?

**Yes, and the spec is safe here — but not for the reason it would give.**

Write `R = Σ_k ρ(L_k)` with `ρ = CVaR_α` over `k ∈ {imbalance+activation, peak,
delineation}` (peak itself splits further, one term per tariff regime).

1. `R` is **not** `CVaR_α` of anything. It is not a function of the aggregate loss
   `L = Σ_k L_k`, and it cannot be — the decomposition is not recoverable from `L`.
2. As a functional of the loss **vector** `(L_1,…,L_K)` it is monotone,
   translation-equivariant, positively homogeneous and subadditive: a coherent risk
   measure on the product space. It is also convex in the decision variables and
   LP-representable term by term. So it is a legitimate decision criterion.
3. By subadditivity, `Σ_k CVaR_α(L_k) ≥ CVaR_α(Σ_k L_k)`, with **equality iff the
   `L_k` are comonotone** (CVaR is comonotone-additive). Summing is therefore a
   **conservative upper bound** on the coherent aggregate risk.

So the answer to the ticket's question is: valid, coherent, and erring safe. The cost
is precise: **the engine cannot see diversification.** The gap
`Σ_k ρ(L_k) − ρ(Σ_k L_k) ≥ 0` is exactly the diversification benefit the objective
throws away, and it is largest when the components are *least* comonotone. ADR-005
exists because these components co-move in the tail, which argues the gap is small —
but that is an empirical claim with a measurable value, not an assertion, and it is
one of the cheapest studies in the programme (no new model: evaluate both functionals
on the same realised ensemble).

**Interaction with the no-netting rule.** `L2`:214-216 forbids the composer from
netting terms. Forming one aggregate per-scenario EUR loss and taking a single CVaR is
**not** netting: no economics is added, scaled or reconciled, and no effect changes
owner. It is a functional of a sum the objective already denominates in EUR. The rule
does not block repair (a); the `C2` payload shape does, and that is the real cost.

**Direction bookkeeping.** The naive tail mean (§1d) understates; summed CVaRs
overstate. The two errors are independent, opposite in sign, and uncontrolled in
magnitude. They do not cancel in any usable sense, and reporting a single "risk term"
without decomposing them makes both invisible.

---

## 5. The peak term: a tail mean of a maximum over a reduced ensemble

> **Claim** (`L2`:55-66) — the peak level is an empirical CVaR tail mean over the
> reduced weighted ensemble of `max_t p_poi[s,t]`.
> **Ticket question** — is that an estimator of anything, and what is its bias as `S` falls?

### 5a. Representability — CONFIRMED

`M(x, ω) = max_t p_poi(x, ω, t)` is a max of affine functions, hence convex in `x` for
each `ω`. `CVaR_α` is convex and non-decreasing, so `CVaR_α(M(x, ·))` is convex in `x`.
Composed with §1's RU block it is **exactly LP-representable, no binaries**. Cost per
tariff regime:

```
S epigraph vars (z_s) + S RU vars (u_s) + 1 (ζ) ;  S·|overSlots| + S rows
```

At `S = 64`, `H = 192` that is ≈ 12k rows per regime. Nameable, affordable, and
**not what `ADR-008`:36-41 or `ADR-010`:65 write down** — those carry a single
unindexed `z_peak`, which is the deterministic epigraph on one trajectory. #5 recorded
that the ADR and `L2` "describe the same term differently". Adjudication: they are not
two descriptions of one term. **They are two different terms, and only `L2`'s is the
risk-averse quantity.** One of the two texts is wrong and must be deleted.

### 5b. What it estimates, and the reduction bound

Target: `CVaR_α(M)` under the true law `P`. Computed: `CVaR_α(M_S)` under `P_w`. Two
gaps — horizon truncation (§3b(ii)) and reduction. For the reduction gap:

- `x ↦ max_t x_t` is 1-Lipschitz in the sup-norm on paths.
- `CVaR_α` is Lipschitz in the Wasserstein-1 distance with constant `(1−α)⁻¹`
  (immediate from the RU form: each `ζ`-section is `(1−α)⁻¹`-Lipschitz, and a
  pointwise min of `L`-Lipschitz functions is `L`-Lipschitz).

Chaining:

```
| CVaR_α(M) − CVaR_α(M_S) |  ≤  W₁(P, P_w) / (1−α)
```

**Reduction error is amplified by `1/(1−α)` — a factor of 20 at `α = 0.95`.** Scenario
reduction (fast-forward / Wasserstein-optimal, ADR-005) minimises `W₁` against *all*
functionals equally; it is not tail-aware. This bound is directly usable as an
acceptance band: choose `S` such that `W₁/(1−α)` sits below a stated kW tolerance.

**Sign of the bias.** Reduction drops the scenarios furthest from the mass — the
extremes — because `W₁` is dominated by mass, not by extremes. Dropping upper-tail
paths **understates** `CVaR_α(M)`. Together with §1d, the peak term carries **two
independent downward biases**, both worsening as `S` falls, both pointing at
under-hedging the one charge the spec is most anxious about.

### 5c. `INV-D-07` does not cover this — REFUTED as sufficient

`INV-D-07` requires the reduced ensemble to preserve **marginal means** within a
tolerance. Neither bias in §5b is a mean effect, and `M` is a functional of a **path**,
not of any marginal — so `INV-D-07` is not merely weak here, it is **vacuous for the
peak term**. A tail-fidelity companion is required (§9).

### 5d. Degeneracy at realistic `S` — the answer to "what is its bias as `S` falls"

At `S = 64`, `α = 0.95`: the tail carries `1−α = 0.05` of mass, ≈ 3.2 scenarios'
worth under uniform weights, and **one or two** under the non-uniform weights a
reduction actually produces. The peak risk level is then a function of essentially the
single worst retained path. It is discontinuous in the weights, has enormous sampling
variability, and is not an estimator of the population CVaR in any useful sense.

The controlling quantity is again `n_eff = (1−α)/max_s w_s` from §1d. Both defects —
the naive estimator's bias and the tail's degeneracy — are bounded by the same number.
`n_eff` should be computed and enforced, not left implicit in the interaction of `S`,
`α` and the weight profile (§9).

---

## 6. Convexity direction

### 6a. Peak, "max-of-a-max, Jensen in the unhelpful direction" — CONFIRMED

`max_t(·)` is convex, so by Jensen `E[max_t p(t)] ≥ max_t E[p(t)]`: the max of the mean
path understates the mean of the max. And `CVaR_α(M) ≥ E[M] ≥ max_t E[p(t)]`, so a
mean-forecast peak understates the risk-averse level **twice over**. The gap
`E[max_t p] − max_t E[p]` grows with slot count and dispersion; at `H = 192` it is not
a rounding term.

**Per-regime and in aggregate.** Each `EpigraphTerm` is a max over its own slot set
(`AnnualLeistungspreis`: all slots; `AtypicalHlzf`: HLZF slots only). A max over a
subset is still convex, so the argument holds **per regime**, which is the
per-accumulator analogue here. Note the aggregate is then `Σ_r peakPrice_r ·
CVaR_α(max over slot-set_r)` — a sum of CVaRs, and therefore §4's bound, not a joint
tail.

### 6b. `MAX[AW − MW_month; 0]` convex, mean projection understates — CONFIRMED, conditionally

`x ↦ max(x, 0)` is convex and non-decreasing; `AW − MW_month` is affine; the composition
is convex. Hence `E[max(AW − MW, 0)] ≥ max(E[AW] − E[MW], 0)`. **Confirmed.**

This is a call payoff, and the gap is its time value: **strictly positive iff
`P(AW > MW_month) ∈ (0,1)`.** If the accumulator is deep in or out of the money for the
whole month, the mean projection is exact and the tail machinery buys nothing on this
term. That condition is checkable per accumulator per month and tells you *when* the
term matters — which is worth more than the inequality.

`ADR-017`:102-106's consequence — `MW_month` crosses C1 as `double[S]`, derived per
scenario on the shared axis, never an independent series — **stands**, and follows from
the convexity plus ADR-005's correlation premise.

### 6c. The transfer from that term to `V_del` — REFUTED

`L2`:142-147 states the convexity of `MAX[AW − MW; 0]` inside a bullet whose subject is
the risk treatment of **`V_del`**. The step does not transfer. `V_del` is built from:

| Construct | Curvature in the uncertainty |
|---|---|
| `MAX[AW − MW_month; 0]` | convex |
| `(1)¼ = MIN[Z1NB; Z2V]`, `(2)¼ = MIN[Z1NE; Z2E]` | **concave** |
| `(25) = (24)·(23)`, `(27) = (24)·(2)`, `(31) = (30)·(28)` | **bilinear — neither** |

`(24)¼` is a parameter of the solve (`L3` §2), so per scenario these products are linear
in the decisions — but *across* scenarios both factors are uncertain, so `V_del` as a
function of `ω` is **in general neither convex nor concave**. Jensen has no sign for
`V_del`, and no statement about the direction of the mean projection's error on
`V_del` follows from the convexity of one of its parts.

**Consequence, and it is a good one for the spec.** The *decision* — "`V_del` is
evaluated per scenario and the tail of `V_del` itself is taken, not a per-accumulator
sign" — is **correct and survives**, precisely *because* a tail functional of the
realised EUR quantity needs no curvature assumption. Only the *justification* is
refuted. And the adjacent sentence — "the direction of pessimism differs per accumulator
and a single `cvarLevel` would push some of them the wrong way" — is a leftover from the
rejected per-accumulator alternative. Under the aggregate-tail decision there is one
loss, one `α`, one orientation, and the concern does not arise. Per `CLAUDE.md` rule 2 it
should be deleted, not qualified.

---

## 7. Verdict table

| # | Claim | Anchor | Verdict |
|---|---|---|---|
| 1 | CVaR is LP-representable (RU), so the risk term does not change the problem class | `L2`:100-107, `ADR-008`:44-46 | **Confirmed** — holds for atomic, non-uniformly weighted `P_w` (RU 2002) |
| 2 | RU holds for the *reduced weighted* empirical ensemble | derived | **Confirmed** — weights are objective coefficients only |
| 3 | Peak level = "weighted mean of the worst `(1−α)` mass" | `L2`:61-66 | **Refuted as written** — the naive tail mean understates by up to `(tail spread)/n_eff`; only the split-atom form is CVaR |
| 4 | CVaR is coherent | absent from spec | **Conditional** — coherent in the RU/superquantile form; TCE on an atomic measure is not subadditive |
| 5 | Summed CVaR terms are a valid risk measure | absent from spec | **Confirmed with restatement** — coherent on the loss vector; a conservative upper bound on `CVaR_α(Σ L_k)`, equality iff comonotone. Not CVaR of anything |
| 6 | Per-tick static CVaR is time-consistent | absent from spec | **Refuted** — no repair inside the law-invariant coherent class (Kupper–Schachermayer) |
| 7 | Nested CVaR would fix it at no cost | absent from spec | **Conditional** — time-consistent (Ruszczyński), but strictly more conservative than static and compounding with stage count (rectangular hull) |
| 8 | `prorationFactor` correctly splits the peak charge | `L2`:67-72 | **Refuted** — prorating a max is exact only for a time-additive charge; the split also straddles a CVaR/expectation boundary |
| 9 | Proration is "the most common way to make the engine pathologically peak-averse" | `L2`:67-72 | **Confirmed**, with the mechanism now derived (§3b(ii)) |
| 10 | Peak epigraph `z ≥ p_poi[t]`, `z ≥ realisedPeak` is the peak term | `ADR-008`:36-41, `ADR-010`:65 | **Refuted** — that is the deterministic term; the risk-averse one needs `z_s` per scenario, ≈12k rows per regime at `S=64, H=192` |
| 11 | The CVaR peak is an estimator of the population peak risk | `L2`:61-66 | **Conditional** — bias `≤ W₁/(1−α)`, a 20× amplification at `α=0.95`; degenerate at `S=64` unless `n_eff` is controlled |
| 12 | `INV-D-07` (marginal means) suffices as reduction fidelity | `INV-D-07` | **Refuted for tail functionals** — vacuous for a functional of a path max |
| 13 | Peak is a max-of-a-max; the mean forecast understates it (Jensen) | `L2`:61-66 | **Confirmed**, per regime and in aggregate; understates twice over vs `CVaR_α` |
| 14 | `MAX[AW − MW_month; 0]` convex ⇒ mean projection understates the premium | `L2`:142-147, `ADR-017`:102-106 | **Confirmed, conditionally** — strict iff `P(AW > MW_month) ∈ (0,1)`; exact otherwise |
| 15 | That convexity justifies the risk treatment of `V_del` | `L2`:142-147 | **Refuted** — `V_del` mixes convex `MAX`, concave `MIN` and bilinear products; neither convex nor concave. The *decision* stands, the *argument* does not |
| 16 | A single `cvarLevel` "would push some accumulators the wrong way" | `L2`:142-147 | **Refuted** — vacuous under the aggregate-tail decision it sits beside; delete |
| 17 | `MW_month` must cross C1 as `double[S]` on the shared axis | `ADR-017`:102-106 | **Confirmed** — follows from 14 plus ADR-005 |
| 18 | `V` is risk-neutral while the tick objective is risk-averse | `L0` §5.4 + `L3`:103-110 | **Confirmed as a defect** — risk attitude is discontinuous at a horizon boundary that moves every tick |
| 19 | The peak term is convex, hence exact with no binaries | `ADR-008`:37, `ADR-011`:41-44 | **Conditional** — holds because `peakPrice` is a **scalar**, so the charge is linear in the level. A rate with decreasing marginal €/kW makes the term concave: relaxation understates the charge (third downward bias) and the risk term loses convexity. RU is unaffected |

---

## 8. The five recorded absences, resolved

| # | Absence (#5) | Resolution |
|---|---|---|
| 1 | No coherence or subadditivity property stated | §2. Coherent **iff** the RU/superquantile form is used. State the form, not just the name |
| 2 | No time-consistency text; nested vs terminal never addressed | §3. Per-tick static CVaR is inconsistent and unrepairable in-class; adopt terminal-aggregate CVaR now, measure the gap, target nested once `V` settles |
| 3 | Tail functionals never reconciled into one risk measure | §4. The sum is coherent on the loss vector and a conservative bound; say so explicitly, or aggregate to one EUR loss and take one tail |
| 4 | No invariant constrains any risk parameter | §9. Four proposed |
| 5 | `α` orientation never defined | §0. All risk functionals on losses in EUR, `L = −(objective contribution)`, `α` = confidence, tail = worst `(1−α)`. `DelineationView` must negate |

---

## 9. Consequences for the spec

Advisory. ADRs are Hugo's act.

**Delete** — per `CLAUDE.md` rule 2, replaced not annotated:

- `L2`:61-66 "weighted mean of the worst `(1−α)` mass" → the split-atom RU definition.
- `L2`:142-147 "a single `cvarLevel` would push some of them the wrong way" → delete
  outright; and the convexity sentence keeps its own subject (`MAX[AW − MW; 0]`) rather
  than standing as an argument about `V_del`.
- One of `ADR-008`:36-41 / `L2`:55-66 — the deterministic and the scenario-indexed peak
  epigraph are different terms and cannot both be the spec.

**Add:**

- The loss/orientation convention of §0, in `00-overview/02-conventions.md` — it is
  vocabulary, and it binds every document.
- The RU block as the normative definition, with the `z_s` per-scenario epigraph and its
  stated row and variable cost.
- A named decision on aggregation: one terminal CVaR on aggregate EUR loss (recommended),
  or summed CVaRs *declared as a conservative upper bound* with the diversification gap
  registered as an empirical claim with a band.
- Risk attitude of `V` stated explicitly, whatever it is. Silence is what produced §3b(ii).

**Proposed invariants** — filling absence 4:

| Proposed | Statement | Response |
|---|---|---|
| `INV-V-a` | `cvarLevel ∈ (0,1)`, `cvarWeight ≥ 0`, `chanceLevel ∈ (0,1)` | `HALT` — a negative `cvarWeight` is silent risk-seeking |
| `INV-V-b` | `n_eff = (1−α)/max_s w_s ≥ 10` for every CVaR term | warn + `DEGRADED`; controls §1d bias and §5d degeneracy together |
| `INV-V-c` | Every risk functional is evaluated on a loss under the §0 convention; a view taking a tail of a benefit negates it | `HALT` — sign defect, not tuning |
| `INV-D-h` | Reduction preserves `CVaR_α` of each risk-carrying marginal within a stated band — companion to `INV-D-07`, which is vacuous for path functionals | warn + `DEGRADED` |

**Registrable claims for #5** — new, from this note, all `analytic` unless marked:

```
naive tail mean understates CVaR by ≤ (tail spread)/n_eff        analytic
Σ_k CVaR_α(L_k) ≥ CVaR_α(Σ_k L_k), equality iff comonotone       analytic
diversification gap Σ_k ρ(L_k) − ρ(Σ_k L_k) on realised data     empirical, band TBD
peak reduction error ≤ W₁(P,P_w)/(1−α)                           analytic
rolling-policy vs claimed-objective CVaR gap                     empirical, band TBD  (§3c(c))
P(AW > MW_month) ∈ (0,1) per accumulator per month               empirical, band TBD
peakPrice is a deterministic scalar, set pre-backtest             assumed-declared
peakPrice doubles as the peak term's risk weight (L3:109)         analytic
```

`peakPrice` is registered **`assumed-declared`**, not `derived`: it is an educated guess
fixed before the backtest, and it is the input the positive-homogeneity pass-through
depends on. Per the protocol's fifth mechanism the consequence is pre-committed — if the
backtest's peak-charge attribution lands outside its band, the guess is what changed, and
the analytic claims above are untouched by that.

---

## 10. Sources

Primary only; each supports a specific line above.

| Result used | Source |
|---|---|
| RU minimisation formula, continuous case | Rockafellar & Uryasev (1999), *Optimization of Conditional Value-at-Risk*, J. Risk 2(3) |
| Extension to general/atomic distributions; joint `(x,ζ)` minimisation | Rockafellar & Uryasev (2002), *Conditional value-at-risk for general loss distributions*, J. Banking & Finance 26(7) |
| Coherence axioms | Artzner, Delbaen, Eber & Heath (1999), *Coherent Measures of Risk*, Math. Finance 9(3) |
| CVaR coherent; TCE not subadditive under atoms | Acerbi & Tasche (2002), *On the coherence of expected shortfall*, J. Banking & Finance 26(7); Pflug (2000) |
| No law-invariant coherent time-consistent measure but `E` and ess-sup | Kupper & Schachermayer (2009), *Representation results for law invariant time consistent functions*, Math. Finan. Econ. 2(3) |
| Nested risk measures, ρ-dynamic programming | Ruszczyński (2010), *Risk-averse dynamic programming for Markov decision processes*, Math. Prog. 125(2) |
| Time consistency in multistage stochastic programming; nested conservatism | Shapiro (2009, 2012) |
| Time consistency ⇔ rectangularity | Epstein & Schneider (2003), *Recursive multiple-priors*, J. Econ. Theory 113(1) |
| Scenario reduction, `W₁` optimality | Dupačová, Gröwe-Kuska & Römisch (2003), *Scenario reduction in stochastic programming*, Math. Prog. 95(3) |
