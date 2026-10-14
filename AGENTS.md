# AGENTS.md — Project Rules

Eğitim ve sertifikasyon yönetim sistemi: eğitim tanımları ve oturum takvimi, kurum personeli ile üye
firma çalışanlarının kaydı, katılım işleme, sertifika üretimi ve geçerlilik takibi. .NET 10 Web API +
Angular 21 + PostgreSQL 17 · Clean Architecture (Domain / Application / Infrastructure / Api) ·
modüller: Training · Members · Enrollment · Certification · Identity.

## Operating mode

**Mode: lite** (`workflows/README.md`); every workflow honors its gates.
**Start work** with `/new-feature` in lite (it chains the segments and asks at each gate).
Segment commands always stop (`workflows/segments.md`).
**Language: chat=tr · docs=tr**; protocol fields stay English (`docs/conventions.md`).

## Invariant rules (these survive bootstrap — never delete or weaken them)

1. **No spec, no code.** Every piece of work starts as a spec in `specs/active/` (from `specs/TEMPLATE.md`).
2. **Plan before build.** A human approves the plan before any code is written.
3. **The producer never verifies its own work.** Review and QA run in a separate session or a read-only subagent, working from files (diff + spec), never from the builder's chat.
4. **Evidence over claims.** "Done" requires `scripts/check` green and every acceptance criterion mapped to proof. Never claim completion without showing evidence.
5. **Tests are protected.** Weakening asserts, deleting or skipping tests to get to green is forbidden — always.
6. **Proposal rule.** Every question, option, or finding comes with your own recommendation and rationale. The human decides; nothing is applied without approval.
7. **Shipped specs are immutable.** Files under `specs/done/` are never edited.
8. **Uncertainty is surfaced, not assumed.** On ambiguity or a docs/code conflict: stop and use the matching recovery ramp (`prompts/recovery/`).

## Where things live

| What | Where |
|---|---|
| Architecture, boundaries & domain language | `docs/architecture.md` · `docs/domain.md` |
| Conventions · testing · security · git | `docs/conventions.md` · `docs/testing.md` · `docs/security.md` · `docs/git.md` |
| Decisions (ADRs) · roles (who may do what) | `docs/decisions/` · `docs/roles/` |
| Specs & plans | `specs/active/` · `specs/plans/` · shipped → `specs/done/` |
| Processes & gates · prompts & recovery ramps | `workflows/` · `prompts/` |
| The single verification command | `scripts/check` (steps: `scripts/check.conf`) |
