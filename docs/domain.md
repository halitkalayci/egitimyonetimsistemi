# Domain

## Ubiquitous language
Dokümanlar ve konuşma Türkçe, kod İngilizce (`docs/conventions.md`, Language). Eşleşme tablosu
bağlayıcıdır: bir terim koda girerken yalnızca bu tablodaki tipe dönüşür.

| Term | Meaning | Notes / not to be confused with |
|---|---|---|
| Üye Firma (`MemberFirm`) | Birliğe üye tüzel kişi; çalışanları eğitimlere katılabilir | Üyeliği aktif/pasif olabilir (BR-6). Katılımcı değildir, katılımcının bağlı olduğu kurumdur |
| Katılımcı (`Participant`) | Eğitime kaydolan kişi: kurum personeli **veya** üye firma çalışanı | "Öğrenci" demiyoruz. Kullanıcı (`User`) ile aynı şey değil: katılımcının sistem hesabı olmayabilir |
| Eğitmen (`Instructor`) | Bir oturumu veren kişi | Eğitimin değil, **oturumun** özelliğidir |
| Eğitim (`Training`) | Tekrarlanabilir eğitim tanımı: müfredat, süre, ön koşul | Takvimdeki tek bir olay değildir. Kayıt doğrudan Eğitime yapılmaz |
| Oturum (`Session`) | Eğitimin belirli tarih/yer/eğitmenle somut örneği; toplam süresi (saat) bellidir | Kayıt, kontenjan ve katılım **oturuma** bağlıdır |
| Kayıt (`Enrollment`) | Bir katılımcının bir oturuma kaydı ve durumu | Durumlar: `Confirmed`, `Waitlisted`, `Cancelled`. Katılımla aynı şey değil |
| Katılım (`Attendance`) | Oturum gerçekleştikten sonra fiilî katılım: katılınan sürenin oturum süresine **oranı** (0–100) | Durum bu orandan türetilir: `Attended` (≥ katılım eşiği), `PartiallyAttended` (0 < oran < eşik), `NoShow` (0). Kaydın varlığı katılım anlamına gelmez |
| Katılım Eşiği (`AttendanceThreshold`) | Sertifikaya hak kazanmak için gereken asgari katılım oranı; sertifikasyon tanımında tutulur, **varsayılan %70** | Kontenjanla ilgisi yoktur. Sınav/değerlendirme notu değildir — sistemde değerlendirme kavramı yok |
| Sertifikasyon (`Certification`) | Sertifikayı üreten şablon/kural: katılım eşiği, geçerlilik süresi, yenileme uyarı eşiği | Sertifikanın kendisi değil, onu tanımlayan kural |
| Sertifika (`Certificate`) | Bir katılımcının bir eğitimi başarıyla tamamladığına dair, numaralı ve (varsa) geçerlilik süreli belge | Numarası tekildir ve değişmez; silinmez, **iptal** edilir (BR-5) |
| Sertifika Numarası (`CertificateNumber`) | `IMM-<yıl>-<6 haneli sıra>` biçiminde, veritabanı sekansından üretilen tekil iş değeri | Birincil anahtar (UUID v7) değildir ve ondan türetilmez |
| Kontenjan (`Capacity`) | Bir oturumun kabul ettiği onaylı kayıt üst sınırı | Fiziksel salon kapasitesiyle aynı olmak zorunda değil |
| Bekleme Listesi (`Waitlist`) | Kontenjan dolu olduğunda kayıtların girdiği sıralı kuyruk | Kayıt durumudur (`Waitlisted`), ayrı bir varlık gibi konuşulmaz |
| Üye Firma Temsilcisi (`FirmRepresentative`) | Kendi firmasının çalışanlarını oturumlara **kaydeden** ve onların kayıt/katılım/sertifika verisini gören kullanıcı rolü | Yalnızca kendi firmasının verisi — istisnası yok (`docs/security.md`) |

## Business rules
- **BR-1:** Kontenjanı dolu bir oturuma yapılan kayıt `Waitlisted` olur; bir oturumun `Confirmed`
  kayıt sayısı kontenjanı hiçbir koşulda aşamaz.
- **BR-2:** Aynı katılımcı aynı oturuma ikinci kez kaydolamaz (iptal edilmiş kayıt yeniden kayda engel değildir).
- **BR-3:** Tamamlanmış bir oturumun kayıtları değiştirilemez (ekleme, iptal, katılımcı değişikliği yok).
- **BR-4:** Sertifika, katılımcının **katılım oranı sertifikasyonun katılım eşiğine (varsayılan %70)
  eşit veya üstündeyse** üretilir. Ayrı bir sınav/değerlendirme koşulu yoktur: katılım yeterlidir.
- **BR-5:** Sertifika numarası `IMM-<yıl>-<6 haneli sıra>` biçiminde üretilir, tekil ve
  değiştirilemezdir. Sertifika silinmez; gerekçe kaydıyla **iptal** edilir ve iptal, numarayı
  yeniden kullanıma açmaz.
- **BR-6:** Üye firma çalışanı, firmasının üyeliği aktif olduğu sürece kaydolabilir. Üyelik pasife
  düşerse mevcut kayıtlar korunur, yeni kayıt yapılamaz.
- **BR-7:** Geçerlilik süreli bir sertifika, bitiş tarihinden sertifikasyon tanımındaki uyarı eşiği
  (X gün) kadar önce "yenilenmeli" durumuna geçer; bitiş tarihinden sonra geçersizdir. Uyarı ilk
  sürümde yalnızca uygulama içindeki uyarı listesinde görünür (e-posta/SMS kapsam dışı).
- **BR-8:** Üye firma temsilcisi, yalnızca kendi firmasının çalışanlarını kaydedebilir ve yalnızca
  onların kayıt/katılım/sertifika verisini görebilir.

Her BR-n en az bir testle eşleşir (`docs/testing.md`, What must be tested).

## Key domain invariants
- Onaylı kayıt sayısı ≤ kontenjan — her zaman, hangi yoldan kayıt gelirse gelsin (BR-1).
- Bir katılımcının bir oturumda en fazla bir aktif kaydı vardır (BR-2).
- Katılım oranı her zaman 0–100 aralığındadır; katılım kaydı var olan ve tamamlanmış bir oturuma
  bağlıdır — oturum olmadan katılım olmaz.
- Katılım eşiğinin altındaki bir katılımla sertifika oluşmaz (BR-4).
- Her sertifika tam olarak bir katılımcıya ve bir eğitime bağlıdır; aynı eğitim için aynı anda
  birden fazla **geçerli** sertifikası olamaz.
- Sertifika numarası üretildikten sonra hiçbir işlemle değişmez; iptal, numarayı serbest bırakmaz.
- Üye firma temsilcisinin eriştiği veya oluşturduğu her kayıt kendi firmasına aittir (BR-8).
