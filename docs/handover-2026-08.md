# BTM Engine — Handover

> **HANDOVER · AUG 2026** — 12 sections · ~60 min
> Transcribed from the artifact "BTM Engine Handover"
> (`https://claude.ai/code/artifact/11e95bad-e95e-4978-9aff-3fb5202fae1c`),
> and checked against the repository at `98a3086`, 2026-09-03.

## Contents

1. [What this is](#01--what-this-is)
2. [The problem](#02--the-problem-stated-exactly)
3. [Why it is hard](#03--why-this-is-hard-in-four-moves)
4. [The layer model](#04--six-components-strictly-forward)
5. [Four clocks](#05--four-clocks-four-irreversibilities)
6. [Seven seams](#06--seven-seams-and-one-that-carries-the-weight)
7. [Fan, not tree](#07--a-fan-not-a-tree)
8. [The objective](#08--the-objective-and-why-no-prices-cross-c2)
9. [V(SOC) and duality](#09--vsoc-and-two-dualities-pointing-opposite-ways)
10. [Risk](#10--risk-which-inconsistency-you-pay-for)
11. [Expensive to reverse](#11--what-is-expensive-to-reverse)
12. [Where things live](#12--where-things-live-and-how-a-claim-earns-trust)

---

## 01 · What this is

**SPECIFICATION HANDOVER**

A cross-market optimisation engine for a behind-the-meter battery.

This repository contains no implementation. It contains the normative contracts, the layer designs, the compliance architecture and the argument for why each is shaped the way it is.

The engine co-optimises day-ahead spot, continuous intraday and aFRR — capacity and energy — against peak shaving, load shifting and arbitrage, under asset, POI and German grid-fee constraints. One site, one grid connection point, one battery.

What exists today is a specification that has been argued to a fairly deep level and a set of C# type sketches that make the seams concrete. What does not exist is a line of engine code. That is deliberate: the decisions below are the kind where retrofitting costs more than the whole build, so the theory goes first.

| Figure | Quantity |
|---|---|
| 12.9k | lines of normative prose — 10.6k of it in `00-overview/`–`04-compliance/` |
| 17 | architecture decisions — live ids at 001–018, 003 deleted |
| 7 | frozen seam contracts |
| 168 | glossary terms |
| 0 | lines of engine code |

### How to read the repository

Reading order is stated in the README and each part assumes the previous one. Start at `00-overview/01-system-model.md`, then `02-conventions.md` — every other document depends on those being unambiguous. Keep the glossary open. Then the ADRs, then the contracts.

Two status values are used throughout: **Normative** binds as written, and **Advisory** is guidance that may be departed from with reason. Consistency between documents is held by the ADRs and by review.

### Run sheet

- 01 What this is
- 02 The problem — markets, streams, asset
- 03 Why it is hard — four difficulties
- 04 The layer model
- 05 Four clocks
- 06 Seven seams
- 07 Fan, not tree
- 08 The objective
- 09 V(SOC) and the two dualities
- 10 Risk
- 11 Decisions expensive to reverse
- 12 Where things live

---

## 02 · The problem, stated exactly

**SCOPE**

Scope is settled and deliberately narrow. The market enumeration is a closed enum on the C3 contract — adding a member is a contract change, not a feature.

| Market | Gate | First-stage decision | Commitment |
|---|---|---|---|
| aFRR capacity | S1 | `(limitPriceEurPerMwH, volumeMw)` per block and direction — `rUp[b]`, `rDn[b]` are the *offered* MW | Frozen at submission; award fixes the SOC corridor for the whole block, and obliges an energy offer of at least the awarded MW (`INV-P-12`) |
| Day-ahead spot | S2 | A monotone price–quantity bid curve per slot | Position frozen at clearing |
| Continuous intraday | S3, S4 | The order sent this tick — one slot wide, and nothing else | Freezes progressively, as fills occur |
| aFRR energy | exogenous | Activation is stochastic, not chosen | Delivery shortfall is a halt condition |

**Reserve capacity is pay-as-bid** (`ADR-018`): awarded capacity is paid the price submitted, not a clearing price. So the first-stage decision carries a price as well as a volume. `μ` — the headroom dual of §03 — is the *reservation* price and a Planner output, crossing C3 as `reserveShadowValueEurPerMwH`, audit-only. The markup over `μ` is `IReserveBidPolicy`, a sibling of `IQuotingPolicy` living on the Planner side of the seam and never inside the MILP. Award probability comes from the shared scenario ensemble as `P(award | p, b) = Σ_s w_s · 1[afrrCapPriceEurPerMwH[s,b] ≥ p]`, and `B(E) = max_p [ p · E · P(award | p, b) ]` is an envelope, with concavity verified against the breakpoints rather than assumed (`INV-V-12`). Feasibility binds on the full offer — an award obliges all of it — and only the revenue term is discounted.

`ADR-018` records one deliberate tension with `ADR-002`: pay-as-bid is a market rule written into prose and an unconditional invariant rather than into the `MarketCalendar`, on the scoping ground that the engine targets Germany. It is the one assumption there that a second jurisdiction would force back open.

Gate times are not in the specification, on purpose. They live in a versioned `MarketCalendar` loaded from data and replayed with the backtest. Market rules change; a rule change must be a data change. A wrong gate time hard-coded in the engine silently makes a backtest optimistic, which is the worst possible failure because it looks like alpha.

### The asset and the bridge

One grid connection point, one PV plant in Direktvermarktung, one battery without its own charging point, plus inflexible site load. Two quarter-hour-accurate bidirectional meters: Z1 at the grid point, Z2 at the storage.

```
p_poi[t]  = load[t] − pv_out[t] − p_batt[t]           MW, slot average
pv_out[t] = Σ_k ( pv_avail[t,k] − q[t,k] )            post-curtailment

soc[t+1]  = soc[t] + Δt·( η_c·p_charge[t] − p_discharge[t]/η_d )
```

> THE POI BRIDGE. CHARGE AND DISCHARGE ARE SEPARATE NON-NEGATIVE VARIABLES — A SINGLE SIGNED VARIABLE LETS THE OPTIMISER ROUND-TRIP FOR FREE THROUGH THE LOSS TERM.

There are three sign frames and conflating them is the classic expensive mistake. **Battery frame:** discharge positive. **POI frame:** import positive, because every network charge is levied on import peak and the quantity the tariff measures must make "peak" a maximum rather than a minimum. **Market frame:** sale positive. The market frame is sign-aligned with the battery frame, so battery ↔ POI is the only sign flip in the system, and it happens in exactly one place — the bridge above.

Curtailment `q[t,k]` is a decision variable, never a rule. Without it the generation allocation is infeasible whenever available PV exceeds load plus charge headroom plus the export limit — an ordinary condition, not an edge case. But it is also not merely a feasibility slack: under the delineation regime it moves the monthly aggregates that decide how the month gets paid.

### What is out

FCR and mFRR appear nowhere in the repository — not in scope, not even as rejected alternatives. Redispatch and capacity markets are out for v1. Multi-asset is anticipated in the contract keying but not designed for. Flexible load is out: site load is fixed, so the battery and curtailment are the only levers. And the engine is not a forecasting system — models are fitted offline, the engine consumes artefacts.

---

## 03 · Why this is hard, in four moves

**DIFFICULTY**

Any one of these is tractable alone. The engine exists because they interact.

### 1 · The markets cannot be optimised separately

Reserve does not consume energy — it consumes power headroom and SOC corridor. Every MW of upward reserve held is a MW that cannot be sold on spot and a block of SOC that cannot be traded away, for the whole delivery block. And the capacity decision is made first, before the intraday information that determines its opportunity cost exists.

The obvious fix — reserve X MW, then let the spot MILP work with what is left — is rejected on a specific argument. It is a primal restriction: it deletes options from the feasible set.

> If X is wrong, the loss is unbounded and, worse, invisible: the spot MILP reports a clean optimum over a feasible set that was silently amputated.
> — ADR-010

The instrument used instead is a dual price — charge the spot problem for the headroom it consumes, at what aFRR would have paid for it. Dual pricing weakly dominates primal restriction: it recovers the same answer when the allocation happens to be right, and does better whenever it is wrong. That internal transfer price of a MW of battery headroom is, per the ADR, where the edge over less careful participants lives.

Three tiers sit behind one interface, with a measured gap between them: a joint stochastic MILP as the accuracy reference, a Lagrangian decomposition that discovers the headroom price and certifies a bound, and a learned price surface for the fast path. No tier ships without its gap against Tier 1 measured — reported as p50 / p90 / p99 and worst case, never as a mean, and conditioned on regime.

Under pay-as-bid the dual is necessary but not sufficient. Offering *at* `μ` earns exactly zero surplus on every award — the whole margin is the markup — so `ADR-018` keeps `μ` as the reservation price and floors the bid on it: `INV-P-11` forbids bidding below `μ`, because at that price an award destroys value. The bid-policy fixed point — `B(E)` depends on `P(award | p)`, `p` on `μ`, `μ` on the solve — is taken inside Tier 2's existing subgradient loop. Tier 3 prices once against `μ̂` and eats the one-pass error, which T5 reports like any other tier gap. This is the ladder doing the job it exists for.

### 2 · Everything is correlated, in the tail

> A high-price hour is precisely the hour where the peak charge bites, the aFRR is activated, and the battery is wanted in three places at once. Sampling each series from its own marginal destroys exactly the dependence structure that determines whether the co-optimisation is worth anything. It will systematically overstate the value of the strategy.
> — ADR-005

Hence one joint ensemble on one shared scenario axis — a single `[S × H × K]` array with weights summing to one, used by every uncertain series. Code that indexes one series by `s` and another by `s'` is a defect. The contract calls this the invariant most worth defending.

### 3 · A settlement rule reaches back into dispatch

This is the part that surprises people. The German delineation regime (Abgrenzungsoption) pays for the same MWh through two competing routes, and which route it takes is decided by a calendar-month aggregate.

```
grey  (16) = MAX[ (11) − (6)·pv_share ; 0 ]      relieves levies
green (28) = MIN[ (11) ;  (6)·pv_share ]          Marktprämie
```

> (11) IS STORAGE ENERGY THAT REACHED THE GRID. IT SPLITS AT (6)·PV_SHARE — THE MONTH'S PV-VERSUS-GRID CHARGING MIX.

That split is set by the aggregate of roughly 2,880 quarter-hourly decisions, each of which is also being driven by spot, aFRR, peak and imbalance. Three properties make it hostile to the rest of the design:

- The payout is bilinear — a ratio of decision-dependent monthly sums multiplied by a MIN — which sits outside the closed algebra the valuation layer is allowed to emit.
- The active branch of every MIN is chosen by the optimiser, so a lever's marginal effect has no fixed sign. Raising curtailment by one unit has four different sensitivity signatures depending on which side of which MIN is active.
- Levies are charged on a post-battery quantity, not on grid import — which breaks the ordering the valuation composer previously relied on.

The resolution is §09's second duality: the MILP carries the month-to-date accumulators as state equations, and valuation publishes only marginal values.

### 4 · The tariff has a cliff, and the battery walks toward it

The §19(2) intensive-use reduction requires at least 7,000 full-load hours and more than 10 GWh of annual consumption. Full-load hours are annual energy divided by annual peak power — so battery operation moves the numerator and the denominator. The asset cannot avoid changing its own qualification.

Falling below the threshold loses the reduction for the entire year. Of the four clocks in §05, the accounting clock is the only one whose irreversibility column reads **Absolute**.

> Losing a year of network-charge reduction for a day's spread is the single largest downside event in the BTM business case and it must not be reachable through an approximation error.
> — ADR-011

So qualification is not a price and not a report. It is a state variable, a dimension of the value function, and it carries a hard guard on top of the economics.

---

## 04 · Six components, strictly forward

**ARCHITECTURE**

The engine is a strictly layered pipeline. Belief is a bitemporal, content-addressed view over market and site data that can physically not return information the engine did not yet know. Valuation turns beliefs into linearizable economic primitives. The Planner composes those into one MILP family solved at gates and emits order intent. Execution is a pre-existing simulator behind a thin adapter. Settlement recomputes the truth ex post.

```
  L1 · Belief   ──C1──▶  L2 · Valuation  ──C2──▶  L3 · Planner  ──C3──▶  L4 · Execution  ──C4──▶  L5 · Settlement
  bitemporal store       emits shapes             the MILP              external, adapter        recomputes truth
        ▲                                                                                              │
        │                                                                                              │
        └──────────  C6 · read next tick, lag ≥ 1  ──  L0 · State / Value  ──  C5 · end of tick  ◀─────┘
                                                     the only back edge
```

Within a tick every arrow points right. L0 is snapshotted at the top of the tick and written only at the end of it, so no component can read a value written later in the same tick.

### The cycle, and why L0 exists

The intended flow is not acyclic. `00-overview/01-system-model.md` §2 names **two** dependencies that point backwards. The marginal value of stored energy — `λ_SOC` — is a dual of the Planner's own optimisation, so Valuation cannot compute it without solving what the Planner is about to solve. And `pPoiRealisedPeakMw` is a Settlement output that `PeakView` needs, as is the §19(2) qualification state, which is realised history.

Filled orders constraining later solves travel the same path — L0 holds the commitment ledger alongside realised peak and V(SOC) — but the specification does not count that as a third feedback edge: the ledger is carried forward by design rather than closing a cycle that had to be broken.

The resolution is to add L0 as the only backwards path, always read at a lag of at least one tick. The lag is a first-class contract field, not an implementation detail: a Planner that knows the peak state is three slots stale treats the gap conservatively rather than assuming it was empty.

That purity is not tidiness. L1, L2, L3 and L5 being pure functions of their declared inputs is what makes every seam independently recordable, replayable and property-testable. It is the foundation of the entire compliance architecture. Any hidden state in L2 or L3 destroys it.

---

## 05 · Four clocks, four irreversibilities

**TIME**

The engine is not driven by one loop. Each clock has a different information set and a different cost of being wrong, and conflating them is what makes the problem look intractable.

| Clock | Period | Drives | Irreversibility |
|---|---|---|---|
| `C_accounting` | Month / year boundary | Peak reset, §19(2) evaluation | Absolute — a lost qualification is lost for the period |
| `C_slow` | Daily, or on material change | Refit V(SOC); recalibrate fill and activation models | Cheap to redo |
| `C_gate` | Market-defined | Binding submissions | Binding at gate — becomes a commitment |
| `C_tick` | Event-driven, sub-gate | Continuous intraday re-optimisation, dispatch | Reversible until the intraday gate |

The Planner therefore runs at six gates, not once a day: S0 slow loop, S1 reserve, S2 day-ahead, S3 post-DA rebalance, S4 continuous intraday, S5 dispatch. The same core model is solved at every one of them. What changes is which variables are free and which are pinned by the commitment ledger. One formulation, one set of tests, six gates.

Two of the six are not optimisations. `02-layers/L3-planner.md` §7 puts **S5 out of scope**: it is a controller, holding a setpoint inside the SOC corridor, so only its interface is specified. And S0 commits nothing — it refits `V`.

Two rules attach and both are load-bearing. Every solve is stamped with the clock that triggered it, and that stamp enters the content hash of the resulting plan. And the slow loop's output is an input, never a dependency — if it has not run, the Planner uses the last valid value function with a staleness penalty. It never blocks.

One detail worth carrying: the time grid is quarter-hours counted from the Unix epoch in UTC, monotone and gap-free. But tariff logic is not UTC. A civil day has 92, 96 or 100 quarter-hour slots depending on daylight saving. Any code that assumes 96 is wrong twice a year.

---

## 06 · Seven seams, and one that carries the weight

**CONTRACTS**

The seams are the frozen surface. Each is versioned, each has a field table with units and ranges, and each has invariants that are checked where the payload crosses.

| Seam | Payload | What it guarantees |
|---|---|---|
| C0 | — | Conventions applying to every seam: validation placement, versioning, identity, quality metadata |
| C1 | `BeliefSnapshot` | Everything Valuation may know, as of a knowledge-time boundary. Valuation has no access to the store, the network, or a clock |
| C2 | `ValuationBundle` | The load-bearing seam. All economics, in a closed algebra. The Planner sees no prices and no scenario arrays |
| C3 | `ExecutionIntent` | Deliberately thin. Intent only — quoting is a separate policy |
| C4 | `ExecutionOutcome` | What actually happened. Settlement reconstructs truth from this and metered reality, with no sight of intent |
| C5 | `StateUpdate` | The only backwards edge. Written end of tick |
| C6 | `StateSnapshot` | The read side, lagged. Separate from C5 because it has a different direction |

If you read one contract, read C2. It is where the economics stop being opinions and become algebra, and it is the seam that decides whether the Planner's problem is solvable.

Settlement being blind to intent is not an oversight. An identifier crosses C4 as an opaque key and nothing else does. Ex-post accounting therefore cannot be contaminated by ex-ante belief — which is the only reason its numbers are worth anything as evidence against the Planner.

> **PROCESS**
> A change to any file in `03-contracts/` requires, in the same commit: its version bump, an update to the affected layer designs, and an update to the corresponding conformance tests. The README calls this the single most important process constraint in the project. It is the one habit to inherit intact.

---

## 07 · A fan, not a tree

**SOLUTION METHOD**

The declared solution method, in one line: rolling two-stage SAA over a scenario fan, glued by the commitment ledger and closed by a terminal value function.

The distinction that matters is what the ensemble actually is. It is a fan: S complete paths, all distinct from slot 1, with a flat filtration and no interior nodes. It encodes no progressive branching. A tree would; this does not.

```
F₁ = F₂ = … = F_H          flat filtration — everything is known at slot 1, or nothing is
```

So each solve is two-stage, and the policy that emerges from solving repeatedly is multistage. Those are different statements and the specification keeps them apart:

> The policy is multistage. Each solve is two-stage.
> — L3 · PLANNER

Non-anticipativity is enforced structurally, by omission: first-stage variables simply have no scenario index. That is exactly equivalent to imposing explicit non-anticipativity constraints — variable elimination on the equality across scenarios — and it needs no convexity, continuity or integrality assumption to be valid.

What the method guarantees is stated precisely and no more than that: an exactly optimal here-and-now decision for the S-point weighted distribution, given V. No claim of multistage optimality is available, and none is made.

### The error is declared, not hidden

The second stage sees the whole planning horizon at once, while reality reveals it progressively. So the model treats its future self as better informed than it will be. That is stage-aggregation error. It is largest at the reserve gate — the one commitment made before the month's route is knowable — and smallest at continuous intraday.

The important consequence: this error survives an exact value function. V prices the state at the end of the horizon; it cannot repair a wrong information structure inside it. Which is why the compliance level that measures the optimality gap carries three separate instruments rather than one number.

---

## 08 · The objective, and why no prices cross C2

**MATHEMATICS**

```
max  Σ LinearTerms + Σ PwlTerms + V(socTerminal)
     − Σ peakPriceEurPerMw·zPeak·proration
     + Σ_j λ_j · A_j
     − cvarWeight · CVaR_α(imbalance + activation cost)
```

> ASSEMBLED ENTIRELY FROM C2 TERMS. THE PLANNER ADDS NO ECONOMICS OF ITS OWN — IF IT NEEDS A NUMBER, A VIEW MUST OWN IT.

The original framing was correct and incomplete: several terms are not linear in the decision — peak is a max, aFRR capacity is a curve — so publish functions, not scalars. The question the specification actually answers is *functions in what form?*

Arbitrary delegates do not work: the Planner would have to sample them to build a MILP, which is slow, lossy, and silently changes the problem being solved. Scalars do not work either: the economics are wrong. So Valuation emits exactly five shapes, and nothing else crosses C2.

| # | Shape | What it is |
|---|---|---|
| SHAPE 1 | `LinearTerm` | A coefficient on a variable. Energy value, cycle cost, the delineation marginals. |
| SHAPE 2 | `PwlTerm` | A piecewise-linear curve with declared curvature. Concave-and-maximised needs no binaries — which is the difference between a 200 ms solve and a 200 s one. |
| SHAPE 3 | `EpigraphTerm` | How a max becomes linear. The peak charge, floored by realised peak. |
| SHAPE 4 | `BoundTerm` | Feasibility, not price. Power limits, the prequalified reserve envelope, reliable intraday volume. |
| SHAPE 5 | `CouplingConstraint` | Rows that tie markets together — headroom, SOC corridor, POI envelope. |

On top of the algebra sits a term ownership matrix: thirteen economic effects, each with exactly one owning view. The composer rejects any bundle that prices the same effect on the same variable in the same slot twice. Double-counting is the failure this layer is designed to make structurally impossible, not the failure it hopes to catch in review.

Quality never becomes a branch. Every value crossing C1 carries provenance and quality, and downstream those map onto a risk multiplier: a degraded price belief widens the risk level and shrinks position bounds; an imputed load forecast widens the peak safety margin. Bad data takes the same code path with more conservative parameters. One path to test, and the response to degradation is continuous rather than a cliff.

---

## 09 · V(SOC), and two dualities pointing opposite ways

**MATHEMATICS**

The horizon has to be closed by something. The obvious move is to publish a scalar marginal value of stored energy. The specification rejects it on two grounds, and the second is the interesting one.

First, it is circular — that marginal value is a dual of the Planner's own problem, so Valuation cannot produce it without solving what the Planner is about to solve. Second, a scalar is a linearisation around an assumed operating point:

> The peak charge creates a large, state-dependent kink: the marginal value of the MWh that keeps you below the peak threshold is enormous; the marginal value of the next one is close to the spot spread. No scalar represents both.
> — ADR-007

```
V : SOC → EUR    breakpoints {(e_i, v_i)}, slopes strictly decreasing
                 conditioned on (peakState, qualState, calendar)
```

So V is not one curve. It is a family of concave curves indexed by discrete state, and that indexing is what lets a genuinely non-concave feature — the qualification cliff — be represented without being smoothed away. Concavity in SOC given the discrete state; discreteness handled by the discrete state. Because it is concave and the objective maximises, it embeds with no binary variables and the relaxation is exact.

### The inversion

The marginal value is now endogenous. It is the subgradient of V at the optimal terminal SOC, computed after the solve, recorded for diagnostics, and it crosses no contract at all. Valuation publishes the curve; the Planner derives the dual. That inversion is what removes the cycle.

### And now the same idea, backwards

Delineation from §03 does the exact opposite, and this contrast is the thing to hold on to. There, the MILP carries the seven month-to-date accumulators and their state equations as constraints, and Valuation publishes only the marginal values `λ_j = ∂V_del/∂A_j`, recomputed every tick.

| | Stored energy | Delineation |
|---|---|---|
| Valuation publishes | the curve | the dual |
| Planner derives | the dual | the state, via constraints |
| Why | the dual is of the Planner's own problem — circular | the value is a closed-form function of published formulas — no circularity |
| Produced by | a fitted artefact, on the slow clock | arithmetic on a projection, every tick |
| Can go stale | yes — staleness and invalidation are distinct | no artefact exists to go stale |

The two value functions are treated as separable — total continuation value is the SOC part plus the delineation part — and that separability rests on one asserted structural condition about monthly throughput, which removes the only channel coupling them.

### Staleness is not invalidation

A stale curve is an out-of-date approximation of the right curve: its slopes get shrunk and the engine carries on, becoming progressively indifferent to terminal SOC rather than confidently wrong about it. An invalidated curve is the wrong curve — one fitted while qualified says nothing useful about an at-risk world. Invalidation forces a defensive posture until a refit lands, because shrinking the slopes of a curve conditioned on the wrong state does not make it less wrong.

> **NOT YET VERIFIED**
> The duality argument above is the shape the specification commits to, and the reasoning for it is written down. Treat the structure as decided and the justification as outstanding work: the method for producing V is explicitly recorded as unsettled.

---

## 10 · Risk: which inconsistency you pay for

**MATHEMATICS**

Risk enters as conditional value at risk, on losses denominated in euros, at a confidence level that widens when input quality degrades. The choice is made on a tractability argument rather than a preference one.

```
CVaR_α(L) = min_ζ { ζ + (1−α)⁻¹ · E_w[(L − ζ)⁺] }

Rockafellar–Uryasev, as linear rows:
min  ζ + (1−α)⁻¹ Σ_s w_s u_s
s.t. u_s ≥ L_s − ζ ,  u_s ≥ 0 ,  ζ free      S+1 vars, S rows, no binaries
```

That is the whole reason it is CVaR and not, say, a variance penalty: same risk intent, no quadratic term, no loss of MILP structure. The reformulation holds for distributions with atoms, which is exactly what a reduced weighted ensemble is — the weights enter only as objective coefficients.

One more property does quiet work throughout. CVaR is positively homogeneous, so a deterministic non-negative rate can be moved in and out of the functional. That is what lets the peak tail be taken in megawatts and priced outside it, and it is the step that fails first if the tariff rate is ever made uncertain or non-linear in the level.

### The part worth knowing before you touch it

The engine applies a static risk measure afresh at every gate, on a fresh conditional ensemble, inside a horizon that moves. That is not the same object as any single risk measure describes, and the gap has a name: time-inconsistency.

It is not repairable by tuning. Within the law-invariant coherent class, the only time-consistent measures are expectation and essential supremum — CVaR is not on that list and cannot be put on it. Nesting the measure recursively is time-consistent, but it is strictly more conservative than the static form, and the conservatism compounds with the number of stages.

> So the choice is not consistent versus inconsistent. It is which inconsistency is paid for.
> — TN-01 · CVAR ACROSS STAGES

Two mechanisms make it concrete. A scenario can leave the tail between ticks simply because uncertainty resolved favourably, and the engine then unwinds a hedge it paid to establish — systematic, not noise, because the conditional tail shrinks as the horizon shortens. And the continuation value beyond the horizon is a plain expectation, so risk attitude is discontinuous at a boundary that moves every tick. Both point the same way: the engine over-hedges early in an accounting period relative to the policy it will actually follow.

> **NOT YET VERIFIED**
> The risk machinery is specified and the tractability argument is solid. The aggregation question — one tail on one aggregate loss, versus a sum of tails per term — has no recorded decision yet, and the empirical claims that would settle it carry no acceptance bands. This and §09 are the two places where I would start.

---

## 11 · What is expensive to reverse

**DECISIONS**

Seventeen decisions are recorded, each with context, decision, consequences and rejected alternatives. They carry a reversibility grade, and the grade is the reading order that matters. IDs are stable: a deleted decision keeps its number and its row, and the number is never reissued.

| ADR | Decision | Cost to reverse |
|---|---|---|
| 004 | Bitemporal, three-tier, content-addressed belief store — every read carries an as-of, with no overload that omits it | VERY EXPENSIVE |
| 005 | One joint scenario ensemble on one shared axis | VERY EXPENSIVE |
| 006 | Forward within a tick, lagged across ticks, via L0 | VERY EXPENSIVE |
| 008 | Valuation emits five linearizable shapes, never prices — this is the C2 contract | VERY EXPENSIVE |
| 013 | Content-addressed determinism; every artefact replayable from a manifest | VERY EXPENSIVE |
| 017 | Delineation enters as state equations plus marginal values | VERY EXPENSIVE |
| 007 | Publish V(SOC) as a concave curve, not a scalar | Expensive |
| 011 | Tariff regime is a plug-in; qualification is a state variable | Expensive later — if `PeakView` hardcodes `max` |
| 002 · 009 · 010 · 012 · 014 · 016 · 018 | Clocks and calendar-as-data; term ownership; the co-optimisation ladder; intent versus quoting; the degradation ladder; curtailment as a priced decision; reserve capacity is pay-as-bid and the bid price is a policy, not a dual | Moderate |
| 001 | C# throughout; the solver sits behind an abstraction | Cheap |
| 015 | Register of deliberately deferred decisions — a title-only placeholder, status **Open**, not a decision and so ungraded | — |
| 003 | ~~Typed quantities and two reference frames~~ — **Deleted**, superseded by `00-overview/02-conventions.md` §5.2 and §1. Keeps its id and its row | — |

> "Very expensive" decisions must be implemented first and correctly, because retrofitting them means rewriting every layer above.
> — 01-ADR/README

In practice that sequencing means the first two workstreams are the Belief store (004, 005) and the contract surface (006, 008, 017), before any economics or optimisation is written — see `05-implementation/P0-workstreams.md`.

Three decisions are deliberately deferred and named as such: the MILP solver (Gurobi intended), the per-tick latency budget, and intraday fill-model fidelity. Nothing in the specification depends on them. Where a document must reference one, it references the abstraction and never the choice. They return only if a technical note acquires a dependency on one.

Two of the ADRs above are worth reading purely as arguments, whatever you end up doing: the co-optimisation ladder, for the primal-restriction-versus-dual-price case, and the delineation decision, for how a regulatory formula gets turned into something a MILP can carry.

---

## 12 · Where things live, and how a claim earns trust

**ORIENTATION**

| Looking for | Go to |
|---|---|
| The layer model, the clocks, the non-goals | `00-overview/01-system-model.md` |
| Units, sign frames, the time grid, naming | `00-overview/02-conventions.md` |
| The delineation regime, transcribed | `00-overview/03-mispel-reference.md` |
| What a word means — never what a thing does | `00-overview/04-glossary.md` |
| Why something is the way it is | `01-adr/` |
| What crosses a seam, with units and invariants | `03-contracts/` |
| Internals of a layer | `02-layers/` |
| How any of it gets tested | `04-compliance/` |
| Arguments not yet accepted as binding | `06-theory/` |
| Type sketches for the contract payloads | `stubs/` |
| Sequencing and the agent playbook | `05-implementation/` |
| Which word kept which meaning | `00-overview/04-glossary.md` §14 |

The vocabulary rulings live in glossary §14, which records seven contested words. Four further rulings — `gate`, `tier`, `primitive`, `oracle` — are argued in `TN-03` §§3.2–3.5 but not yet propagated, and by design do not enter §14 until they are: a ruling is recorded with its rename, never before it.

### Seven levels of test, one question each

T0 asks whether a component computes what its own spec says. T1, whether what crosses a seam satisfies the contract. T2 covers properties that must hold for every input, including inputs nobody wrote a fixture for. T3 is determinism and replay. T4 is calibration against observed reality. T5 is the optimality gap and its cost. T6 is the adversarial audit — can the engine be made to peek, to accept garbage, or to use an expired artefact. A lookahead divergence there is a stop-ship.

### How a claim earns trust — an open question

The repository has no claim register, no verification protocol and no executable checks. There was a `07-verification/` directory holding all three — `PROTOCOL.md`, a `claims.yaml` of roughly three thousand claims harvested wholesale from the prose with nine carrying evidence labels, a ruling set as data, and four stdlib-Python checks over the register, the unit and frame naming rule and the glossary. It was deleted wholesale in commit `d6f05c1`. `06-theory/TN-03-vocabulary.md` carries a NOTE recording that its harvested corpus included the directory, so its references to `PROTOCOL` and `claims.yaml` are historical; two of its rulings, `gate` §3.2 and `primitive` §3.5, were argued against senses `PROTOCOL` owned and now stand on their remaining senses alone.

So how a mathematical or empirical claim earns trust is currently unanswered. One mechanism from the deleted protocol is worth carrying into whatever replaces it: **no number before its sign.** Establish the direction of an effect and the condition under which it holds before anyone estimates its magnitude.

### What I would do first

The mandate here is to get the theory solid before anything is built, so the build order is not the first question. The first questions are the two marked open in §09 and §10 — the duality argument and the risk aggregation — because they sit under the objective function itself, and because everything at the very-expensive tier in §11 is downstream of them being right.

The third is what replaces the claim register. Two concrete backlogs are already in the repository and need no new machinery: the eight open findings in `TN-03` §4 — including `T1` claiming to be the single authoritative invariant register while omitting 35 invariants, which does not merely omit but silently redirects two tests to the wrong ids — and the four unpropagated vocabulary rulings in `TN-03` §§3.2–3.5.
