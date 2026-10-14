# ADR 0011 — Kimlik doğrulama: JWT bearer + refresh token rotasyonu

- Status: Accepted
- Date: 2026-10-14

## Context
İstemci ayrı bir Angular SPA (ADR-0009). Kullanıcılar kurum personeli, eğitmenler, üye firma
temsilcileri ve katılımcılar; üye firma temsilcisinin verisi firma bazında izole edilmek zorunda
(`docs/security.md`). İleride mobil veya üçüncü taraf istemci olasılığı var. Seçenekler: aynı
origin'de cookie tabanlı oturum, JWT bearer + refresh, veya harici kimlik sağlayıcı (OIDC).

## Decision
JWT bearer + refresh token. Access token ≤ 15 dakika ömürlü ve istemcide yalnızca bellekte
tutulur; refresh token rotasyonla yenilenir ve yeniden kullanım tespitinde oturum zincirinin
tamamı iptal edilir. Yetkilendirme default-deny'dir: global `[Authorize]` fallback politikası,
anonim erişim yalnızca açıkça işaretlenmiş uç noktalarda.

## Consequences
- Kazanç: istemciden bağımsız API (mobil/üçüncü taraf eklenebilir); CORS ve cookie `SameSite`
  kısıtlarına bağlı kalmadan ayrı dağıtım; token içindeki doğrulanmış talepler (claim) ile
  yetkilendirme sade.
- Maliyet: token saklama sorumluluğu istemcide — bellekte tutma sayfa yenilemede oturumun refresh
  akışıyla kurulmasını gerektirir; refresh token'ın güvenli saklanması, rotasyon ve iptal listesi
  (çıkış, rol değişikliği) ek altyapı demektir. Cookie tabanlı çözümün XSS'te token çalınamaması
  avantajından vazgeçiyoruz; bu nedenle XSS önlemleri (Angular sanitizasyonu, `innerHTML` yasağı)
  pazarlık konusu değildir.
- Her uç noktanın yetki testi zorunludur; yetki testi olmayan uç nokta tamamlanmış sayılmaz.

## Alternatives considered
- **Aynı origin cookie tabanlı oturum:** token JS'e hiç düşmediği için XSS'e karşı daha dayanıklıydı
  ve CORS gerektirmiyordu; API'nin istemciden bağımsız kalması tercih edildiği için seçilmedi.
- **Harici kimlik sağlayıcı (Entra ID / Keycloak, OIDC):** kurumsal SSO ve üye firma federasyonu
  için en güçlü seçenek; ilk sürümde dış altyapı bağımlılığı ve kurulum maliyeti nedeniyle
  ertelendi — kapsam dışı olarak `docs/architecture.md`'de kayıtlı.

## Revisit triggers
- Üye firmalar kendi kimlik sağlayıcılarıyla giriş (federasyon/SSO) isterse → OIDC'ye geçiş ADR'si.
- Refresh token rotasyonu/iptal listesi beklenenden karmaşık gelirse veya oturum kaçırma olayı
  yaşanırsa → cookie tabanlı BFF deseni yeniden değerlendirilir.
