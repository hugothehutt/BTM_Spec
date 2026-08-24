# 04 — Lagrangian decomposition on the coupling block

Scope: duality-gap conditions for a MILP, validity of the dual bound (incl. under early termination),
bundle vs subgradient, primal recovery, and the soundness of "certified gap per solve".

Notation used throughout. Primal is a **maximisation**:

```
(P)   z* = max { f'x : Ax <= b  (coupling),  x in X }          X = {Cx <= d, x integer-mixed}
(LRu) L(u) = max { f'x + u'(b - Ax) : x in X },   u >= 0
(LD)  z_LD = min_{u>=0} L(u)
```

`u` = `(mu_up[b], mu_dn[b], ...)` in the spec. Coupling block relaxed:

| # | constraint (<=, so multiplier >= 0) | economic reading of `u` |
|---|---|---|
| 1 | `p_d[t] + rUp[b(t)] <= pMaxDischarge[t]` | price of discharge-power headroom |
| 2 | `p_c[t] + rDn[b(t)] <= pMaxCharge[t]`   | price of charge-power headroom |
| 3 | `socMin - soc[t] + rUp[b(t)]*D/eta_d <= 0` | price of energy headroom down |
| 4 | `soc[t] + rDn[b(t)]*D*eta_c <= socMax` | price of energy headroom up |

All four are `<=` after sign-normalisation, hence `u >= 0` is the admissible set. Relaxing them makes
`L(u)` separable: `L(u) = L_energy(u) + L_reserve(u) + const(u)` — an **energy/spot** subproblem in
`(p_c, p_d, soc)` and a **reserve** subproblem in `(rUp, rDn)`.

---

## Provable under stated assumptions

### A. Weak duality and the shape of the dual function

| # | Claim | Exact assumptions | Source |
|---|---|---|---|
| A1 | `L(u) >= z*` for **every** `u >= 0`. For a max primal the Lagrangian is an **upper** bound and `(LD)` is a **minimisation**. | `X != {}`; `u >= 0` componentwise for `<=` coupling constraints. For **equality** coupling constraints `u` is free (sign-unrestricted) and A1 still holds. | Guignard 2003, §3–4 (min convention, mirrored); Geoffrion 1974 |
| A2 | `L(u)` is the pointwise **max** of finitely many affine functions of `u` (one per vertex of `conv(X)`), hence **piecewise-linear, convex, non-smooth**, and finite iff `X` bounded. | `X` a bounded MILP set with rational data ⇒ `conv(X)` a polytope with finitely many vertices. | Guignard 2003, §8 (`z(lambda)` convex, "differentiable a.e.", subdifferential nonempty/compact — Thm 8.1) |
| A3 | `L` is differentiable at `u` iff `(LRu)` has a unique optimum; otherwise `s = (b - Ax(u))` is a subgradient. Non-differentiability at `u*` is the generic case, and is **caused by multiple subproblem optima**. | as A2 | Guignard 2003, §8, Defs 8.1–8.2 + Thm 8.1 |
| A4 | Sandwich at any `u >= 0`: if `x(u)` solves `(LRu)` and is **feasible for (P)**, then `f'x(u) <= z* <= L(u)`. If in addition complementary slackness `u'(b - Ax(u)) = 0` holds, `x(u)` is **optimal** for (P) and `z* = f'x(u)`. CS is sufficient, **not necessary**. | `x(u)` an exact optimum of `(LRu)` | Guignard 2003, **Thm 4.1** (parts 1–3) + Remark 4.1 |
| A5 | If the dualized constraints are **equalities** and `x(u)` is feasible, CS holds automatically ⇒ `x(u)` optimal. | equality coupling only | Guignard 2003, §4, §7 |

> **Sign trap for the spec.** The four coupling constraints above are inequalities, so A5 does **not**
> apply. Landing a feasible Lagrangian solution does **not** certify optimality; you must still
> check `u'(b - Ax(u)) = 0`.

### B. Geoffrion's theorem, the integrality property, and when the gap is zero

| # | Claim | Assumptions | Source |
|---|---|---|---|
| B1 | **Primal-relaxation characterisation.** `z_LD = v(PR)` where `(PR) = max { f'x : Ax <= b, x in conv(X) }`. The dual bound is exactly the optimum over the **convexified subproblem set intersected with the coupling constraints**. | rational data (so `conv(X)` = conv hull of its extreme points); `X` bounded | **Geoffrion 1974, Thm 1**; restated as Guignard 2003 **Thm 5.1** |
| B2 | `z_LP >= z_LD >= z*` (max convention). The Lagrangian bound is **never worse** than the LP bound. | B1 | Guignard 2003, Cor 5.1/5.2 |
| B3 | **Integrality Property (IP).** `(LR)` has the IP iff `conv{x in X : Cx <= d} = {x : Cx <= d}` — i.e. the subproblem polyhedron is already integral. | Def 5.1 | Guignard 2003, **Def 5.1** |
| B4 | **If IP holds ⇒ `z_LD = z_LP`**: the whole Lagrangian machinery buys **nothing** over solving one LP relaxation. If IP fails ⇒ `z_LD` **may be** strictly tighter than `z_LP` (not guaranteed to be). | B1, B3 | Guignard 2003, **Cor 5.1, Cor 5.2** |
| B5 | **Zero duality gap for a MILP iff** the optimum of `(PR)` is attained at a point of `X` — equivalently `max{f'x : Ax<=b, x in conv(X)} = max{f'x : Ax<=b, x in X}`. Sufficient conditions: (i) some `u >= 0` admits an `(LRu)`-optimal `x(u)` that is (P)-feasible **and** satisfies CS (A4); (ii) `X` is already a polyhedron (no integrality) ⇒ LP strong duality; (iii) `conv(X) ∩ {Ax<=b}` has an integral optimal vertex. **No condition based on solver behaviour, iteration count, or dual convergence implies a zero gap.** | B1, A4 | Geoffrion 1974 Thm 1; Guignard 2003 Thm 4.1, Cor 5.1–5.2 |
| B6 | **The gap does not close by solving `(LD)` better.** `z_LD - z*` is a property of the *formulation* (the difference between `conv(X) ∩ {Ax<=b}` and `conv(X ∩ {Ax<=b})`), independent of the dual algorithm. | B1 | Geoffrion 1974 |

### C. Lagrangian RELAXATION vs Lagrangian DECOMPOSITION — they are different theorems

| # | Claim | Source |
|---|---|---|
| C1 | **LD = variable splitting.** `(P): max{f'x : Ax<=b, Cx<=d, x in X}` is rewritten `max{f'x : Ax<=b, x in X, Cy<=d, y in X, x=y}`; dualize **only `x = y`** (multipliers free in sign). `L_LD(l) = max{(f-l)'x : Ax<=b, x in X} + max{l'y : Cy<=d, y in X}`. | Guignard & Kim 1987; Guignard 2003 §7 |
| C2 | **Guignard–Kim theorem.** `z_LD_split = max { f'x : x in conv(X ∩ {Ax<=b}) ∩ conv(X ∩ {Cx<=d}) }`. | **Guignard 2003, Thm 7.1** (attributing Guignard & Kim 1987) |
| C3 | **Dominance corollary.** (a) If *one* of the two subproblems has the IP, `z_LD_split` equals the **better** of the two ordinary LR bounds. (b) If *both* have the IP, `z_LD_split = z_LP`. Hence LD is `>=` as strong as either LR, and can be **strictly** stronger only when **neither** subproblem is integral. | **Guignard 2003, Cor 7.1** |
| C4 | Because the copy constraint `x = y` is an **equality**, a Lagrangian solution with `x(l) = y(l)` is automatically **optimal for (P)** (A5). This is the one structural feature that gives LD a primal-recovery hook that plain LR lacks. | Guignard 2003 §7 |

> **Naming defect in the spec.** What the spec relaxes is a set of **pre-existing coupling
> constraints**, not a manufactured copy constraint. That is **Lagrangian relaxation**, not
> Lagrangian decomposition in the Guignard–Kim sense. Therefore **Thm 7.1 / Cor 7.1 do not apply**:
> the spec inherits no dominance theorem beyond `z_LD >= z*` and `z_LP >= z_LD` (B2), and it does
> **not** inherit the equality-constraint optimality certificate C4.

### D. Validity of the dual bound at early termination — the load-bearing result

| # | Claim | Assumptions | Source |
|---|---|---|---|
| D1 | `L(u_k)` is a valid upper bound on `z*` at **any** iteration `k`, with **no** requirement that `u_k` be near-optimal. Early termination of the *dual iteration* costs bound quality, never validity. | `u_k >= 0`; `(LRu_k)` solved **to optimality** | A1 |
| D2 | **Inexact subproblem, wrong direction.** For a max inner problem, any `xbar in X` gives `l_k = f'xbar + u_k'(b - A xbar) <= L(u_k)` — a **lower** estimate of the dual function. `l_k` is **NOT** an upper bound on `z*` and **must never be reported as the dual bound**. | `xbar in X` feasible for the subproblem | Frangioni 2020, §5.1 (inexact oracle yields `l_i <= f(x_i)`, `z_i` an eps-subgradient with `eps_i = f(x_i) - l_i`) |
| D3 | **The exact condition the spec needs.** Let `Ubar_k >= L(u_k)` be *any* valid upper bound on the subproblem optimum — e.g. the MIP solver's own **dual / best-bound** value at its termination (its LP/B&B bound), **not** its incumbent. Then `Ubar_k >= L(u_k) >= z*`, so **`Ubar_k` is a valid certificate**. With separable subproblems the **sum of the per-subproblem solver dual bounds** is valid: `Ubar_energy + Ubar_reserve >= L_energy + L_reserve = L(u_k) >= z*`. | each subproblem's reported bound is a genuine bound (solver in valid-bound mode; no unsafe presolve, no objective cutoff below `L`) | Direct from A1 + monotonicity; the eps-subgradient bookkeeping is Frangioni 2020 §5.1 |
| D4 | A bundle method **converges under inexact oracles** with errors `eps_i <= epsbar < inf` (`epsbar` need not be known), returning an `epsbar`-optimal dual point; Noise Reduction / proximal-parameter attenuation steps are required when linearisation errors go negative. | proximal or level bundle with NR steps | Frangioni 2020, §5.1 (citing Kiwiel 2006; de Oliveira–Sagastizábal–Lemaréchal 2014) |

> **Two different numbers, and the spec must keep both.** The bundle *model* consumes the
> **lower** estimates `l_k` (to stay a valid lower model of the convex dual function). The
> **certificate** must be built from the **upper** estimates `Ubar_k`. A MIP solver returns both
> (incumbent + best bound) from one solve; using the wrong one silently invalidates every gap the
> system reports.

### E. Bundle vs subgradient

| # | Claim | Assumptions | Source |
|---|---|---|---|
| E1 | **Subgradient, divergent series.** `u_{k+1} = [u_k - t_k s_k]_+` with `t_k > 0`, `sum t_k = inf`, `t_k -> 0` (equivalently `sum t_k = inf`, `sum t_k^2 < inf`) ⇒ `L(u_k) -> z_LD`. | `L` convex, subgradients bounded on the iterate set (`X` bounded), `z_LD` finite | Polyak 1967; Held–Wolfe–Crowder 1974 |
| E2 | **Polyak step** `t_k = eps_k (L(u_k) - z_LD)/||s_k||^2`, `eps_k in (0,2)` gives `||u_k - u*||` **monotonically non-increasing** — but requires the **unknown** `z_LD`. With an estimate, over/undershoot is unavoidable and monotonicity is lost. | exact `z_LD` | Guignard 2003, §9.1 + Remark 9.1 |
| E3 | **No monotone bound improvement, no finite termination, no usable stopping test** for plain subgradient. The dual value sequence is sawtoothed; the only implementable stop is an iteration/time cap. | — | Guignard 2003, §9.1 ("practical convergence is unpredictable", sawtooth pattern that may "keep deteriorating"); Frangioni 2020 §4.4 ("convergence of subgradient methods is so slow that the only feasible stopping criterion is a limit on the number of iterations") |
| E4 | **Proximal bundle** maintains a stability center `ubar_i`, a cutting-plane lower model, and serious/null steps; it produces an **aggregate subgradient** `(zbar_i, alphabar_i)` with `zbar_i in d_{alphabar_i} L(ubar_i)`, and `(||zbar_i||, alphabar_i) -> (0,0)` **is the computable stopping criterion**: `Delta_i <= eps` certifies `ubar_i` is `eps`-optimal for `(LD)`; `zbar_i = 0` and `alphabar_i = 0` ⇒ exactly optimal. | convex `L`, standard PBM parameter control | Frangioni 2020, §3.1 (`Delta_i <= eps` "a convenient approximate stopping condition"), §3.3 (aggregate pair) |
| E5 | **The aggregate is a primal point.** In the Lagrangian case the bundle master's dual multipliers `theta_i` reconstruct `ubar_i = sum_b u^b theta_b in conv(U)`, and PBM convergence **is** the statement `{ubar_i} -> u*` optimal for the **convexified** problem `max{f'x : Ax = b, x in conv(X)}`. | `X` finite / bounded | Frangioni 2020, §5 eqs (42)–(44) |
| E6 | **Worst-case complexity does NOT favour bundle.** PBM on `M`-Lipschitz convex `L`: `O(eps^-3)` for arbitrary fixed prox parameter (Kiwiel 2000), improving to the **optimal nonsmooth rate** `O(M^2 ||u_0-u*||^2 / eps^2)` only with an optimised/nonconstant stepsize — the *same* order as the plain subgradient method. | Lipschitz convex, no growth condition | Díaz & Grimmer 2023, Table 1 and surrounding text |
| E7 | Unstabilised cutting-plane (Kelley) is convergent but "usually slow and unstable"; stabilisation is what fixes it, and the proximity parameter is "the one parameter that may be delicate to adjust". | — | Guignard 2003, §9.3, §9.5 |

> **Verdict on the spec's "bundle over subgradient on convergence-stability grounds":**
> **CONFIRMED, but for the right reason and not the stated one.** The provable advantage is
> (i) a **computable eps-optimality stopping criterion** (E4) that plain subgradient simply lacks
> (E3), (ii) a monotone stability center so the *reported* dual bound never degrades, and
> (iii) native tolerance of **inexact subproblem oracles** (D4) — which is exactly what a
> time-capped MILP subproblem is. It is **NOT** an asymptotic-rate advantage (E6). Additional
> system-specific reason: the reserve bid grid (`minBidMw`/`bidStepMw`) creates **symmetric optima**
> in the reserve subproblem ⇒ `L` non-differentiable at `u*` with a fat subdifferential (A3) ⇒ the
> classic subgradient zig-zag regime; bundle aggregation is the standard remedy.

### F. Primal recovery — what is and is not provable

| # | Claim | Source |
|---|---|---|
| F1 | **Why Lagrangian solutions are infeasible.** `x(u)` optimises over `X` only; the coupling constraints were *deleted*. For `u` below the true shadow price the two subproblems each claim the same headroom (the reserve block sells `rUp` while the energy block discharges to `pMaxDischarge`), violating row 1. Feasibility of `x(u)` occurs only by accident, and even then optimality needs CS (A4). | Guignard 2003, §4, §10 ("Lagrangean relaxation generates infeasible integer solutions") |
| F2 | **Ergodic / convex-combination recovery converges to the CONVEXIFIED optimum.** A subgradient scheme on `(LD)` with appropriately chosen convexity weights produces an ergodic sequence `xhat_k = sum_i w_i^k x(u_i)` converging to an optimal solution of the **convex** primal — i.e. of `(PR)` (B1), **not** of `(P)`. | Larsson, Patriksson & Strömberg 1999; Gustavsson, Patriksson & Strömberg 2015; identical statement for bundle aggregates in Frangioni 2020 §5 (E5) |
| F3 | **Explicit rate + explicit infeasibility, and the convexity hypothesis.** With running averages `xhat_k` and constant stepsize `alpha`: `||g(xhat_k)_+|| <= ||mu_k||/(k alpha)`; under Slater `||g(xhat_k)_+|| <= B*/(k alpha)` and `f(xhat_k) <= f* + ||mu_0||^2/(2k alpha) + alpha L^2/2`. **The proof requires `X` convex** so that `xhat_k in X`; the paper states plainly that the averages "need not satisfy the primal inequality constraints ... they can be primal infeasible". | Nedić & Ozdaglar 2009, Prop. 1(a)–(c), Prop. 2(a)–(c) |
| F4 | **Therefore, for a MILP with `X` non-convex, `xhat_k` is generally FRACTIONAL** (it lies in `conv(X)`, not `X`) and **simultaneously** still violates the coupling constraints by `O(1/k)`. Both defects are present at once. Recovery yields a *bound-certifying* primal point for `(PR)`, never a MILP-feasible dispatch. | F2 + F3 + B1 |
| F5 | **Volume algorithm.** Barahona & Anbil's VA extends deflected subgradient to produce approximate primal vectors from estimated face volumes; it targets **LP** primal solutions of the convexified problem, and the original paper carries **no convergence proof** — a convergence-bearing variant (green/yellow/red steps, "RVA") is due to Bahiense, Maculan & Sagastizábal 2002. VA therefore inherits F4 exactly. | Barahona & Anbil 2000; Bahiense et al. 2002; Guignard 2003 §9.6 |
| F6 | **The unavoidable conclusion.** For a MILP, **no dual method guarantees a feasible integer primal.** Integer feasibility must come from a **Lagrangian heuristic** (repair / rounding / variable-fixing on `x(u)`) or an independent fallback, and Lagrangian heuristics "are essentially problem dependent" with **no approximation guarantee in general**. | Guignard 2003, §10 (heuristic taxonomy: violation repair; the "lazy" heuristic = fix the confident variables, solve the remainder optimally) |
| F7 | **Guarantee under special structure — it exists, and its hypotheses are precise.** For `min sum_i c_i'x_i` s.t. `sum_i H_i x_i <= b`, `x_i in X_i`, `i in I`, with `m` coupling constraints: (a) **duality gap bound** `J_P* - J_D* <= m * max_i gamma_i`, `gamma_i = max_{X_i} c_i'x_i - min_{X_i} c_i'x_i`; (b) **tighten** the RHS to `bbar = b - rho`, `rho_k = m * max_i (max_{X_i} H_ik x_i - min_{X_i} H_ik x_i)`; then **every inner solution `x(lambda*)` of the tightened dual is FEASIBLE for the original problem** (Thm 3.1), with suboptimality `<= (m + ||rho||_inf/zeta) * max_i gamma_i` (Thm 3.3). Relative suboptimality `-> 0` at a `1/|I|` rate as `|I| -> inf`. | Vujanic, Mohajerin Esfahani, Goulart, Mariéthoz & Morari 2016 (Automatica), Thm 2.3, Assump. 2.4, Thm 3.1, Assump. 3.2, Thm 3.3; sharper adaptive-tightening variants in Falsone, Margellos & Prandini 2019 |
| F8 | Hypotheses of F7: `X_i` compact mixed-integer sets; **uniqueness** of the optima of the convexified LP and of the dual (Assump. 2.4); a **Slater point with slack growing in `|I|`** (Assump. 3.2); and the tightened problem must remain feasible ("resources sufficiently abundant"). The bound rests on the **Shapley–Folkman–Starr** argument (Aubin & Ekeland 1976; Bertsekas et al. 1983). Computing `rho` itself needs `max_{X_i} H_ik x_i` — a MIP *maximisation* per row, "not necessarily as easy as minimisations over the same feasible set". | Vujanic et al. 2016, §2.1, §3 + footnote 1 |

### G. The certified gap

| # | Claim | Source |
|---|---|---|
| G1 | If `z_p` is the objective of a **verified-feasible** point of `(P)` and `Ubar` is a **valid upper bound** on `z*` (per D3), then `z_p <= z* <= Ubar` and `Ubar - z_p` is a **rigorous** bound on the optimality loss of `z_p`. This is exactly the two-sided bracket Lagrangian relaxation is prized for. | A1 + A4; Guignard 2003 §10 / Conclusion ("brackets around the optimal integer value") |
| G2 | The relative form `(Ubar - z_p)/|Ubar|` is sound **only** if `Ubar` is bounded away from 0. `z*` here is a trading profit that can be near zero or negative, so `|Ubar|` in the denominator is unsafe. Standard solver convention is `(bound - incumbent)/(|incumbent| + tol)`, or an absolute gap in currency. | standard MIP gap definition; no theorem needed |

---

## Empirical claim requiring our own verification

Every row is a **prediction from a paper on other instances**, to be measured on our own 192-slot /
64-scenario BTM model before any of it is relied on.

| # | Prediction | Origin | How we falsify it |
|---|---|---|---|
| P1 | `z_LD` is strictly tighter than `z_LP` for this model. | B4 only says it *may* be, contingent on IP failing. | Solve `(P)`'s LP relaxation and the Lagrangian dual to (near-)optimality on the same instance; compare. If `z_LP - z_LD ~ 0`, Tier 2 buys nothing over one LP and must be dropped or re-cut. |
| P2 | The energy subproblem lacks the integrality property (binaries: import/export complementarity, MIN linearisation). | Inference from B3; **not** established. If price signs make simultaneous charge/discharge unprofitable, those binaries are LP-tight and IP may effectively hold. | Solve the energy subproblem's LP relaxation at several `u`; check integrality of the LP optimum. |
| P3 | The reserve subproblem (bid grid `minBidMw` / `bidStepMw`) lacks IP. | Inference from B3. Discrete step grids are generally non-integral polyhedra. | Same test on the reserve block. |
| P4 | Bundle needs materially fewer subproblem solves than subgradient to reach a given `Ubar`. | Guignard 2003 §9.1/§9.5 practice reports; **not** implied by E6. | Head-to-head on a fixed subproblem-solve budget; record the `Ubar` trajectory. |
| P5 | PBM iterations to `eps`: `O(eps^-3)` with a fixed prox parameter, `O(eps^-2)` with a tuned/nonconstant one. | Díaz & Grimmer 2023, Table 1 (worst case, Lipschitz convex). | Fit the observed `Ubar_k - z_LD` decay; treat as an upper envelope, not a forecast. |
| P6 | Vujanic-style tightening would yield a feasibility guarantee here. | Vujanic et al. 2016, Thms 3.1/3.3. | **Expected to FAIL** — see Applicability §2. Test by counting `|I|` (independent subsystems) against `m` (coupling constraints) in our split. |
| P7 | Lagrangian bound + Lagrangian heuristic gives a tighter bracket than LP bound + LP-based heuristic. | Guignard 2003, Conclusion. | Measure `Ubar - z_p` (Tier 2) against `z_LP - z_p_LPheuristic`. |
| P8 | The ergodic / bundle-aggregate primal `xhat_k` violates the coupling block by `O(1/k)`. | Nedić & Ozdaglar 2009, Prop. 2(a) — **assumes `X` convex**, which ours is not. | Log `||(A xhat_k - b)_+||_inf` per iteration; expect a plateau, not decay to 0, because of F4. |

---

## Applicability to the target system

### 1. Is "certified gap per solve" sound?

**Yes — conditionally, and the conditions are not free.** Three things must all hold, and only the
first is currently implied by the spec.

| Requirement | Status | Consequence if violated |
|---|---|---|
| **R1.** `u_k >= 0` on all four multiplier families, all iterations (projection after every step). | Must be asserted. All four coupling rows are `<=` after normalisation. | A negative multiplier makes `L(u_k)` **not** a relaxation. Bound silently invalid. |
| **R2.** The reported dual value is `Ubar_k =` sum of the **solver best-bound** values of the energy and reserve subproblems — **never** the sum of incumbents. | **The single most likely defect in an implementation.** Under a per-solve time cap the subproblems *will* terminate at nonzero MIP gap (D2/D3). | Reporting incumbents yields `l_k <= L(u_k)`, which can fall **below** `z*`. Every downstream "certified gap" becomes a lie, and the Tier-1-dominance test then fails for the wrong reason. |
| **R3.** `z_p` is a point re-verified against the **full, unrelaxed** model (all four coupling rows plus everything Tier 2 never relaxed), not merely a repaired `x(u_k)`. | Must be an explicit feasibility re-check, not an inherited claim. | `z_p > z*` ⇒ negative reported gap ⇒ certificate inverted. |

Given R1–R3, `z_p <= z* <= Ubar_k` at **every** iteration `k`, including iteration 1 and including the
cap-exhausted exit. The certificate is **iteration-anytime**: this is the genuinely strong property of
Tier 2 and is worth building around. Add two guards:

- **Denominator (G2).** Profit `z*` can be ~0 or negative. Report the **absolute** gap in currency as
  primary; use a relative gap only with a `max(|Ubar|, |z_p|, eps)` denominator.
- **Invariant.** `Ubar_k >= z_p - tol` must be checked every iteration. A violation means R1, R2, or
  R3 is broken — beyond `tol` it is never "just numerics".

Also record: `Ubar_k` certifies against `z*` of the **MILP as formulated**, not against the true
economics. `z_LD - z*` (B6) is a formulation constant; **no amount of dual iteration reduces it**, so a
persistent floor in the reported gap is expected and is **not** an algorithm defect. The spec should
state a target *gap floor* per instance class rather than a target gap of 0.

### 2. Is "feasible primal on every exit path" achievable, and at what cost?

**Achievable, but only by paying for it outside the dual method. The spec's own DEFENSIVE baseline is
the payment, and it should be named as such in the contract.**

Blunt chain:

1. `x(u_k)` is generically **coupling-infeasible** (F1) — and infeasible in the most physically obvious
   way: reserve block and energy block both spend the same MW of headroom.
2. Ergodic averaging, the volume algorithm, and bundle aggregation all converge to the optimum of the
   **convexified** problem `max{f'x : Ax<=b, x in conv(X)}` (F2, E5, F5). For a MILP that point is
   **fractional** — a bid strictly between grid steps, a complementarity binary at 0.4 — and
   **additionally** still violates the coupling block by a residual that only vanishes under a convexity
   assumption we do not satisfy (F3/F4). It is useless as a dispatch.
3. **No dual method delivers an integer-feasible primal for a MILP (F6).** Not a tuning problem.
4. The one family of results that *does* guarantee feasibility (Vujanic et al., F7) **does not apply
   here**, and the reason is structural:
   - It needs **many independent subsystems** (`|I| -> inf`) and **few coupling constraints** (`m`
     small). Its bounds scale as `m * max_i gamma_i` with relative error `~1/|I|`.
   - Our split has **`|I| = 2`** (energy, reserve) and **`m ~ 4 x 192 = 768`** coupling rows per
     scenario. The ratio is inverted by roughly three orders of magnitude: `m * max_i gamma_i` exceeds
     the objective itself, and the tightening `rho` would drive `pMaxDischarge` negative, rendering the
     tightened problem infeasible and violating the paper's own precondition (F8).
   - A *scenario-wise* split (`|I| ~ 64`) does not rescue it: the coupling block is **intra**-scenario,
     so scenarios are not the subsystems being coupled by these rows.
   - Computing `rho` requires **maximising** `H_ik x_i` over each mixed-integer `X_i` (F8) — a second
     family of MIPs, per row.

   **Do not attempt constraint tightening in Tier 2.** Record F7 as ruled out, with this reason.
5. Feasibility must therefore come from (a) a **Lagrangian heuristic** — clip `rUp/rDn` down to the
   largest grid step that restores all four rows, then re-solve the energy block with reserves fixed
   (exactly Guignard's "lazy" heuristic, F6); and (b) an **unconditional fallback**.

**The cost, stated plainly:**

| Cost | Detail |
|---|---|
| The fallback must be **dual-independent**. | It cannot be derived from `x(u_k)`, or it inherits `x(u_k)`'s infeasibility. The **DEFENSIVE baseline** ("honour commitments, protect peak, no new exposure") is the right object *precisely because* it is constructed without reference to `u`. It must be feasible **by construction**, and that is a standing proof obligation, not a test. |
| **No approximation guarantee** on the fallback (F6). | On the cap-exhausted path the reported gap `Ubar - z_defensive` can be arbitrarily large. That is **honest**, not a bug — but the spec must not promise a *bounded* gap on every exit path, only a *valid* one. |
| Extra MILP solves. | The repair heuristic re-solves the energy block with reserves fixed. Budget it **inside** the solve budget, not on top of it, or the cap is not a cap. |
| The **feasibility-restoration ladder** becomes load-bearing. | It is the mechanism that converts "repair attempt failed" into "fall through to DEFENSIVE" without an exception escaping the tier interface. |
| Two objects tracked separately at all times. | `z_p` = best **verified-feasible** objective so far (heuristic result, else defensive baseline); `Ubar` = best **valid upper bound** so far (`min_k Ubar_k`, monotone by construction). Every exit path returns the pair. |

**Wording fix for the spec:** replace "every exit path returns a feasible primal" with
"every exit path returns a **verified-feasible** primal **and** a **valid** upper bound; only the
non-cap-exhausted paths are expected to return a **small** gap."

### 3. Is the "Tier 2 dual bound dominates Tier 1 objective" test correctly stated?

**The direction is correct for a maximisation problem. The test is under-specified in four ways, and
one of them is the failure it will actually see.**

| Aspect | Verdict |
|---|---|
| **Direction** | **Correct.** `L_2(u) >= z* >= z_Tier1_incumbent` (A1). For a maximisation primal the Lagrangian bound is an **upper** bound, so `dual_bound_T2 >= objective_T1` is the right inequality. If the model is ever flipped to minimisation this test must flip with it — pin the objective sense explicitly in the test. |
| **Defect 1 — which Tier 1 number** | Valid **only** against Tier 1's **incumbent objective value** (the value of the feasible schedule it returns). **Invalid** against Tier 1's *solver dual bound*: both `L_2(u)` and Tier 1's B&B bound are valid upper bounds on `z*` and are **mutually incomparable** — a tight B&B bound legitimately sitting either side of `L_2(u)` proves nothing. Assert on the incumbent. |
| **Defect 2 — Tier 1 at nonzero MIP gap** | **Does NOT cause spurious failure — it makes the test weaker, not wrong.** An early-terminated Tier 1 returns an incumbent `<= z*`, so `L_2(u) >= z* >= incumbent` holds a fortiori. The real risk is the opposite: with a loose Tier 1 the test becomes nearly vacuous and stops detecting a broken Tier 2. **Mitigation:** run the assertion only on instances where Tier 1's gap is tight and recorded, and additionally assert the *reverse* bracket `L_2(u) <= z_Tier1_bound + tol` when Tier 1 **is** solved to proven optimality — that catches an *over-tight* (i.e. invalid) Tier 2 bound, which the one-sided test cannot. |
| **Defect 3 — tolerance and equality** | If the duality gap is zero and both tiers solve exactly, `L_2 = z_Tier1`. A strict `>` fails. Use `L_2 >= z_T1 - tol`, with `tol` scaled to objective magnitude. |
| **Defect 4 — the real spurious-failure mode** | `L_2(u)` computed from subproblem **incumbents** instead of subproblem **best bounds** (R2 / D2). That makes `L_2` an *under*-estimate of `L(u)` which can drop below `z*`, so the test fails while nothing about the decomposition is wrong. **This is the failure that will actually occur, and its cause is a one-line bug in which solver attribute is read.** Guard it by asserting `bound >= incumbent` per subproblem before summing. |
| **Precondition** | Both tiers must be evaluated on **identical** model data — same scenario set, same 192 slots, same bid grid, same big-M values. A tightened big-M in one tier alone breaks the comparison with no bug in either. |

### 4. Other structural notes on Tier 2

| Note | Detail |
|---|---|
| It is **relaxation**, not **decomposition** (C-block). | Rename, or actually implement variable splitting. If the strictly stronger Guignard–Kim bound (C2/C3) is wanted, split `soc`/power into an energy copy and a reserve copy and dualize `x = y`. That also buys the **equality**-constraint optimality certificate (C4/A5): when the copies agree, the solution is provably optimal for `(P)` — something the current inequality-relaxation scheme can never give (A4/A5). Cost: many more multipliers, and it strictly helps only if **neither** block has the IP (C3). |
| Multiplier dimension. | ~`4 x 192 = 768` multipliers per scenario. Plain subgradient degrades badly at this dimension against a non-smooth optimum; a bundle master QP at ~768 variables with a bounded bundle is cheap. Reinforces E4. |
| `mu_up[b]`, `mu_dn[b]` per 4-hour **block** vs coupling rows per 15-min **slot**. | The relaxed rows are slot-indexed, so there is one multiplier **per row** — slot-indexed — even though `rUp` is block-indexed; the reserve subproblem then sees `sum_{t in b} mu_up[t]` as the aggregated block price. Forcing multipliers to be block-constant is a *restriction of the dual feasible set*, which **weakens the bound** (though it stays valid, A1). Decide this deliberately; do not let it happen by indexing accident. |
| Symmetry in the bid grid. | Multiple reserve-subproblem optima ⇒ `L` non-differentiable at `u*` (A3) ⇒ subgradient zig-zag. Break symmetry inside the subproblem (lexicographic tie-break on bid selection) **and** use bundle aggregation. |
| Tier 3 (`mu_hat` learned surface). | Tier 3 is Tier 2 stopped at `k = 1` with a warm-started `u`. **It inherits the same certificate** (D1): evaluating `L(mu_hat)` with valid subproblem bounds yields a valid `Ubar` for one extra subproblem solve. Tier 3 can therefore report a certified gap too, and should — otherwise Tier 3 is the only tier with no error control at all. |

---

## Sources

Primary, in order of load-bearing weight.

1. **Geoffrion, A. M. (1974).** "Lagrangean relaxation for integer programming." *Mathematical Programming Study* **2**, 82–114. DOI [10.1007/BFb0120690](https://doi.org/10.1007/BFb0120690) — https://link.springer.com/chapter/10.1007/bfb0120690 — Thm 1 (primal-relaxation characterisation `v(LD) = v(PR)`), Integrality Property.
2. **Guignard, M. (2003).** "Lagrangean relaxation." *TOP* **11**(2), 151–228. DOI [10.1007/BF02579036](https://doi.org/10.1007/BF02579036) — full text used: https://cepac.cheme.cmu.edu/pasi2008/slides/grossmann/library/reading/Top_Lagragean_Relaxation.pdf — Thm 4.1 (sandwich + CS optimality), Thm 5.1 (Geoffrion), Def 5.1 + Cor 5.1/5.2 (IP), Thm 7.1 + Cor 7.1 (Lagrangean decomposition), Thm 8.1 + §8 (subdifferential), §9.1–9.7 (subgradient / dual ascent / cutting plane / column generation / bundle / volume), §10 (Lagrangean heuristics).
3. **Guignard, M. & Kim, S. (1987).** "Lagrangean decomposition: a model yielding stronger Lagrangean bounds." *Mathematical Programming* **39**(2), 215–228. DOI [10.1007/BF02592954](https://doi.org/10.1007/BF02592954) — https://link.springer.com/article/10.1007/BF02592954 — companion: *RAIRO Rech. Opér.* **21**(4), 307–323, https://www.numdam.org/item/RO_1987__21_4_307_0.pdf
4. **Frangioni, A. (2020).** "Standard Bundle Methods: Untrusted Models and Duality." In *Numerical Nonsmooth Optimization — State of the Art Algorithms*, Springer, 61–116. DOI [10.1007/978-3-030-34910-3_3](https://doi.org/10.1007/978-3-030-34910-3_3) — full text used: https://www.plan4res.eu/wp-content/uploads/2018/07/StandardBundle.pdf — §3.1 (`Delta_i <= eps` stopping condition), §3.3 (aggregate subgradient), §4.4 (subgradient methods have no usable stopping criterion), §5 eqs (42)–(44) (bundle aggregate → convexified primal optimum), **§5.1 (inexact oracles: `l_i <= f(x_i)`, eps-subgradients, valid lower model, Noise Reduction, convergence under `eps_i <= epsbar`)**.
5. **Frangioni, A. (2005).** "About Lagrangian methods in integer optimization." *Annals of Operations Research* **139**, 163–193. DOI [10.1007/s10479-005-3447-9](https://doi.org/10.1007/s10479-005-3447-9) — https://link.springer.com/article/10.1007/s10479-005-3447-9
6. **Nedić, A. & Ozdaglar, A. (2009).** "Approximate primal solutions and rate analysis for dual subgradient methods." *SIAM Journal on Optimization* **19**(4), 1757–1780. DOI [10.1137/070708111](https://doi.org/10.1137/070708111) — preprint (LIDS Report 2753) used: https://optimization-online.org/wp-content/uploads/2007/03/1627.pdf — Prop. 1(a)–(c), Prop. 2(a)–(c); explicit `||g(xhat_k)_+|| <= B*/(k alpha)`; **convexity of `X` is a stated hypothesis**.
7. **Larsson, T., Patriksson, M. & Strömberg, A.-B. (1999).** "Ergodic, primal convergence in dual subgradient schemes for convex programming." *Mathematical Programming* **86**(2), 283–312. DOI [10.1007/s101070050090](https://doi.org/10.1007/s101070050090) — https://link.springer.com/article/10.1007/s101070050090
8. **Gustavsson, E., Patriksson, M. & Strömberg, A.-B. (2015).** "Primal convergence from dual subgradient methods for convex optimization." *Mathematical Programming* **150**(2), 365–390. DOI [10.1007/s10107-014-0772-2](https://doi.org/10.1007/s10107-014-0772-2)
9. **Önnheim, M., Gustavsson, E., Strömberg, A.-B., Patriksson, M. & Larsson, T. (2017).** "Ergodic, primal convergence in dual subgradient schemes for convex programming, II: the case of inconsistent primal problems." *Mathematical Programming* **163**(1–2). DOI [10.1007/s10107-016-1055-x](https://doi.org/10.1007/s10107-016-1055-x)
10. **Barahona, F. & Anbil, R. (2000).** "The volume algorithm: producing primal solutions with a subgradient method." *Mathematical Programming* **87**(3), 385–399. DOI [10.1007/s101070050002](https://doi.org/10.1007/s101070050002) — https://link.springer.com/content/pdf/10.1007/s101070050002.pdf
11. **Bahiense, L., Maculan, N. & Sagastizábal, C. (2002).** "The volume algorithm revisited: relation with bundle methods." *Mathematical Programming* **94**(1), 41–70.
12. **Held, M., Wolfe, P. & Crowder, H. P. (1974).** "Validation of subgradient optimization." *Mathematical Programming* **6**(1), 62–88. DOI [10.1007/BF01580223](https://doi.org/10.1007/BF01580223) — https://link.springer.com/article/10.1007/BF01580223
13. **Polyak, B. T. (1967).** "A general method of solving extremum problems." *Soviet Mathematics Doklady* **8**, 593–597. (Divergent-series and Polyak-step convergence; restated in source 2, §9.1.)
14. **Kiwiel, K. C. (1990).** "Proximity control in bundle methods for convex nondifferentiable minimization." *Mathematical Programming* **46**(1–3), 105–122. DOI [10.1007/BF01585731](https://doi.org/10.1007/BF01585731)
15. **Díaz, M. & Grimmer, B. (2023).** "Optimal convergence rates for the proximal bundle method." *SIAM Journal on Optimization* **33**(2). DOI [10.1137/21M1428601](https://doi.org/10.1137/21M1428601) — preprint https://arxiv.org/abs/2105.07874 (Table 1: `O(eps^-3)` at fixed prox parameter; `O(M^2||u_0-u*||^2/eps^2)` optimised).
16. **de Oliveira, W., Sagastizábal, C. & Lemaréchal, C. (2014).** "Convex proximal bundle methods in depth: a unified analysis for inexact oracles." *Mathematical Programming* **148**, 241–277. DOI [10.1007/s10107-014-0809-6](https://doi.org/10.1007/s10107-014-0809-6)
17. **Vujanic, R., Mohajerin Esfahani, P., Goulart, P. J., Mariéthoz, S. & Morari, M. (2016).** "A decomposition method for large scale MILPs, with performance guarantees and a power system application." *Automatica* **67**, 144–156. DOI [10.1016/j.automatica.2016.01.006](https://doi.org/10.1016/j.automatica.2016.01.006) — preprint https://arxiv.org/abs/1411.1973 — Thm 2.3 (gap `<= m max_i gamma_i`), Assump. 2.4, Thm 3.1 (feasibility under tightening), Assump. 3.2 (Slater with increasing slack), Thm 3.3 (performance), Thm 3.4 (block refinement), Thm 3.8 (without Slater), footnote 1 (cost of computing `rho`).
18. **Falsone, A., Margellos, K. & Prandini, M. (2019).** "A decentralized approach to multi-agent MILPs: finite-time feasibility and performance guarantees." *Automatica* **103**, 141–150. DOI [10.1016/j.automatica.2019.01.009](https://doi.org/10.1016/j.automatica.2019.01.009) — preprint https://arxiv.org/abs/1706.08788
19. **Aubin, J.-P. & Ekeland, I. (1976).** "Estimates of the duality gap in nonconvex optimization." *Mathematics of Operations Research* **1**(3), 225–245. (Shapley–Folkman duality-gap bound; cited as [AE76, p.233] in source 17.)
20. **Bertsekas, D. P., Lauer, G. S., Sandell, N. R. & Posbergh, T. A. (1983).** "Optimal short-term scheduling of large-scale power systems." *IEEE Transactions on Automatic Control* **28**(1), 1–11. (Sharper Shapley–Folkman gap estimates specialised to integer programs.)
21. **Sherali, H. D. & Choi, G. (1996).** "Recovery of primal solutions when using subgradient optimization methods to solve Lagrangian duals of linear programs." *Operations Research Letters* **19**(3), 105–113. DOI [10.1016/0167-6377(96)00019-3](https://doi.org/10.1016/0167-6377(96)00019-3)
22. **Nemhauser, G. L. & Wolsey, L. A. (1988).** *Integer and Combinatorial Optimization*, Wiley, Ch. II.3 (Lagrangian duality; `z_LD = max{cx : Ax<=b, x in conv(X)}`). DOI [10.1002/9781118627372](https://doi.org/10.1002/9781118627372)
23. **Fisher, M. L. (1981/2004).** "The Lagrangian relaxation method for solving integer programming problems." *Management Science* **27**(1), 1–18; reprinted **50**(12 Supp.), 1861–1871. DOI [10.1287/mnsc.1040.0263](https://doi.org/10.1287/mnsc.1040.0263)
