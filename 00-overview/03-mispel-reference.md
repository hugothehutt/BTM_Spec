# 03 — MiSpel Delineation Reference (Abgrenzungsoption, A1 and A5)

Normative transcription of the delineation machinery from `Anlage1_Arbeitsstand.pdf`
(§ 4.1.1, § 4.2–4.4, § 5.1–5.4.1). Formula numbers are the source's, cited unchanged.

## 1. Scope

- **Basisfall A1 "Stromspeicher"** — one Entnahme-/Einspeisestelle, one EE-Anlage, one
  Stromspeicher without Ladepunkt, plus sonstiger Verbrauch. Measured by two
  quarter-hour-accurate Zweirichtungszähler **Z1** (grid point) and **Z2** (storage).
- **Sonderfall A5** — A1 with several *gleichartige* EE-Anlagen behind one
  Einspeisestelle, settled over a joint measurement per § 24 Abs. 3 (i.V.m. § 19 Abs. 3
  S. 2 Halbsatz 2) EEG. Measurement unchanged: **Z1** and **Z2** suffice. Sole addition
  is the leistungsgewichteter Zuordnungs-Faktor `ZF`, splitting the förderfähige
  Netzeinspeisung per plant so each is settled at its own `AW`. Saldierung,
  Speicherverluste and umlagebelasteter Netzbezug are determined as in A1 (§ 5.3).
- **Abgrenzungsoption only** — no Pauschaloption, no Ausschließlichkeitsoption.
- PV in Direktvermarktung only. No Volleinspeisung, no Indifferenzbereich, no
  Ladepunkt, no A2/A3/A4/A6. Energy carries two labels only: green and grey (§ 7).

## 2. Notation

| Symbol | Meaning | Granularity | Unit |
|---|---|---|---|
| `Z1NB¼` | Netzbezug at the Entnahmestelle (NB: Netzbezug) | quarter-hour | MWh |
| `Z1NE¼` | Netzeinspeisung at the Einspeisestelle (NE: Netzeinspeisung) | quarter-hour | MWh |
| `Z2V¼` | Verbrauch im Stromspeicher (V: Verbrauch) | quarter-hour | MWh |
| `Z2E¼` | Erzeugung im Stromspeicher (E: Erzeugung) | quarter-hour | MWh |
| `AW¼`, `AWa¼`, `AWb¼` | anzulegender Wert of the EE-Anlage, resp. of plant a / b in A5 | quarter-hour | EUR/MWh |
| `Pa_inst`, `Pb_inst` | installierte Leistung of EE-Anlage a resp. b per § 24 Abs. 3 S. 2 Halbsatz 2 EEG. For gleichartige Windenergieanlagen an Land, use Referenzertrag / Standardertrag instead (§ 24 Abs. 3 S. 2 Halbsatz 1 EEG). | static | MW |
| `ZF` | Zuordnungs-Faktor — leistungsgewichteter share of the Netzeinspeisung assigned to one plant | static | fraction 0..1 |
| index `¼` | the formula applies to the single quarter-hour | | |
| `∑M` / `∑J` | sum over the quarter-hours of a calendar month / the months of a calendar year | | |
| `MIN[m;n]`, `MAX[m;n]`, `WENN[m>n;o;p]` | spreadsheet MIN, MAX, IF (`o` if `m>n`, else `p`) | | |

Monthly and yearly boundaries are Europe/Berlin civil-calendar boundaries
(`02-conventions.md` § 4.2). Within this scope `(14) ≡ (14)A1` and
`(19) ≡ (19)A1,A4`, so `(20)` carries no case distinction.

The four `Z` registers are meter values published per quarter-hour in the
smaller energy unit. They are normalised to MWh at dataload, in the same pass as
UTC normalisation (`02-conventions.md` § 2), and every register and formula in
this document is stated in the normalised unit. The scale is the whole of the
difference: no formula, ratio or case distinction changes, because every term of
every formula is scaled alike and the two ratios — `ZF` and `(30)` — cancel it.

## 3. Formulas

### Quarter-hour values

| No. | Formula | What it is |
|---|---|---|
| `(1)¼` | `MIN[ Z1NB¼ ; Z2V¼ ]` | zeitgleicher Netzstromverbrauch im Stromspeicher |
| `(2)¼` | `MIN[ Z1NE¼ ; Z2E¼ ]` | zeitgleiche Netzeinspeisung aus dem Stromspeicher |
| `(23)¼` | `Z1NE¼ − (2)¼` | grundsätzlich förderfähige zeitgleiche Netzeinspeisung direct from the EE-Anlage; AW>0 not yet applied |
| `(24)¼` | `WENN[ AW¼ > 0 ; 1 ; 0 ]` | AW>0 indicator of the quarter-hour |
| `(25)¼` | `(24)¼ · (23)¼` | förderfähige zeitgleiche Netzeinspeisung direct from the EE-Anlage, in AW>0 hours |
| `(27)¼` | `(24)¼ · (2)¼` | zeitgleiche Netzeinspeisung aus dem Stromspeicher, in AW>0 hours |

### Monthly values

| No. | Formula | What it is |
|---|---|---|
| `(3)` | `∑M Z1NB¼` | gesamter Netzbezug |
| `(4)` | `∑M Z1NE¼` | gesamte Netzeinspeisung |
| `(5)` | `∑M Z2V¼` | Verbrauch im Stromspeicher |
| `(6)` | `∑M Z2E¼` | Erzeugung im Stromspeicher |
| `(9)` | `∑M (1)¼` | zeitgleicher Netzstromverbrauch im Stromspeicher |
| `(10)` | `(5) − (9)` | zeitgleicher Verbrauch von EE-Strom im Stromspeicher |
| `(11)` | `∑M (2)¼` | Basiswert der zeitgleichen Netzeinspeisung aus dem Stromspeicher |
| `(12)` | `MAX[ (6) − (5) ; 0 ]` | Fremdtankstrom |
| `(13)` | `MAX[ (11) − (12) ; 0 ]` | berücksichtigungsfähige zeitgleiche Netzeinspeisung aus dem Stromspeicher |
| `(14)A1` | `(6) / (5)` | Wirkungsgrad der Stromspeicherung |
| `(15)` | `(14) · (10)` | EE-Speichererzeugung |
| `(16)` | `MAX[ (13) − (15) ; 0 ]` | **saldierungsfähige Netzeinspeisung** |
| `(17)A1` | `MAX[ (5) − (6) ; 0 ]` | Verluste im Stromspeicher |
| `(18)` | `(16) / (6)` | share of the saldierungsfähige Netzeinspeisung in der Speichererzeugung |
| `(19)A1` | `(18) · (17)` | privilegierungsfähige Stromspeicherverluste (§ 21 EnFG) |
| `(20)` | `MIN[ (16) + (19) ; (3) ]` | umlagereduzierende Strommenge |
| `(21)` | `(3) − (20)` | **umlagebelasteter Netzbezug** |
| `(26)` | `∑M (25)¼` | förderfähige Netzeinspeisung direct from the EE-Anlage, in AW>0 hours |
| `(28)` | `MIN[ (13) ; (15) ]` | grundsätzlich förderfähige Netzeinspeisung von EE-Speichererzeugung; AW>0 not yet applied |
| `(29)` | `∑M (27)¼` | zeitgleiche Netzeinspeisung aus dem Stromspeicher, in AW>0 hours |
| `(30)` | `(29) / (11)` | AW>0-Anteil der zeitgleichen Netzeinspeisung aus dem Stromspeicher |
| `(31)` | `(30) · (28)` | förderfähige Netzeinspeisung von EE-Speichererzeugung, in AW>0 hours |
| `(32)` | `(26) + (31)` | **insgesamt förderfähige Netzeinspeisung** |

### Yearly values

| No. | Formula | What it is |
|---|---|---|
| `(22)` | `∑J (21)` | umlagebelasteter Netzbezug im Kalenderjahr |
| `(33)` | `∑J (32)` | insgesamt förderfähige Netzeinspeisung im Kalenderjahr |

### A5 — several gleichartige EE-Anlagen

`x ∈ {a, b}` indexes the gleichartige EE-Anlagen. `(2)¼`, `(11)`, `(23)¼` and
`(28)` are reused unchanged from above and refer in A5 to the **sum** of the
jointly measured plants.

| No. | Formula | What it is |
|---|---|---|
| `(ZFx)` | `Px_inst / (Pa_inst + Pb_inst)` | Zuordnungs-Faktor of plant `x` |
| `(23x)¼ A5` | `(ZFx) · (23)¼` | grundsätzlich förderfähige zeitgleiche Netzeinspeisung attributed to plant `x` |
| `(24x)¼` | `WENN[ AWx¼ > 0 ; 1 ; 0 ]` | AWx>0 indicator of plant `x` |
| `(25x)¼` | `(24x)¼ · (23x)¼` | förderfähige zeitgleiche Netzeinspeisung of plant `x`, in AWx>0 hours |
| `(27x)¼` | `(24x)¼ · (2)¼` | zeitgleiche Netzeinspeisung aus dem Stromspeicher, in AWx>0 hours |
| `(26x)` | `∑M (25x)¼` | monthly förderfähige Netzeinspeisung direct from plant `x` |
| `(28x)A5` | `(ZFx) · (28)` | grundsätzlich förderfähige Netzeinspeisung von EE-Speichererzeugung attributed to plant `x` |
| `(29x)` | `∑M (27x)¼` | monthly zeitgleiche Netzeinspeisung aus dem Stromspeicher in AWx>0 hours |
| `(30x)` | `(29x) / (11)` | AWx>0-Anteil der zeitgleichen Netzeinspeisung aus dem Stromspeicher |
| `(31x)` | `(30x) · (28x)` | förderfähige Netzeinspeisung von EE-Speichererzeugung for plant `x`, in AWx>0 hours |
| `(32x)` | `(26x) + (31x)` | insgesamt förderfähige Netzeinspeisung for plant `x`, monthly |
| `(33x)` | `∑J (32x)` | insgesamt förderfähige Netzeinspeisung for plant `x`, yearly |

### Notes

- **Speichervorrang.** `(1)¼` and `(2)¼` implement the gesetzlich gewillkürte
  vorrangige Zuordnung (source § 2.1.5): on **charging, grid supply is assigned
  first**; on **discharging, storage generation is assigned first**.
- **Efficiency.** `(14)A1 = (6)/(5)` is a *derived* monthly ratio. `η_c` and `η_d` are
  constant, non-calibrated inputs of each backtest and generate `Z2V¼` and `Z2E¼`;
  `(14)` therefore deviates from `η_c·η_d` whenever SOC crosses a month boundary. It
  is never fitted.
- **Fremdtankstrom.** `(12) = MAX[(6) − (5); 0]` has no Ladepunkt source in A1 and can
  be positive only through SOC carried across the month boundary. Formula kept.
- **Source defect.** The source prints `(29b) = ∑J (27b)¼`; by symmetry with `(29a)`
  and its own caption ("im Kalendermonat") this is `∑M`, which is used here.

## 4. Meter ↔ engine symbols

Meters record **energy per slot**, engine variables are slot-average power
(`02-conventions.md` § 1). Convert with `Δt = 0.25 h` here and nowhere else.

```
P_net      = Σ_k ( pv_avail[t,k] − q[t,k] )
Z1NB¼ / Δt = MAX( 0,  load + p_charge − P_net − p_discharge )
Z1NE¼ / Δt = MAX( 0,  P_net + p_discharge − load − p_charge )
Z2V¼  / Δt = p_charge
Z2E¼  / Δt = p_discharge
```

- `Z1NB¼ · Z1NE¼ = 0` — one Zweirichtungszähler cannot import and export in one slot.
- `(Z1NB¼ − Z1NE¼) / Δt = load + p_charge − P_net − p_discharge` — the POI bridge of
  `02-conventions.md` § 1.

## 5. Decisions versus rules

- **Off-take is not a control.** `load` is fixed; no flexible loads in scope.
- **Curtailment is a priced decision, never a deterministic rule** (ADR-016), and its
  effect changes branch inside the same solve. Raising `q` by one unit:

  | Slot condition | `∂(3)` | `∂(9)` | `∂(2)¼` | `∂(23)¼` |
  |---|---|---|---|---|
  | exporting, `Z1NE ≤ Z2E` | 0 | 0 | −1 | 0 |
  | exporting, `Z1NE > Z2E` | 0 | 0 | 0 | −1 |
  | importing, `Z1NB < Z2V` | +1 | +1 | 0 | 0 |
  | importing, `Z1NB ≥ Z2V` | +1 | 0 | 0 | 0 |

  Four regimes, each selected by which side of a `MIN` is active. The same holds for
  aFRR down-activation, which is grid-driven charging. No lever may therefore carry a
  delineation coefficient of its own (ADR-017).
- **The machinery is monthly, the decision is quarter-hourly.** `(16)`, `(18)`, `(30)`
  and `(32)` are calendar-month sums and ratios, so a quarter-hour action is valued
  only through its effect on the month's aggregate. `H_plan` truncation and the
  separable `V_del` must carry this (`02-conventions.md` § 4.3, ADR-017).

## 6. The route split

`(11)` — storage energy that reached the grid — is what both routes compete for, and
it splits at the PV-attributed share of storage generation:

```
pv_share    = (10) / (5)
(28)        = MIN[ (11) ;  (6)·pv_share ]     green
(16)        = MAX[ (11) − (6)·pv_share ; 0 ]  grey
(28) + (16) = (13)                            always
(16) + (19) = (16) / η_month                  η_month = (6)/(5)
```

Consequences that follow from the algebra alone:

| Result | Condition |
|---|---|
| `(31) = (29)` — the ratio cancels, the green route is a per-slot linear sum | `(9) = 0`, no grid charging |
| `(16)+(19) = (9) = (3)`, so `(21) = 0` identically | `load = 0`, connection never clips discharge, `(12) = 0` |
| Relief falls by `(12)/η_rt` | `(12) > 0` |
| `(12) = 0` is arithmetically impossible to violate | monthly charge throughput `> η_d · E_usable / (1 − η_rt)` ≈ 8 charge-equivalent cycles |

**Discharging into load forfeits both routes.** Such an MWh never enters `(11)`, so it
earns neither `(30)·premium` nor `levy_rate/η_month`. This is priced by the state
equations, not by a term (ADR-017).

## 7. Green versus grey

Identical everywhere behind the meter; they differ **only at injection to the grid**:

- **Green** → **förderfähige Netzeinspeisung** `(32)`, in A5 per plant `(32x)`, settled
  at that plant's `AW` and only in AW>0 quarter-hours via `(24)¼`.
- **Grey** → **saldierungsfähige Netzeinspeisung** `(16)`, which through `(20)`/`(21)`
  reduces the umlagebelasteter Netzbezug to zero in its own amount. No Umlagen on it.

`(19)A1` extends the same relief to the privilegierungsfähige Stromspeicherverluste
per § 21 EnFG, in the share `(18)`.
