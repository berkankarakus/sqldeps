# LSP tanı yol haritası

Bu liste, `sqldeps lsp` için gerçek veritabanına bağlanmadan üretilebilecek T-SQL
tanılarını önceliklendirir. Bir tanı yalnızca eldeki indeks/sorgu bağlamı yeterliyse
`error` olmalıdır; indeks eksik veya eski olabiliyorsa `warning`/`information` olarak
sunulmalıdır.

## Durum özeti

### Tamamlanan son feature’lar

- [x] Filtered-index predicate’lerini sorgu koşullarıyla eşleştirme.
- [x] `NULL` değerlerin `NOT NULL` hedeflere aktarımını raporlama.
- [x] JOIN tür ailelerindeki kesin uyumsuzlukları raporlama.
- [x] Aggregate dışı iç içe ifadelerde `GROUP BY` doğrulaması.
- [x] Sabit true/false UPDATE/DELETE filtrelerini raporlama.
- [x] Kullanılmayan procedure `OUTPUT` parametrelerini raporlama.
- [x] Adı bilinmeyen result-set ifadelerini ordinal ile koruma.
- [x] CLI `lint --fail-on` ile CI çıkış kodu eşiği.
- [x] CLI `indexes --json` ile makine-okunabilir indeks listesi.
- [x] CLI `doctor --json` ile indeks sağlık durumunun otomasyon çıktısı.
- [x] Komut sonrasında `--help`/`-h` kullanımı.

### Aktif feature kuyruğu

- [x] Filtered-index collation ve Unicode/tür uyumluluğunun güvenli alt kümesini genişlet.
- [ ] Güvenli quick fix editlerini ilgili aralıkla sınırlayıp değişiklik sonrası kısmi yeniden parse et.

### Ertelenen entegrasyonlar

- [ ] `sqldeps mcp` sunucusu ekle; aktif indeks, nesne arama, bağımlılık ve tanı sorgularını
  MCP araçları olarak güvenli ve makine-okunabilir biçimde sun.
- [ ] Neovim canlı veritabanı bağlantısına karar ver: sqldeps LSP’sinden ayrı bir katmanda
  `vim-dadbod`/`sqlcmd` gibi bir istemci, salt-okunur kimlik, TLS sertifika doğrulaması ve
  repo dışı credential yönetimi değerlendirilsin. sqldeps’in statik analiz sınırı korunmalı.

### Kalan işler — öncelik sırası

Bu sıra MCP, performans ölçümleri ve T-SQL dışı lehçe sağlayıcılarını kapsam dışı bırakır.

1. **P1 — Mimari sınırlar:** `SqlDeps.Core` sınırını ve CLI çıktı katmanını sadeleştir.
2. **P2 — Test/spec mimarisi:** yeni davranışlarda senaryo ailelerini ve normalize contract snapshot’larını koru.
3. **P2 — Kod üretimi:** güvenli editlerin kısmi yeniden parse ve kapsam doğrulamasını tamamla.

### Ertelenen doğruluk çalışmaları

- [ ] local-corpus içindeki tüm tespitleri kural bazında incele; FP/FN örneklerini çıkar,
  regression senaryolarını ve düzeltme önceliklerini güncelle. Kısmi yeniden parse ve codegraph
  refactor adayları tamamlandıktan sonra yeniden ele alınacak.

Tamamlanan fakat eski üst başlıkları açık görünen maddeler aşağıdaki refactor/model bölümlerinde `[x]` olarak
işaretlendi; aynı davranış için tekrar feature açılmayacak.

### Feature uygulama alt görevleri

- [x] Result-set tip uyumu:
  - [x] `CAST`/`CONVERT` ile açıkça bilinen result-set tiplerini çıkar.
  - [x] Hedef tablo metadata’sıyla yalnızca kesin uyumsuz tip ailelerini karşılaştır.
  - [x] Belirsiz implicit conversion durumlarında tanı üretme.
  - [x] `INSERT ... EXEC` ve `INSERT ... SELECT` için ayrı regression corpus ekle.
- [x] NULLability:
  - [x] `VALUES (NULL)` ve `SELECT NULL` → `NOT NULL` hedef tanısı.
  - [x] Nullable kaynak kolonun `NOT NULL` hedefe aktarımında kesin kolon akışını raporla.
  - [x] Default/identity/computed kolonlarda false-positive senaryolarını test et.
- [x] JOIN tür/collation:
  - [x] Açık tür ailesi uyumsuzluklarını raporla.
  - [x] Collation metadata’sını normalize ederek yalnızca kesin uyuşmazlıkları raporla.
  - [x] Numeric widening için uyumlu alt kümeyi koru.
  - [x] Güvenli DATE/DATETIME dönüşüm alt kümesini koru.
  - [x] Diğer tarih/tür dönüşümlerini genişletmeden önce ayrı FP/FN corpus’u oluştur.
  - [x] JOIN, WHERE ve APPLY akışlarını birbirinden ayıran testler ekle.
- [x] DML filtre güvenliği:
  - [x] Tamamen sabit true/false filtreleri raporla.
  - [x] `WHERE 1 = 1 AND ...` gibi etkisiz sabit parçaları düşük güvenle raporla.
  - [x] Değişken, parametre ve nondeterministic fonksiyonlarda tanıyı bastır.
  - [x] UPDATE/DELETE/MERGE için ortak koşul değerlendirme testleri ekle.
- [x] Procedure sözleşmesi:
  - [x] Kullanılmayan `OUTPUT` parametrelerini raporla.
  - [x] Parametrelerin kesin literal tür uyumsuzluklarını sınırlı alt kümeyle yakala.
  - [x] Named/positional/output bağlama sonucunu ortak model olarak üret.
  - [x] Default ve nullable parametrelerle false-positive testleri ekle.
- [x] Güvenli quick fix’ler:
  - [x] Düşük confidence tanılarında metin değiştiren quick fix’leri bastır.
  - [x] Edit aralığı belge sınırları dışına taşıyorsa quick fix üretme.
  - [x] Edit sonrası yalnızca ilgili batch’i yeniden parse et; daha dar aralık parse’ı sonraki optimizasyon
    adımı olarak korunuyor.
  - [x] Üretilen editin diff kapsamı dışına taşmasını ayrıca reddet.
  - [x] Fix başına açıklama, kaynak tanı ve confidence bilgisini döndür.
- [x] T-SQL kapsam genişletmesi:
  - [x] Inline table-valued function kaynaklarını alias/kolon çözümleme modeline bağla; `APPLY` için temel derived-table akışını koru.
  - [x] `PIVOT`/`UNPIVOT` açıkça üretilen kolonlarını yerel kapsamda tanıt.
  - [x] Inline table-valued function result kolonlarını güvenli alt kümeyle çıkar.
  - [x] `OPENJSON ... WITH (...)` açık kolonlarını LSP kapsamına çıkar.
  - [x] Multi-statement table-valued function result kolonlarını çıkar.
  - [x] Her özellik için parse edilemeyen ve kısmi metadata testleri ekle.
- [x] İleri LSP:
  - [x] Procedure/function signature help için parametre imzası üret.
  - [x] Diagnostic related information ile kaynak metadata konumlarını göster.
  - [x] Semantic token kapsamını yalnızca kesin çözülen nesnelerle sınırla.
  - [x] Neovim 0.12 uyumluluk smoke testlerini genişlet.

### CI/CD ve smoke test düzeni

- [x] `test.sh` orkestratör olarak bırakıldı; ortak yardımcılar, indeks, CLI, LSP ve FP/FN corpus’ları
  `tests/smoke/` altında ayrıldı.
- [x] Geliştirme sırasında hedefli suite seçimi eklendi: `unit`, `index`, `cli`, `lsp`, `quick-fix`,
  `fp-fn` ve `report`; push öncesi varsayılan `all` akışı korunuyor.
- [x] LSP JSON-RPC framing ve geçici workspace hazırlığı `tests/smoke/common.sh` içindeki ortak helper’lara
  taşındı; LSP ve FP/FN suite’leri tek başına çalışabiliyor.
- [x] GitHub Actions, LSP smoke için resmi Neovim 0.12.5 Linux arşivini kurup sürümü doğruluyor.
- [x] Install smoke testi `status <folder>` sözleşmesine göre indekslenen klasörü açıkça geçiriyor.
- [x] Neovim yoksa veya 0.12 değilse smoke testi belirsiz `command not found` yerine açıklayıcı hata veriyor.

### FP/FN kabul kriterleri

- [x] DML sabit ifade corpus’u; boş/eş string, sayısal sınır, değişken, nondeterministic fonksiyon ve
  `NULL`/`IN` karşı örneklerini LSP + indeks testlerinde kapsıyor. Collation metadata’sı olmadan farklı
  string literal’ları bilinçli olarak atlanıyor (`Index_DmlConstantCorpus_KeepsOnlyCollationIndependentStringComparisons`).
- [x] DML conversion ve `INSERT ... EXEC` type fixture’ları kural kodu, kaynak aralığı ve confidence alanını birlikte assert ediyor.
- [x] Her tanı kuralı için en az bir true-positive fixture ve beklenen tanı kodu/seviyesi ekle.
- [x] Her kesin false-positive sınırı için negatif fixture veya açık güvenli-alt-küme kararı ekle; tanı yokluğu/karar
  `tests/smoke/fp-fn-report.sh` tarafından matrix guard ile doğrulansın.
- [x] Bilinen false-negative sınırları için karar yaz: güvenli alt kümeye ekle, bilinçli olarak atla veya
  metadata yetersizliği olarak raporla (`tests/smoke/fp-fn-decisions.md`).
- [x] `NULL`, değişken, parametre, nondeterministic fonksiyon, alt sorgu ve stale-index durumlarını
  sabit ifade testlerinden ayrı tut.
- [x] Quick-fix preview editleri; düşük confidence, belge dışı aralık, parse hatası ve doğrudan command
  üretmeme sınırlarıyla birlikte regression corpus’unda doğrulanıyor (`tests/smoke/lsp.sh` ve
  hedefli `tests/smoke/quick-fix.sh`).
- [x] DML, filter conversion, JOIN/date conversion, INSERT EXEC type ve NULLability için temsilî
  true-positive/false-positive sınırları ayrı corpus’ta rule + confidence + kritik kaynak aralığıyla
  doğrulanıyor (`tests/smoke/fp-fn.sh`).
- [x] Eksik lint aileleri için transaction/DELETE güvenliği, set-operation genişliği, MERGE determinism ve
  derived DML target true-positive/negative fixture’ları eklendi (`fp_fn_lint_edges`, `fp_fn_set_operation`,
  `fp_fn_merge`, `fp_fn_derived_target`).
- [x] MERGE `USING (SELECT TOP ... )` kaynaklarında `ORDER BY` yokluğunu ayrı determinism tanısı olarak raporla;
  ordered source için negatif regression fixture ekle.
- [x] `BEGIN TRAN` hemen ardından gelen ve CATCH içinde rollback içeren TRY/CATCH desenini güvenli kabul et;
  regression testiyle doğrula.
- [x] Production rule taraması ile smoke corpus karşılaştırıldı; diagnostic olarak kalan tek özel durum
  `reindex` code action komutudur, lint rule değildir.
- [x] Bilinçli FN/temkin kararları kayda geçirildi: collation metadata’sı olmayan farklı string literal’ları,
  subquery/variable/nondeterministic DML koşulları ve belirsiz implicit conversion’lar güvenli alt kümeye
  alınmıyor.
- [x] Derived table alias’ının kendi tanımındaki kolonları dış scope’a sızdırmaması regression corpus’unda
  doğrulandı; bilinmeyen kaynak tabloda `zoneID`/`rank`/`count` tanıları derived alias’a bağlanmıyor.
- [x] Test sonucu yalnızca çıktı içeriyor mu diye değil, kural + kaynak aralığı + confidence birlikte
  doğrulanarak değerlendirilsin; FP/FN raporu her rule için pozitif/negatif assertion, range ve confidence
  sayımlarını JSON matrisi olarak üretir (`tests/smoke/fp-fn-report.sh`).
- [x] Yeni bir tanı eklenirken en az bir karşı örnek veya güvenli-alt-küme kararı aynı regression corpus’una
  eklenmeden madde tamamlanmış sayılmasın.
- [x] Kalan tüm rule’lar için aynı matrisi tamamla; özellikle action üretmeyen bilgi tanıları için
  rule + severity + range, confidence üreten tanılar için ayrıca confidence assert et.
- [x] Production’dan çıkan her emitted rule kodunun FP/FN smoke corpus’unda en az bir fixture referansı
  bulunduğunu CI guard’ı ile kontrol et; yeni rule karşı fixture olmadan eklenemiyor.
- [x] LSP tanı metadata’sını ortak mapper’a taşı; `source`, `confidence` ve açık `stale` bayrağını
  canlı, güncel indeks ve eski indeks yollarında aynı sözleşmeyle üret.
- [x] Workspace diagnostic smoke testinde `fresh-index` → `stale-index` geçişini, severity/confidence
  değişimini ve açık belge ile indeks tanılarının tekilleştirilmesini doğrula.

### CLI sözleşmesi kabul kriterleri

- [x] Her komut için geçerli seçenekler, zorunlu argümanlar ve hata durumları `--help` içinde görünür olsun.
- [x] `--json` çıktıları sabit alan adları ve türleriyle belgelenmiş olsun; insan-okunur tablo alanları
  JSON sözleşmesini değiştirmesin.
- [x] Exit-code matrisi belgelenip test edilsin: başarılı işlem, stale index, lint eşiği, kullanım hatası
  ve eksik indeks birbirinden ayrılmalı.
- [x] `--quiet`, `--no-color`, `--no-pager` ve `NO_COLOR` yönlendirilmiş stdout altında regression testiyle
  doğrulansın.
- [x] `--db` ile aktif indeks çözümlemesinin önceliği tüm sorgu/diagnostic komutlarında aynı olsun.

### Quick fix kabul kriterleri

- [x] Edit uygulanmış metin bellekte yeniden parse edilsin; yeni parse hatası üreten öneri reddedilsin.
- [x] Tek bir editin değiştirdiği aralık, tanının hedef aralığı veya tanımlı insertion point dışına çıkamasın.
- [x] Stale diagnostic ile güncel açık belge uyuşmuyorsa yalnızca açıklama/yeniden indeksleme önerisi verilsin.
- [x] Quick-fix aileleri için mevcut metin, yeni metin, diff kapsamı ve confidence regression fixture’ında
  doğrulansın; corpus bunu `null-comparison`, alias, similar-column, SELECT-star, WHERE, paging ve
  typed-literal action’larında kapsıyor; `ambiguous-column` adayları da iki bounded edit olarak doğrulanıyor.
  `null-comparison` ve literal paging editleri sonrası gerçek `didChange` yeniden analiz smoke testiyle
  doğrulanıyor; similar-column, `INSERT` ve `UPDATE` kolon önerileri için de edit sonrası ilgili
  `unknown-*-column` tanısının kalktığı, INSERT/UPDATE önerilerinin aralık ve confidence metadata’sını
  koruduğu; `ambiguous-column` için de adaylardan biri uygulanınca tanının kalktığı doğrulanıyor.
- [x] Quick-fix metadata’sında tanının confidence’ı korunurken edit güveni ayrı raporlansın; stale tanıda
  text edit yerine yalnızca reindex command üretildiği sözleşmesi korunmalı.
- [x] Düşük maliyetli quick-fix genişletmeleri: `ambiguous-column` adaylarını diff/range/confidence ile doğrula.
- [x] `null-comparison` ve paging editlerini gerçek yeniden-analiz sonucu ile regression test et.
- [x] `sqldeps lint --json` ile LSP diagnostic metadata’sını ortak JSON sözleşmesine
  çıkar; CLI/LSP çıktılarında aynı `source/confidence/stale` alanlarını kullan.
- [x] FP/FN corpus’undan kural kapsam raporu üret (`rule`, fixture kapsamı, FP guard, FN kararı,
  quick-fix var/yok); CI’da yalnızca eksilen kapsamı fail ettir.

## Tamamlananlar

- [x] ScriptDom sözdizimi hatalarını gerçek satır ve sütunda gösterme.
- [x] Bilinmeyen alias için `could not be bound` tanısı.
- [x] Alias verilmiş tabloyu eski tablo adıyla kullanma.
- [x] Aynı sorgu kapsamında tekrarlanan alias.
- [x] Alias ile üç parçalı başvuru ve muhtemel eksik virgül (`p. i.column`).
- [x] Bilinmeyen sütun ve belirsiz niteleyicisiz sütun.
- [x] Bu sütun denetimlerini `SELECT`, `WHERE`, `JOIN ... ON`, `GROUP BY` ve `HAVING`
  içindeki ifadelerde çalıştırma.
- [x] `INSERT` hedefinde olmayan sütun ve açık sütun listesi/değer sayısı uyuşmazlığı.
- [x] Doğrudan `UPDATE dbo.Table SET ...` hedefinde olmayan sütun.
- [x] Mevcut kalite uyarıları: `SELECT *`, `NOLOCK`, `READUNCOMMITTED`, `MERGE`,
  dinamik SQL, `UPDATE`/`DELETE` için eksik `WHERE`, yinelenen tanım, erişilemeyen
  nesne ve çapraz veritabanı başvurusu.

## Sonraki küçük işler

- [x] Alias'lı güncelleme hedefini çözme: `UPDATE i SET ... FROM dbo.Info i`.
- [x] `DELETE i FROM dbo.Info i ...` için çözümlenemeyen hedef alias doğrulaması.
- [x] `INSERT ... SELECT` için hedef sütun sayısı ile seçilen ifade sayısını karşılaştırma.
- [x] Çok satırlı `INSERT ... VALUES (...), (...)` satırlarının her birinde değer sayısı
  tutarlılığı.
- [x] `INSERT` sütun listesindeki yinelenen sütun adları.
- [x] Aynı `SET` listesinde yinelenen güncelleme sütunu.
- [x] Bir tabloya alias verildiğinde, tablonun şemasız veya şemalı adıyla yapılan tüm
  başvurularda düzeltme önerisi.
- [x] `ORDER BY` içindeki bilinmeyen/ambiguous sütun ve görünmeyen select-list alias.
- [x] `TOP`, `OFFSET` ve `FETCH` ifadelerinde negatif veya anlamsız sabit değerler.
- [x] Açık CTE sütun listesi ile CTE select-list sütun sayısı uyuşmazlığı.

## Sorgu kapsamı ve ad çözümleme

- [x] İç sorgu, derived table, CTE ve `APPLY` için alias kapsam zinciri.
- [x] CTE/derived-table üzerinde olmayan sütun: `active.typoColumn`.
- [x] CTE/derived-table niteleyicisiz sütunları için belirsizlik denetimi.
- [x] Aynı isimli CTE, tablo alias'ı ve tablo adı arasındaki gölgeleme/çakışma.
- [x] `UNION`/`INTERSECT`/`EXCEPT` kollarında farklı sayıda select elemanı.
- [x] `CASE` dallarında açıkça uyumsuz sabit tipler için düşük güvenli uyarı.
- [x] `GROUP BY` sorgusunda aggregate olmayan doğrudan seçili sütunun grup listesinde olmaması.
- [x] `DISTINCT` ile geçersiz `ORDER BY` sütun başvuruları.
- [x] `ORDER BY` ordinalinin select-list sınırları dışında olması.

## DML doğrulaması

- [x] `UPDATE ... FROM` içinde hedef alias'ın birden fazla tabloyla eşleşmesi.
- [x] `DELETE ... FROM` içinde hedef alias'ın birden fazla tabloyla eşleşmesi.
- [x] `DELETE` ve `UPDATE` hedefinin türetilmiş/CTE tablo olup olmamasını denetleme.
- [x] `MERGE` içinde aynı hedef sütuna birden çok `UPDATE` ataması.
- [x] `MERGE` eşleşme koşulunda hedefi etkileyen, deterministik olmayan desenler.
- [x] `INSERT` için açık identity sütunu kullanımı için düşük güvenli `IDENTITY_INSERT` warning’i
  (indeks identity bilgisini topluyor).
- [x] `NOT NULL` ve varsayılanı olmayan hedef sütunun `INSERT`te atlanması
  (sütun nullability/default bilgisi indekse eklenince).
- [x] Tahmini tip uyumsuzluğu: metne sayısal, tarih/saat veya `uniqueidentifier`
  sabit atama (düşük güvenli uyarı).
- [x] `INSERT ... VALUES` içinde string literal’ın sayısal kolona atanması için düşük güvenli
  implicit-conversion warning’i.

## Nesne ve çağrı doğrulaması

- [x] Açık şema nitelemeli ve indekste bulunmayan procedure başvurusu.
- [x] Açık şema nitelemeli ve indekste bulunmayan function başvurusu.
- [x] İndekste bulunmayan view veya table başvurusu.
- [x] Birden fazla şemada bulunan şemasız tablo adı için belirsizlik uyarısı.
- [x] Birden fazla şemada bulunan şemasız procedure/function adı için belirsizlik uyarısı.
- [x] Procedure çağrısında bilinmeyen named parameter.
- [x] Aynı procedure parametresinin birden fazla kez verilmesi.
- [x] Gerekli procedure parametresinin atlanması; fazla positional argüman.
- [x] Scalar/table-valued function için argüman sayısı doğrulaması.
- [x] Result-set çıkarımı ve doğrulaması:
  - [x] Procedure/view içindeki top-level `SELECT` çıktı kolonlarını çıkar.
  - [x] `UNION`/derived query çıktı kolonlarını ortak modele bağla; çok kaynaklı `SELECT *` sonuçlarını birleştir.
  - [x] `INSERT ... EXEC` hedef kolonları ile procedure result-set kolon sayısını karşılaştır.
  - [x] Table variable hedefleriyle `INSERT ... EXEC` için düşük güvenli tanı üret.

## Güvenlik, doğruluk ve performans uyarıları

- [x] FP/FN incelemesi: bilinen `SELECT *` genişliklerini `INSERT ... SELECT` sayımında kullan;
  kaynak şeması çözülemiyorsa tanıyı bastır.

- [x] Bilinen primary/unique/nonclustered metadata’sına göre `WHERE` içinde indekslenmemiş sütun kullanımı için düşük güvenli tanı.
- [x] Bilinen foreign key hedefiyle uyuşmayan JOIN eşleşmeleri için düşük güvenli tanı.
- [x] Bilinen index metadata’sına göre `ORDER BY` içinde indekslenmemiş sütunlar için düşük güvenli tanı.
- [x] Bilinen index metadata’sına göre JOIN koşullarında indekslenmemiş sütunlar için düşük güvenli tanı.
- [x] Başta `%` olan sabit `LIKE` deseni için SARG/indeks-seek uyarısı.
- [x] Sütuna fonksiyon uygulanan karşılaştırmalar için düşük güvenli SARG uyarısı.
- [x] Diğer SARG edilemeyen filtrelerin ilk güvenli alt kümesi: tek bir indekslenmiş
  sayısal/tarih/uniqueidentifier kolona karşılaştırılan string sabitler için düşük güvenli uyarı.
- [x] Filtresiz `SELECT` için düşük güvenli bağlamsal uyarı; `UPDATE`/`DELETE` için mevcut
  koruyucu uyarılar kullanılmaya devam ediyor.
- [x] Açık `NULL` içeren `NOT IN` listeleri için `NOT EXISTS`/NULL uyarısı; nullable alt sorgu
  uyarısı, metadata olmadan false-positive ürettiği için devre dışı bırakıldı.
- [x] `= NULL` / `<> NULL`; `IS NULL`/`IS NOT NULL` düzeltmesi.
- [x] `COUNT(column)` ve `COUNT(*)` farkının muhtemel yanlış kullanımı.
- [x] `INSERT ... VALUES` içinde integer/decimal hedeflerde kesin saptanabilen hassasiyet kaybı.
- [x] `money`/`smallmoney` ve `real`/`float` sabitlerinde güvenli precision-loss alt kümesi.
- [x] Örtük dönüşüm ve olası hassasiyet kaybı: tarih/saat türleri için literal precision-loss alt kümesi.
- [x] Transaction içinde `NOLOCK`, uzun transaction veya hata yönetimi olmadan
  `BEGIN TRANSACTION`.
- [x] `BEGIN TRANSACTION` için TRY/CATCH ve rollback kontrolü olmayan akışlara düşük güvenli warning;
  TRY/CATCH kapsamındaki başlangıçlar false-positive azaltmak için atlanır.
- [x] Dinamik SQL'de string birleştirme ve parametresiz kullanıcı girdisi riski.
- [x] `SELECT *` için olası hızlı düzeltme: tek tablo ve indekslenmiş sütun listesiyle genişletme.

## İndeks/model yatırımları

- [x] Tablo sütunlarına nullability, identity, computed/default, precision/scale ve
  collation bilgisini ekleme.
- [x] İlişki ve result-set metadata’sı:
  - [x] Primary key ve unique constraint tanımlarını ortak modele ekle.
  - [x] Foreign key kolonlarını ve hedeflerini çıkar.
  - [x] `ALTER TABLE ... ADD CONSTRAINT` ile eklenen anahtarları da çıkar.
  - [x] Nonclustered index kolonları ve sıra bilgisini çıkar.
  - [x] Metadata için SQLite tablolarını ve şema sürümünü güncelle.
  - [x] View/procedure result-set kolonlarını kalıcı indeks ve LSP modeline bağla.
- [x] Geçici tablo kapsamı:
  - [x] `CREATE TABLE #t`/`ALTER TABLE #t` sütunlarını çıkar.
  - [x] Batch sınırları arasında yerel şema sızıntısını engelle.
  - [x] Procedure kapsamı ve nested procedure tanımlarını ayır.
  - [x] `DROP TABLE` ve yeniden tanımlama aralıklarını izle.
- [x] Table variable bildirimlerinin oturum/kapsam içi sütunlarını çıkarma.
- [x] SQL Server catalog/ScriptDom sürüm farklarını tanı çıktısına güven seviyesi olarak yansıt;
  parser kaynaklı parse error’ları `confidence=low` / `confidenceReason=scriptdom-tsql160-parser`,
  catalog erişimi olmayan şemasız unknown-object tanılarını da `source-index-no-catalog` gerekçesiyle raporla.
- [x] İndeks güncelliği:
  - [x] LSP workspace kökünden ilgili SQLite indeksini güvenilir biçimde bul; `SQLDEPS_HOME` active-index
    ve yerel index dizini adaylarını source-root metadata’sıyla doğrula.
  - [x] Kaynak hash farklarını workspace yükleme sonucuna taşı.
  - [x] Eski indeks kaynaklı tanıları `information` seviyesine indir ve `stale-index` kaynağını ekle.
  - [x] Stale-index tanısına `sqldeps reindex` code action önerisi ekle.
  - [x] `workspace/executeCommand` üzerinden stale-index reindex action’ını çalıştır.
  - [x] Filtered index metadata’sını key/include ayrımıyla birlikte koru.
  - [x] Filtered index predicate’ini sorgu koşullarıyla eşleştirerek güvenli indeks kanıtı üret.

- [x] Beklenen result-set doğrulaması:
  - [x] Table variable ve temp table hedefleri için bilinen kolon sayılarını kullan.
  - [x] `INSERT ... EXEC` akışında kolon listesi yoksa hedef şemadan sayıyı çıkar.
  - [x] Subquery ve explicit derived-table kolon adlarını result-set eşleşmesinde kullan.
  - [x] Adı çözülemeyen result-set ifadelerini ordinal bilgisiyle sayımda koru.
  - [x] Bilinen result-set kolonlarında güvenli tip uyumu alt kümesini doğrula.

## Okunabilirlik ve refactor

- [ ] LSP’yi ayrı projeye taşımadan önce ortak `SqlDeps.Core` sınırını değerlendir; bağımsız dağıtım veya sürümleme
  ihtiyacı oluşursa ancak o zaman `SqlDeps.LanguageServer` executable’ı çıkar.
- [x] LSP doğrulayıcılarının ortak parse edilmiş batch’leri kullanması.
- [x] LSP quick fix edit üretimini ortaklaştırma.
- [x] Procedure çağrısı, sütun doğrulama ve alias kararlarını isimli metotlara ayırma.
- [x] Procedure/function çağrı bağımlılıklarını `DependencyCollector.Calls.cs` partial dosyasına ayırma.
- [x] `QueryColumnValidator` AST finder ve tablo-reference yardımcılarını `QueryColumnValidator.Finders.cs`
  partial dosyasına ayırma; visitor karar akışını aynı bırakarak destek tiplerini izole etme.
- [x] `UPDATE`/`DELETE` hedef çözümleme, DML WHERE ve yazma hedefi tanılarını
  `QueryColumnValidator.Dml.cs` partial dosyasına ayır; ortak kapsam çözümlemesini koru.
- [x] `INSERT` visitor dispatch’ini `QueryColumnValidator.Insert.cs` partial dosyasına ayır; result-set ve
  metadata tanı yardımcılarını ortak visitor state’iyle koru.
- [x] Dinamik SQL fallback davranışını tek metotta toplama.
- [x] Dinamik SQL ayrıştırma ve sabit SQL çözümleme yardımcılarını `DependencyCollector.DynamicSql.cs` ve `DynamicSqlText.cs` dosyalarına ayırma.
- [x] DML hedef/kolon kaydını `DependencyCollector.Writes.cs` dosyasına ayırma.
- [x] Nesne tanımı ve tablo şeması çıkarımını `DependencyCollector.Definitions.cs` dosyasına ayırma.
- [x] `LanguageServer` içindeki JSON/LSP mesaj ayrıştırma ve code-action üretimini ayrı bileşenlere ayırma.
- [x] CLI argüman ayrıştırmasını durum nesnesi ve seçenek yardımcılarına ayırma.
- [x] Batch splitter lexical state kontrollerini yorum/string/quoted-identifier yardımcılarına ayırma.
- [x] LSP istek dispatch'i ile bildirim dispatch'ini ayırma.
- [x] `INSERT`/`UPDATE` doğrulama kontrollerini isimli metotlara ayırma.
- [x] Sütun tablo çözümleme ve dinamik SQL birleşim guard'larını isimli metotlara ayırma.
- [x] SQLite yazma akışında şema, metadata, nesne, bağımlılık, parametre, sütun, tanı ve reachability kayıtlarını isimli metotlara ayırma.
- Kodlama politikası: uzun koşulları ve dönüşüm/parsing içeren guard’ları isimli metotlara ayır; kontrol akışını
  boş satırlarla görsel olarak ayır.
- Kodlama politikası: yeni davranış eklemeden önce mevcut helper/benzer akışları ara; yinelenen mantığı eklemek
  yerine mevcut davranışı genişlet.

### Kapsamlı refactor planı

- [x] **Domain ve metadata sınırları:** result-set, tablo sütunu, key, foreign-key ve index modellerini
  immutable `MetadataCatalog` sözleşmesinde grupla; LSP doğrulama sınırı bu sözleşmeyi kullanır.
  SQLite yazma/okuma tarafındaki geniş aktarım nesneleri named mapper ile korunuyor.
- [x] **Indexer pipeline:** parse, collect, merge ve cache adımlarını `IndexedFileContribution` tek dosya
  katkısı modelinde birleştir; cache aktarımını `FromCache` ile merkezileştir.
- [x] **LSP analiz akışı:** `QueryColumnValidator` içindeki tablo, yerel kapsam, index ve result-set
  çözümlemelerini ayrı odaklı partial bileşenlere böl; her bileşenin tek bir karar sorumluluğu olsun.
- [x] **LSP workspace yaşam döngüsü:** yükleme, refresh, stale kontrolü ve açık belge birleştirmesini
  ortak snapshot akışına taşı; lock altında dosya I/O yapılmasını kaldır.
- [x] **LSP action sözleşmesi:** edit ve command code action metadata üretimini ortak yardımcılarla ayır;
  stale-index command action’ını diğer quick fix akışlarından izole et.
- [x] **Storage ve migration:** SQLite şema sürümünü açık bir uyumluluk sözleşmesine bağla; writer
  `schema_version` metadata’sını yazar, uyumsuz/eksik sürüm cache’i incremental reader tarafından reddedilir
  ve mevcut atomik yeniden indeksleme akışıyla yeniden oluşturulur. Gerçek in-place migration gerektiğinde yeni
  sürüm için ayrı migration adımı eklenir.
- [x] **CLI ve çıktı:** komut dispatch’i, indeks seçimi, pager/renklendirme ve hata mesajlarını
  ayrı katmanlara ayır; `CommandLine.Dispatch`, `CommandLine.IndexSelection`, `ConsoleOutput.*` ve
  ortak top-level hata akışıyla aynı durum mesajlarının tekrarını azalt.
- [x] CLI’nin named index sorgularını `SqlDeps.Core.IndexQueryService` sınırına taşı; keyfi SQL yerine
  whitelist’li operasyonlar kullanılsın ve ileride MCP aynı servisi tüketebilsin.
- [x] CLI usage/version/command dispatch ve top-level hata yakalamayı `CommandLine.Dispatch.cs` partial
  dosyasına ayır; handler ve çıktı davranışını koru.
- [x] `CommandLine` handler’larını indeksleme, sorgu/lint, indeks yönetimi, seçenek ayrıştırma ve yol çözümleme
  partial dosyalarına ayır; ana dosyayı ortak sabit ve sınıf bildirimi seviyesine indir.
- [ ] **Test mimarisi:** parser/indexer/LSP/storage testlerini senaryo ailelerine ayır; ortak JSON-RPC
  ve fixture yardımcılarıyla test tekrarını azalt.
- [ ] **Performans doğrulaması:** refactor öncesi/sonrası parse, collect, SQLite ve LSP snapshot
  sürelerini aynı corpus üzerinde karşılaştır; yalnız ölçümle doğrulanan optimizasyonları koru.

### Uygulanabilir refactor alt görevleri

- [x] `QueryColumnValidator.Validate` içindeki global metadata sözlüklerini tek kurulum akışında hazırla;
  her batch’te aynı result/index/foreign-key sözlüklerinin yeniden kurulmasını engelle.
- [x] `QueryColumnValidator.ColumnVisitor` içindeki tanı üretimini konu başlıklarına ayır:
  kolon çözümleme, indeks kanıtı, scope/order, tür uyumu, DML hedefleri, result-set ve güvenlik yardımcıları
  `QueryColumnValidator.*Diagnostics.cs` partial dosyalarına taşındı; visitor davranışı korunuyor.
- [x] Filtered-index predicate normalizasyonunu ayrı bir `SqlExpressionNormalizer` yardımcısına taşı;
  metin normalizasyonu ile indeks kararını aynı metotta tutma.
- [x] `DependencyCollector` içindeki sabit literal değerlendirmelerini ortak bir `ConstantExpressionEvaluator`
  bileşeninde birleştir; DML ve diğer sabit koşul tanıları aynı kuralları kullansın. DML ve MERGE kararları
  aynı evaluator’ı kullanıyor; mevcut collation-bağımlı string eşitliği temkinli davranışını koruyor.
- [x] `DependencyCollector` tanı metotlarında `AddLint` çağrılarının kural, seviye ve mesaj üretimini
  küçük tanı fabrikasına ayır; `LintDiagnosticFactory` tanı içeriğini AST konumundan ayırır, mevcut
  spec ve FP/FN çıktıları korunur.
- [x] SQL Server built-in aggregate/scalar function adlarını workspace UDF aramasından ayır; gerçek corpus’ta
  `SUM`, `COUNT` ve diğer katalog adlarının `unknown-function` yanlış pozitif üretmediğini LSP regression ile doğrula.
- [x] `TOP (0)` tanısının `QuerySpecification` ve `TopRowFilter` traversal’ında iki kez üretilmesini kaldır;
  aynı AST düğümü için tek diagnostic regression’ı ekle.
- [x] LSP smoke framing’inde `Content-Length` değerini karakter değil UTF-8 byte uzunluğu olarak hesapla;
  Unicode SQL regression’ı ekle.
- [x] Workspace dışında yaşayan `msdb.dbo.sp_*`/`xp_*` sistem prosedürlerini unknown-procedure tanısından
  ayır; qualified external system procedure regression’ı ekle.
- [x] `ResultColumn`, `TableColumn` ve `TableIndexColumn` metadata aktarımını isimli kayıt/mapper
  yardımcılarıyla sadeleştir; positional constructor çağrılarını azalt. `SqliteMetadataMapper` writer/reader
  row sözleşmesini ve cache round-trip regression’ını taşır.
- [x] SQLite schema oluşturma ve cache okuma sözleşmesinde `schema_version` metadata kontrolünü ortaklaştır;
  schema değişikliklerinde loader/writer drift’ini cache uyumluluk kontrolüyle önle.
- [x] `LanguageServer.LoadWorkspace`, refresh ve açık belge birleştirmesini ortak snapshot üretim
  akışına taşı; workspace diagnostic stale dosya I/O’sunda lock kapsamını yalnızca bellek snapshot’ıyla sınırla.
- [x] `ProcedureParameterValidator` için positional/named/output parametre eşleştirmesini tek bir
  bağlama yardımcısında materialize et; tüm tanı metotları aynı binding sonucunu kullansın.
- [x] LSP tanı ve code-action confidence/source dönüşümünü tek bir metadata mapper’ında topla; stale
  reindex action’ı da `stale-index` için düşük confidence sözleşmesini kullanır.
- [x] Test fixture JSON-RPC framing’ini `tests/smoke/common.sh:lsp_frame` ortak helper’ına taşı;
  workspace ve invalid-request LSP regresyonlarının kopyala-yapıştır maliyetini azalt.
- [x] Temiz HOME altında install/publish, version/help, JSON index/status ve LSP initialize akışını
  `tests/smoke/install.sh` ve `./test.sh install` ile otomasyona al; CI inline tekrarını kaldır.
- [ ] Her refactor adımında public davranış değişmediğini doğrulayan snapshot/regression testlerini
  koru; yalnız isimlendirme ve sorumluluk sınırı değişikliklerini ayrı commit’le.
- [x] `IndexDatabaseTests` monolitini ortak `SqlFolderTestBase` ve storage, metadata, scope/DML, lint/MERGE
  senaryo ailelerine ayır; mevcut 42 test adını ve assertion’larını koru.
- [x] Refactor regression snapshot/contract fixture’larını senaryo aileleriyle eşleştir; özellikle CLI/LSP JSON
  sözleşmelerindeki dinamik alanları normalize ederek karşılaştır. `./test.sh spec`, `specs/cli-lint.json` ve
  `specs/lsp-diagnostics.json` bu sözleşmeleri doğrular.

## Güvenlik incelemesi

- [x] Dinamik SQL, dosya/SQLite erişimi, CLI/LSP girdileri, kaynak sınırları ve credential/SSH yüzeyleri için ilk read-only inceleme.

| Önem | Bulgu | Önlem |
|---|---|---|
| P1 | LSP `Content-Length`, header satırı ve JSON gövdesi için üst sınır yoktu. | Header/gövde limitleri, duplicate-header reddi ve parse edilemeyen JSON sonrası kontrollü devam eklendi; `tests/smoke/lsp.sh`. |
| P1 | SQL dosyası ve tek batch boyutu için kaynak limiti yoktu. | 64 MiB dosya ve 16 MiB batch/belge limitleri uygulanıyor; aşımda anlaşılır parse/index uyarısı yazılıyor. |
| P1 | Workspace indeksleme `Task.Run` ile başlıyor, iptal token’ı ve tekil çalışma garantisi yok. | Önceki yük iptal ediliyor, `SemaphoreSlim` ile tek indeksleme işi çalışıyor ve sürüm guard’ı eski sonucu reddediyor. |
| P2 | Dosya URI’si dışındaki girdiler `PathFromUri` tarafından düz metin yol gibi kabul edilebiliyordu. | Yalnızca absolute `file:` URI kabul ediliyor; workspace kökü dışındaki dosyalar da reddediliyor. |
| P2 | CLI’nin kullanıcı tarafından verilen SQLite/yol hedefi kasıtlı olarak serbest; izin/containment politikası yok. | CLI için mevcut davranışı koru; LSP ve otomasyon modunda açık hedef dizin politikası belgeleyip isteğe bağlı kısıt ekle. |
| Bilgi | SQLite sorguları parametreli; indeks geçici dosyaya yazılıp atomik taşınıyor. | Mevcut yaklaşımı koru ve güvenlik regresyon testi ekle. |

## Operasyonel kalite ve sürümleme

- [x] **P0** Güvenlik incelemesini tamamla; dosya yolu, SQLite yolu, LSP URI/JSON girdisi ve kaynak sınırları için bulgu/önlem tablosu çıkar.
- [x] LSP JSON-RPC header ve gövde boyutları için üst sınır uygula.
- [x] SQL dosyası, batch ve açık LSP belgesi için kaynak boyutu sınırları uygula.
- [x] LSP çalışma alanı kökü için `rootUri`, `rootPath` ve `workspaceFolders` biçimlerini destekle.
- [x] Hatalı LSP isteklerinde sunucuyu kapatmadan JSON-RPC `-32602` hatası döndür.
- [x] İndeks metadata’sı mevcutken çözülemeyen tablo/view adlarını LSP warning’i olarak raporla.
- [x] Birden fazla şemadaki procedure/function adları şemasız çağrıldığında belirsizlik warning’i üret.
- [x] **P1** Parse/reach/SQLite sürelerini sabit bir corpus üzerinde karşılaştıran küçük bir performans baseline’ı ekle; ScriptDom parse süresi, batch boyutu ve allocation değerlerini profiler/ölçüm çıktısıyla kaydet.
- [x] **P1** LSP için büyük dosya, eksik `initialize`, bozuk JSON-RPC ve hızlı `didChange` akışlarını kapsayan regresyon senaryoları ekle.
- [x] **P1** Parse edilemeyen batch’lerde geçerli önek davranışını ve sonraki batch’in indekslenmesini golden fixture olarak koru.
- [x] **P2** `dotnet publish`/`install.sh` sonrasında temiz makine smoke test’i ekle; `sqldeps`, `sqldeps lsp` ve aktif indeks akışını doğrula.
- [x] **P2** SQLite şema sürümü ve indeks formatı için geriye dönük uyumluluk/migrasyon politikasını belge.

### Performans tarama notları

- Statik tarama: 5 ScriptDom parse çağrısı, 16 string/memory API kullanımı, 163 LINQ çağrısı,
  10 source-generated regex kullanımı ve 32 dosya/SQLite I/O noktası bulundu.
- Öncelikli ölçüm zinciri: `SqlFolderIndexer.IndexFile` içindeki `ReadAllBytes` + SHA-256 + decode,
  `BatchSplitter.Split` ve `TSql160Parser.Parse`; sonuçları dosya/batch boyutuyla ilişkilendir.
- Dinamik SQL, `DependencyCollector.DynamicSql` içinde ikinci bir ScriptDom parse geçişi yapıyor;
  ayrı bir senaryoda sabit SQL ve dinamik SQL maliyetlerini karşılaştır.
- LSP parse yolları (`DocumentAnalysis`) ile CLI indeksleme yollarını ayrı ölç; LSP’de geçerli prefix
  yeniden parse edildiği için hata senaryosunu ayrıca kaydet.
- `dotnet-trace` ve `dotnet-counters` kuruldu; kısa `dotnet-counters` koşusu süreç örnekleme aralığından
  önce bittiği için veri üretmedi. `dotnet-trace` ile 640 dosyalık corpus ölçüldü.
- 2026-11-02 tarihinde global `dotnet-trace` ile aynı Release binary ve gerçek yerel corpus’lar yeniden
  ölçüldü: Hayati 2.537 SQL dosyasında `TSql160Parser.Parse` inclusive `%50,65`, bildb 1.895 dosyada
  `%42,03`; exclusive `EH.DispatchEx` sırasıyla `%45,01` ve `%34,66` oldu. Her iki indekste de parse
  error sayısı `0`; bu maliyet hatalı dosya raporlamasından çok ScriptDom parser’ın iç exception-dispatch
  davranışına işaret ediyor.
- Aynı Hayati corpus’unda üç tur gerçek süre karşılaştırması (`--full`): `--jobs 1` ortalama `4,14s`,
  `--jobs 2` `3,15s`, `--jobs 4` `2,38s`, `--jobs 8` `2,57s`. Bu makinede `4` güvenli aday olsa da
  platformlar arası varsayılanı değiştirmek için yeterli genel kanıt yok; açık `--jobs` seçeneği korunuyor.
- İlk ölçüm (`fixtures`, 8 dosya, `--full`, 3 koşu): parse `0,10s`, reach `0,00s`, SQLite `0,01s`,
  toplam `0,11–0,12s`. Bu küçük corpus ScriptDom darboğazını ayırmak için henüz yeterli değildir;
  local-corpus ölçümü ve dosya/batch boyutu dağılımı gereklidir.
- İlk profiler trace’i doğrudan Release executable ve 640 dosyalık geçici corpus üzerinde alındı:
  `TSqlParser.Parse` `%34,65` inclusive, `SqlFolderIndexer.ParseBatch` `%34,21`,
  `SqlFolderIndexer.CollectBatch` `%21,84`, `SqlScriptGenerator.GenerateScript` `%18,19`.
  Bu, ScriptDom parse ve tablo tanımı script üretiminin ilk optimizasyon adayları olduğunu gösteriyor;
  yüzdeler örneklem tabanlıdır ve kesin benchmark yerine yön gösterir.
- `dotnet-counters` ilk kısa koşuda örnek üretemedi; process counter aralığından önce bitti. Daha büyük
  corpus veya tekrarlı benchmark ile GC heap, allocation ve CPU counter’ları ayrıca toplanmalı.
- İlk profiler bulgusuna karşı düşük riskli yerel optimizasyon uygulandı: `DependencyCollector` artık
  her sütun/parametre için yeni `Sql160ScriptGenerator` üretmek yerine collector başına tek örnek kullanıyor.
- Aynı 640 dosyalık corpus üzerinde optimizasyon öncesi ve sonrası 5’er koşu karşılaştırıldı: parse süresi
  her iki sürümde de `0,14s`, toplam süre yaklaşık `0,18s` oldu. Bu nedenle yeniden kullanım değişiklikleri
  kayda değer parse kazanımı olarak doğrulanmadı; küçük maliyetleri nedeniyle şimdilik korunuyor.
- `TSql160Parser` örneği aynı dosyanın batch’leri arasında ve LSP belgesinin batch’leri arasında yeniden
  kullanılıyor. Sonraki performans adayı parser örneği üretimini değil, ScriptDom’un `TSqlParser.Parse` ve
  `SqlScriptGenerator.GenerateScript` çağrılarını azaltmak; bunun için batch boyutu, tekrar parse edilen
  dinamik SQL ve üretilen SQL metninin gerçekten tüketilip tüketilmediği ayrı ölçülmeli.
- Yeni trace’te `SqlScriptGenerator.GenerateScript` exclusive payı yaklaşık `%0,23` kaldığı için generator
  optimizasyonu şu an öncelik değil. CTE + TRY/CATCH ön-taramalarını tek AST ziyaretinde birleştirme denemesi
  post-trace’te parser `%52,76`, `EH.DispatchEx` `%46,85` verdi ve anlamlı süre kazanımı göstermedi; değişiklik
  geri alındı.

### Codegraph ve profiler kaynaklı yeni adaylar

#### Codegraph refactor adayları (2026-11-03)

Codegraph, mevcut dirty çalışma ağacıyla (`indexed_at_commit=8a8df07e`, 1.399 node / 2.961 edge)
çıkarıldı. Aşağıdaki sıralama statik boyut, çağrı bağlantısı ve sorumluluk yoğunluğuna dayanır;
yeni bir davranış veya performans iddiası değildir.

- [x] **P1 — `LanguageServer` orkestrasyonunu ayır:** `Lsp/LanguageServer.cs` içinde 88 method ve
  1.235 method-satırı var; `Update` (55), `RefreshWorkspaceFile` (50), `LoadWorkspace` (70) ve
  `Handle` (63) workspace yaşam döngüsü, belge sürümü ve RPC orkestrasyonunu aynı sınıfta topluyor.
  Workspace load/refresh/snapshot yardımcıları `LanguageServer.Workspace.cs` partial’ına taşındı.
- [x] **P1 — `DependencyCollector` sorgu kapsamını ayır:** `Indexing/DependencyCollector.cs` içinde
  91 method ve 1.131 method-satırı var; `Visit(QuerySpecification)` (52), alias doğrulama (38) ve
  kolon çözümleme yardımcıları aynı stateful visitor’da birleşiyor. Query-scope/alias kararlarını
  `DependencyCollector.QueryScope.cs` partial’ına taşı; mevcut lint golden’ları korunuyor.
- [x] **P2 — Code-action dispatch zincirini sadeleştir:** `AddReplacementActions` 75 satırlık ve
  çok sayıda action üreticisini sıralı biçimde çağırıyor. Diagnostic-rule → action factory tablosu
  veya konu partial’ları ile yeni quick-fix ekleme maliyetini düşürmek; action sırası ve dedup
  sözleşmesi değişmemeli. Temel ve yapısal action dispatch’i ayrı partial metotlarına ayrıldı; sıra korundu.
- [x] **P2 — SQLite writer aktarım sınırını küçült:** `SqliteStore.WriteNew` 14 alt yazıcı çağrısını
  ve uzun parametre listesini taşıyor. `IndexWriteSnapshot` benzeri tek isimli aktarım nesnesiyle
  writer/cache model sınırını sadeleştir; SQLite round-trip testleri korunuyor.
- [x] **P3 — Yerel kolon toplama yardımcılarını ayır:** `DocumentAnalysis` içindeki
  `LocalColumnCollector.ColumnsFromSources` 43 satır ve birden fazla derived/CTE kaynağını yönetiyor.
  Kaynak çözümleme metodu `LocalColumnCollector.Sources.cs` partial’ına taşındı; kapsam regression’ları korundu.

#### Senkronizasyon sonrası yeni adaylar

- [x] **P1 — Belge güncelleme orkestrasyonu:** `LanguageServer.Update` içindeki tekrar eden belge saklama
  ve sürüm kontrolünü ortak metoda ayır.
- [x] **P2 — Yapısal SELECT-star action’ı:** `MultiTableSelectStarAction` içindeki kaynak keşfi,
  metadata yeterlilik kararı ve edit sonrası doğrulama yardımcı metotlara ayrıldı; action akışının
  güvenli üretim ve belirsizlik kontrolleri korundu.
- [x] **P2 — Semantic-token maskesi:** `MaskNonCode` içindeki string, satır yorumu ve blok yorumu
  maskeleme kuralları lexical state yardımcılarına ayrıldı; satır sonlarını koruyan davranış sürdürüldü.
- [x] **P2 — SQLite atomik yazma orkestrasyonu:** `SqliteStore.Write` snapshot oluşturmayı koruyarak
  geçici dosya yolu, atomik taşıma ve cleanup adımlarını ayrı yardımcı sınırlara ayırdı.
- [x] **P3 — Cache yükleme orkestrasyonu:** `IndexCache.Load` uyumluluk kontrolü ile tablo yükleme
  pipeline’ına ayrıldı; eski cache reddetme davranışı korundu.

#### Codegraph sync sonrası refactor adayları (2026-11-03)

Güncel codegraph senkronizasyonu 1.454 node ve 2.984 edge üretti. Aşağıdaki adaylar davranış
değiştirmeden sorumluluk sınırlarını daraltmak için seçildi:

- [x] **P2 — `MissingRequiredInsertColumnAction`:** hedef sütun keşfi, default/identity kararı ve
  edit üretimini ayrı yardımcılarla sadeleştirdi; 88 satırlık code-action akışını tek sorumluluklara böldü.
- [x] **P2 — `DerivedQueryColumnAction`:** derived/CTE kaynak çözümleme ile öneri metni üretimini ayırdı;
  82 satırlık akışta belirsizlik kontrollerini korudu.
- [x] **P2 — Derived-column edit üretimi:** kaynak sütun uygunluğu, aynı satırdaki edit konumları ve
  WorkspaceEdit parçaları ayrı yardımcı metotlara ayrıldı; code-generation regression’ları korundu.
- [x] **P2 — `ConstantExpressionEvaluator.Evaluate`:** literal normalizasyonu, karşılaştırma ve
  üç-değerli mantık kararlarını ayrı metotlara böldü; mevcut sabit filtre regression’larını korudu.
- [x] **P2 — Dinamik SQL quick-fix akışı:** parça okuma, değişken tip çözümleme, parametre adı üretimi
  ve `sp_executesql` argümanlarının oluşturulması ayrı yardımcı metotlara ayrıldı; güvenli SQL parse
  kontrolü ve mevcut quick-fix regression’ları korundu.
- [x] **P2 — `LoadWorkspace` orkestrasyonu:** indeks/cache hazırlığı, dosya indeksleme ve analiz modeline
  aktarım ayrı yardımcı metotlara ayrıldı; cancellation ve workspace sürüm kontrolleri korundu.
- [x] **P2 — `LanguageServer.Handle` request doğrulaması:** JSON-RPC request alanlarının doğrulanması
  ve geçersiz request yanıtı ayrı metotlara ayrıldı; request/notification response sırası korundu.
- [x] **P2 — `DependencyCollector.QueryScope.Visit`:** alias kaydı, bilinen alias kümesi ve SELECT
  column usage üretimi ayrı yardımcı metotlara ayrıldı; dependency/index smoke davranışı korundu.
- [x] **P2 — `QuickFixEdits` güvenlik akışı:** edit aralığı doğrulama/sıralama, metin uygulama ve parse
  problemi karşılaştırması ayrı yardımcı metotlara ayrıldı; quick-fix smoke davranışı korundu.
- [x] **P2 — `RefreshWorkspaceFile` katkı yenileme:** dosya okuma sınırı, eski katkıları kaldırma ve
  yeni katkıları ekleme ayrı yardımcı metotlara ayrıldı; workspace refresh akışı korundu.
- [x] **P3 — `DynamicSqlText.TryReadConstant`:** string literal, concatenation uygunluğu ve recursive
  sabit birleştirme ayrı yardımcı metotlara ayrıldı; dynamic SQL sabit tanıma davranışı korundu.
- [x] **P3 — `SemanticTokens`:** token toplama ve LSP delta encoding ayrı yardımcı metotlara ayrıldı;
  resolved object token sırası ve veri dizisi korunuyor.
- [x] **P2 — Dynamic SQL edit aralığı:** expression okuma, `EXEC(...)` prefix/kapanış range çözümleme
  ve edit listesi üretimi ayrı yardımcı metotlara ayrıldı; quick-fix regression’ları korundu.
- [x] **P3 — `DependencyCollector.OnlySqlTrivia`:** line/block comment atlama ve başlangıç kararları
  ayrı yardımcı metotlara ayrıldı; transaction TRY/CATCH tespit davranışı korundu.
- [x] **P2 — `LanguageServer.Handle` yürütme sınırları:** request ve notification exception akışları
  ayrı güvenli yürütme yardımcılarına ayrıldı; shutdown ve error response davranışı korundu.
- [x] **P2 — `CommandLine.Run` dispatch akışı:** help/version kontrolü, command dispatch ve top-level
  hata sınırı ayrı metotlara ayrıldı; CLI exit code ve usage davranışı korundu.
- [x] **P2 — `DocumentAnalysis.Analyze` batch akışı:** batch boyutu kontrolü, parse/hata kaydı ve
  geçerli prefix/collect işlemleri ayrı yardımcı metotlara ayrıldı; FP/FN smoke davranışı korundu.
- [x] **P2 — `SqlFolderIndexer.IndexFile` hotpath sınırları:** cache katkısı kararı ve batch indeksleme
  ayrı yardımcı metotlara ayrıldı; index smoke ve cache hit davranışı korundu.
- [x] **P2 — Derived source column resolution:** tablo metadata sözlüğü ve benzersiz kaynak eşleşmesi
  ayrı yardımcı metotlara ayrıldı; code-generation regression’ları korundu.
- [x] **P2 — INSERT literal diagnostics:** hücre çözümleme, string conversion, nullability ve numeric
  precision tanıları ayrı yardımcı metotlara ayrıldı; FP/FN smoke davranışı korundu.
- [x] **P2 — `QueryColumnValidator.Validate`:** metadata sözlüklerinin kurulumu batch doğrulama akışından
  ayrıldı; statement/diagnostic sırası ve unknown/ambiguous ile DML kuralları korundu.
- [x] **P3 — `JsonRpcConnection.Read`:** LSP header, gövde okuma ve JSON parse adımları ayrı sınırlara
  ayrıldı; eksik header/gövde ve bozuk JSON davranışı korundu.

- [ ] **P1 / Hotpath** AST tekrar taramalarını azalt:
  - [ ] `AliasesIn` taramasını statement visitor’ıyla birleştir.
  - [ ] Query/table/column finder ziyaretlerini tek kapsam ziyaretinde toplama seçeneğini ölç.
  - [ ] İç içe sorgu doğruluk regression testlerini koru.
- [x] CTE + TRY/CATCH ön-taramalarını tek AST ziyaretinde birleştirme denemesini gerçek corpus trace’iyle
  doğrula; kazanım görünmediği için refactor’ı geri al.
- [ ] **P1 / Hotpath** ScriptDom generator maliyetini azalt:
  - [ ] `GenerateScript` çağrılarını kullanım noktalarına göre sınıflandır.
  - [ ] Kaynak metin dilimi ile generator çıktısını benchmark’la.
  - [ ] Lazy metadata üretimini yalnızca ölçüm sonucu anlamlıysa uygula.
- [x] **P1 / Hotpath** `Parallel.For` için varsayılan worker sayısını sabit corpus üzerinde ölç; ScriptDom
  parse CPU’su ile thread-pool beklemesini ayır ve `--jobs` için güvenli varsayılan belirle. Varsayılan
  artık `min(Environment.ProcessorCount, 4)`; açık `--jobs` değeri bunu geçersiz kılar.
- [ ] **P2** `BatchSplitter` ve parse aşamasında ara `string` kopyalarını ölç; davranış korunuyorsa
  `ReadOnlyMemory<char>`/buffer tabanlı akış için küçük benchmark ekle.
- [ ] **P2 / LSP** `AllObjects`, `AllTableColumns` ve benzeri snapshot üretimlerini workspace sürümüyle
  önbellekle; aynı belge değişmeden gelen completion/hover isteklerinde tekrar tahsisleri ölç.
- [ ] **P2** Parse, collect, generator ve SQLite sürelerini CI’da aynı corpus ile trend olarak sakla;
  yalnızca toplam süre değil batch sayısı, ortalama batch boyutu ve allocation değerini de raporla.

Hotpath ölçüm özeti: codegraph `SqlFolderIndexer.Index → IndexFile → IndexBatch → ParseBatch/CollectBatch`
zincirini, `DependencyCollector.Collect` ve `DocumentAnalysis.Analyze` girişlerini doğruladı. `dotnet-trace`
640 dosyalık corpus’ta `TSqlParser.Parse` `%34,65`, `SqlFolderIndexer.CollectBatch` `%21,84` ve
`SqlScriptGenerator.GenerateScript` `%18,19` inclusive örnekleme payı gösterdi; yüzdeler yönlendiricidir,
değişiklik öncesi/sonrası aynı koşulda tekrar ölçülmelidir.

Periyodik ölçüm notu: alias taraması kaldırıldıktan ve query finder cache’i eklendikten sonra aynı 640
dosyalık corpus’ta üç koşu `parse 0,14–0,15s`, toplam `0,19–0,20s` verdi; baseline parse `0,14s` idi.
Bu iki refactor ölçülebilir parse kazanımı sağlamadı. Yeni `dotnet-trace` oturumu diagnostic Unix socket
izin hatasıyla başlatılamadı; mevcut trace yüzdeleri yön gösterici olarak tutuluyor.

İkinci ölçüm notu: `GenerateSql` fragment cache’i ve ortak LSP offset helper’ı sonrasında aynı corpus’ta
parse yine `0,14–0,15s`, toplam `0,19–0,20s` kaldı. Bu değişiklikler tekrar maliyetini azaltıyor ancak
ana CLI parse hotpath’ini değiştirmiyor; sonraki optimizasyon doğrudan ScriptDom çağrı sayısını azaltmalı.

Kaynak dilimi denemesi yalnızca default expression alanına uygulandı; aynı corpus’ta parse `0,14–0,16s`,
toplam `0,19–0,21s` ölçüldü. Data type çıktısında format değişikliği yaratmaması için genel `GenerateSql`
fallback’i korundu; bu yaklaşımın ölçülebilir kazanımı doğrulanmadı.

Worker benchmark notu: aynı 640 dosyalık corpus’ta `--jobs 1` parse `0,29s`, `--jobs 2` `0,19–0,20s`,
`--jobs 4` `0,16s`, `--jobs 8` `0,14s` verdi. `Parallel.For` varsayılanı (`-1`, runtime processor count)
korundu; ek bir worker limiti kazanım sağlamıyor.

Batch allocation notu: `BatchSplitter.AddBatch` içindeki çift `ToString`/`Trim` üretimi tek string
oluşturacak şekilde sadeleştirildi. Aynı corpus’ta sonraki üç koşu parse `0,14–0,15s`, toplam
`0,18–0,20s` verdi; ana hotpath için anlamlı fark oluşmadı.

## Yeni öneriler ve karar sırası

Bu bölüm, mevcut küçük tanı işlerinden sonra hangi yatırımların yapılacağını belirtir.

1. **P0 — Güvenlik incelemesi — tamamlandı:** dosya yolu/SQLite yolu doğrulaması, LSP URI ve JSON-RPC sınırları,
   dinamik SQL metinlerinin loglanması, kaynak boyutu limitleri ve credential yüzeyleri için bulgu tablosu.
2. **P1 — LSP sağlamlığı — tamamlandı:** eksik `initialize`, bozuk veya aşırı büyük JSON-RPC mesajı, hızlı `didChange`,
   eski indeks ve eşzamanlı workspace yüklemesi için regresyon senaryoları.
3. **P1 — Metadata temeli — tamamlandı:** `nullability`, `identity`, `default`, precision/scale ve computed bilgilerini
   ortak modele ekle; `INSERT` eksik kolon ve örtük dönüşüm tanılarını ancak bu metadata ile etkinleştir.
4. **P1 — Performans baseline’ı:** parse/reach/SQLite sürelerini sabit corpus üzerinde ölç, değişiklik başına
   önce/sonra karşılaştırmasını CI çıktısına ekle.
5. **P1 — Kapsam doğruluğu — tamamlandı:** unknown view/table ve şemasız procedure/function belirsizliği;
   şemasız çözümlemelerde `information + low`, şemalı ama bulunamayanlarda `warning + medium`, stale indekslerde
   ortak `stale-index + low` metadata ile raporlanıyor.
6. **P2 — Kullanıcı deneyimi — tamamlandı:** `SELECT *` için yalnızca tek tablo ve bilinen sütunlarda güvenli quick fix;
   `INSERT` sütun tamamlama; `WHERE` ekleme için düzenleme yerine şablon önerisi.
7. **P2 — Dağıtım:** `dotnet publish`/`install.sh` temiz makine smoke testi, sürüm çıktısı ve SQLite şema
   migrasyon politikasını aynı CI akışında doğrula.
8. **En son — Diğer SQL lehçeleri:** önce ortak model/diagnostic sınırını sabitle, sonra PostgreSQL, MySQL,
   SQLite ve Oracle sağlayıcılarını ayrı corpus ve LSP testleriyle ekle.

### Yeni feature adayları

- [x] Kod üretimi Aşama 1 — güvenli lokal editler:
  - [x] Tek ifade/tek satır quick fix altyapısını kullan.
  - [x] Preview/diff olarak typed literal quick fix üret.
  - [x] Üretim öncesi confidence eşiği uygula.
  - [x] Üretim sonrası metni yeniden parse et.
  - [x] Edit aralığı dışında değişiklik yapılmadığını doğrula.
- [x] Kod üretimi Aşama 2 — yapısal editler:
  - [x] `INSERT` kolon listesi için yalnızca tek-row, tek-satır, parse-safe placeholder preview edit’i üret.
  - [x] Procedure çağrısında eksik zorunlu parametre için kullanıcı onaylı placeholder preview edit’i üret.
  - [x] Derived query kolonlarını kapsam modeliyle doğrula.
  - [x] Otomatik uygulama yerine kullanıcı onaylı workspace edit döndür.
- [x] Kod üretimi Aşama 3 — çok tabloluk SQL:
  - [x] PK/FK/index metadata’sı olmadan JOIN/UPDATE/MERGE için yapısal edit üretme; mevcut
    UPDATE/MERGE akışları fail-closed kalır, çok tablolu SELECT * yalnızca ilişki ve erişim metadata’sıyla açılır.
  - [x] Üretim sonrası alias/kolon çözümleme ve yeniden parse kontrolü yap.
  - [x] Belirsiz veya düşük güvenli durumda yalnızca açıklama üret.
  - [x] Tek veya çoklu kaynaklı derived query/CTE’de kolon tek kaynakta kesin çözülebiliyorsa alias-qualified
    bounded edit üret; eşleşen explicit CTE kolon listesine iki edit uygula, `UNION`, `*` ve belirsizliği
    fail-closed koru.
- [x] Kod üretimi Aşama 4 — kontrollü akış:
  - [x] LSP edit yanıtına açıklama, confidence ve diff bilgisi ekle.
  - [x] Dosyaya doğrudan yazmayı engelle; açık kullanıcı onayı iste.
  - [x] Üretim kararlarını ve reddedilen önerileri `tests/smoke/code-generation.sh` regression fixture’ında sakla.
  - [x] Eşit kolon şekline sahip çok satırlı `INSERT ... VALUES` için tüm satırlara bounded placeholder
    edit’i üret; eşit olmayan satırları fail-closed reddet.
  - [x] Kolon sayısı kesin eşleşen ve `*`/`UNION` içermeyen `INSERT ... SELECT` için bounded `NULL`
    placeholder edit’i üret; belirsiz SELECT şekillerini fail-closed reddet.
- [x] Dynamic SQL için güvenli alt kümede `EXEC(...)` string birleşimini `sp_executesql` parametrelerine
  dönüştüren kullanıcı onaylı quick fix üret; tipi bilinmeyen değişkenleri ve dinamik nesne adlarını reddet.
- [x] İlişki/result-set metadata’sını kod üretimine bağla; `INSERT ... EXEC`, tablo değişkeni
  ve uygulama içi beklenen kolon karşılaştırmalarını düşük güvenli ve explanation-only tut.
- [x] Structural generation kararlarını ayrı `tests/smoke/code-generation.sh` fixture’ında; kabul,
  reddetme, yeniden analiz ve explanation-only sonuçlarıyla doğrula.
- [x] Workspace diagnostic isteği ekle; açık belge dışında indekslenen SQL dosyalarını da tek seferde
  raporlayabilen CLI/LSP akışı oluştur.
- [x] Açık belgelerin canlı tanılarını `workspace/diagnostic` içinde tekrar raporlama; Neovim’de aynı
  `nolock`/lint mesajının iki kez görünmesini önle, pull-capable istemcilerde push bildirimini bastır ve
  LSP smoke regression’ı ekle.
- [x] Aynı satırdaki aynı kural/mesaj occurrence’larını CLI ve LSP sunumunda tekilleştir; indeks içindeki
  gerçek occurrence kayıtlarını koru (`tests/smoke/lsp.sh`).
- [x] Diagnostic’ler için güven seviyesi ve kaynak (`live-document`, `fresh-index`, `stale-index`) alanlarını
  ortak modele taşı; LSP `data.source`, `data.confidence` ve açık `data.stale` alanlarını aynı mapper ile
  iletiyor. Stale workspace tanıları düşük güven/severity ile, açık belgeler ise canlı tanı olarak ayrılıyor.

## T-SQL dışındaki SQL lehçeleri

- [ ] Lehçe sağlayıcı temeli:
  - [ ] Parser/formatter/identifier kurallarını sağlayıcı arayüzüne ayır.
  - [ ] T-SQL sağlayıcısını varsayılan ve geriye uyumlu bırak.
  - [ ] `languageId`/uzantı/ayar önceliğiyle lehçe seçimini ekle.
  - [ ] Ortak model ve tanıların lehçeden bağımsız sözleşmesini sabitle.
- [ ] PostgreSQL sağlayıcısı:
  - [ ] `public`, `RETURNING`, `WITH RECURSIVE`, `ILIKE` ve dolar-quote parse desteği.
  - [ ] `ON CONFLICT` ve PostgreSQL DDL eşlemesi.
  - [ ] PostgreSQL corpus ve LSP regression testleri.
- [ ] MySQL/MariaDB sağlayıcısı:
  - [ ] Backtick, `LIMIT`, `ON DUPLICATE KEY` ve `DELIMITER` desteği.
  - [ ] Procedure gövdeleri ve MySQL DDL eşlemesi.
  - [ ] MySQL corpus ve LSP regression testleri.
- [ ] SQLite sağlayıcısı:
  - [ ] Gevşek tür sistemi, `WITHOUT ROWID`, `UPSERT` ve `PRAGMA` desteği.
  - [ ] Sınırlı `ALTER TABLE` davranışının ortak modele eşlenmesi.
  - [ ] SQLite corpus ve LSP regression testleri.
- [ ] Oracle/PLSQL sağlayıcısı:
  - [ ] Paketler, `BEGIN ... END`, `:bind`, `MERGE` ve `DUAL` desteği.
  - [ ] Oracle identifier kurallarının eşlenmesi.
  - [ ] Oracle corpus ve LSP regression testleri.

## Kullanıcı deneyimi

- [x] CLI kurulum/smoke testleri için `--version` ve `version` alias'ı.
- [x] Güvenli LSP code action'ları: belirsiz sütun için aday alias seçimi.
- [x] `SELECT *` için indekslenmiş sütunlardan açık select-list üretme; değişiklikten
  önce kullanıcının gözden geçirebileceği quick fix olarak sunma.
- [x] Benzer sütun adını güvenli quick fix ile dönüştürme.
- [x] `INSERT INTO <table> (` bağlamında indekslenmiş tablo sütunlarını tamamlama.
- [x] `UPDATE`/`DELETE` için `WHERE` eklemeyi otomatik düzenleme yerine yalnızca öneri/şablon olarak sunma.
- [x] Bilinmeyen sütunda tek ve yakın eşleşme varsa benzer sütun adını önerme.
- [x] Benzer sütun adı önerisini alias'ı koruyan LSP quick fix olarak uygulama.
- [x] Tekrarlanan düşük güvenli uyarıları `disable-next-line` ile susturma.
- [x] Girintili SQL bloklarında suppression yorumunu hedef satırla aynı hizaya koyma.
- [x] Tanı kodlarını README'de örnekleriyle belgelemek.
- [x] LSP istemci yapılandırmasında error/warning/info renkleri için öneri eklemek.
