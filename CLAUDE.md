# BTM Engine — Specification Repository

## Repo structure

| Path | Contents |
|---|---|
| `00-overview/` | Entry point: the layer model and four clocks (`01`), the normative units / signs / time-grid / naming conventions (`02`), the MiSpel delineation reference (`03`). |
| `01-adr/` | Decisions expensive to reverse, `ADR-001` … `ADR-017`, each with context, decision, consequences and rejected alternatives. `ADR-015` is the open-decision register; `ADR-017` fixes how the delineation regime enters the objective. |
| `02-layers/` | Internals of the six layers: `L0` state/value store, `L1` belief, `L2` valuation, `L3` planner, `L4` execution boundary, `L5` settlement. |
| `03-contracts/` | The seven frozen seams: `C0` conventions plus `C1`–`C6` payloads, each versioned with a field table and invariants. |
| `04-compliance/` | The seven test levels `T0`–`T6`: architecture, invariant register, property/metamorphic tests, determinism, calibration, optimality gap, adversarial audit. |
| `05-implementation/` | Advisory: `P0` workstreams and sequencing, `P1` agent playbook. |
| `stubs/` | C# type sketches for the contract payloads, quantities, enums and interfaces. Illustrative, not the implementation. |
| `README.md` | Reading order, specification status per part, one-paragraph system summary. |
| `docs/agents/` | Agent-skill configuration: issue tracker, triage labels, domain-doc layout. |

---

## Agent skills

### Issue tracker

GitHub Issues on `hugothehutt/BTM_Spec`, via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The five canonical roles, each label string equal to its name. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context. The glossary is `00-overview/04-glossary.md`; the ruling set is `07-verification/rulings.json`; ADRs live in `01-adr/`, not `docs/adr/`. See `docs/agents/domain.md`.
