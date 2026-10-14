# ADR 0010 — Clean Architecture katmanları ve modül sınırları

- Status: Accepted
- Date: 2026-10-14

## Context
Sistemde dört net iş alanı var (eğitim/oturum, üyelik/katılımcı, kayıt/katılım, sertifikasyon) ve
bunlar tek veritabanı ve tek işlem sınırında yaşıyor. İş kuralları (BR-1…BR-7) altyapıdan bağımsız
ve hızlı test edilebilir olmalı; ajanlarla çalışırken sınırların "yazılı tavsiye" olarak kalması
yeterli değil, derleyici veya test tarafından zorlanması gerekiyor. Alternatif olarak modül başına
dikey dilim (modüler monolit) değerlendirildi.

## Decision
Dört ayrı .NET projesi: `Domain`, `Application`, `Infrastructure`, `Api`; bağımlılık yönü içe
doğru. İş modülleri bu katmanların içinde aynı isimli klasörler olarak yaşar. `docs/architecture.md`
içindeki 1–5 numaralı yasak bağımlılıklar NetArchTest (1–4) ve ESLint `boundaries` (5) ile
`scripts/check` içinde zorunlu kılınır.

## Consequences
- Kazanç: bağımlılık yönü proje referanslarıyla derleme zamanında garanti; `Domain` testleri
  altyapısız ve milisaniyelik; altyapı değişimi (ör. e-posta sağlayıcısı) tek katmanda kalır;
  sınır ihlali ilk `check` koşusunda kırmızı olur.
- Maliyet: dosya ve dolaylılık sayısı artar (port arayüzü + uygulaması); küçük bir özellik birden
  çok projeye dokunur; modül sınırı katman sınırından daha zayıf kalır — aynı katman içindeki iki
  modül klasörü birbirinin iç tipine teknik olarak erişebilir, bu yüzden 2. ve 5. kurallar
  mimari/lint testleriyle ayrıca denetlenir.
- Modülleri ileride ayrı servise taşımak, dikey dilim mimarisine göre daha fazla iş gerektirir.

## Alternatives considered
- **Modüler monolit (dikey dilim):** özellik başına tek klasör, daha az dolaylılık ve kolay servis
  ayrımı; katmanlı yapının derleyiciyle zorlanan bağımlılık yönü tercih edildiği için seçilmedi.
- **Tek projeli basit katmanlı yapı:** en hızlı başlangıç; üç iş alanı büyüdükçe birbirine karışma
  riski ve test edilebilir sınır yokluğu nedeniyle reddedildi.
- **Mikroservisler:** tek veritabanı/işlem sınırı ihtiyacı ve solo ekip karşısında dağıtık
  tutarlılık ve işletme maliyeti gerekçesiz bulundu.

## Revisit triggers
- Katmanlar arası dolaylılık, basit değişikliklerin maliyetini sürekli artırıyorsa.
- Bir modül bağımsız ölçeklenme veya ayrı sürüm döngüsü gerektirirse.
- Mimari testler sürekli "aşılması gereken" bir engel olarak yaşanıyorsa (kuralın kendisi yanlış olabilir).
