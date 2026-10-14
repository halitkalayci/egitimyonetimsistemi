# ADR 0009 — Yığın: .NET 10 Web API + Angular 21 + PostgreSQL

- Status: Accepted
- Date: 2026-10-14

## Context
Sıfırdan bir eğitim ve sertifikasyon yönetim sistemi kuruyoruz: form ve liste yoğun, kurum içi
kullanıcı sayısı sınırlı, ama üye firma çalışanlarına da açılacağı için dışa dönük bir yüzü var.
Ekip solo (+ AI ajanları), yani seçilen yığının bakım yükü doğrudan tek kişinin üzerine biner.
Tek bir doğrulama komutu (`scripts/check`) şart: her build/test adımının kaynak hijyeni
(ADR-0007) ile uyumlu, deterministik ve çevrimdışı çalışabilir olması gerekiyor.

## Decision
Backend .NET 10 (ASP.NET Core Web API), istemci ayrı bir Angular 21 SPA, veri deposu PostgreSQL 17
(EF Core 10 / Npgsql). Node 24 LTS. Sürüm yükseltmeleri yeni bir ADR gerektirir.

## Consequences
- Kazanç: güçlü tipli uçtan uca geliştirme; EF Core migration'ları ile şema evrimi izlenebilir;
  PostgreSQL lisans maliyeti yok ve `timestamptz`/`numeric`/kısıt davranışı veri kurallarımıza
  (UTC, `decimal`, tekillik) doğal oturuyor; Testcontainers ile gerçek veritabanına karşı test.
- Maliyet: iki ayrı araç zinciri (dotnet + npm) demek — `scripts/check` iki kat adım, iki kat
  kaynak hijyeni envanteri, iki kat bağımlılık güncellemesi. SPA + API ayrımı, sunucu tarafı
  render'a kıyasla kimlik doğrulama ve hata yönetiminde fazladan iş getiriyor (bkz. ADR-0011).
- Docker gereksinimi: entegrasyon testleri Testcontainers kullandığı için Docker olmadan
  `scripts/check` tam çalışmaz.

## Alternatives considered
- **Blazor Server (tek uygulama):** tek deploy, tek dil, kimlik doğrulama kutudan çıkıyordu;
  bileşen ekosistemi ve SPA deneyimi tercihi nedeniyle reddedildi.
- **Razor Pages / MVC:** en düşük araç yükü, ancak zengin etkileşimli ekranlarda (takvim, kontenjan,
  bekleme listesi) elle JS yazmayı gerektirir.
- **SQL Server:** ekip aşinalığı yüksek; lisans maliyeti ve PostgreSQL'in bu veri kuralları için
  yeterliliği nedeniyle seçilmedi.

## Revisit triggers
- `scripts/check` süresi kabul edilemez hale gelirse (iki araç zinciri yükü).
- Mobil istemci veya üçüncü taraf entegrasyonu birinci sınıf gereksinim olursa.
- Docker'ın geliştirme/CI ortamında sürdürülemez olduğu görülürse (test veritabanı stratejisi).
