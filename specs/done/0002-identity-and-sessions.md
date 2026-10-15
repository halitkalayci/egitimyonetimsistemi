# Spec 0002 — Kimlik, oturum ve kullanıcı yönetimi

- Status: Shipped
- Mode: lite
- Plan: `specs/plans/0002-plan.md`

## Intent
Eğitim sorumlusu, sisteme kimin girebileceğini ve kimin neyi görebileceğini kontrol etmek istiyor:
bugün iskelet her isteği reddediyor ama kimse giriş de yapamıyor, yani sistem kullanılamaz durumda.
Bu iş, kişilerin kimliğini doğrulayıp kendilerine tanınmış rolle çalışmalarını sağlar; eğitim
sorumlusu (ve kendi firması için üye firma temsilcisi) hesap açar, rol atar, gerektiğinde hesabı
kapatır veya şifre sıfırlar. Başarı ölçütü: bir kullanıcı giriş yapıp rolünün izin verdiği işlemleri
yapabiliyor, izin verilmeyenlerde reddediliyor, üye firma temsilcisi başka firmanın verisine hiçbir
yoldan erişemiyor ve çalınmış bir oturum anahtarı ikinci kez kullanıldığında o oturum zinciri
tamamen kapanıyor. Bilinçli olarak yapılmayanlar: kendi kendine kayıt, iki aşamalı doğrulama,
e-posta ile şifre sıfırlama (bildirim altyapısı henüz yok) ve dış kimlik sağlayıcıyla SSO.

## Requirements
- Kişi kaydı ve sistem hesabı **tek kayıttır**: hesap bilgileri (e-posta + şifre) kişi kaydının
  opsiyonel parçasıdır (`docs/domain.md`, tek kayıt modeli). Hesabı olmayan kişi giriş yapamaz.
- Kullanıcı, e-postası ve şifresiyle giriş yapar; başarılı girişte rolünün izin verdiği işlemleri
  yapabilir, izin verilmeyen işlemlerde reddedilir.
- Giriş başarısız olduğunda neden ayırt edilmez: olmayan e-posta ile yanlış şifre aynı yanıtı alır
  (kullanıcı varlığı sızmaz).
- Ardışık başarısız denemeler hesabı geçici olarak kilitler; kilit süresince doğru şifre bile
  kabul edilmez, süre sonunda hesap kendiliğinden açılır. Kilitli hesaba yapılan ek denemeler
  kilit süresini **uzatmaz**.
- Olmayan bir e-posta ile yapılan denemede de şifre doğrulama maliyeti ödenir; yanıt süresi
  kullanıcının var olup olmadığını sızdırmaz.
- Aynı ağ adresinden yapılan giriş denemeleri hız sınırına tabidir; bu sınır, tek bir kullanıcıyı
  kasten kilitleyerek hizmet engellemeye karşı kullanıcı kilidinden bağımsız çalışır.
- Yeni hesap tek kullanımlık geçici şifreyle açılır. Kullanıcı **ilk girişte şifresini değiştirmek
  zorundadır**; değiştirmeden başka hiçbir işlem yapamaz.
- Kullanıcı kendi şifresini değiştirebilir; mevcut şifresini doğrulamak zorundadır ve eski şifre
  değişiklikten sonra çalışmaz.
- Şifresini unutan kullanıcı için eğitim sorumlusu yeni bir geçici şifre üretir; bu şifre ekranda
  yalnızca bir kez gösterilir ve sonradan görüntülenemez.
- Oturum kısa ömürlüdür ve arka planda sessizce yenilenir: kullanıcı, yenileme sürdüğü sürece
  yeniden giriş yapmak zorunda kalmaz. Yenileme penceresi dolduğunda yeniden giriş istenir.
- Her yenilemede yeni bir yenileme anahtarı verilir ve eskisi geçersizleşir. Aynı anahtarın ikinci
  kez kullanılması tespit edilirse **o oturum zincirinin tamamı** iptal edilir.
- Yenileme anahtarı istemciye tarayıcının JavaScript'ine kapalı bir çerezle verilir: sayfa
  yenilendiğinde oturum korunur ama anahtar istemci kodundan okunamaz.
- Kullanıcı birden fazla cihazdan aynı anda giriş yapabilir. "Çıkış" yalnızca o cihazın oturumunu
  kapatır; "tüm oturumları kapat" kullanıcının bütün oturumlarını kapatır.
- Eğitim sorumlusu rol atar/değiştirir, hesabı devre dışı bırakır ve yeniden açar. Rol değişikliği
  ve devre dışı bırakma, kullanıcının **yenileme anahtarlarını anında** geçersiz kılar; elinde olan
  kısa ömürlü access token ise ömrü dolana kadar (≤ 15 dk) çalışmaya devam eder — bu, durumsuz
  (stateless) token doğrulamasının bilinçli olarak kabul edilen sonucudur.
- Devre dışı bir hesap giriş yapamaz.
- Hesap açma yetkisi: eğitim sorumlusu her hesabı açabilir; üye firma temsilcisi **yalnızca kendi
  firmasının** çalışanları için hesap açabilir ve kullanıcı listesinde yalnızca kendi firmasının
  kullanıcılarını görür (BR-8).
- Kullanıcı yönetimi işlemleri yalnızca bu iki role açıktır; diğer roller reddedilir. Üye firma
  temsilcisi yalnızca `Participant` rolü atayabilir; diğer rollerin ataması eğitim sorumlusuna aittir.
- Bir e-posta sistemde yalnızca bir hesaba ait olabilir.
- Şifreler hiçbir biçimde geri okunabilir saklanmaz; loglarda şifre, token ve kişisel veri yer almaz
  (`docs/security.md`, Personal data).
- Token imzalama anahtarı yapılandırmadan gelir; anahtar tanımlı değilse uygulama açılışta açık bir
  hata ile durur, sessizce güvensiz çalışmaz.

## User experience
- **Flow (giriş):** kullanıcı uygulamayı açar → giriş ekranı (e-posta, şifre) → başarılı giriş →
  rolüne göre karşılama sayfası. Hesap geçici şifreyle açılmışsa araya **zorunlu şifre değiştirme**
  ekranı girer ve atlanamaz.
- **Flow (kullanıcı yönetimi):** Admin/firma temsilcisi → Kullanıcılar listesi → "Yeni kullanıcı"
  (ad, e-posta, rol; temsilcide firma otomatik ve değiştirilemez) → kaydet → geçici şifre **bir kez**
  gösterilir → listeye dönüş. Listedeki bir kullanıcıda: rol değiştir · şifre sıfırla · devre dışı
  bırak / yeniden aç.
- **States — Giriş ekranı:**
  - *Loading:* istek sürerken düğme devre dışı ve bekleme göstergesi görünür.
  - *Error (kimlik):* "E-posta veya şifre hatalı." — hangisinin yanlış olduğu belirtilmez.
  - *Error (kilit):* "Hesabınız geçici olarak kilitlendi. Lütfen daha sonra tekrar deneyin."
  - *Error (devre dışı):* "Hesabınız kullanımda değil. Eğitim sorumlusuna başvurun."
  - *Error (bağlantı):* sunucuya ulaşılamazsa Türkçe genel hata + yeniden dene.
  - *Success:* karşılama sayfasına yönlenir.
- **States — Zorunlu şifre değiştirme:** *Loading* · *Error* (mevcut şifre yanlış / yeni şifre
  politikaya uymuyor, hangi kuralın ihlal edildiği yazılır) · *Success* (karşılama sayfası).
- **States — Kullanıcılar listesi:** *Loading* · *Empty* ("Henüz kullanıcı yok") · *Error* ·
  *Success* (liste) · *No permission* (rolü yetmeyen kullanıcı bu sayfayı göremez, yetki mesajı görür).
- **States — Yeni kullanıcı / geçici şifre:** *Error* (e-posta zaten kullanımda; alan bazlı mesaj) ·
  *Success* (geçici şifre bir kez, kopyalanabilir biçimde gösterilir ve "bir daha gösterilmeyecek"
  uyarısı yazar).
- **Design reference:** none — the flow above is the design.
- **Accessibility:** her form alanının görünür etiketi vardır (placeholder etiket yerine geçmez);
  hata mesajları ekran okuyucuya duyurulur (`role="alert"`); şifre alanının göster/gizle düğmesi
  klavyeyle çalışır ve durumu `aria-pressed` ile bildirilir; her sayfada tek `h1`; tüm akış
  klavyeyle tamamlanabilir; metin/zemin kontrastı WCAG AA (≥ 4.5:1).

## Constraints & out of scope
- **Kapsam dışı:** kendi kendine kayıt (self-registration) · iki aşamalı doğrulama (MFA) · e-posta
  veya SMS ile şifre sıfırlama · dış kimlik sağlayıcı / SSO (ADR-0011 revisit konusu) · katılımcı
  profil yönetimi ve üye firma CRUD'u (sonraki spec) · denetim kaydı (audit log) altyapısı.
- Oturum parametreleri: access token **≤ 15 dakika**, yenileme penceresi **7 gün**, hesap kilidi
  **5 başarısız denemeden sonra 15 dakika** (ADR-0011 ve `docs/security.md` ile uyumlu).
- Şifre politikası: en az **10 karakter**; yaygın/sızdırılmış şifre listesi kontrolü kapsam dışı.
- Access token istemcide **yalnızca bellekte** tutulur; `localStorage`/`sessionStorage` kullanılmaz.
  Yenileme anahtarı **`HttpOnly`, `SameSite=Strict`** çerezde tutulur (HTTPS'te `Secure`).
- Giriş denemesi hız sınırı: aynı IP için **dakikada 10** deneme.
- **Kapsam notu (review M-4):** üye firma CRUD'u bu spec'te olmadığı için Admin, yeni bir firma
  çalışanının firmasını seçim listesinden değil **firma kimliğini (UUID) yazarak** belirler. Firma
  seçim listesi üye firma ekranlarıyla birlikte 0004'te gelir.
- Kullanıcı listesinde sayfalama, arama ve sıralama **kapsam dışıdır** (ilk sürümde kullanıcı sayısı
  küçük); liste yetenekleri 0004'te üye firma ve katılımcı listeleriyle birlikte gelir.
- Başlangıç yöneticisi hesabı yapılandırmadan okunan değerlerle tohumlanır; şifresi depoda yer almaz
  ve ilk girişte değiştirilmesi zorunludur.
- Mimari `docs/architecture.md`'ye uyar: kimlik kuralları `Application` katmanında, token üretimi ve
  saklama `Infrastructure`'da; `Api` yalnızca sözleşme döner.

## Acceptance criteria
- [x] AC-1 — Geçerli e-posta ve şifreyle giriş yapan kullanıcı, kendi rolünü taşıyan bir oturum alır
      ve rolünün izin verdiği korumalı bir uca erişebilir.
- [x] AC-2 — Yanlış şifre ile giriş reddedilir; yanıt, olmayan bir e-posta ile yapılan girişin
      yanıtından **ayırt edilemez** (aynı durum kodu ve aynı mesaj).
- [x] AC-3 — 5 ardışık başarısız denemeden sonra hesap kilitlenir; kilit süresince **doğru şifreyle
      de** giriş yapılamaz ve yanıt kilitli olduğunu söyler. Kilitli haldeki ek denemeler kilit
      süresini uzatmaz (ilk kilitten 15 dk sonra hesap açılır).
- [x] AC-4 — Kilit süresi (15 dk) geçtikten sonra doğru şifreyle giriş yeniden mümkündür; başarılı
      giriş başarısız deneme sayacını sıfırlar.
- [x] AC-5 — Devre dışı bırakılmış hesap doğru şifreyle bile giriş yapamaz.
- [x] AC-6 — Geçici şifreyle giriş yapan kullanıcı, şifresini değiştirmeden korumalı uçlara
      erişemez; yanıt, şifre değişikliğinin gerektiğini makine tarafından ayırt edilebilir biçimde
      bildirir.
- [x] AC-7 — Şifre değiştirme mevcut şifrenin doğrulanmasını gerektirir; başarılı değişiklikten
      sonra **eski şifre çalışmaz**, yeni şifre çalışır.
- [x] AC-8 — 10 karakterden kısa yeni şifre reddedilir ve hangi kuralın ihlal edildiği yanıtta yer alır.
- [x] AC-9 — Şifreler geri okunabilir biçimde saklanmaz: veritabanındaki değer düz metin şifreyi
      içermez ve aynı şifre iki kullanıcıda farklı saklanır (tuzlama).
- [x] AC-10 — Access token süresi dolduğunda yenileme anahtarıyla yeni bir access token alınır ve
      kullanıcı yeniden giriş yapmak zorunda kalmaz.
- [x] AC-11 — Her yenilemede **yeni** bir yenileme anahtarı verilir ve kullanılan eski anahtar
      geçersizleşir.
- [x] AC-12 — Kullanılmış bir yenileme anahtarı ikinci kez sunulduğunda istek reddedilir **ve** o
      zincirin en son verilmiş anahtarı da dahil olmak üzere tüm zincir iptal edilir.
- [x] AC-13 — Yenileme penceresi (7 gün) dolduğunda yenileme reddedilir ve yeniden giriş istenir.
- [x] AC-14 — Aynı kullanıcı iki ayrı oturum açabilir; birinden çıkış yapmak **diğerini etkilemez**.
- [x] AC-15 — "Tüm oturumları kapat" sonrasında kullanıcının bütün oturumlarındaki yenileme
      anahtarları reddedilir.
- [x] AC-16 — Kullanıcının rolü değiştirildiğinde yenileme anahtarları **anında** reddedilir; eline
      geçmiş access token ise en geç ömrü kadar (≤ 15 dk) geçerli kalır ve bu bilinçli bir
      kabuldür. Yeniden giriş yapıldığında yeni rol geçerlidir, eski rolle erişilen uç reddedilir.
- [x] AC-17 — Hesap devre dışı bırakıldığında açık oturumlarındaki yenileme anahtarları anında reddedilir.
- [x] AC-18 — Eğitim sorumlusu yeni hesap açtığında geçici şifre yanıtta **bir kez** döner; aynı
      kullanıcı tekrar sorgulandığında şifre hiçbir uçtan okunamaz.
- [x] AC-19 — Üye firma temsilcisi, kendi firması dışında bir firmaya hesap açmayı denediğinde
      reddedilir (negatif test; istek gövdesindeki firma bilgisine güvenilmez).
- [x] AC-20 — Üye firma temsilcisinin kullanıcı listesi **yalnızca** kendi firmasının kullanıcılarını
      içerir; başka firmanın kullanıcısını kimlikle doğrudan sorguladığında reddedilir.
- [x] AC-21 — `Instructor` ve `Participant` rolleri kullanıcı yönetimi uçlarına eriştiğinde `403`
      alır; kimliksiz istek `401` alır ve iki yanıt da `ProblemDetails` + `traceId` taşır.
- [ ] AC-22 — Giriş ekranı dört hata durumunu (kimlik, kilit, devre dışı, bağlantı) ve bekleme
      durumunu ayrı ayrı gösterir; hiçbirinde durum kodu, token veya ham sunucu metni ekranda görünmez.
- [x] AC-23 — Zorunlu şifre değiştirme ekranı atlanamaz: doğrudan başka bir sayfaya gidilmeye
      çalışıldığında kullanıcı yine bu ekrana yönlenir.
- [x] AC-24 — Kullanıcılar listesi boşken boş durum mesajı gösterir (hata görünümü değil); rolü
      yetmeyen kullanıcı bu sayfada yetki mesajı görür.
- [ ] AC-25 — Erişilebilirlik: her form alanının görünür etiketi var, hata mesajları `role="alert"`
      ile duyuruluyor, göster/gizle düğmesi klavyeyle çalışıyor ve `aria-pressed` taşıyor, her
      sayfada tek `h1`, kontrast ≥ 4.5:1.
- [x] AC-26 — Hiçbir log kaydında şifre, geçici şifre, access/refresh token veya kişisel veri
      (ad, e-posta) yer almaz — yalnızca kullanıcı kimliği (UUID) ve `traceId`.
- [x] AC-27 — İmzalama anahtarı yapılandırmada yoksa uygulama açılışta açık bir hata ile durur;
      anahtar depoda hiçbir dosyada bulunmaz.

- [x] AC-28 — Yenileme anahtarı yanıt **gövdesinde yer almaz**; `HttpOnly` ve `SameSite=Strict`
      işaretli çerez olarak verilir, dolayısıyla istemci JavaScript'i tarafından okunamaz.
- [x] AC-29 — Olmayan bir e-posta ile giriş denemesinde de şifre doğrulayıcısı çağrılır (zamanlama
      üzerinden kullanıcı varlığı sızmaz).
- [x] AC-30 — Aynı ağ adresinden dakikada 10'dan fazla giriş denemesi yapıldığında istek `429` ile
      reddedilir; bu sınır hesap kilidinden bağımsızdır.
- [x] AC-31 — Sistemde kayıtlı bir e-postayla ikinci hesap açılamaz: istek `409` ve hangi alanın
      çakıştığını söyleyen bir mesaj döner.
- [x] AC-32 — Üye firma temsilcisi `Participant` dışında bir rol (`FirmRepresentative`, `Instructor`,
      `Admin`) atamayı denediğinde reddedilir (negatif test).

## Definition of Done
- [ ] Every acceptance criterion mapped to proof (test or reproducible observation)
      — 30/32 testle kanıtlı (VERIFY + dar VERIFY, 2026-10-15). AC-22 ve AC-25 yalnızca bileşen
      testine dayanıyor; ekran görüntüleri insan kararıyla PR takip maddesi (plan, VERIFY triage).
- [x] `scripts/check` green
- [x] Independent review done; real findings fixed, noise rejected with written rationale
- [x] Docs / ADRs updated if behavior or architecture changed
- [x] Spec moved to `specs/done/` (it becomes immutable there)

## Scorecard (fill at ship — honest numbers make the process improvable)
| Metric | Value |
|---|---|
| Spec revisions | 1 (review M-4: firma alanı kapsam notu, onaydan sonra) |
| Fix rounds | 3 (review round 1 · round 2 · VERIFY sonrası); round 3 bulguları noise |
| Review findings: real / noise | 24 real (R1: 17 · R2: 5 · VERIFY: AC-4, AC-19) · 2 noise (R3) · 1 investigate (L-7 → ölçüldü, takip) |
| Regressions introduced | 1 (M-2 düzeltmesi giriş sayacında yarış sızıntısı getirdi → R2-M-1'de yakalanıp düzeltildi) |
| Bugs escaped to production | — (henüz üretim yok) |
