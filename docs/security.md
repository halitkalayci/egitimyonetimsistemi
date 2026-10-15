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
- **Şifre saklama:** PBKDF2-HMAC-SHA256, 210.000 iterasyon, 128 bit tuz, sürümlenmiş biçim
  (`v1$iter$salt$hash`), sabit zamanlı karşılaştırma (ADR-0012). Şifre politikası: **en az 10
  karakter**. Yeni hesap tek kullanımlık geçici şifreyle açılır ve ilk girişte değiştirilmesi zorunludur.
- **Kaba kuvvet savunması iki katmanlı:** hesap başına **5 başarısız denemeden sonra 15 dakika**
  kilit (kilitli haldeki denemeler süreyi uzatmaz) **ve** ağ adresi başına **dakikada 10 giriş
  denemesi** sınırı (`429`). İkinci katman, bir hesabı kasten kilitleyerek hizmet engellemeyi önler.
- **Ters vekil arkasında istemci adresi:** hız sınırı bağlantının uzak adresine göre bölümlenir. Bir
  ters vekil (reverse proxy) arkasında bu adres vekilin kendisidir; `ForwardedHeaders` ara katmanı
  yalnızca güvenilen vekil(ler) için açılmadan dağıtılırsa **tüm istemciler tek adreste toplanır** ve
  sistem genelinde dakikada 10 giriş sınırı oluşur. Yalnızca bilinen vekil adreslerine güvenilir;
  `X-Forwarded-For` başlığı herkesten kabul edilirse saldırgan her denemede adres değiştirerek
  sınırı aşar.
- **Oturum saklama:** access token istemcide yalnızca bellekte; yenileme anahtarı `HttpOnly`,
  `SameSite=Strict` (HTTPS'te `Secure`), `/api/auth` yoluna kapsanmış çerezde. `SameSite=Strict`
  gereği istemci ve API **aynı origin'den** servis edilmelidir (geliştirmede dev-server proxy'si,
  üretimde aynı origin veya ters vekil).
- **Rol/durum değişikliğinin etkisi:** yenileme anahtarları **anında** geçersizleşir; eldeki access
  token ömrü (≤ 15 dk) kadar geçerli kalır — durumsuz doğrulamanın bilinçli olarak kabul edilen sonucu.
- **Roller:** `Admin` (eğitim sorumlusu), `Instructor`, `FirmRepresentative` (üye firma temsilcisi),
  `Participant`. `Admin` her rolü atayabilir; `FirmRepresentative` yalnızca kendi firmasındaki
  `Participant` hesaplarını yönetir ve yalnızca `Participant` rolü atayabilir. Diğer roller kullanıcı
  yönetimine erişemez. `Admin` ve `Instructor` hiçbir üye firmaya bağlı olmaz (domain değişmezi).
  Kimse kendi hesabını devre dışı bırakamaz veya kendi rolünü değiştiremez; son etkin `Admin`
  hesabı kapatılamaz.
- **Default-deny:** global `[Authorize]` fallback politikası; anonim erişim yalnızca açıkça
  `[AllowAnonymous]` ile işaretlenmiş uç noktalarda (giriş, token yenileme). Yeni uç nokta
  yetkisiz açılamaz — yetki testi olmayan uç nokta tamamlanmış sayılmaz.
- **Veri izolasyonu:** `FirmRepresentative` yalnızca kendi üye firmasının çalışanlarını oturumlara
  kaydedebilir (BR-8) ve yalnızca kendi firmasının katılımcı, kayıt ve
  sertifika verisini görür/değiştirir. Kural `Application` katmanında uygulanır (istemci filtresine
  veya uç nokta parametresine güvenilmez) ve her ilgili kullanım senaryosu için **negatif testi**
  vardır. `Participant` yalnızca kendi verisini görür.
- Yetki kararı hiçbir zaman istemciden gelen rol/firma bilgisine dayanmaz; imzası doğrulanmış
  access token'daki taleplerden (rol, firma) verilir ve her istekte veritabanından yeniden okunmaz.
  Sunucudaki kayıtla karşılaştırma **yenilemede** yapılır: hesabın oturum sürümü değişmişse (rol,
  durum, şifre değişikliği, tüm oturumları kapatma) yenileme reddedilir. Bu yüzden eldeki access
  token ömrü (≤ 15 dk) kadar eski yetkiyle çalışabilir — yukarıdaki bilinçli kabulün aynısı.
  İstek gövdesindeki firma bilgisi firma temsilcisi için yok sayılır ve çağıranın kendi firması
  kullanılır.
- Giriş yanıtı kullanıcının var olup olmadığını sızdırmaz: olmayan e-posta ile yanlış şifre aynı
  yanıtı alır ve kullanıcı bulunamadığında da şifre doğrulama maliyeti ödenir. Devre dışı bir
  hesap da yanlış şifreyle denendiğinde aynı yanıtı alır; "hesabınız kullanımda değil" mesajı
  yalnızca doğru şifreyi bilene gösterilir.
- **Bilinçli kabul — kilit yanıtı:** kilitli hesap, şifre doğru da olsa yanlış da olsa "kilitli"
  yanıtını verir; bu, 5 yanlış denemeden sonra e-postanın var olduğunu gösterir. Kilit süresince
  şifreye göre farklı yanıt vermek, kilidi atlayan bir şifre doğrulama kâhini yaratacağı için
  tercih edilmedi. Sızıntının maliyeti hesap başına 5 deneme ve ağ adresi başına dakikada 10 deneme
  sınırıdır.

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
