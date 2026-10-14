# Testing

## The contract
- Every acceptance criterion maps to at least one test (criterion ↔ test map lives in the plan).
- Tests assert **behavior**, not implementation details or mere status codes.
- The whole suite runs inside `scripts/check` — one command, everywhere.

## Frameworks & layout
- **Backend birim/entegrasyon:** xUnit v3 + Shouldly (assertion) + NSubstitute (test double).
  FluentAssertions kullanılmaz (v8+ ticari lisans).
- **Entegrasyon veritabanı:** Testcontainers ile **gerçek PostgreSQL** konteyneri. EF InMemory ve
  SQLite kullanılmaz — `timestamptz`, `numeric`, kısıtlar ve migration davranışını taklit
  etmedikleri için yanıltıcı yeşil üretirler. Konteyner koşu sonunda durdurulur (Resource hygiene).
- **Mimari testler:** NetArchTest — `tests/Architecture.Tests`.
- **Frontend birim:** `ng test --watch=false` (CI'da headless, tek işçi).
- **E2E:** Playwright, `--workers=1`, headless; yalnızca 3–5 kritik akış.
- **Yerleşim:** `tests/<Katman veya Modül>.Tests/` (örn. `tests/Domain.Tests`,
  `tests/Application.Tests`, `tests/Api.IntegrationTests`, `tests/Architecture.Tests`);
  Angular testleri kaynak dosyanın yanında `*.spec.ts`; E2E `e2e/` altında.
- **Adlandırma:** `MethodOrBehavior_ShouldExpected_WhenCondition`; iş kuralı testleri adında ilgili
  `BR_n` geçer (`Enroll_ShouldWaitlist_WhenCapacityFull_BR_1`) — kriter ↔ test eşlemesi böyle okunur.
- **Kapsam (coverage) yüzdesi hedefi yoktur:** yüzde yeşil olurken davranış kırık kalabilir.
  Ölçüt, aşağıdaki listenin tamamının karşılanmasıdır.

## What must be tested
- **Her iş kuralı (BR-1…BR-8)** en az bir testle, tercihen `Domain` veya `Application` seviyesinde.
- **Katılım eşiği sınırı** (BR-4): eşiğin bir altı sertifika üretmez, tam eşik üretir — sınır değeri
  testi zorunlu (varsayılan %70, sertifikasyon tanımından okunur).
- **Her domain invariant'ı** (`docs/domain.md`, Key domain invariants) — özellikle kontenjan sınırı
  eşzamanlı kayıt altında (BR-1) ve sertifika numarasının değişmezliği (BR-5).
- **Her yasak bağımlılık** (`docs/architecture.md` 1–5) mimari testle / ESLint kuralıyla.
- **Yetkilendirme:** her uç nokta için kimliksiz erişim reddi; üye firma temsilcisinin başka
  firmanın verisine erişememesi (negatif test zorunlu) — `docs/security.md`, AuthN / AuthZ.
- **Kalıcılık:** EF eşlemeleri ve migration'lar gerçek PostgreSQL üzerinde; `timestamptz` ile UTC
  dönüşümü, `numeric` hassasiyeti, tekillik kısıtları.
- **Hata sözleşmesi:** iş kuralı ihlalinin `ProblemDetails` olarak döndüğü ve iç detay (stack trace,
  SQL, başka kullanıcının verisinin varlığı) sızdırmadığı.
- **Kritik akışlar (E2E):** (1) oturum oluştur → katılımcı kaydet → kontenjan dolunca bekleme
  listesi, (2) katılım işle → sertifika üret → doğrula, (3) sertifika iptali ve yenileme uyarısı.
## Protected-tests rule
Weakening asserts, deleting, or skipping tests to reach green is forbidden. A red test triggers
`prompts/recovery/red-test.md` (R-02) — first decide what is wrong: code, test, or spec.

## Characterization tests
Before refactoring untested code (`workflows/refactor.md`) and for brownfield change requests
(`workflows/change-request.md`, Preserved behavior), pin the current behavior first — warts
included. They are written from observation, not from what the code "should" do.

## Evidence for UI criteria
A screenshot is evidence for a UI criterion; for change requests, before/after screenshots that
also show the preserved behavior. Screenshots go into the PR, not the repo (Evidence policy).
A screenshot proves a criterion only against the spec's "User experience": every state it lists
(empty, loading, error, success, no permission), compared with the linked design if there is one.

## Evidence policy
1. **Evidence is referenced, not stored.** Each acceptance criterion's evidence is a pointer: the
   test's name and the command that reproduces it (`./scripts/check`). The criterion ↔ evidence
   table in the spec/plan is the only evidence artifact that lives in the repo.
2. **Reproducible evidence is never filed.** No saved check output, console dumps, coverage reports
   or log copies in the repo — git history and CI runs already archive every execution.
3. **Non-reproducible evidence** (a screenshot of a manual UI check, a one-off measurement, an
   external-system confirmation) goes into the pull request (description or attachment); the
   evidence table links to it. It is the exception, not the rule.
4. **Never clone or copy the repository inside its own working tree.** Clean-clone verification
   runs in a temporary directory outside the repo; only the one-line result (command + green/red)
   is recorded in the evidence table. The clone is always deleted, never kept.
5. **There is no `evidence/` (or `.evidence/`) directory in this workspace.** An agent that feels
   the need to create one is about to violate rule 1. `scripts/doctor` warns on both violations.

## Resource hygiene
1. **A check run leaves nothing running.** No survivors of any kind: build/compile daemons,
   package-manager services, test hosts and parallel workers, watch modes, browsers, the application
   under test, containers or emulators started for the run. Agents run check an order of magnitude
   more often than humans; per-run residue compounds into machine starvation.
2. **The residue test** (the rule's definition, not advice): run `./scripts/check` twice back to
   back; after each run, process count and memory return to the baseline measured before the first.
3. Warm-process caches trade memory for speed for humans who build rarely; with an agent, trade the
   seconds back for a flat memory profile.
4. Interactive modes (watchers, dev servers, REPLs) never belong in check. e2e runs single-worker
   and headless by default; one component owns the app-under-test lifecycle — started for the run,
   stopped at the end, including on failure and timeout.

## Determinism
Flaky tests are fixed, not retried or skipped — see R-03. Evidence of a fix: 5 consecutive green runs.
