# Multi-stage stochastic methods — findings

Resolves [Multi-stage stochastic methods](https://github.com/hugothehutt/BTM_Spec/issues/13),
a `wayfinder:research` ticket of
[Map: earning confidence in the BTM theoretical model](https://github.com/hugothehutt/BTM_Spec/issues/2).

**Mandate.** Methods and proofs, not numbers. Every claim below is either *provable
under stated assumptions* or a *prediction requiring our own verification*. No
reported empirical result from any paper is treated as ground truth. Per the map's
verification protocol §1 (analytic gate), the sign and the branch condition come
before any measurement.

**Status.** Research complete, nothing adopted. Nothing here has been written into
the spec. Each item below is a candidate for a TN or a spec correction and needs
Hugo's decision first.

---

## 1. The five sub-answers

| File | Sub-question | One-line verdict |
|---|---|---|
| [`01-methods.md`](01-methods.md) | MSP vs SDDP vs ADP | Only the deterministic-equivalent MSP is untouched by stagewise dependence. Plain SDDP is **invalid** here, not merely loose |
| [`02-nonanticipativity-twostage.md`](02-nonanticipativity-twostage.md) | Non-anticipativity; two-stage as an approximation | Index-omission is *exactly* equivalent to explicit NACs, no assumptions. The per-gate two-stage model sits inside the VMS bound; the executed rolling policy does not |
| [`03-terminal-value.md`](03-terminal-value.md) | Terminal value functions | Concavity holds on weaker conditions than `ADR-007` assumes. The peak coordinate never needed discretising; the qualification cliff is the unbounded one |
| [`04-lagrangian.md`](04-lagrangian.md) | Lagrangian decomposition on the coupling block | "Certified gap per solve" is sound. "Feasible primal on every exit path" cannot come from the dual, and Tier 2 is *relaxation*, not *decomposition* |
| [`05-scenario-reduction.md`](05-scenario-reduction.md) | Scenario reduction | `INV-D-07`'s tolerance is **derivable** as `λ_k·σ_k·D_J`. The chance constraint does **not** survive reduction under any Wasserstein budget |

---

## 2. Provable, and it contradicts the spec

The items that would change text. Ordered by blast radius.

| # | Finding | Spec location | Source |
|---|---|---|---|
| C1 | The `[S × H]` shared-axis ensemble is a **fan, not a recombining tree**, so `F_t = F_1` for all `t`. The target is a sequence of two-stage SAA problems glued by `V`, *not* a multistage program. Any multistage-optimality claim would be false | `ADR-005`, `L3-planner.md` §1 | Heitsch–Römisch–Strugarek 2006 |
| C2 | "Two-stage lookahead + exact terminal `V` = optimal" is **false** for S1→S4. An exact `V` removes horizon-truncation error only; stage-aggregation error survives it. The window has four decision epochs, not one | `L3-planner.md` §2 "Two-stage structure" | Powell 2019 |
| C3 | A Wasserstein/Fortet–Mourier budget bounds **nothing** for a chance constraint — one-line proof: `δ₀` vs `δ_η` has `ζ→0` while `α_B = 1`. Chance-constrained stability needs the polyhedral **discrepancy** | `L3-planner.md` §2 coupling block, `ADR-005` | Rachev–Römisch 2002 Prop. 3.9 |
| C4 | Tier 2 relaxes pre-existing coupling rows. That is Lagrangian **relaxation**; Guignard–Kim **decomposition** requires variable splitting plus `x = y`. The dominance theorem the name invokes does not apply | `L3-planner.md` §3, `ADR-010` | Guignard–Kim 1987 |
| C5 | No dual method guarantees an integer-feasible primal for a MILP; ergodic/volume/bundle recovery converges to the **convexified** optimum, which is fractional. So the spec must promise a *valid* gap on every exit path, never a *small* one | `L3-planner.md` §3 "Tier 2 numerics" | Larsson–Patriksson–Strömberg; Nedić–Ozdaglar |
| C6 | `V` is **jointly concave in (SOC, P̄)** — both enter only as LP right-hand sides. Discretising `peakState` was never required by concavity, and it costs `peakPrice·proration·binwidth` | `ADR-007` | Al Taha–Bitar 2024, Thms 1–3 |
| C7 | Relative gap `(L − z_p)/|L|` is unsafe near zero or negative profit. Report absolute currency; relative only over `max(|L|,|z_p|,ε)` | `L3-planner.md` §3, `T2` tier agreement | — |
| C8 | The `T2` assertion "Tier 2's dual bound must dominate Tier 1's objective" is valid only against Tier 1's **incumbent**. A nonzero Tier 1 MIP gap makes the test **vacuous**, which is worse than failing | `T2` §"Tier agreement" | — |
| C9 | Multipliers are one per relaxed row, hence **slot**-indexed. `μ_up[b]` forces block-constant multipliers, which restricts the dual feasible set and *weakens* the bound — valid, but it must be a deliberate choice | `L3-planner.md` §3 | — |

## 3. Provable, and it vindicates the spec

| # | Finding | Spec location |
|---|---|---|
| V1 | Structural non-anticipativity (omitting the `s` index) is *exactly* equivalent to explicit NACs — plain variable elimination on `x¹=…=x^K`. No convexity, continuity or integrality hypothesis needed | `L3-planner.md` §2 |
| V2 | The no-binaries concave-PWL claim is correct, and concave-maximisation is the easy direction. The λ-formulation's LP relaxation is exact when slopes are strictly decreasing | `ADR-007` |
| V3 | Per gate, the two-stage model is a **restriction** of the multistage one, so `VMS ≥ 0`: conservative, never anticipative | `L3-planner.md` §2 |
| V4 | Concave-hull projection *is* the LP relaxation of the standard PWL formulations, so hull-fitting makes an otherwise silent projection visible rather than adding an error | `ADR-007` |
| V5 | "Certified gap per solve" is sound in principle: any admissible `μ` yields a valid bound at any iteration | `L3-planner.md` §3 |
| V6 | Bundle over plain subgradient is the right call — but for the computable ε-optimality stop and inexact-oracle tolerance, **not** for a convergence rate (bundle matches subgradient's `O(ε⁻²)`) | `L3-planner.md` §3 |
| V7 | Concavity of `V(SOC)` needs only `p_import ≥ 0`, weaker than `ADR-007` assumes. The feasible SOC-profile set is always a convex polytope, efficiency losses included | `ADR-007` |

## 4. Suspected defects to check before anything else

Cheap to check, and each invalidates something load-bearing.

| # | Check | If it fails |
|---|---|---|
| D1 | Is the CVaR `η` (Rockafellar–Uryasev) scenario-indexed anywhere? | A scenario-indexed `η` computes a strict **under-estimate** of CVaR. Silent |
| D2 | Does the `Pending` fill belief depend on the DA position or reserve decisions? | The filtration becomes decision-dependent and structural non-anticipativity fails outright |
| D3 | Does Tier 2 sum solver **best-bound** or solver **incumbent** across subproblems? | Summing incumbents gives `l_k ≤ L(μ_k)`, which can fall below `z*` and invalidates every reported gap |
| D4 | Do the complementarity binaries actually bind at the optimum? | If they do not, the LP relaxation is exact, `V`'s concavity is recovered, and the SDDP/ADP toolbox becomes legitimate |
| D5 | Does either Lagrangian subproblem have the integrality property? | If so `z_LD = z_LP` and Tier 2 buys nothing over one LP relaxation |

## 5. The unbounded one

The **qualification cliff** is the only error in this survey with no finite bound.
`V*` *jumps* by `Δfee` at the threshold, so no Lipschitz modulus exists and no
error bound covers the `qualState` discretisation. The discrete label is not a
sufficient statistic even when the boundary sits exactly on the threshold:
headroom-to-threshold is lost, worth up to `Δfee`.

Compounding it: the concave hull's chord over-values SOC below a non-concavity and
under-values above, so the bias is a pull **toward** the cliff — directional, not
the uniform over-storage one might assume. With SDDP cuts optimistic by
construction in a maximisation, `V̂` is optimistic twice over, and `INV-V-11`'s
slope check catches neither. The missing contract check is per-cell calibration of
realised value against `V̂`.

## 6. Derived, not guessed: the `INV-D-07` tolerance

The one place where this survey replaces a guessed number with a derivation.

With `σ`-normalised coordinates and ground metric `d_1 = (1/H) Σ_{t,k} |ξ−ξ'|/σ_k`,
Kantorovich–Rubinstein duality gives

```
Σ_k |ΔM_k| / σ_k  ≤  W_1(P,Q)  ≤  D_J
```

so the per-series tolerance is `λ_k · σ_k · D_J` — **computed by the reducer
itself**, from the reduction cost it already reports. The bound is tight (`P = ½δ₀ +
½δ₁`, `S=1` attains it), so no smaller universal constant exists and the tolerance
*must* be `D_J`-proportional.

Two consequences the ADR would need:

- Norm choice moves the constant by a factor `H = 192`. State `INV-D-07` against
  **time-averaged** means, not per-slot means.
- Better still, **eliminate** the tolerance: the redistribution inner problem is an
  LP, so adding `K` moment equalities drives `INV-D-07` to exactly zero, feasible
  iff `E_P[ξ] ∈ conv{retained}`, at price `D_J^constr ≥ D_J`. Recommended.

The reduction artefact must materialise five fields for any of this to be
reproducible: `σ_k`, `w_t`, the norm, the order `r`, and `D_J`. For `r ≥ 2` the
metric is not scale-invariant, so silently re-estimating `σ_k` invalidates
cross-version `D_J` comparison.

## 7. Predictions requiring our own verification

Every number any source reported. None is evidence; each is a claim we would have
to earn under the map's protocol. The per-file sections carry the full list.

| Prediction | Source | Where |
|---|---|---|
| 14.2% / 18.3% terminal-valuation uplift | literature | `02` |
| "two-stage + `V` beat all multistage trees" | literature | `02` |
| Strengthened Benders wins in practice despite not being tight | Zou–Ahmed–Sun | `01` |
| FFS deletion biases CVaR downward (mechanism argued, not proved) | derived | `05` |
| CVaR amplification `1/(1−α)` — 20× at α=0.95, 100× at α=0.99 | Ernst–Pichler–Sprungk | `05` |

## 8. What this survey did not settle

- Whether the fan-vs-tree reframing (C1) should change `ADR-005`'s text or only its
  claims. That is a decision, not a finding.
- The magnitude of the stage-aggregation error in C2. Bounded in form, unquantified
  here; needs the offline node-indexed LP relaxation as an upper bound.
- Whether to adopt Wasserstein DRO with radius `ε := D_J`, which converts the
  reduction error into a *provable upper bound* on the true value — the same
  number, but a guarantee instead of a hope.
