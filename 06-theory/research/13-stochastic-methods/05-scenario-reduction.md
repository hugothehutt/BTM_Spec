# 05 — Scenario reduction: what Wasserstein / fast-forward reduction provably preserves

Scope: `Ensemble : [S scenarios] x [H slots] x [K series]`, weights `w >= 0`, `sum w = 1`.
`S_gen -> S ~ 64`, `H ~ 192`, `K ~ 8`. Target of the derivation: `INV-D-07`.

Notation used throughout: `xi in Xi subset R^{H x K}` a whole joint path (one atom = one
coherent multi-series trajectory). `P` = generating ensemble with atoms `xi^i`, weights
`p_i`, `i = 1..N` (`N = S_gen`). `J` = deleted index set, `|J| = N - S`.
`Q = sum_{j not in J} q_j delta_{xi^j}`.

---

## Provable under stated assumptions

### A. The metric the theory actually uses is Fortet-Mourier `zeta_r`, not plain `W_1`

| Object | Exact definition | Source |
|---|---|---|
| Fortet–Mourier metric of order `r >= 1` | `zeta_r(P,Q) := sup { \|int_Xi f d(P-Q)\| : f in F_r(Xi) }` | Rachev–Römisch 2002 eq. (4); Römisch SAGA09 slides p.8 |
| Its function class | `F_r(Xi) := { f : Xi -> R : \|f(xi) - f(xi~)\| <= c_r(xi,xi~) for all xi,xi~ in Xi }` | idem |
| Its cost | `c_r(xi,xi~) := max{ 1, \|\|xi\|\|^{r-1}, \|\|xi~\|\|^{r-1} } * \|\|xi - xi~\|\|` | idem |
| Transportation form (`Xi` bounded) | `zeta_r(P,Q) = inf { int c^_r dEta : pi_1 Eta = P, pi_2 Eta = Q }` with **reduced cost** `c^_r <= c_r`, `c^_r(xi,xi~) = inf { sum_{l=1}^{n-1} c_r(xi_l, xi_{l+1}) : n in N, xi_1 = xi, xi_n = xi~ }` | Rachev–Rüschendorf 1998, quoted in Römisch SAGA09 p.8 |

Facts that pin down the `W_1` relationship — both are elementary and load-bearing:

| Claim | Proof | Consequence |
|---|---|---|
| `zeta_1 = W_1` exactly | `c_1(xi,xi~) = \|\|xi - xi~\|\|`, so `F_1` = the 1-Lipschitz functions; that is verbatim the Kantorovich–Rubinstein dual of `W_1`. Also `c_1` already satisfies the triangle inequality so `c^_1 = c_1`. | For `r = 1` "Fortet–Mourier reduction" *is* Wasserstein-1 reduction. |
| `W_1 = zeta_1 <= zeta_r` for every `r >= 1` | `max{1, \|\|xi\|\|^{r-1}, \|\|xi~\|\|^{r-1}} >= 1` implies `c_r >= c_1`, hence `F_1 subset F_r`, hence the sup over `F_r` dominates. | **Any** `zeta_r` budget the reducer reports is also a valid `W_1` budget. The mean-tolerance derivation below is therefore valid whichever order `r` the implementation uses. |
| `zeta_2 <= C(P,Q) * W_2` with `C(P,Q) = (max{1, 2 int \|\|xi\|\|^2 d(P+Q)})^{1/2}` (Schwarz) | Rachev–Römisch 2002, §3.1 | `zeta_2` is *strictly* finer than `W_2`; Example 3.4 there gives sequences where `W_2 -> 0` but `zeta_2` does not. |

**Do not conflate the three.** `W_1` is the weakest, `zeta_2` the canonical one for the
target model class, and `W_2 / zeta_2` are not interchangeable.

### B. The two-stage stability theorem (the whole justification for the method)

Model (Rachev–Römisch 2002 eq. (12); Römisch SAGA09 p.6):
`min { <c,x> + int Phi(q(xi), h(xi) - T(xi) x) P(dxi) : x in X }`, `W` a **fixed** recourse
matrix, `q(.)`, `h(.)`, `T(.)` affine in `xi`, `X` polyhedral, `Xi` polyhedral.

| Assumption | Statement |
|---|---|
| (A1) relatively complete recourse | `h(xi) - T(xi) x in W(Y)` for **all** `(xi,x) in Xi x X`. Satisfied automatically under complete recourse `W(Y) = R^s`; otherwise it is a **restriction on the support of `P`**. |
| (A2) dual feasibility | `q(xi) in D` for all `xi in Xi`, `D = { u : {z : W^T z - u in Y*} != empty }`. |
| (A3) moments | `int_Xi \|\|xi\|\|^2 P(dxi) < +infty` (order `r` in general: `P in P_r(Xi)`). |
| (A4) geometry | `S(P) != empty`; `U` an open **bounded** neighbourhood of `S(P)`; `X` bounded/compact. |

**Theorem (Rachev–Römisch 2002, Thm 3.3).** Under (A1)–(A4) there exist `L > 0`,
`eps > 0` such that for all `Q in P_2(Xi)` with `zeta_2(P,Q) < eps`:

```
|v(P) - v(Q)|  <=  L * zeta_2(P,Q)
empty != S(Q)  subset  S(P) + Psi_P( L * zeta_2(P,Q) )
```

where `Psi_P` is the inverse of the growth function of the objective near `S(P)`.
Verbatim conclusion of that section: "*`zeta_2` is the canonical metric for two-stage
models with fixed recourse.*"

**Order reduction to `r = 1`.** Same paper, end of §3.1: if `q(.)` is *non-stochastic*
**or** `(T(.), h(.))` are *non-stochastic*, (A3) weakens to `P in P_1(Xi)` and the theorem
holds with `zeta_1 = W_1`. **In the BTM model both are stochastic** (prices enter `q`,
load/PV enter `h`), so the canonical order is `r = 2`, not `r = 1`. A reducer configured
with `r = 1` is optimising a metric that is *weaker than the one the stability theorem
needs*. It still yields a valid mean bound (row 2 of the table in §A) but its
optimal-value guarantee is not the theorem's.

### C. What the reduction algorithm exactly optimises — and what is heuristic

The optimal-reduction problem decomposes (Römisch SAGA09 pp. 9–10, 20–21):

```
min_J  min_{q in S_n}  d( P, sum_{j not in J} q_j delta_{xi^j} )
       ^^^^^^^^^^^^^^  inner: optimal redistribution — EXACT
^^^^^  outer: support selection — NP-HARD
```

| Part | Status | Exact result |
|---|---|---|
| **Inner (redistribution), fixed `J`** | **Provably optimal, closed form** | `D_J := zeta_r(P,Q*) = min_Q zeta_r(P,Q) = sum_{i in J} p_i * min_{j not in J} c^_r(xi^i, xi^j)` with `q*_j = p_j + sum_{i in J_j} p_i`, `J_j = { i in J : j = j(i) }`, `j(i) in argmin_{j not in J} c^_r(xi^i, xi^j)`. I.e. **each deleted atom's mass goes entirely to its nearest retained atom in the (reduced-cost) metric.** (Dupačová–Gröwe-Kuska–Römisch 2003.) |
| **Outer (which `S` atoms to keep)** | **NP-hard** | `min { D_J : J subset {1..N}, \|J\| = N - S }`. Römisch SAGA09 p.10, verbatim: "*the problem of finding the optimal set `J` for deleting scenarios is NP-hard and polynomial time algorithms are not available*". Equivalent MILP form (Arpón et al. eq. 2.16): transportation LP + binary selection `r_j in {0,1}`, `sum_j r_j = M`. |
| **Fast forward selection / backward reduction** | **Greedy heuristics. NO optimality guarantee, no approximation ratio.** | Backward reduction: `J^[0] = empty`; `J^[i] = J^[i-1] + argmin_l sum_{k in J^[i-1] u {l}} p_k min_{j not in ...} c^_r(xi^k, xi^j)`. Forward selection: `J^[0] = {1..N}`; delete-from-`J` greedy. Both terminate with the exact redistribution step. |

**Therefore:** the redistribution step is a theorem; the selection step is a heuristic.
`D_J` returned by the heuristic is an *upper* bound on the achievable optimum and a
*correct, computed* value of `zeta_r(P,Q)` for the `Q` actually produced. That last point
is what makes the tolerance below derivable rather than guessed.

### D. Kantorovich–Rubinstein duality — the only tool needed for moment bounds

For a metric `d` on `Xi` (any norm; l.s.c. suffices) and `P,Q` with finite support:

```
W_1^d(P,Q) = inf_{pi in Pi(P,Q)} int d dpi = sup { int f d(P-Q) : Lip_d(f) <= 1 }
```

Immediate corollary, used repeatedly below:

```
for any f with Lip_d(f) = L_f < infty :   | E_P[f] - E_Q[f] |  <=  L_f * W_1^d(P,Q)
```

(Villani, *Optimal Transport: Old and New*, Thm 5.10; Rachev 1991 Ch. 5–6.)

### E. Multi-stage: the two-term bound

**Theorem 1, Heitsch & Römisch, *Comput. Manag. Sci.* 2009** (first part is essentially
Heitsch–Römisch–Strugarek, *SIAM J. Optim.* 17:511–525, 2006, Thm 2.1). Assumptions:
(A1) `xi in L_r(Omega,F,P;R^s)`; (A2) relatively complete recourse *locally around* `xi`;
(A3) finite optimal values in a neighbourhood **and** locally uniform level-boundedness of
the objective; **`X_1` bounded**. Then there exist `L, delta > 0` with

```
| v(xi) - v(xi~) |  <=  L * ( ||xi - xi~||_r  +  D_{f,inf}(xi, xi~) )        (6)

D_{f,inf}(xi,xi~)   := sup_{||x||_inf <= 1}  sum_{t=2}^{T-1} || E[x_t|F_t(xi)] - E[x_t|F_t(xi~)] ||_{r'}
D*_{f,inf}(xi,xi~)  := sup_{||x||_inf <= 1}  sum_{t=2}^{T}   || E[x_t|F_t(xi)] - E[x_t|F_t(xi~)] ||_{r'}
```

and for solution sets, `d_{l_inf}( S_eps(xi), S_eps(xi~) ) <= (L_bar/eps) * ( ||xi - xi~||_r + D*_{f,inf}(xi,xi~) )`
for `eps in (0, eps_bar)`. Remains valid with `E` replaced by a multi-period **polyhedral
risk functional** (which includes multi-period CVaR) satisfying uniform level-boundedness
(Eichhorn & Römisch 2008).

**Verbatim conclusion of that paper** (§6): "*the incorporation of the filtration distance
into the reduction of scenario trees is indispensable. This implies, in particular, that
deleting scenarios in input trees for multi-stage models according to the methodology
presented in Dupačová et al. (2003) and Heitsch and Römisch (2003, 2007) is not
appropriate as the information (filtration) structure is not taken into account.*"

Their reduction criterion is consequently the **weighted sum**
`w_1 ||xi - xi^red||_r + w_2 D*_{f,inf}(xi, xi^red) <= eps_appr`; setting `w_2 = 0`
recovers pure distributional reduction and *loses the second term entirely*.

### F. Chance constraints: the canonical metric is a DISCREPANCY, not Wasserstein

`B`-discrepancy: `alpha_B(P,Q) := sup_{B in B} |P(B) - Q(B)|`.

**Proposition 3.9, Rachev & Römisch 2002 (§3.3, linear chance-constrained model
`min { <c,x> : x in X, P({xi : T(xi)x >= h(xi)}) >= p })`.** Assume (i) `S(P) != empty`,
`U` an open bounded neighbourhood of `S(P)`; (ii) the map
`x |-> { y : P({xi : T(xi)x >= h(xi)}) >= p - y }` is **metrically regular** at each
`(x_bar, 0)`, `x_bar in S(P)`. Then there exist `L > 0`, `eps > 0`, `k in N` with

```
| v(P) - v(Q) |  <=  L * alpha_{B_ph^k}(P,Q)
S(Q) subset S(P) + Psi( L * alpha_{B_ph^k}(P,Q) )
```

whenever `alpha_{B_ph^k}(P,Q) < eps`, where `B_ph^k` is the **polyhedral discrepancy**
(polyhedra with at most `k` faces). Verbatim: "*the polyhedral discrepancy `alpha_{B_ph^k}`
on `Xi` ... is a natural candidate for a canonical metric of linear chance-constrained
stochastic programs*"; and (§ intro) "*for two-stage models containing integer variables
and for chance constrained models, the relevant integrands are discontinuous and their
canonical classes contain products of (locally) Lipschitzian functions and of
characteristic functions of sets describing regions of continuity.*"

**No Wasserstein bound on a discrepancy can exist.** Proof (one line, ours): take
`P = delta_0`, `Q = delta_eta` on `R`, `B = (-inf, 0]`. Then `W_1(P,Q) = eta -> 0` while
`alpha_B(P,Q) = 1` for every `eta > 0`. Hence there is **no** function `phi` with
`phi(0+) = 0` satisfying `alpha_B <= phi(W_1)`. A `W_1` (or `zeta_r`) tolerance therefore
conveys **zero** information about chance-constraint satisfaction.

The reverse direction *does* hold in a Hölder sense for the mixed-integer class: with
`F = F_{r,B_rect}(Xi)` and `Xi` bounded (Römisch SAGA09 p.19)
`alpha_{B_rect}(P,Q) <= d_F(P,Q) <= C * alpha_{B_rect}(P,Q)^{1/(s+1)}` and
`d_F(P,Q) <= C ( zeta_r(P,Q) + alpha_{B_rect}(P,Q)^{1/(s+1)} )`. The exponent `1/(s+1)`
with `s = dim(xi)` makes this practically vacuous at `s = H*K`. The recommended
reduction criterion in that literature is
`d_lambda(P,Q) = lambda * alpha_{B_rect}(P,Q) + (1-lambda) * zeta_r(P,Q)`, `lambda` close
to 1 (Henrion–Küchler–Römisch 2009), for which **no closed-form `D_J` exists** — "An
explicit formula for `D_J` is no longer available!" (SAGA09 p.21); the inner problem is
still an LP, the outer still NP-hard.

### G. CVaR: Lipschitz w.r.t. `W_1`, but with a `1/(1-alpha)` amplification

**Claim.** For a loss `G(x, .) : Xi -> R` with `Lip_d(G(x,.)) = L_G`, and
`CVaR_alpha` in the Rockafellar–Uryasev sense:

```
| CVaR_alpha^P[G(x,.)] - CVaR_alpha^Q[G(x,.)] |  <=  ( L_G / (1 - alpha) ) * W_1^d(P,Q)
```

**Derivation (complete).** `CVaR_alpha[Z] = min_eta { eta + E[(Z - eta)_+] / (1-alpha) }`
(Rockafellar & Uryasev 2000). For fixed `eta`, `xi |-> (G(x,xi) - eta)_+` is
`L_G`-Lipschitz in `d` (composition of a 1-Lipschitz positive part with `G`). By §D,
`|E_P[(G-eta)_+] - E_Q[(G-eta)_+]| <= L_G W_1^d(P,Q)` **uniformly in `eta`**. Two
functions of `eta` differing by at most `c` everywhere have minima differing by at most
`c`; here `c = L_G W_1 / (1-alpha)`. QED. The constant matches the dual-density bound
`E[Z*^q]^{1/q} <= 1/(1-alpha)` for AV@R in Ernst–Pichler–Sprungk 2022 (§4.2), where the
optimal density satisfies `pi(Z* = 1/(1-alpha)) = 1 - alpha`, `pi(Z* = 0) = alpha`.

| `alpha` | tail fraction | amplification `1/(1-alpha)` |
|---|---|---|
| 0.90 | 10 % | 10x |
| 0.95 | 5 % | 20x |
| 0.99 | 1 % | 100x |

**VaR / quantiles are NOT Lipschitz w.r.t. `W_1`** — a quantile is a generalised inverse
CDF and jumps where the CDF is flat; the `delta_0` vs `delta_eta` argument of §F applies
directly. `QuantileViews` inherit **no** `W_1`-based guarantee. A quantile bound requires
the Kolmogorov metric (= cell discrepancy), i.e. again a discrepancy.

**Effective de-risking under deletion (rigorous, Arpón–Homem-de-Mello–Pagnoncelli, Prop.
1).** In the effective-scenario / DRO framework, deleting a scenario set `D` and
renormalising the remaining measure yields exactly
`min_x CVaR_{alpha'}[ G(x,xi) | Xi \ D ]` with
`alpha' = (alpha - sum_{i in D} P_i) / (1 - sum_{i in D} P_i)`. Since `alpha' < alpha`
whenever `P(D) > 0`, **removing scenarios mechanically lowers the effective risk level.**
That paper also confirms (via Römisch & Wets 2007) that a two-stage CVaR problem does
satisfy the condition needed for the Kantorovich-type stability bound
`v(P) - v(P^) <= d_{G,rho}(P,P^) <= h(rho) * mu^_c(P,P^)`.

---

## Deriving the INV-D-07 tolerance

### D.0 The negative result first: exact mean preservation is NOT implied

| Statement | Proof |
|---|---|
| Wasserstein-/Fortet-Mourier-optimal reduction does **not** in general preserve marginal means. | The retained support is a strict **subset** of `P`'s atoms, so `E_Q[xi] in conv{ xi^j : j not in J }`. If `E_P[xi]` lies outside that hull, **no** admissible reweighting attains it. Even when it lies inside, the optimal weights are *fully determined* by the nearest-neighbour redistribution rule (§C) — geometry, not moments — and satisfy no moment equation. |
| The bound `\|E_P[f] - E_Q[f]\| <= Lip(f) W_1` is **tight**; no smaller universal tolerance exists. | Take `K = H = 1`, `P = 1/2 delta_0 + 1/2 delta_1`, `S = 1`. Both admissible `Q` (`delta_0`, `delta_1`) give `W_1 = 1/2` and `\|E_P - E_Q\| = 1/2`. Equality. Hence the tolerance **must** be proportional to the achieved reduction distance; any constant chosen independently of `D_J` is either violated or vacuous. |

**Corollary for the spec: `INV-D-07` cannot be a hard-coded number. It must be a function
of the reducer's own reported `D_J`.**

### D.1 Metric normalisation — the exact conditions the bound needs

The bound is only as meaningful as the ground metric. Three normalisations must be fixed
**and materialised in the artefact**, or the guarantee is not reproducible:

| # | Item | Requirement | Why |
|---|---|---|---|
| N1 | **Per-series scale** `sigma_k > 0`, units of series `k` | Estimated once from the *generating* ensemble `P` (e.g. `sigma_k = ` cross-scenario, cross-slot std or robust IQR of series `k`), then **frozen** and versioned alongside the ensemble. | Raw coordinates mix EUR/MWh, MW, and a dimensionless direction. Any norm on `R^{H*K}` implicitly prices them against each other. Without `sigma_k` the "distance" is unit-dependent and the reduction result depends on whether prices are in EUR or ct. |
| N2 | **Per-slot weight** `w_t > 0` | Use `w_t = 1/H` (uniform slot averaging). | Fixes the `H`-dependence of the Lipschitz constants (see D.3). A non-uniform `w_t` is admissible (e.g. down-weighting far slots) but then the bound's constant changes to `max_t 1/(H w_t)`. |
| N3 | **Norm / aggregation across `(slot, series)`** | `l_1` aggregation. | `l_1` is the choice that makes the *coordinate-projection* class 1-Lipschitz after averaging, and it makes the bound hold **jointly over all `k` as a budget**, not merely per-series (D.4). `l_2` costs a factor `sqrt(H)`; `l_inf` loses the joint-budget form. |

Fix the **normalised coordinates** `z_{t,k} := xi_{t,k} / sigma_k` and the ground metric

```
d_1(xi, xi')  :=  (1/H) * sum_{t=1}^{H} sum_{k=1}^{K}  | xi_{t,k} - xi'_{t,k} | / sigma_k
```

`d_1` is a genuine metric (weighted `l_1`), hence `c^_1 = c_1 = d_1` and
`zeta_1^{d_1} = W_1^{d_1}` with no reduced-cost gap.

**Warning on `r >= 2`.** `c_r` contains `max{1, ||z||^{r-1}, ||z'||^{r-1}}`, whose kink sits
at `||z|| = 1`. So `zeta_r` for `r >= 2` is **not** scale-invariant even after N1: the
choice of `sigma_k` materially changes `zeta_2`. Record `sigma` in the artefact; do not
retune it between versions without re-deriving the tolerance.

### D.2 The master inequality

Let `Delta m_{t,k} := E_P[xi_{t,k}] - E_Q[xi_{t,k}]` and
`Delta M_k := (1/H) sum_t Delta m_{t,k}` (the time-averaged marginal mean of series `k`).
Let `eps := W_1^{d_1}(P,Q)`.

Take `f(z) = (1/H) sum_{t,k} c_{t,k} z_{t,k}` with `|c_{t,k}| <= 1`. For a linear
functional the Lipschitz constant is the dual norm; the dual of `d_1` (weighted `l_1` with
weight `1/H` per cell) is `max_{t,k} |a_{t,k}| * H` where `a` are the coefficients of `f`
in `z`. Here `a_{t,k} = c_{t,k}/H`, so `Lip_{d_1}(f) = max_{t,k} |c_{t,k}| <= 1`.
Choosing `c_{t,k} = sign(Delta m_{t,k})` and applying §D:

```
  (1/H) * sum_{t=1}^{H} sum_{k=1}^{K}  | Delta m_{t,k} | / sigma_k   <=   W_1^{d_1}(P,Q)      (M1)
```

`(M1)` is the strongest statement available and it implies, by the triangle inequality
`|sum_t Delta m_{t,k}| <= sum_t |Delta m_{t,k}|`:

```
  sum_{k=1}^{K}  | Delta M_k | / sigma_k    <=   W_1^{d_1}(P,Q)                               (M2)
  | Delta M_k |                             <=   sigma_k * W_1^{d_1}(P,Q)     for each k       (M3)
```

`(M2)` is the operationally important form: **one `W_1` budget buys one shared allowance
across all `K` series, not `K` independent allowances.**

Per-*cell* means are weaker: `f(z) = c z_{t0,k0}` has `Lip_{d_1} = H|c|`, giving
`|Delta m_{t,k}| <= sigma_k * H * W_1^{d_1}`. The `H` factor is the price of asking a
pointwise question of an `H`-averaged metric. **State `INV-D-07` against the time-averaged
marginal mean `M_k`, not per-slot means**, or the tolerance inflates by `H = 192`.

### D.3 Norm sensitivity — the exact constants

For `Delta M_k` (coefficients `1/H` on `H` cells, in `z`-coordinates):

| Ground metric on normalised `z` | dual norm | `Lip(M_k / sigma_k)` | Resulting bound |
|---|---|---|---|
| `(1/H) sum_{t,k} \|z\|` (= `d_1`, **recommended**) | weighted `l_inf` | `1` | `\|Delta M_k\| <= sigma_k * W_1` |
| `sum_{t,k} \|z\|` (unweighted `l_1`) | `l_inf` | `1/H` | `\|Delta M_k\| <= sigma_k * W_1 / H` |
| `(sum_{t,k} z^2)^{1/2}` (unweighted `l_2`, the `scenred` default) | `l_2` | `1/sqrt(H)` | `\|Delta M_k\| <= sigma_k * W_1 / sqrt(H)` |
| `max_{t,k} \|z\|` (`l_inf`) | `l_1` | `1` | `\|Delta M_k\| <= sigma_k * W_1` |

Note `W_1^{l_1} >= W_1^{l_2} >= W_1^{l_inf}` (from `||.||_1 >= ||.||_2 >= ||.||_inf`), so
the rows are not comparable a priori; which is numerically tightest is an empirical
question. What is **not** empirical: the metric must be declared, because the constant
changes by up to a factor `H` between rows.

### D.4 The tolerance, stated

Let the reducer report `D_J` — which it computes anyway, in closed form, as
`D_J = sum_{i in J} p_i * min_{j not in J} c^_r(xi^i, xi^j)` (§C). Then:

1. If the reducer runs at `r = 1` with cost `d_1`: `W_1^{d_1}(P,Q) = D_J` **exactly**.
2. If the reducer runs at `r >= 2`: `W_1^{d_1}(P,Q) <= zeta_r(P,Q) = D_J` (§A row 2), so
   `D_J` is still a valid, conservative budget.

Either way, set `eps := D_J` and define, with any allocation `lambda_k >= 0`,
`sum_k lambda_k = 1` (default `lambda_k = 1/K`):

```
INV-D-07  (derived form):
    for each series k:   | mean_P(series k)  -  mean_Q(series k) |  <=  lambda_k * sigma_k * D_J
    aggregate (stronger, preferred):
                         sum_k | Delta M_k | / sigma_k  <=  D_J
```

Properties of this tolerance, all of which the current guessed constant lacks:

| Property | Why it holds |
|---|---|
| **Derived, not guessed** | It is `Lip(f) * W_1`, with `Lip(f) = 1` established in D.2 and `W_1 <= D_J` in D.4. |
| **Tight** | The two-atom example in D.0 attains it; no smaller universal constant exists. |
| **Computed by the pipeline itself** | `D_J` is the reducer's own objective value. No new computation. |
| **Falsifiable** | The aggregate form is a single scalar check on the materialised artefact. |
| **Version-stable** | Depends only on `(sigma, w, norm, r, D_J)` — all of which are frozen in the artefact. |

### D.5 If exact mean preservation is actually wanted, buy it explicitly

The inner redistribution problem is an LP (SAGA09 p.21). Add `H*K` (or `K`, for
time-averaged means) linear equality constraints:

```
min_q   d( P, sum_{j not in J} q_j delta_{xi^j} )
s.t.    sum_{j not in J} q_j * xi^j_{t,k}  =  E_P[xi_{t,k}]    for all (t,k)  [or averaged over t]
        q >= 0,   sum_j q_j = 1
```

| Fact | Consequence |
|---|---|
| Feasible **iff** `E_P[xi] in conv{ xi^j : j not in J }` | Checkable by an LP feasibility test before committing to `J`. With `S = 64` retained joint paths and a mean that is by construction an average of `S_gen` paths, feasibility is likely but not guaranteed — test it. |
| Optimal value `D_J^{constr} >= D_J` | You trade a **larger, still measured** `W_1` for `INV-D-07` at tolerance **0**. The optimal-value bound of §B degrades by exactly `L * (D_J^{constr} - D_J)`. |
| Weights are no longer nearest-neighbour | The Dupačová et al. closed form no longer applies; the redistribution becomes a solved LP rather than a formula. |

**Recommendation.** Do both: enforce exact means via the constrained LP (making
`INV-D-07` trivially `0`, an unambiguous invariant), and *separately* materialise
`D_J^{constr}` as the audited approximation budget that everything else (§below) is
derived from. This removes the guessed number in two directions at once.

---

## What reduction provably does NOT preserve

All bounds below are `Lip(f) * W_1^{d_1}` with `eps := W_1^{d_1}(P,Q) <= D_J`, in
`sigma`-normalised coordinates, on a support with `max_{t,k} |z_{t,k}| <= R`
(`R` = normalised support radius; for `~5 sigma` tails, `R ~ 5`).

| Quantity | Preserved? | Derivable bound | Derivation |
|---|---|---|---|
| Time-averaged marginal mean `M_k` | No, but tightly bounded | `\|Delta M_k\| <= sigma_k eps` | D.2 |
| Per-cell mean `m_{t,k}` | No | `\|Delta m_{t,k}\| <= sigma_k H eps` | D.2, `Lip = H` |
| Second moment `E[z_i^2]` | **No** | `<= 2 R H eps` | `z |-> z^2` is `2R`-Lipschitz on `\|z\| <= R`; `Lip_{d_1}` of a single cell adds `H` |
| Variance | **No — can collapse to 0** | `\|Delta Var\| <= 4 R H eps` | `\|Delta E[z^2]\| + \|E_P[z]^2 - E_Q[z]^2\| <= 2RH eps + 2RH eps`. Counterexample: `P = 1/2 delta_0 + 1/2 delta_1`, `S = 1` gives `Var_Q = 0`, `Var_P = 1/4`. |
| Cross-series product `E[z_i z_j]` | **No** | `<= R H eps` | `\|z_i z_j - z'_i z'_j\| <= R(\|Delta z_i\| + \|Delta z_j\|) <= R * H * d_1(z,z')` |
| **Covariance** `Cov(z_i,z_j)` | **No, but bounded** | `\|Delta Cov\| <= 3 R H eps` | `\|Delta E[z_i z_j]\| + \|E_P z_i E_P z_j - E_Q z_i E_Q z_j\| <= RH eps + 2RH eps` |
| **Correlation** | **No, and NOT uniformly boundable** | none without a floor on `Var_Q` | `corr = Cov / sqrt(Var_i Var_j)`; the previous row plus the variance-collapse row means the denominator can go to 0. A correlation tolerance requires an *additional* materialised invariant `Var_Q(series k) >= v_min > 0`, then `\|Delta corr\| <= (3RH eps + \|corr\| * 4RH eps) / v_min` by the quotient rule. |
| **Tail / `CVaR_alpha`** | **No — amplified `1/(1-alpha)`** | `<= L_G eps / (1-alpha)` | §G. At `alpha = 0.95` a `0.5`-unit mean tolerance corresponds to a `10`-unit CVaR tolerance. |
| **VaR / quantiles** | **No — not even continuous** | **no `W_1` bound exists** | §G; needs Kolmogorov metric |
| **Joint chance constraint** | **No — no bound of any kind exists** | **none** | §F: `alpha_B` is not dominated by any function of `W_1` vanishing at 0. |
| **Filtration / temporal information structure** | **No — and reduction of a fan cannot fix it** | second term of eq. (6), §E | A *fan* of `S` distinct paths has `F_t(xi) = F_1(xi) = sigma(all S atoms)` for every `t`: it is an **anticipative / perfect-foresight** description. `D_{f,inf}(fan, true tree)` is therefore `O(1)` and **unaffected by any amount of `W_1` refinement**. |

Two structural remarks on correlation, since it is the spec's stated rationale:

1. **What survives.** Every retained atom is an intact joint path, so the *within-atom*
   coupling across series and slots is exact by construction. This is a real and
   sufficient defence of the "one shared scenario axis" design against per-series
   marginal sampling: the reduced ensemble can never manufacture a cross-series
   co-movement that does not occur in `P`.
2. **What does not.** The *ensemble-level* correlation matrix of `Q` is unconstrained: it
   is whatever the nearest-neighbour weights happen to produce. The row above gives a
   derivable tolerance, but its constant is `3 R H`, i.e. it degrades with support
   diameter and (per-cell) with horizon length. So the argument "correlation is the point"
   survives *qualitatively* but is not quantitatively protected by `INV-D-07` or by any
   `W_1` budget alone. If correlation is load-bearing, add explicit second-moment
   equality constraints to the redistribution LP (D.5) for the `K*(K-1)/2` series-level
   covariances — that is `28` more equalities for `K = 8`, trivially cheap.

---

## Empirical claim requiring our own verification

Every entry is a **prediction to be tested on our own ensemble**, not ground truth.

| # | Claim | Source | How we would falsify it |
|---|---|---|---|
| E1 | After a **50 %** reduction of an electrical-load scenario tree, the optimally reduced tree "retains about 90 % of relative accuracy". | Heitsch & Römisch, *COAP* 24:187–206 (2003), abstract; same claim recurs in Dupačová et al. 2003 for load trees. | Compute `v(P)` on the full `S_gen` ensemble and `v(Q_S)` for `S in {16,32,64,128}`; plot relative gap vs `D_J`. Predict roughly linear (§B). |
| E2 | Fast forward selection yields a reduced set "more similar in distribution" to the original than backward reduction **when the reduction is substantial** (our case: `S_gen -> 64`). | Heitsch & Römisch 2003 (as reported in the survey literature). | Run both at identical `S`, compare achieved `D_J`. Cheap A/B on the materialised artefact. |
| E3 | Reduction relying **directly** on `zeta_r` beats reduction relying on the earlier *upper bound* of `zeta_r`. | Heitsch & Römisch 2007, "A note on scenario reduction for two-stage stochastic programs", *ORL*. | Only relevant if the implementation uses the bound; check which cost the library computes. |
| E4 | Incorporating the filtration distance leads to a **smaller** number of remaining scenarios (for a fixed error budget) than the `L_r`-distance alone. | Heitsch & Römisch, *CMS* 2009, §5. | Not directly testable on a fan; testable if we ever build an actual tree for the S1..S4 cascade. |
| E5 | `CVaR_alpha` under reduction is systematically **under**-estimated. | Ours, from the mechanism: FFS deletion cost is `p_i * min_j c(xi^i, xi^j)`, proportional to `p_i`; low-probability extreme atoms are cheap to delete, and their mass is then transported *inward* to the nearest retained atom. Consistent in direction with Arpón et al. Prop. 1 (`alpha' < alpha`). | Compute `CVaR_alpha` of the realised objective on `P` vs `Q` for `alpha in {0.90, 0.95, 0.99}` across many ensemble versions; check sign of the bias. **This is a bias claim, not a bound — the §G bound is two-sided.** |
| E6 | For a bounded `Xi` with Lipschitz bounded density and a uniformly Lipschitz class `F`, `min_{Q in P_n} d_F(P,Q) = O((log n)^d / n)` (Koksma–Hlawka), attainable by transformed QMC sequences. | Römisch SAGA09 p.4. | Only meaningful at our `d`; see the quantization row below. |

---

## Applicability to the target system

Blunt assessment, feature by feature.

| System feature | Verdict |
|---|---|
| `INV-D-05` shared scenario axis | **Fully compatible and actively supported by the theory.** Reduction operates on whole atoms `xi^i in R^{H x K}`; the shared axis is what makes `d_1` a metric on joint paths at all. Reducing per-series would be a different (and unjustifiable) operation. |
| `INV-D-06` weights non-negative, sum to 1 | **Guaranteed by construction.** `q*_j = p_j + sum_{i in J_j} p_i >= 0` and `sum_j q*_j = sum_i p_i = 1` exactly, because redistribution is a partition of the deleted index set. No tolerance needed; this is an identity. |
| `INV-D-07` mean of each marginal within a tolerance | **Currently unfounded; fixable exactly.** Replace the guessed constant with `lambda_k sigma_k D_J` (D.4), or eliminate the tolerance entirely with the constrained redistribution LP (D.5). Prefer D.5: it converts a soft invariant into `= 0` and moves the approximation error into a single audited scalar `D_J^{constr}`. |
| Canonical metric order | **`r = 2`, not `r = 1`.** Prices enter the second-stage *costs* `q(xi)`, load/PV enter the *right-hand side* `h(xi)`. Rachev–Römisch's `r = 1` relaxation requires one of the two to be deterministic. Neither is. If the reducer is configured at `r = 1` (i.e. plain `W_1`), its `D_J` is a valid mean budget but **not** the optimal-value budget the stability theorem provides. |
| (A1) relatively complete recourse | **Must be checked, not assumed.** With `soc` bounds, ramp limits, and a *joint chance constraint* the second stage is **not** feasible for every `(xi, x)` — that is the whole point of the chance constraint. So (A1) fails as literally stated for the chance-constrained formulation. The two-stage Lipschitz theorem of §B does **not** apply to the chance-constrained problem; §F applies instead, with a discrepancy. Adding explicit slack/penalty variables to the second stage (soft `soc` bounds priced at a high penalty) restores complete recourse and with it the §B guarantee — this is a design decision the spec should make deliberately. |
| `CVaR_alpha` term in the objective | **Survives, with a `1/(1-alpha)` penalty.** `CVaR` *is* Lipschitz w.r.t. `W_1` (§G), so a `D_J` budget does buy a `CVaR` tolerance — inflated `20x` at `alpha = 0.95`, `100x` at `alpha = 0.99`. Concretely: whatever mean tolerance we accept, the CVaR tolerance is `20x` larger in `sigma`-units. With `S = 64` and `alpha = 0.95` the tail is `3.2` atoms of weight; after non-uniform redistribution a **single** retained atom can carry the entire tail. Report the reduced ensemble's effective tail sample size `1 / sum_{j in tail} q_j^2` as a materialised diagnostic. |
| Joint chance constraint `soc[s,t] in [socMin,socMax]` for mass `>= 1-eps` | **Does NOT survive. This is the hard failure.** §F proves no `W_1`/`zeta_r` tolerance implies anything about `\|P(B) - Q(B)\|`. A binding chance constraint can flip feasible↔infeasible at arbitrarily small `D_J`. Three options, in order of preference: (a) **verify ex post on the full `S_gen` ensemble** — take the `x` from the reduced problem and simulate `soc` over all `S_gen` atoms; this is LP-free and gives a hard number, no theory needed; (b) use the mixed criterion `d_lambda = lambda alpha_{B_rect} + (1-lambda) zeta_r`, `lambda -> 1`, accepting that `D_J` loses its closed form and the reduction becomes an LP-per-candidate; (c) replace the chance constraint by its `CVaR` conservative approximation, which *is* `W_1`-Lipschitz by §G — this reinstates a provable bound at the cost of conservatism. **Recommend (a) unconditionally, plus (c) if the constraint is binding in practice.** |
| Marginal `QuantileViews` | **No guarantee.** Quantiles are not `W_1`-continuous (§G). Any quantile displayed from the reduced ensemble is a *report about `Q`*, not an estimate of `P`'s quantile with a bound. Either compute `QuantileViews` from the **full** `S_gen` ensemble (cheap — it is a weighted sort, no optimisation) or label them as reduced-ensemble quantities. This is the cleanest fix in the whole document. |
| `CVaR TailStatistics` for the peak-demand view | Bounded by §G with `L_G = 1` (peak demand is a max of coordinates, `1`-Lipschitz in `l_inf`, hence `H`-Lipschitz in `d_1`): `\|Delta CVaR\| <= sigma_k H eps / (1-alpha)`. The `H` factor is severe. Use an `l_inf`-based ground metric if peak statistics are the priority — see the norm table in D.3. |
| Multi-stage S1→S4 cascade | **The two-stage bound is valid per solve; it is NOT valid for the policy.** §E's eq. (6) requires a filtration term, and a fan of `S` distinct paths is an anticipative description with `O(1)` filtration distance from any real tree. Heitsch & Römisch state in terms this explicit that Dupačová-et-al./Heitsch–Römisch scenario deletion "*is not appropriate*" for multi-stage input trees. Practical consequence: driving `D_J` down has **diminishing returns** past the point where the filtration error dominates, and the reduction tolerance must not be advertised as a bound on the value of the four-stage policy. Quantifying that requires either building an actual tree (with `D*_{f,inf}` in the reduction criterion) or measuring the anticipativity gap directly by comparing the fan-based policy value against a perfect-foresight upper bound. |
| "Correlation across series is the point" | **Survives qualitatively, is not quantitatively protected.** Within-atom coupling is exact (atoms are intact joint paths) — that defeats per-marginal sampling. Ensemble-level covariance is only bounded by `3 R H eps`, and correlation not at all without a variance floor. If the cross-market value argument is load-bearing, add `K(K-1)/2 = 28` series-level covariance equalities to the redistribution LP of D.5. |
| Reproducibility of the artefact | The tolerance is only reproducible if `(sigma_k, w_t, norm, r, D_J)` are all materialised in the versioned artefact. Add these five fields. Note especially that for `r >= 2` the metric is **not** scale-invariant, so silently re-estimating `sigma_k` on a new ensemble version invalidates cross-version comparison of `D_J`. |

### Alternatives with stronger guarantees

| Alternative | What it guarantees | What it costs | Verdict for BTM |
|---|---|---|---|
| **Moment matching** (Høyland & Wallace 2001; Høyland–Kaut–Wallace 2003) | Specified moments (means, covariances, skew, kurtosis) matched **exactly by construction**, so `INV-D-07` becomes vacuous. | **No bound whatsoever on `\|v(P) - v(Q)\|`.** Matching finitely many moments controls no probability metric; Kaut & Wallace (2007) exists precisely because matched-moment ensembles can carry badly wrong optimal values. Also generates *new* atoms, destroying the "atoms are real observed joint paths" property that gives the correlation argument its force. | **No** as a replacement. **Yes** as an *additive constraint* on the redistribution LP (D.5) — that keeps real atoms, keeps the `W_1` bound, and gets exact moments. Best of both. |
| **Optimal quantization** (Graf & Luschgy 2000; Pagès 1998) | `inf_{|supp| = N} W_p(P,Q) = O(N^{-1/d})` (Zador / Graf–Luschgy) — an *optimal rate*, better than the greedy heuristic's unguaranteed `D_J`. | The rate's `d` is the intrinsic dimension. Nominally `d = H*K = 192*8 = 1536`, so `N^{-1/1536}` is **vacuous**. Only the *effective* dimension `d_eff` (number of driving latent factors) makes it bite. | **Diagnostic value, not a method.** Measure `d_eff` (e.g. PCA spectrum per series). If `d_eff ~ 10-20`, then halving `W_1` needs `2^10`–`2^20` times more scenarios: **`D_J` cannot be driven small by increasing `S`.** That is a decisive argument for accepting a measured tolerance and robustifying against it, rather than chasing accuracy. |
| **Wasserstein DRO** (Mohajerin Esfahani & Kuhn 2018) | Replace `min E_Q[l]` by `min sup_{P' in B_eps(Q)} E_{P'}[l]` with **`eps := D_J`**. Since `W_1(P,Q) <= D_J`, the true `P` is *in the ball*, so the reduced-problem value is a **provable upper bound** on the true-ensemble value. The reduction error stops being an unquantified approximation and becomes an explicit robustness radius. Tractable finite convex reformulation when `l` is a pointwise max of concave-in-`xi` functions on convex `Xi`; and always `sup_{B_eps} E[l] <= E_Q[l] + eps * Lip(l)`. | Conservatism proportional to `eps`; the objective changes, so this is a modelling decision not a post-processing step. Does **not** by itself repair the chance constraint (a `W_1` ball does not control discrepancies) unless the chance constraint is replaced by its distributionally-robust `CVaR` approximation. | **This is the principled alternative to a guessed tolerance** and the one to flag in the ADR. It uses the *same* number `D_J` that D.4 derives, but turns it into a guarantee instead of a hope. |

---

## Sources

Primary, with the specific result used:

1. J. Dupačová, N. Gröwe-Kuska, W. Römisch, "Scenario reduction in stochastic
   programming: An approach using probability metrics", *Mathematical Programming*
   95(3):493–511, 2003. DOI [10.1007/s10107-002-0331-0](https://doi.org/10.1007/s10107-002-0331-0).
   — Optimal-reduction problem; nearest-neighbour redistribution rule; backward reduction.
2. H. Heitsch, W. Römisch, "Scenario reduction algorithms in stochastic programming",
   *Computational Optimization and Applications* 24(2–3):187–206, 2003.
   DOI [10.1023/A:1021805924152](https://doi.org/10.1023/A:1021805924152).
   PDF: https://link.springer.com/content/pdf/10.1023/A:1021805924152.pdf
   — Fast forward selection, simultaneous backward reduction; the 50 %/90 % empirical claim (E1).
3. H. Heitsch, W. Römisch, "A note on scenario reduction for two-stage stochastic
   programs", *Operations Research Letters* 35(6):731–738, 2007.
   PDF: https://www.wias-berlin.de/people/heitsch/HR07orl.pdf
   — Reduction directly on `zeta_r` rather than on its upper bound.
4. S. T. Rachev, W. Römisch, "Quantitative stability in stochastic programming: The method
   of probability metrics", *Mathematics of Operations Research* 27(4):792–818, 2002.
   PDF: https://www2.mathematik.hu-berlin.de/~romisch/papers/RaRo02.pdf
   — **Definition of `zeta_p` (eq. 4); assumptions (A1)–(A3); Theorem 3.3 (`zeta_2`
   canonical for two-stage fixed recourse); the `r = 1` relaxation; Example 3.4 (`zeta_2`
   vs `W_2` differ); §3.3 + Proposition 3.9 (chance-constrained stability requires the
   polyhedral discrepancy `alpha_{B_ph^k}`).** This is the load-bearing citation for §B and §F.
5. W. Römisch, "Scenario Reduction Techniques in Stochastic Programming", SAGA 2009,
   Sapporo. PDF: https://www2.mathematik.hu-berlin.de/~romisch/papers/Sapp09.pdf
   — Self-contained statements of `zeta_r`, `F_r`, `c_r`, the Rachev–Rüschendorf
   transportation form with reduced cost `c^_r`, assumptions (A1)–(A3), the closed-form
   `D_J`, the NP-hardness statement, both algorithms verbatim, the `d_lambda` mixed
   criterion, the `1/(s+1)` Hölder relation between `d_F` and `alpha_{B_rect}`,
   Koksma–Hlawka `O((log n)^d / n)`.
   Companion: https://www2.mathematik.hu-berlin.de/~romisch/papers/Habana08.pdf
6. H. Heitsch, W. Römisch, "Scenario tree reduction for multistage stochastic programs",
   *Computational Management Science* 6:117–133, 2009.
   PDF: https://www.wias-berlin.de/people/heitsch/HeRo09.pdf
   — **Theorem 1: the two-term bound `|v(xi) - v(xi~)| <= L(||xi - xi~||_r + D_{f,inf})`;
   exact definitions of `D_{f,inf}` and `D*_{f,inf}`; assumptions (A1)–(A3) + `X_1`
   bounded; the verbatim statement that pure scenario deletion "is not appropriate" for
   multistage input trees.**
7. H. Heitsch, W. Römisch, C. Strugarek, "Stability of multistage stochastic programs",
   *SIAM Journal on Optimization* 17(2):511–525, 2006.
   DOI [10.1137/050632865](https://doi.org/10.1137/050632865). — Original Thm 2.1 behind item 6.
8. H. Heitsch, W. Römisch, "Scenario tree modeling for multistage stochastic programs",
   *Mathematical Programming* 118(2):371–406, 2009.
   DOI [10.1007/s10107-007-0197-2](https://doi.org/10.1007/s10107-007-0197-2).
9. W. Römisch, "Stability of stochastic programming problems", in A. Ruszczyński,
   A. Shapiro (eds.), *Stochastic Programming*, Handbooks in Operations Research and
   Management Science, Vol. 10, Elsevier, 2003, pp. 483–554.
   DOI [10.1016/S0927-0507(03)10008-4](https://doi.org/10.1016/S0927-0507(03)10008-4).
10. R. Henrion, C. Küchler, W. Römisch, "Scenario reduction in stochastic programming with
    respect to discrepancy distances", *Computational Optimization and Applications*
    43(1):67–93, 2009. DOI [10.1007/s10589-007-9123-z](https://doi.org/10.1007/s10589-007-9123-z).
    — Reduction under cell discrepancy / Kolmogorov metric.
11. R. Henrion, C. Küchler, W. Römisch, "Discrepancy distances and scenario reduction in
    two-stage stochastic mixed-integer programming", *Journal of Industrial and Management
    Optimization* 4(2):363–384, 2008.
    DOI [10.3934/jimo.2008.4.363](https://doi.org/10.3934/jimo.2008.4.363).
    PDF: https://www2.mathematik.hu-berlin.de/~romisch/papers/HeKR08.pdf
12. R. Henrion, W. Römisch, "Metric regularity and quantitative stability in stochastic
    programs with probabilistic constraints", *Mathematical Programming* 84:55–88, 1999.
    DOI [10.1007/s101070050015](https://doi.org/10.1007/s101070050015).
    — Metric regularity condition (ii) of Proposition 3.9.
13. R. T. Rockafellar, S. Uryasev, "Optimization of conditional value-at-risk",
    *Journal of Risk* 2(3):21–41, 2000. DOI [10.21314/JOR.2000.038](https://doi.org/10.21314/JOR.2000.038).
    — The `min_eta { eta + E[(Z-eta)_+]/(1-alpha) }` formula used in the §G derivation.
14. O. G. Ernst, A. Pichler, B. Sprungk, "Wasserstein sensitivity of risk and uncertainty
    propagation", *SIAM/ASA Journal on Uncertainty Quantification* 10(3):915–948, 2022.
    DOI [10.1137/20M1325459](https://doi.org/10.1137/20M1325459). arXiv:
    [2003.03129](https://arxiv.org/abs/2003.03129).
    PDF: https://www.tu-chemnitz.de/mathematik/numa/PubArchive/ernstEtAl2022.pdf
    — §4.2: the AV@R dual density satisfies `pi(Z* = 1/(1-alpha)) = 1-alpha`,
    `pi(Z* = 0) = alpha`, hence `E[Z*^q]^{1/q} <= 1/(1-alpha)`; Thm 4.13 / eq. (4.19)
    `|rho_P(X) - rho_Q(X)| <= C d_p(P,Q)^beta`. Confirms the `1/(1-alpha)` constant.
15. A. Pichler, "Evaluations of risk measures for different probability measures",
    *SIAM Journal on Optimization* 23(1):530–551, 2013.
    DOI [10.1137/110859072](https://doi.org/10.1137/110859072).
16. S. Arpón, T. Homem-de-Mello, B. Pagnoncelli, "Scenario reduction for stochastic
    programs with Conditional Value-at-Risk", *Mathematical Programming* 170:327–356, 2018.
    Preprint (verified, used here): https://optimization-online.org/wp-content/uploads/2018/03/6520.pdf
    — Eq. (2.15) `v(P) - v(P^) <= d_{G,rho}(P,P^) <= h(rho) mu^_c(P,P^)`; eq. (2.16) the
    MILP form of optimal reduction; Proposition 1 (deleting `D` and renormalising gives
    `CVaR_{alpha'}` with `alpha' = (alpha - P(D))/(1 - P(D))`, i.e. **removing mass
    mechanically de-risks**); the appeal to Römisch & Wets (2007) for the CVaR two-stage
    stability precondition. *(Journal volume/pages not re-verified in this pass; the
    preprint URL is authoritative for everything cited.)*
17. M. Rahimian, G. Bayraksan, T. Homem-de-Mello, "Identifying effective scenarios in
    distributionally robust stochastic programs with total variation distance",
    *Mathematical Programming* 173:393–430, 2019.
    DOI [10.1007/s10107-017-1224-6](https://doi.org/10.1007/s10107-017-1224-6).
    — The "effective scenario" notion Arpón et al. build on.
18. W. Römisch, R. J.-B. Wets, "Stability of `eps`-approximate solutions to convex
    stochastic programs", *SIAM Journal on Optimization* 18(3):961–979, 2007.
    DOI [10.1137/060657716](https://doi.org/10.1137/060657716).
19. K. Høyland, S. W. Wallace, "Generating scenario trees for multistage decision
    problems", *Management Science* 47(2):295–307, 2001.
    DOI [10.1287/mnsc.47.2.295.9834](https://doi.org/10.1287/mnsc.47.2.295.9834).
20. K. Høyland, M. Kaut, S. W. Wallace, "A heuristic for moment-matching scenario
    generation", *Computational Optimization and Applications* 24(2–3):169–185, 2003.
    DOI [10.1023/A:1021853807313](https://doi.org/10.1023/A:1021853807313).
21. M. Kaut, S. W. Wallace, "Evaluation of scenario-generation methods for stochastic
    programming", *Pacific Journal of Optimization* 3(2):257–271, 2007.
    PDF: https://work.michalkaut.net/papers_etc/SG_evaluation.pdf
    — Why matched moments do not imply correct optimal values.
22. S. Graf, H. Luschgy, *Foundations of Quantization for Probability Distributions*,
    Lecture Notes in Mathematics 1730, Springer, 2000.
    DOI [10.1007/BFb0103945](https://doi.org/10.1007/BFb0103945).
    — Rigorous proof of Zador's theorem: `N^{-1/d}` quantization rate.
23. G. Pagès, "A space quantization method for numerical integration", *Journal of
    Computational and Applied Mathematics* 89(1):1–38, 1998.
    DOI [10.1016/S0377-0427(97)00190-8](https://doi.org/10.1016/S0377-0427(97)00190-8).
24. P. Mohajerin Esfahani, D. Kuhn, "Data-driven distributionally robust optimization
    using the Wasserstein metric: performance guarantees and tractable reformulations",
    *Mathematical Programming* 171(1–2):115–166, 2018.
    DOI [10.1007/s10107-017-1172-1](https://doi.org/10.1007/s10107-017-1172-1).
25. C. Villani, *Optimal Transport: Old and New*, Grundlehren der mathematischen
    Wissenschaften 338, Springer, 2009.
    DOI [10.1007/978-3-540-71050-9](https://doi.org/10.1007/978-3-540-71050-9).
    — Theorem 5.10: Kantorovich–Rubinstein duality, the engine of §D.
26. S. T. Rachev, *Probability Metrics and the Stability of Stochastic Models*, Wiley, 1991.
    — Theorem 6.2.1 (`zeta_p` convergence = weak convergence + `p`-th moment convergence);
    explicit `zeta_2` and `W_2` representations on `R` (Ch. 5.4, 13.1).
27. S. T. Rachev, L. Rüschendorf, *Mass Transportation Problems*, Springer, 1998.
    — The `zeta_r` ↔ transportation-with-reduced-cost equivalence on bounded `Xi`.
28. D. W. Walkup, R. J.-B. Wets, "Stochastic programs with recourse", *SIAM Journal on
    Applied Mathematics* 15:1299–1314, 1967 / 17:98–103, 1969.
    — Continuity/piecewise-linearity of the recourse value function `Phi`.
29. Reference implementation of the algorithms (GAMS/SCENRED, Römisch group):
    https://www2.mathematik.hu-berlin.de/~romisch/projects/GAMS/scenred.html
    — Useful for checking which cost `c^_r`, which `r`, and which norm a candidate library
    actually uses (see the D.3 norm-sensitivity table).
