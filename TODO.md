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

### Aktif feature kuyruğu

- [ ] `INSERT ... EXEC` ve `INSERT ... SELECT` için kolon adı/tip uyumunu doğrula.
- [ ] Procedure parametrelerinde kesin literal tip uyumsuzluklarını raporla.
- [ ] Filtered-index collation ve tür uyumluluğunu genişlet.
- [ ] Etkisiz sabit DML koşullarını karma koşullarda düşük güvenle raporla.
- [ ] Kesin çözülebilen tanılar için güvenli quick fix’leri genişlet.
- [ ] `APPLY`, `PIVOT`, `UNPIVOT`, `OPENJSON` ve table-valued function akışlarını modelle.
- [ ] LSP signature help, semantic token ve related information desteğini genişlet.

### Ertelenen entegrasyonlar

- [ ] `sqldeps mcp` sunucusu ekle; aktif indeks, nesne arama, bağımlılık ve tanı sorgularını
  MCP araçları olarak güvenli ve makine-okunabilir biçimde sun.

### Feature uygulama alt görevleri

- [ ] Result-set tip uyumu:
  - [x] `CAST`/`CONVERT` ile açıkça bilinen result-set tiplerini çıkar.
  - [x] Hedef tablo metadata’sıyla yalnızca kesin uyumsuz tip ailelerini karşılaştır.
  - [x] Belirsiz implicit conversion durumlarında tanı üretme.
  - [x] `INSERT ... EXEC` ve `INSERT ... SELECT` için ayrı regression corpus ekle.
- [ ] NULLability:
  - [x] `VALUES (NULL)` ve `SELECT NULL` → `NOT NULL` hedef tanısı.
  - [x] Nullable kaynak kolonun `NOT NULL` hedefe aktarımında kesin kolon akışını raporla.
  - [ ] Default/identity/computed kolonlarda false-positive senaryolarını test et.
- [ ] JOIN tür/collation:
  - [x] Açık tür ailesi uyumsuzluklarını raporla.
  - [x] Collation metadata’sını normalize ederek yalnızca kesin uyuşmazlıkları raporla.
  - [ ] Numeric widening ve tarih/tür dönüşümlerini uyumlu alt kümeye ekle.
  - [ ] JOIN, WHERE ve APPLY akışlarını birbirinden ayıran testler ekle.
- [ ] DML filtre güvenliği:
  - [x] Tamamen sabit true/false filtreleri raporla.
  - [x] `WHERE 1 = 1 AND ...` gibi etkisiz sabit parçaları düşük güvenle raporla.
  - [ ] Değişken, parametre ve nondeterministic fonksiyonlarda tanıyı bastır.
  - [ ] UPDATE/DELETE/MERGE için ortak koşul değerlendirme testleri ekle.
- [ ] Procedure sözleşmesi:
  - [x] Kullanılmayan `OUTPUT` parametrelerini raporla.
  - [x] Parametrelerin kesin literal tür uyumsuzluklarını sınırlı alt kümeyle yakala.
  - [ ] Named/positional/output bağlama sonucunu ortak model olarak üret.
  - [ ] Default ve nullable parametrelerle false-positive testleri ekle.
- [ ] Güvenli quick fix’ler:
  - [x] Düşük confidence tanılarında metin değiştiren quick fix’leri bastır.
  - [ ] Edit sonrası yalnızca ilgili aralığı yeniden parse et.
  - [ ] Diff kapsamı dışına taşan editleri reddet.
  - [ ] Fix başına açıklama, kaynak tanı ve confidence bilgisini döndür.
- [ ] T-SQL kapsam genişletmesi:
  - [ ] `APPLY` kaynaklarını alias/kolon çözümleme modeline bağla.
  - [ ] `PIVOT`/`UNPIVOT` üretilen kolonlarını yerel kapsamda tanıt.
  - [ ] `OPENJSON` ve table-valued function result kolonlarını güvenli alt kümeyle çıkar.
  - [ ] Her özellik için parse edilemeyen ve kısmi metadata testleri ekle.
- [ ] İleri LSP:
  - [ ] Procedure/function signature help için parametre imzası üret.
  - [ ] Diagnostic related information ile kaynak metadata konumlarını göster.
  - [ ] Semantic token kapsamını yalnızca kesin çözülen nesnelerle sınırla.
  - [ ] Neovim 0.12 uyumluluk smoke testlerini genişlet.

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
- [ ] Result-set çıkarımı ve doğrulaması:
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
- [ ] İlişki ve result-set metadata’sı:
  - [x] Primary key ve unique constraint tanımlarını ortak modele ekle.
  - [x] Foreign key kolonlarını ve hedeflerini çıkar.
  - [x] `ALTER TABLE ... ADD CONSTRAINT` ile eklenen anahtarları da çıkar.
  - [x] Nonclustered index kolonları ve sıra bilgisini çıkar.
  - [x] Metadata için SQLite tablolarını ve şema sürümünü güncelle.
  - [x] View/procedure result-set kolonlarını kalıcı indeks ve LSP modeline bağla.
- [ ] Geçici tablo kapsamı:
  - [x] `CREATE TABLE #t`/`ALTER TABLE #t` sütunlarını çıkar.
  - [x] Batch sınırları arasında yerel şema sızıntısını engelle.
  - [x] Procedure kapsamı ve nested procedure tanımlarını ayır.
  - [x] `DROP TABLE` ve yeniden tanımlama aralıklarını izle.
- [x] Table variable bildirimlerinin oturum/kapsam içi sütunlarını çıkarma.
- [ ] SQL Server catalog/ScriptDom sürüm farklarını tanı çıktısına güven seviyesi olarak
  yansıtma.
- [ ] İndeks güncelliği:
  - [ ] LSP workspace kökünden ilgili SQLite indeksini güvenilir biçimde bul.
  - [x] Kaynak hash farklarını workspace yükleme sonucuna taşı.
  - [x] Eski indeks kaynaklı tanıları `information` seviyesine indir ve `stale-index` kaynağını ekle.
  - [x] Stale-index tanısına `sqldeps reindex` code action önerisi ekle.
  - [x] `workspace/executeCommand` üzerinden stale-index reindex action’ını çalıştır.
  - [x] Filtered index metadata’sını key/include ayrımıyla birlikte koru.
  - [x] Filtered index predicate’ini sorgu koşullarıyla eşleştirerek güvenli indeks kanıtı üret.

- [ ] Beklenen result-set doğrulaması:
  - [x] Table variable ve temp table hedefleri için bilinen kolon sayılarını kullan.
  - [x] `INSERT ... EXEC` akışında kolon listesi yoksa hedef şemadan sayıyı çıkar.
  - [x] Subquery ve explicit derived-table kolon adlarını result-set eşleşmesinde kullan.
  - [x] Adı çözülemeyen result-set ifadelerini ordinal bilgisiyle sayımda koru.
  - [ ] Bilinen result-set kolonlarında güvenli tip uyumu alt kümesini doğrula.

## Okunabilirlik ve refactor

- [ ] LSP’yi ayrı projeye taşımadan önce ortak `SqlDeps.Core` sınırını ölç; bağımsız dağıtım veya sürümleme ihtiyacı oluşursa ancak o zaman `SqlDeps.LanguageServer` executable’ı çıkar.
- [x] LSP doğrulayıcılarının ortak parse edilmiş batch’leri kullanması.
- [x] LSP quick fix edit üretimini ortaklaştırma.
- [x] Procedure çağrısı, sütun doğrulama ve alias kararlarını isimli metotlara ayırma.
- [x] Procedure/function çağrı bağımlılıklarını `DependencyCollector.Calls.cs` partial dosyasına ayırma.
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
- [ ] Uzun koşulları, dönüşüm/parsing içeren guard’ları isimli metotlara ayırma; kontrol akışını boş satırlarla görsel olarak ayırma.
- [ ] Yeni davranış eklenmeden önce mevcut helper/benzer akışları arama; yinelenen mantığı eklemek yerine genişletme.

### Kapsamlı refactor planı

- [ ] **Domain ve metadata sınırları:** result-set, key, foreign-key ve index modellerini ortak
  bir metadata sözleşmesinde grupla; SQLite yazma/okuma parametre sırasını isimli aktarım nesnesine taşı.
- [ ] **Indexer pipeline:** parse, collect, merge ve cache adımlarını tek bir dosya katkısı modelinde
  birleştir; aynı metadata’nın tekrar tekrar kopyalanmasını azalt.
- [ ] **LSP analiz akışı:** `QueryColumnValidator` içindeki tablo, yerel kapsam, index ve result-set
  çözümlemelerini ayrı odaklı bileşenlere böl; her bileşenin tek bir karar sorumluluğu olsun.
- [ ] **LSP workspace yaşam döngüsü:** yükleme, refresh, stale kontrolü ve açık belge birleştirmesini
  ortak snapshot akışına taşı; lock altında dosya I/O yapılmasını kaldır.
- [ ] **LSP action sözleşmesi:** edit ve command code action üretimini ortak tip/yardımcılarla ayır;
  stale-index command action’ını diğer quick fix akışlarından izole et.
- [ ] **Storage ve migration:** SQLite şema sürümünü açık migration adımlarına ayır; cache yükleme
  sorgularını metadata tabloları için ortak okuyucularla sadeleştir.
- [ ] **CLI ve çıktı:** komut dispatch’i, indeks seçimi, pager/renklendirme ve hata mesajlarını
  ayrı katmanlara ayır; aynı durum mesajlarının tekrarını azalt.
- [ ] **Test mimarisi:** parser/indexer/LSP/storage testlerini senaryo ailelerine ayır; ortak JSON-RPC
  ve fixture yardımcılarıyla test tekrarını azalt.
- [ ] **Performans doğrulaması:** refactor öncesi/sonrası parse, collect, SQLite ve LSP snapshot
  sürelerini aynı corpus üzerinde karşılaştır; yalnız ölçümle doğrulanan optimizasyonları koru.

### Uygulanabilir refactor alt görevleri

- [ ] `QueryColumnValidator.Validate` içindeki metadata sözlüklerini tek bir immutable snapshot
  nesnesinde grupla; her batch’te aynı sözlüklerin yeniden kurulmasını engelle.
- [ ] `QueryColumnValidator.ColumnVisitor` içindeki tanı üretimini konu başlıklarına ayır:
  kolon çözümleme, indeks kanıtı, DML hedefleri, result-set ve güvenlik uyarıları.
- [ ] Filtered-index predicate normalizasyonunu ayrı bir `SqlExpressionNormalizer` yardımcısına taşı;
  metin normalizasyonu ile indeks kararını aynı metotta tutma.
- [ ] `DependencyCollector` içindeki sabit literal değerlendirmelerini ortak bir `ConstantExpressionEvaluator`
  bileşeninde birleştir; DML ve diğer sabit koşul tanıları aynı kuralları kullansın.
- [ ] `DependencyCollector` tanı metotlarında `AddLint` çağrılarının kural, seviye ve mesaj üretimini
  küçük tanı fabrikalarına ayırmadan önce tekrar eden akışları ölç.
- [ ] `ResultColumn`, `TableColumn` ve `TableIndexColumn` metadata aktarımını isimli kayıt/mapper
  yardımcılarıyla sadeleştir; positional constructor çağrılarını azalt.
- [ ] SQLite schema oluşturma ve cache okuma sorgularındaki kolon listelerini ortak sabitlerle
  eşleştir; schema değişikliklerinde loader/writer drift’ini önle.
- [ ] `LanguageServer.LoadWorkspace`, refresh ve açık belge birleştirmesini ortak snapshot üretim
  akışına taşı; lock kapsamını yalnızca bellek güncellemesiyle sınırla.
- [ ] `ProcedureParameterValidator` için positional/named/output parametre eşleştirmesini tek bir
  bağlama yardımcısına taşı; her tanı metodu kendi eşleştirmesini yapmasın.
- [ ] LSP tanı confidence/source dönüşümünü tek bir metadata mapper’ında topla; kural listelerini
  `LanguageServer` içine dağınık switch ifadelerinden çıkar.
- [ ] Test fixture JSON-RPC üretimini shell değişkenlerinden ortak bir yardımcı komuta taşı;
  yeni LSP regresyonlarının kopyala-yapıştır maliyetini azalt.
- [ ] Her refactor adımında public davranış değişmediğini doğrulayan snapshot/regression testlerini
  koru; yalnız isimlendirme ve sorumluluk sınırı değişikliklerini ayrı commit’le.

## Güvenlik incelemesi

- [x] Dinamik SQL, dosya/SQLite erişimi, CLI/LSP girdileri, kaynak sınırları ve credential/SSH yüzeyleri için ilk read-only inceleme.

| Önem | Bulgu | Önlem |
|---|---|---|
| P1 | LSP `Content-Length`, header satırı ve JSON gövdesi için üst sınır yok. | Header ve gövde boyutu limiti koy; aşımda JSON-RPC hata yanıtı veya kontrollü kapanış üret. |
| P1 | SQL dosyası ve tek batch boyutu için kaynak limiti yok. | Dosya/batch byte limiti ekle; aşımda parse etmeden anlaşılır indeks uyarısı yaz. |
| P1 | Workspace indeksleme `Task.Run` ile başlıyor, iptal token’ı ve tekil çalışma garantisi yok. | Workspace başına `CancellationToken` ve tek aktif yükleme görevi kullan. |
| P2 | Dosya URI’si dışındaki girdiler `PathFromUri` tarafından düz metin yol gibi kabul edilebiliyor. | Dosya okuma/kaydetme işlemlerinde yalnızca `file:` URI kabul et; diğerlerini reddet. |
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

### Codegraph ve profiler kaynaklı yeni adaylar

- [ ] **P1 / Hotpath** AST tekrar taramalarını azalt:
  - [ ] `AliasesIn` taramasını statement visitor’ıyla birleştir.
  - [ ] Query/table/column finder ziyaretlerini tek kapsam ziyaretinde toplama seçeneğini ölç.
  - [ ] İç içe sorgu doğruluk regression testlerini koru.
- [ ] **P1 / Hotpath** ScriptDom generator maliyetini azalt:
  - [ ] `GenerateScript` çağrılarını kullanım noktalarına göre sınıflandır.
  - [ ] Kaynak metin dilimi ile generator çıktısını benchmark’la.
  - [ ] Lazy metadata üretimini yalnızca ölçüm sonucu anlamlıysa uygula.
- [x] **P1 / Hotpath** `Parallel.For` için varsayılan worker sayısını sabit corpus üzerinde ölç; ScriptDom
  parse CPU’su ile thread-pool beklemesini ayır ve `--jobs` için güvenli varsayılan belirle.
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

1. **P0 — Güvenlik incelemesi:** dosya yolu/SQLite yolu doğrulaması, LSP URI ve JSON-RPC sınırları,
   dinamik SQL metinlerinin loglanması, kaynak boyutu limitleri ve credential yüzeyleri için bulgu tablosu.
2. **P1 — LSP sağlamlığı:** eksik `initialize`, bozuk veya aşırı büyük JSON-RPC mesajı, hızlı `didChange`,
   eski indeks ve eşzamanlı workspace yüklemesi için regresyon senaryoları.
3. **P1 — Metadata temeli:** `nullability`, `identity`, `default`, precision/scale ve computed bilgilerini
   ortak modele ekle; `INSERT` eksik kolon ve örtük dönüşüm tanılarını ancak bu metadata ile etkinleştir.
4. **P1 — Performans baseline’ı:** parse/reach/SQLite sürelerini sabit corpus üzerinde ölç, değişiklik başına
   önce/sonra karşılaştırmasını CI çıktısına ekle.
5. **P1 — Kapsam doğruluğu:** unknown view/table ve şemasız procedure/function belirsizliğini indeksin güncellik
   durumuyla birlikte `information`/`warning` güven seviyelerinde raporla.
6. **P2 — Kullanıcı deneyimi:** `SELECT *` için yalnızca tek tablo ve bilinen sütunlarda güvenli quick fix;
   `INSERT` sütun tamamlama; `WHERE` ekleme için düzenleme yerine şablon önerisi.
7. **P2 — Dağıtım:** `dotnet publish`/`install.sh` temiz makine smoke testi, sürüm çıktısı ve SQLite şema
   migrasyon politikasını aynı CI akışında doğrula.
8. **En son — Diğer SQL lehçeleri:** önce ortak model/diagnostic sınırını sabitle, sonra PostgreSQL, MySQL,
   SQLite ve Oracle sağlayıcılarını ayrı corpus ve LSP testleriyle ekle.

### Yeni feature adayları

- [ ] Kod üretimi Aşama 1 — güvenli lokal editler:
  - [x] Tek ifade/tek satır quick fix altyapısını kullan.
  - [x] Preview/diff olarak typed literal quick fix üret.
  - [ ] Üretim öncesi confidence eşiği uygula.
  - [ ] Üretim sonrası metni yeniden parse et.
  - [ ] Edit aralığı dışında değişiklik yapılmadığını doğrula.
- [ ] Kod üretimi Aşama 2 — yapısal editler:
  - [ ] `INSERT` kolon listesi üretimini result-set ve metadata ile doğrula.
  - [ ] Procedure çağrısı üretiminde zorunlu/default parametreleri doğrula.
  - [ ] Derived query kolonlarını kapsam modeliyle doğrula.
  - [ ] Otomatik uygulama yerine kullanıcı onaylı workspace edit döndür.
- [ ] Kod üretimi Aşama 3 — çok tabloluk SQL:
  - [ ] PK/FK/index metadata’sı olmadan JOIN/UPDATE/MERGE üretme.
  - [ ] Üretim sonrası alias/kolon çözümleme ve yeniden parse kontrolü yap.
  - [ ] Belirsiz veya düşük güvenli durumda yalnızca açıklama üret.
- [ ] Kod üretimi Aşama 4 — kontrollü akış:
  - [ ] LSP edit yanıtına açıklama, confidence ve diff bilgisi ekle.
  - [ ] Dosyaya doğrudan yazmayı engelle; açık kullanıcı onayı iste.
  - [ ] Üretim kararlarını ve reddedilen önerileri regression fixture olarak sakla.
- [ ] İlişki/result-set metadata’sını kod üretimine bağla; `INSERT ... EXEC`, tablo değişkeni
  ve uygulama içi beklenen kolon karşılaştırmalarını düşük güvenli tut.
- [x] Workspace diagnostic isteği ekle; açık belge dışında indekslenen SQL dosyalarını da tek seferde
  raporlayabilen CLI/LSP akışı oluştur.
- [ ] Diagnostic’ler için güven seviyesi ve kaynak (`live-document`, `fresh-index`, `stale-index`) alanlarını
  ortak modele taşı; LSP artık `live-document` ve `fresh-index` kaynaklarını `data.source` ile iletiyor,
  deterministic/heuristic bulgular için `data.confidence` da iletiyor; stale-index ayrımı ve Neovim gösterimi kaldı.

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
- [ ] `SELECT *` için indekslenmiş sütunlardan açık select-list üretme; değişiklikten
  önce kullanıcının gözden geçirebileceği quick fix olarak sunma.
- [x] Benzer sütun adını güvenli quick fix ile dönüştürme.
- [x] `INSERT INTO <table> (` bağlamında indekslenmiş tablo sütunlarını tamamlama.
- [x] `UPDATE`/`DELETE` için `WHERE` eklemeyi otomatik düzenleme yerine yalnızca öneri/şablon olarak sunma.
- [x] Bilinmeyen sütunda tek ve yakın eşleşme varsa benzer sütun adını önerme.
- [x] Benzer sütun adı önerisini alias'ı koruyan LSP quick fix olarak uygulama.
- [x] Tekrarlanan düşük güvenli uyarıları `disable-next-line` ile susturma.
- [x] Tanı kodlarını README'de örnekleriyle belgelemek.
- [x] LSP istemci yapılandırmasında error/warning/info renkleri için öneri eklemek.
