# AGENTS.md

`sqldeps`, T-SQL betiklerinden bağımlılık ve LSP bilgisi çıkaran bir .NET aracıdır.

Çalışmaya başlamadan önce `CONTRIBUTING.md` ve `TODO.md` dosyalarını oku. Bu belgelerdeki proje kararlarını ve
mevcut öncelikleri koru. Bir talimat çelişkisi varsa tahminde bulunmak yerine çelişkiyi açıkça belirt.

## Çalışma ilkeleri

- İstenen davranışa odaklan; ilgisiz temizlik, yeniden adlandırma, biçimlendirme veya mimari değişiklik yapma.
- Önce mevcut tasarımı, ilgili çağrı yollarını, testleri ve public contract'ları anla. Mevcut davranışı varsayma; kod ve
  testlerle doğrula.
- Bağımsız doğrulanabilecek en küçük davranış değişikliğini seç. Birbiriyle ilişkili düzenlemeleri tek mantıksal batch
  içinde tamamla.
- Belirsizlikte basit, okunabilir ve mevcut kod tabanıyla tutarlı çözümü tercih et. Yeni bağımlılık, katman veya
  soyutlama eklemek için somut gerekçe ara.
- Kullanıcının değişikliklerini koru; ilgisiz dosyaları geri alma, üzerine yazma veya commit'e dahil etme.
- Kullanıcı açıkça istemedikçe push yapma.

## Kod ararken önce CodeGraph

Bu depoda `.codegraph/` indeksi bulunur. Kaynak kodda bir davranışı, sembolü veya çağrı yolunu ilk kez ararken,
`rg`, `grep`, `find` veya kaynak dosyalarını topluca okumadan önce MCP üzerinden `codegraph_explore` ya da kabukta
`codegraph explore "<sorgu>"` kullan.

- CodeGraph sonucuyla ilgili dosyaları ve sembolleri belirledikten sonra bu dosyaları doğrudan okuyabilir ve dar
  kapsamlı metin araması yapabilirsin.
- Kod taşıma, public contract veya bağımlılık yönü değişikliklerinde düzenleme sonrasında CodeGraph ile çağrı
  yollarını, etkilenen tüketicileri ve kaynak-test eşleşmelerini yeniden kontrol et.
- İndeks eski, eksik veya kullanılamazsa bunu belirt; ardından `rg` gibi araçlarla devam et. CodeGraph çıktısını tek
  doğruluk kaynağı sayma.
- CodeGraph `fixtures/*.sql` dosyalarını indekslemez. SQL fixture'ları için `rg` veya sqldeps kullan. Yapılandırma,
  belge ve diğer indekslenmeyen dosyalar doğrudan okunabilir.

## C# kodlama standartları

`.editorconfig`, desteklediği biçimlendirme, adlandırma ve dil stili kurallarında otoritedir. Mevcut proje hedef
framework'ünü, C# dil sürümünü, nullable ayarlarını ve analyzer yapılandırmasını koru; yalnızca stil tercihi için
değiştirme.

- Okunabilirlik: Açıklayıcı ve alan diline uygun isimler kullan. Gereksiz kısaltmalardan, iç içe koşullardan, yoğun tek
  satırlık ifadelerden ve anlaşılması zor LINQ zincirlerinden kaçın.
- Kontrol akışı: Erken dönüşleri ve açık dallanmayı tercih et. Koşullu bloklarda süslü parantez kullan. switch
  expression veya pattern matching'i yalnızca anlamı netleştiriyorsa kullan.
- Türler: `.editorconfig` kurallarına göre `var` kullan; türün anlaşılmadığı yerlerde açık tür yaz. Nullability
  sözleşmelerini koru; uyarıları `!` ile gelişigüzel bastırma.
- Metotlar ve sınıflar: Tek bir sorumluluğa odaklan. Sırf satır sayısını azaltmak için yardımcı metot çıkarma;
  gereksiz interface, generic, factory veya wrapper oluşturma.
- Bağımlılıklar: Var olan dependency injection yaklaşımını izle. Gizli bağımlılıklardan, service locator kullanımından
  ve gereksiz mutable/static durumdan kaçın.
- Hatalar: Hataları sessizce yutma. Geniş kapsamlı catch blokları, keyfi fallback'ler ve gereksiz null kontrolleri
  ekleme. Hata mesajlarını eyleme geçirilebilir tut; mevcut exception ve logging sözleşmelerini koru.
- Async ve kaynak yönetimi: I/O işlemlerinde mevcut async modelini izle. Senkron bloklamadan (`.Result`, `.Wait()`)
  kaçın. CancellationToken iletimini, IDisposable/`IAsyncDisposable` sahipliğini ve stream yaşam döngüsünü koru.
- Performans: Ölçüm olmadan mikro optimizasyon yapma. Ancak açıkça gereksiz allocation, tekrar enumerasyon veya tüm
  dosyayı belleğe alma gibi sorunları yeni kodda üretme. Büyük SQL girdilerinde mevcut streaming yaklaşımını koru.
- Yorumlar: Kodun ne yaptığını tekrarlamak yerine karmaşık kararların nedenini açıkla. Güncelliğini yitirmiş yorumları
  değişen kodla birlikte düzelt.
- Uyumluluk: Parser çıktısı, dependency graph, LSP konumları/range'leri, diagnostics ve public API davranışlarını
  kasıtsız değiştirme. T-SQL edge case'lerini ve mevcut fixture beklentilerini koru.

Yeni C# özelliklerini yalnızca hedef dil sürümü destekliyorsa ve okunabilirliği artırıyorsa kullan. Mevcut kodu topluca
yeni sözdizimine dönüştürme.

## Geliştirme, test ve refactor akışı

Her mantıksal batch için aşağıdaki sırayı izle:

1. Kapsamı belirle: En küçük bağımsız doğrulanabilir davranış değişikliğini, etkilenen bileşenleri ve ilgili testleri
   tespit et.
2. Uygula ve dar kapsamlı doğrula: Değişiklik sırasında ilgili unit testleri ve uygun `test.sh` smoke bölümünü
   çalıştır. Davranış değişiyorsa mümkün olduğunca önce hatayı gösteren regresyon testi ekle.
3. Etkileri incele: Kod taşıma, public contract veya bağımlılık yönü değiştiyse CodeGraph ile çağrı yollarını ve
   kaynak-test eşleşmelerini yeniden kontrol et.
4. TODO'yu güncelle: Yeni doğrulanmış bulguları ve somut takip işlerini `TODO.md` içine ekle; gerçekten tamamlanan
   maddeleri temizle. Belirsiz veya tekrarlı TODO oluşturma.
5. Tam doğrulama: Batch tamamlandığında `./test.sh` çalıştır. Her küçük düzenlemeden sonra tam suite çalıştırma.
   Başarısız testleri araştır; mevcut ve yeni hataları ayırt et. Çalıştırılamayan kontrolleri ve nedenlerini açıkça
   bildir.
6. Değişiklikleri gözden geçir: `git diff` ve `git status` ile kapsamı, istemsiz değişiklikleri ve test sonuçlarını
   kontrol et.
7. Commit: Yalnızca ilgili dosyaları stage ederek batch'i tek bir mantıksal conventional commit olarak kaydet. Mikro
   commit veya ilgisiz değişiklik oluşturma. Testler başarısızsa veya doğrulama tamamlanmadıysa bunu gizleme; commit
   öncesinde engeli bildir.

Birden fazla bağımsız batch varsa aynı döngüyü her biri için tekrarla. Kullanıcı özellikle commitsiz çalışma istiyorsa
commit adımını atla.

## Öncelikler ve sınırlar

- Performans incelemesine ölçülebilir baseline veya somut aday olmadan yeniden başlama.
- Deneysel özellikleri, ek SQL lehçelerini ve paketleme işlerini mevcut öncelik sırası değişmedikçe sona bırak.
- Testleri geçirtmek için beklenen çıktıları gerekçesiz değiştirme veya doğrulamayı gevşetme.
- Başarılı araç çalışması, test veya commit sonucu uydurma. Son raporda değişiklikleri, çalıştırılan kontrolleri,
  sonuçları ve varsa açık engelleri kısa ve somut biçimde özetle.
