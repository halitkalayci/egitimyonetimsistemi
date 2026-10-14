# Security

## Secrets
- Secrets never enter the repo, specs, prompts, or chat. `.env` is gitignored; provide `.env.example`.
- Agents never print secret values, even when debugging.
- Yerel geliştirmede sırlar `dotnet user-secrets` ile tutulur; CI/üretimde ortam değişkeni veya
  gizli yönetimi (secret store) üzerinden gelir. Bağlantı dizesi, JWT imzalama anahtarı ve SMTP
  bilgisi hiçbir koşulda `appsettings*.json` içinde commit'lenmez.

## Input & output
- Doğrulama **sınırda** yapılır: `Api` katmanında FluentValidation ile her istek DTO'su; geçersiz
  istek `400` + `ProblemDetails`. `Domain` kendi invariant'ını ayrıca korur (çift doğrulama bilinçlidir).
- Beyaz liste yaklaşımı: bilinmeyen alanlar yok sayılmaz, reddedilir; sayfalama ve sıralama
  parametreleri izinli değer kümesiyle sınırlanır (sıralama alanı doğrudan SQL'e geçmez).
- Veri erişimi yalnızca parametreli sorgularla (EF Core / parametreli SQL). Dize birleştirmeyle
  sorgu kurulmaz.
- Dosya yükleme (sertifika eki vb.): tür ve boyut beyaz listesi, özgün dosya adı kullanılmaz,
  içerik uygulama kökünün dışında saklanır.
- Çıktıda kaçış/kodlama Angular'ın varsayılan sanitizasyonuna bırakılır; `innerHTML` ve
  `bypassSecurityTrust*` kullanımı ADR gerektirir.
- Hata mesajları iç detay sızdırmaz (`docs/conventions.md`, Error handling).

## AuthN / AuthZ
- **Kimlik doğrulama:** JWT bearer + refresh token. Access token kısa ömürlüdür (≤ 15 dk) ve
  istemcide **yalnızca bellekte** tutulur — `localStorage`/`sessionStorage` XSS'te okunabilir.
  Refresh token rotasyonla yenilenir; yeniden kullanım tespit edilirse o oturum zincirinin tamamı
  iptal edilir. Token'lar sunucu tarafında iptal edilebilir olmalıdır (çıkış ve rol değişikliği).
- **Roller:** `Admin` (eğitim sorumlusu), `Instructor`, `FirmRepresentative` (üye firma temsilcisi),
  `Participant`. Rol ataması yalnızca `Admin` tarafından yapılır.
- **Default-deny:** global `[Authorize]` fallback politikası; anonim erişim yalnızca açıkça
  `[AllowAnonymous]` ile işaretlenmiş uç noktalarda (giriş, token yenileme). Yeni uç nokta
  yetkisiz açılamaz — yetki testi olmayan uç nokta tamamlanmış sayılmaz.
- **Veri izolasyonu:** `FirmRepresentative` yalnızca kendi üye firmasının çalışanlarını oturumlara
  kaydedebilir (BR-8) ve yalnızca kendi firmasının katılımcı, kayıt ve
  sertifika verisini görür/değiştirir. Kural `Application` katmanında uygulanır (istemci filtresine
  veya uç nokta parametresine güvenilmez) ve her ilgili kullanım senaryosu için **negatif testi**
  vardır. `Participant` yalnızca kendi verisini görür.
- Yetki kararı hiçbir zaman istemciden gelen rol/firma bilgisine dayanmaz; token'daki doğrulanmış
  talep (claim) ile sunucudaki kayıt karşılaştırılır.

## Personal data (KVKK)
- Katılımcı verisi kişisel veridir. Loglara kişisel veri yazılmaz: ad, e-posta, telefon, T.C. kimlik
  numarası log'a girmez — yalnızca kayıt ID'si (UUID) ve `traceId` yazılır.
- Sertifika dosyaları ve katılımcı listeleri yetki kontrolünden geçmeyen bir URL'den servis edilmez
  (tahmin edilemez bağlantı tek başına yetki değildir).
- Veri ihracı (Excel/PDF rapor) rol bazlı yetkiye bağlıdır ve denetim kaydı bırakır.
- Saklama: katılım ve sertifika kayıtları yasal saklama süresi boyunca tutulur. Silme,
  anonimleştirme ve ilgili kişi başvurusu (KVKK m.11) akışı henüz tanımlı değildir — ayrı bir ADR
  ile netleştirilecektir (açık madde).
- Test ve demo ortamlarında gerçek kişisel veri kullanılmaz; üretim yedeği geliştirici makinesine
  indirilmez — veri maskeleme veya üretilmiş veri kullanılır.

## Dependencies
- Yeni bağımlılık eklemek bir **ADR** gerektirir (`docs/decisions/`): ne çözdüğü, lisansı, bakım
  durumu (son sürüm tarihi, açık güvenlik sorunları) ve kaldırma maliyeti yazılır.
- Lisans kısıtı: kapalı kaynak kurumsal kullanımla çelişen lisanslar (örn. ticari lisans gerektiren
  paketler) açık onay olmadan eklenmez — FluentAssertions v8 bu nedenle kullanılmıyor.
- Güvenlik açığı taraması CI'da çalışır: `dotnet list package --vulnerable --include-transitive` ve
  `npm audit --omit=dev`. Ağ bağımlılığı olduğu için `scripts/check` içinde değildir (check
  deterministik ve çevrimdışı kalır).
- Paket kilit dosyaları (`packages.lock.json`, `package-lock.json`) commit'lenir; CI `npm ci` kullanır.

## Review lens
Security is a mandatory dimension of every independent review (see `prompts/review.md`), not a
separate afterthought phase.
