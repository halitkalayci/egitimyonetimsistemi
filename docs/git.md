# Git

## Branching
- `feature/<spec-no>-<short-name>` — **no branch without a spec.** Also for CHANGE-lane mini-specs.
- Refactors: `refactor/<spec-no>-<short-name>` (mini-spec with Changed behavior = none).
- Fixes: `fix/<report-id>-<short-name>` (bug fixes carry a report, not a spec number);
  incidents: `incident/<date>-<short-name>`; trivial changes: `trivial/<short-name>` (see below).
- Dal adları İngilizce ve `kebab-case` (`docs/conventions.md`, Language).

## Commits
- Conventional Commits, with a plan reference: `feat(catalog): paging endpoint [plan 0001/3]`.
- Agent commits follow the same standard: the agent writes the message, the human approves.
- Kapsam (scope) alanı iş modülü adıdır: `training`, `members`, `enrollment`, `certification`,
  `identity` (`docs/architecture.md`, Modules). Mesaj dili İngilizce.

## Forbidden
- Direct commits to the default branch.
- Force push, history rewriting on shared branches. Undo = `git revert` (see recovery R-11).
- Migration dosyası elle düzenlenmez; yanlış migration yeni bir migration ile düzeltilir.

## Trivial changes
- Policy: **(1) PR + one reviewer, no spec** — bootstrap'ta seçilen politika. Yalnızca
  `workflows/change-request.md` triyaj rubriğinin TRIVIAL olarak sınıflandırdığı istekler için
  geçerlidir: kabul kriteri değişmiyor, paylaşılan kod elle tutulmuyor.
- Always: one narrow commit, `scripts/check` green. A second file or a new test means re-triage.
- Bu depo solo geliştiriliyor: "one reviewer" rolünü, insan ikinci bir göz olarak hazır değilse
  read-only review subagent'ı üstlenir (`/review`, invariant 3 — üreten kendi işini doğrulamaz).
  PR açılır, review raporu PR'a eklenir, sonra merge edilir.

## Pull requests
- PR template checklist completed; `scripts/check` green in CI; squash-merge.
- PR açıklaması spec/mini-spec numarasını ve kabul kriteri ↔ kanıt tablosunu referanslar;
  üretilemeyen kanıt (ekran görüntüsü, tek seferlik ölçüm) PR'a eklenir, depoya konmaz
  (`docs/testing.md`, Evidence policy).
