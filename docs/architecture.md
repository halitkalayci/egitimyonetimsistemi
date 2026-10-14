# Architecture

## System overview
Kurum içi eğitim ve sertifikasyon yönetim sistemi: eğitim tanımları, oturum takvimi, katılımcı
kaydı, katılım işleme ve sertifika üretimi/geçerlilik takibi. Katılımcılar hem kurum personeli
hem de üye firma çalışanlarıdır. Mimari stil: **tek dağıtım birimli (monolit) Clean Architecture**
— `Domain`, `Application`, `Infrastructure`, `Api` ayrı .NET projeleri; istemci ayrı bir Angular 21
SPA; veri deposu tek bir PostgreSQL veritabanı, tek işlem (transaction) sınırı. Gerekçe: bağımlılık
yönü derleyici tarafından zorlanır, iş kuralları altyapıdan bağımsız test edilir ve bu boyutta
dağıtık bir topoloji yalnızca maliyet üretir (ADR-0010; yığın kararı ADR-0009).

## Layers and dependency direction
Bağımlılık her zaman içe doğrudur: `Api → Application → Domain`, `Infrastructure → Application`.

| Katman | Tek sorumluluk | İçermez |
|---|---|---|
| `Domain` | İş kuralları (BR-n), varlıklar, değer nesneleri, invariant'lar | Hiçbir proje ve hiçbir altyapı paketi referansı |
| `Application` | Kullanım senaryoları (komut/sorgu), port arayüzleri, yetki kuralları | `DbContext`, `HttpContext`, dosya sistemi, HTTP istemcisi |
| `Infrastructure` | Port'ların uygulanması: EF Core kalıcılık, PostgreSQL, e-posta, PDF, saat | `Api` referansı |
| `Api` | HTTP sınırı: uç noktalar, DTO/contract, kimlik doğrulama, ProblemDetails | İş kuralı, EF entity'si dışa verme |

## Modules / components and ownership
İş modülleri bu katmanların içinde aynı isimli klasörler olarak yaşar (`Application/Training/...`).

| Module | Single responsibility | Owns |
|---|---|---|
| `Training` | Eğitim tanımı ve oturum takvimi | Eğitim, Oturum, eğitmen ataması, kontenjan |
| `Members` | Üye firma ve katılımcı kimliği/profili | Üye Firma, Katılımcı, firma üyelik durumu |
| `Enrollment` | Kayıt ve katılım yaşam döngüsü (BR-1…BR-3) | Kayıt, Bekleme Listesi, Katılım |
| `Certification` | Sertifikasyon tanımı, sertifika üretimi/iptali/yenilemesi (BR-4…BR-7) | Sertifikasyon (katılım eşiği, geçerlilik), Sertifika, numara sekansı, iptal gerekçesi |
| `Identity` | Kimlik doğrulama, rol/yetki, token yaşam döngüsü | Kullanıcı, Rol, refresh token zinciri |

## Communication rules
- **Katmanlar arası:** yalnızca yukarıdaki bağımlılık yönü. `Infrastructure`, `Application`'daki
  port arayüzlerini uygular; `Application` somut altyapıyı asla bilmez.
- **Modüller arası:** aynı süreçte doğrudan çağrı serbesttir, **ama yalnızca** diğer modülün
  `Application` katmanındaki public contract'ı (komut/sorgu arayüzü + DTO) üzerinden. Başka modülün
  entity'si, `DbContext` tipi veya `Infrastructure` sınıfı kullanılamaz.
- **İşlem sınırı:** bir kullanıcı eylemi tek Unit of Work içinde tamamlanır. Modüller arası
  olay/mesajlaşma şimdilik yoktur; ihtiyaç doğarsa yeni bir ADR ile gelir.
- **İstemci → sunucu:** yalnızca HTTP/JSON. Angular feature'ları birbirini çağırmaz; paylaşım
  `core/` (altyapı, interceptor, guard) ve `shared/` (sunum bileşenleri) üzerinden olur.

## Forbidden dependencies (make them testable)
1. `Domain` hiçbir projeye ve hiçbir altyapı paketine referans vermez (EF Core, ASP.NET, MediatR dahil).
2. `Application` yalnızca `Domain`'e referans verir; `DbContext`, `HttpContext`, dosya sistemi ve
   HTTP istemcisi kullanmaz — yalnızca port arayüzleri.
3. `Infrastructure` → `Application` + `Domain`; `Api`'ye asla referans vermez.
4. `Api` hiçbir uç noktada EF entity'si döndürmez/kabul etmez; her zaman DTO/contract.
5. Angular feature klasörleri birbirinin iç dosyalarını import etmez; paylaşım `core/` ve `shared/` üzerinden.

**Nasıl zorunlu kılınır:** 1–4 mimari testlerle (`tests/Architecture.Tests`, NetArchTest), 5 ESLint
`boundaries` kuralıyla. Her ikisi de `scripts/check` adımıdır — ihlal kırmızıdır, tartışma konusu değildir.

## Deliberately out of scope
Mikroservis ayrımı, mesaj kuyruğu, çoklu veritabanı · video/içerik barındırma (bu bir LMS değil) ·
değerlendirme/sınav ve not yönetimi (sertifika yalnızca katılıma bağlıdır, BR-4) · e-posta/SMS
bildirimi (ilk sürümde yalnızca uygulama içi uyarı listesi, BR-7) · ödeme ve faturalama · mobil
uygulama · çok dilli arayüz (ilk sürüm Türkçe) · dış kimlik
sağlayıcıyla federasyon/SSO (ADR-0011 revisit tetikleyicisi).
