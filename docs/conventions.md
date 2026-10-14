# Conventions

## Language
- Chat language (interviews, sessions): Türkçe
- Document language (docs/, specs, plans, ADRs, review and verify reports): Türkçe
- Always English (protocol, not prose): ANEW core files, template headings and field labels,
  `Status:` values (Draft / Approved / In progress / Shipped), `Approved by / on:`, `Source:`.
- Code identifiers, branch names, commit messages: **İngilizce**. Türkçe alan terimi ↔ İngilizce tip
  eşleşmesi `docs/domain.md` (Ubiquitous language) tablosunda bağlayıcıdır.
- Agent messages (handoff summaries, refusals, `Next:` lines): chat language; command names, file
  paths and protocol fields verbatim.
- Kullanıcıya görünen arayüz metinleri ve hata mesajları Türkçedir; i18n anahtarları İngilizce.

## Language & framework versions
- .NET 10 / C# 14 — `Nullable: enable`, `TreatWarningsAsErrors: true`, `ImplicitUsings: enable`.
- Angular 21 + TypeScript `strict` (`strictTemplates` dahil), Node 24 LTS, paket kilidi commit'lenir.
- PostgreSQL 17; EF Core 10 (Npgsql). Sürüm yükseltmesi ADR gerektirir.

## Naming
- C#: tipler/metotlar `PascalCase`, parametre/yerel `camelCase`, arayüzler `I` önekli; dosya adı = tip adı.
- Kullanım senaryoları: `<Fiil><Nesne>Command` / `<Nesne>Query` + `Handler` (`EnrollParticipantCommand`).
- Veritabanı: tablo ve kolonlar `snake_case`, tablo adları çoğul (`enrollments`), FK `<tablo>_id`.
- Angular: dosyalar `kebab-case`, sınıflar `PascalCase`, feature klasörü modül adıyla aynı
  (`features/enrollment/`), sinyal tabanlı store `<feature>.store.ts`.
- Testler: `MethodOrBehavior_ShouldExpected_WhenCondition`; iş kuralı testleri adında `BR_n` taşır.
- Branch: `feature/<spec-no>-<short-name>` (ayrıntı: `docs/git.md`).

## Error handling
Tek geçerli desen: **Result + ProblemDetails**.
- Beklenen iş kuralı ihlali (BR-n) `Application` katmanında `Result<T>` olarak döner — beklenen
  durum için exception atılmaz; akış kontrolü exception'a bağlanmaz.
- `Api` katmanı `Result`'ı RFC 9457 `ProblemDetails`'e çevirir; hata kodu alan adıyla eşleşir.
- Gerçekten beklenmeyen hata (programlama hatası, altyapı arızası) exception olur: global handler
  `500` + `traceId` döner, sunucu tarafında `Error` seviyesinde loglanır.
- Kullanıcıya **asla** sızmayanlar: stack trace, SQL, bağlantı dizesi, iç tip adları, üçüncü taraf
  servis ham yanıtı, başka bir katılımcının/firmanın verisinin varlığını ima eden mesajlar.
- Angular: tek `HttpInterceptor` `ProblemDetails`'i kullanıcı mesajına çevirir; `401` → oturum
  yenileme, `403` → yetki mesajı. Bileşenler ham HTTP hatası işlemez.
- HTTP durum eşlemesi: doğrulama hatası `400`, kimliksiz `401`, yetkisiz `403`, bulunamadı `404`,
  **iş kuralı ihlali `409 Conflict`** (`ProblemDetails.type` ilgili BR numarasını taşır),
  beklenmeyen hata `500`. Dikkat: BR-1 bir hata değildir — kontenjan dolu oturuma yapılan kayıt
  `Waitlisted` olarak **başarıyla** oluşur (`201`), hata yolu değildir.

## Data rules
- Tüm zamanlar **UTC**: PostgreSQL `timestamptz`, .NET `DateTimeOffset`. Yerel saate yalnızca
  sunum katmanında çevrilir. Saat, `Application`'da `IClock` port'u üzerinden alınır (test edilebilirlik).
- Para ve oranlar `decimal` / `numeric(p,s)` — `float`/`double` asla.
- Birincil anahtarlar **UUID v7** (`uuid` / `Guid.CreateVersion7()`). Sertifika numarası ayrı bir iş
  alanı değeridir, ID'den türetilmez.
  Gerekçe: zaman sıralı olduğu için indeks dostudur (v4'ün rastgeleliği sayfa bölünmesi üretir),
  dışa açık kimlikte kayıt sayısını sızdırmaz (`bigint identity` sızdırır) ve veri birleştirme/
  taşımada çakışmaz.
- Enum'lar veritabanında **metin** olarak saklanır (int sıralaması kırılgandır).
- Fiziksel silme yoktur: kayıtlar durum alanıyla yönetilir (`Cancelled`, iptal edilmiş sertifika vb.).
- Metinsel kimlik alanları (e-posta, vergi no) normalize edilir: `trim` + küçük harf; e-posta ve
  vergi no üzerinde tekillik veritabanı kısıtıyla garanti edilir.
- Para/oran/tarih doğrulaması sınırda (FluentValidation) yapılır; `Domain` yine de kendi
  invariant'ını korur — doğrulama iki kez yapılır, bu bilinçlidir.

## Enforced by tooling
Prose tavsiyedir, araç kanundur. `scripts/check` içindekiler:
- Derleyici: `Nullable`, `TreatWarningsAsErrors`, `strict` TypeScript.
- `dotnet format --verify-no-changes` (stil ve `.editorconfig` analiz kuralları).
- Mimari testler (NetArchTest): `docs/architecture.md`'deki 1–4 numaralı yasaklar.
- ESLint `boundaries` kuralı: feature klasörleri arası import yasağı (yasak 5).
- EF Core migration tutarlılığı: model ile migration arasında fark varsa derleme kırmızı olur.
- CI'da ayrıca: `dotnet list package --vulnerable`, `npm audit` (ağ bağımlılığı olduğu için
  `scripts/check` içinde değil — `docs/security.md`, Dependencies).
