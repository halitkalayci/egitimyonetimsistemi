# Spec 0001 — Yürüyen iskelet (hello world)

- Status: Shipped
- Mode: lite
- Plan: `specs/plans/0001-plan.md`

## Intent
Ekip, bu sistemin ilk gerçek özelliğini yazmaya başlamadan önce uçtan uca yürüyen bir iskelet
istiyor: tarayıcıdan gelen bir isteğin sunucuya, sunucudan veritabanına gidip geri dönebildiğinin
kanıtlanmış olması. Başarı ölçütü teknik bir gösteri değil **güven**: tek bir doğrulama komutu
(`./scripts/check`) gerçek derleme ve testleri koşup yeşil dönüyor, mimari sınırlar ilk günden
araçla korunuyor, yeni bir özelliğe başlayan kişi "bunu nereye yazacağım" sorusunu sormuyor ve
yeni bir makinede proje tek komutla ayağa kalkıyor. Bu iş bilinçli olarak hiçbir iş kuralı
içermiyor (BR-1…BR-8'in hiçbiri uygulanmıyor) ve gerçek alan varlıkları oluşturmuyor; eğitim,
kayıt, katılım ve sertifika davranışları kendi spec'lerinde gelecek. Kullanıcı yönetimi, giriş
ekranı ve token akışı da bu işin dışında — yalnızca "yetkisiz istek varsayılan olarak reddedilir"
politikasının kurulduğu kanıtlanıyor.

## Requirements
- Sistem ayağa kalktığında, istemciden gelen bir isteğe **veritabanından okunan** bir değerle
  yanıt verebiliyor. Okunan veri alan diline ait değildir (nötr sistem bilgisi).
- Sistem, veritabanına erişemediğinde bunu açıkça bildiriyor; iç detay (bağlantı dizesi, SQL,
  stack trace) yanıta sızmıyor.
- Sistemin döndürdüğü veri bir **listedir**: kayıt yoksa boş liste döner, bu bir hata değildir.
- Yetkilendirme varsayılan olarak kapalıdır: kimlik bilgisi taşımayan bir istek, açıkça anonim
  olarak işaretlenmemiş bir yere eriştiğinde reddedilir. İskelet, bunun kanıtlanabilmesi için
  bilinçli olarak **korumalı bir uç** da içerir (içeriği önemsizdir, varlığı kanıt içindir).
- Kullanıcı, tek bir sayfada sistemden gelen değeri görebiliyor; bekleme ve hata durumlarında ne
  olduğunu anlıyor.
- Projede tek bir doğrulama komutu vardır ve **gerçek işi** yapar: kodu derler, biçimi denetler,
  testleri (birim + entegrasyon + mimari) ve istemci tarafı denetim/testlerini koşar.
- Doğrulama komutu çalıştıktan sonra arkada hiçbir süreç bırakmaz.
- Mimari sınır ihlali doğrulamayı kırmızıya çevirir; ihlal insan dikkatine bırakılmaz.
- Yeni bir geliştirici, dokümandaki adımları izleyerek veritabanını ve uygulamayı tek komutla
  ayağa kaldırabiliyor.

## User experience
- **Flow:** Kullanıcı uygulamanın kök adresini açar → sayfa açılırken bekleme göstergesi görür →
  sistemden gelen değer (nötr sistem bilgisi, ör. uygulama adı ve veritabanı zamanı) ekranda
  görünür. Başka bir adım, form veya gezinme yoktur.
- **States:**
  - *Loading:* istek sürerken bekleme göstergesi ve "Yükleniyor…" metni; içerik alanı boş.
  - *Success:* sistemden gelen değer okunur biçimde gösterilir.
  - *Empty:* sistem başarıyla yanıt verir ama gösterilecek kayıt yoktur → nötr bir boş durum
    mesajı ("Gösterilecek kayıt yok"), hata görünümü **değil**.
  - *Error:* istek başarısız olur (sunucu veya veritabanı erişilemez) → Türkçe, anlaşılır bir hata
    mesajı ve yeniden dene imkânı; ham hata metni, durum kodu veya stack trace gösterilmez.
  - *No permission:* bu sayfa için geçerli değil (sayfa anonim erişime açıktır) — yetki reddi
    davranışı API seviyesinde AC-4 ile kanıtlanır.
- **Design reference:** none — the flow above is the design.
- **Accessibility:** sayfa klavyeyle kullanılabilir (yeniden dene düğmesi sekme sırasında ve
  `Enter`/`Space` ile çalışır), tek bir `h1` başlık vardır, bekleme ve hata bildirimleri ekran
  okuyucuya duyurulur (`aria-live`), metin/zemin kontrastı WCAG AA (≥ 4.5:1).

## Constraints & out of scope
- **Hiçbir iş kuralı uygulanmaz** (BR-1…BR-8) ve gerçek alan varlığı (Eğitim, Oturum, Katılımcı,
  Sertifika…) oluşturulmaz. İskeletin okuduğu veri nötr sistem bilgisidir.
- Kimlik doğrulama **akışı** kapsam dışıdır: giriş ekranı, kullanıcı/rol yönetimi, token üretimi ve
  refresh rotasyonu yoktur (ADR-0011 ayrı spec'te uygulanır). Yalnızca varsayılan-ret politikası kurulur.
- Dağıtım (deployment) altyapısı, e-posta/bildirim, raporlama, çok dillilik kapsam dışıdır.
- Mimari `docs/architecture.md`'ye uyar: katman sınırları ve yasak bağımlılıklar bu işte araçla
  zorunlu hale gelir, gevşetilmez.
- Uzak depo tanımlı olmadığı için CI koşusu kanıt olarak kullanılamaz; CI tanımının doğruluğu
  gözden geçirmeyle, yeşillik yerel `./scripts/check` ile kanıtlanır.
- `./scripts/check` 5 dakikayı aşmamalıdır; ajan bu komutu insandan bir mertebe daha sık koşar.
- Arayüz metinleri Türkçedir; tasarım dosyası yoktur.

## Acceptance criteria
- [ ] AC-1 — Uygulama çalışırken yapılan bir istemci isteği, **uygulama adını** ve **veritabanı
      sunucusundan okunan zamanı** içeren başarılı bir yanıt döner; zaman UTC olarak taşınır
      (test: gerçek PostgreSQL'e karşı entegrasyon testi).
- [ ] AC-2 — Veritabanı erişilemez durumdayken aynı istek `503` + `ProblemDetails` döner (`500`
      programlama hatasına ayrılmıştır) ve yanıtın hiçbir alanında bağlantı dizesi, SQL metni, iç
      tip adı veya stack trace geçmez. `docs/conventions.md` (Error handling) bu eşlemeyi kaydeder.
- [ ] AC-3 — Yanıtların hata biçimi `ProblemDetails`'tir ve `traceId` taşır (`docs/conventions.md`,
      Error handling).
- [ ] AC-4 — Kimlik bilgisi taşımayan bir istek, anonim işaretlenmemiş bir uca gittiğinde `401`
      ile reddedilir; iskeletin tek genel ucu açıkça anonim işaretlendiği için yanıt verir
      (default-deny kanıtı, negatif test).
- [ ] AC-5 — Sayfa açıldığında bekleme durumu görünür, veri gelince değer görünür (Loading →
      Success geçişi testle kanıtlanır).
- [ ] AC-6 — API hata döndüğünde sayfa Türkçe hata mesajı ve yeniden dene imkânı gösterir; ham
      hata metni/durum kodu ekranda görünmez.
- [ ] AC-7 — Uç boş liste döndürdüğünde sayfa boş durum mesajı gösterir, hata görünümü göstermez
      (test: kayıt tablosu boşken).
- [ ] AC-8 — Sayfa klavyeyle tam kullanılabilir: tek `h1`, yeniden dene düğmesi sekmeyle erişilir ve
      klavyeyle tetiklenir, bekleme/hata bildirimi `aria-live` ile duyurulur.
- [ ] AC-9 — `./scripts/check` yorumda olmayan gerçek adımlarla (derleme · biçim · birim +
      entegrasyon + mimari testler · istemci lint + test + derleme) **yeşil** döner.
- [ ] AC-10 — Kasıtlı bir sınır ihlali (`Domain`'e altyapı referansı **ve** iki feature klasörü
      arasında import) `./scripts/check`'i **kırmızı** yapar; ihlal geri alındığında yeniden yeşil olur.
- [ ] AC-11 — Residue test: `./scripts/check` iki kez üst üste koşulduğunda süreç sayısı her
      koşudan sonra taban değerle **birebir aynıdır** ve boş bellek taban değerin **±200 MB**
      (makine gürültüsü) içindedir; `dotnet`, `MSBuild`, `VBCSCompiler`, `node` veya test
      konteyneri artığı kalmaz.
- [ ] AC-12 — `./scripts/doctor --strict` 0 fail döner.
- [ ] AC-13 — **Depo dışında, geçici bir dizinde** alınan temiz bir kopyada dokümandaki adımlar
      izlenerek veritabanı ve uygulama ayağa kalkar; adımlar `README.md` veya `docs/` içinde
      yazılıdır ve eksik adım içermez. Kopya sonunda silinir, depoya alınmaz (`docs/testing.md`,
      Evidence policy).
- [ ] AC-14 — Depoda hiçbir sır yoktur: bağlantı dizesi ve imzalama anahtarı yapılandırma
      örneğinden (`.env.example` / `appsettings.Example.json`) okunur, gerçek değer commit'lenmez.
- [ ] AC-15 — `./scripts/check` içindeki her test adımı, koştuğu test sayısını raporlar ve bu sayı
      **sıfırdan büyüktür** (bilinmeyen bayrağın test sürecine iletilip sessizce 0 test koşması
      kapatılmıştır).
- [ ] AC-16 — `./scripts/check` tamamı **5 dakikanın altında** tamamlanır (ölçülen süre kaydedilir).

## Definition of Done
- [ ] Every acceptance criterion mapped to proof (test or reproducible observation)
- [ ] `scripts/check` green
- [ ] Independent review done; real findings fixed, noise rejected with written rationale
- [ ] Docs / ADRs updated if behavior or architecture changed
- [ ] Spec moved to `specs/done/` (it becomes immutable there)

## Scorecard (fill at ship — honest numbers make the process improvable)
| Metric | Value |
|---|---|
| Spec revisions | 1 (self-critique bulgularının katlanması, onay öncesi) |
| Fix rounds | 0 (triyaj kararı: bulgular ertelendi) |
| Review findings: real / noise | 17 sunuldu · 0 düzeltildi · 0 yazılı gerekçeyle reddedildi |
| Regressions introduced | 0 |
| Bugs escaped to production | — (henüz üretim yok) |
