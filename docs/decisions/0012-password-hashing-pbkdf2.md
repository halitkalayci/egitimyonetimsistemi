# ADR 0012 — Şifre saklama: BCL PBKDF2

- Status: Accepted
- Date: 2026-10-14

## Context
Spec 0002 sisteme giriş getiriyor ve şifrelerin saklanması gerekiyor. `docs/security.md` iki kısıt
koyuyor: şifreler geri okunabilir biçimde saklanamaz (AC-9) ve **yeni bir bağımlılık eklemek ADR
gerektirir**. Yaygın seçenekler: ASP.NET Core Identity'nin `PasswordHasher<T>`'ı, Argon2 veya
bcrypt için üçüncü taraf paketler, ya da .NET'in kendi kütüphanesindeki PBKDF2. Sistemde kullanıcı
deposu, giriş akışı ve arayüzü kendimiz yazıyoruz; yani Identity paketinin getirdiği şema, ara
katman ve kullanıcı yönetimi altyapısının karşılığı yok.

## Decision
Şifreler .NET'in kendi kütüphanesindeki **PBKDF2-HMAC-SHA256** ile saklanıyor
(`Rfc2898DeriveBytes.Pbkdf2`): 210.000 iterasyon, 128 bit rastgele tuz, 256 bit türetilmiş anahtar.
Saklanan değer kendi parametrelerini taşıyor: `v1$<iterasyon>$<base64 tuz>$<base64 hash>`.
Doğrulama sabit zamanlı karşılaştırma (`CryptographicOperations.FixedTimeEquals`) kullanıyor.

## Consequences
- Kazanç: hiçbir yeni paket yok (ADR zorunluluğu bir kez bu kararla karşılanıyor, her yükseltmede
  tekrar gündeme gelmiyor); biçim sürümlendiği için iş faktörü ileride artırıldığında eski hash'ler
  doğrulanmaya devam eder ve kullanıcılar ilk girişlerinde sessizce yükseltilebilir; algoritma
  FIPS onaylı ve platformla birlikte bakım görüyor.
- Maliyet: PBKDF2, Argon2id'ye kıyasla GPU/ASIC paralelliğine daha açıktır — aynı güvenlik için
  daha yüksek iterasyon gerekir ve bu da her girişte ~100 ms CPU demektir. Giriş hız sınırı
  (dakikada 10 deneme) ve hesap kilidi bu maliyeti saldırgan tarafında da sınırlıyor.
- İterasyon sayısı koda gömülü: artırmak bir kod değişikliği ve yeni bir hash sürümü gerektirir.

## Alternatives considered
- **ASP.NET Core Identity `PasswordHasher<T>`:** iyi test edilmiş ve sürümlenmiş bir biçim sunuyor;
  tüm Identity paketini (kendi kullanıcı/rol şeması ve yönetim altyapısıyla) getirdiği ve bizim
  kendi kişi kaydımız olduğu için reddedildi.
- **Argon2id (üçüncü taraf paket):** bellek-zorlu olduğu için en güçlü seçenek; yeni bir bağımlılık
  ve bakım yükü getirdiği, yerel kütüphanelere bağlı olabildiği için bu sürümde seçilmedi.
- **bcrypt (üçüncü taraf paket):** yaygın ve iyi; 72 bayt girdi sınırı ve yeni bağımlılık maliyeti
  karşısında PBKDF2'nin platform desteği tercih edildi.

## Revisit triggers
- Şifre doğrulama süresi giriş akışında hissedilir gecikmeye yol açarsa (iterasyon ↔ UX dengesi).
- Kurumsal bir güvenlik gereksinimi Argon2id veya donanım destekli bir çözüm şart koşarsa.
- Dış kimlik sağlayıcıya (OIDC/SSO) geçilirse: bu durumda şifre saklama tamamen ortadan kalkar
  (ADR-0011 revisit tetikleyicisi).
