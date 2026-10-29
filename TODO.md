# LSP tanı yol haritası

Bu liste, `sqldeps lsp` için gerçek veritabanına bağlanmadan üretilebilecek T-SQL
tanılarını önceliklendirir. Bir tanı yalnızca eldeki indeks/sorgu bağlamı yeterliyse
`error` olmalıdır; indeks eksik veya eski olabiliyorsa `warning`/`information` olarak
sunulmalıdır.

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
- [ ] Bir tabloya alias verildiğinde, tablonun şemasız veya şemalı adıyla yapılan tüm
  başvurularda düzeltme önerisi.
- [x] `ORDER BY` içindeki bilinmeyen/ambiguous sütun ve görünmeyen select-list alias.
- [x] `TOP`, `OFFSET` ve `FETCH` ifadelerinde negatif veya anlamsız sabit değerler.
- [x] Açık CTE sütun listesi ile CTE select-list sütun sayısı uyuşmazlığı.

## Sorgu kapsamı ve ad çözümleme

- [ ] İç sorgu, derived table, CTE ve `APPLY` için alias kapsam zinciri.
- [x] CTE/derived-table üzerinde olmayan sütun: `active.typoColumn`.
- [x] CTE/derived-table niteleyicisiz sütunları için belirsizlik denetimi.
- [ ] Aynı isimli CTE, tablo alias'ı ve tablo adı arasındaki gölgeleme/çakışma.
- [x] `UNION`/`INTERSECT`/`EXCEPT` kollarında farklı sayıda select elemanı.
- [ ] `CASE` dallarında açıkça uyumsuz sabit tipler için düşük güvenli uyarı.
- [x] `GROUP BY` sorgusunda aggregate olmayan doğrudan seçili sütunun grup listesinde olmaması.
- [ ] `DISTINCT` ile anlamsız/etkisiz `ORDER BY` başvuruları.
- [x] `ORDER BY` ordinalinin select-list sınırları dışında olması.

## DML doğrulaması

- [ ] `UPDATE ... FROM` içinde hedef alias'ın birden fazla tabloyla eşleşmesi.
- [ ] `DELETE` ve `UPDATE` hedefinin türetilmiş/CTE tablo olup olmamasını denetleme.
- [x] `MERGE` içinde aynı hedef sütuna birden çok `UPDATE` ataması.
- [ ] `MERGE` eşleşme koşulunda hedefi etkileyen, deterministik olmayan desenler.
- [ ] `INSERT` için `IDENTITY_INSERT` kapalıyken açık identity sütunu kullanımı
  (yalnızca indeks identity bilgisini de toplamaya başlarsak).
- [ ] `NOT NULL` ve varsayılanı olmayan hedef sütunun `INSERT`te atlanması
  (sütun nullability/default bilgisi indekse eklenirse).
- [ ] Tahmini tip uyumsuzluğu: metne sayısal, tarih/saat veya `uniqueidentifier`
  sabit atama (düşük güvenli uyarı).

## Nesne ve çağrı doğrulaması

- [ ] İndekste bulunmayan procedure, function, view veya table başvurusu.
- [ ] Birden fazla şemada bulunan şemasız nesne adı için belirsizlik uyarısı.
- [x] Procedure çağrısında bilinmeyen named parameter.
- [x] Aynı procedure parametresinin birden fazla kez verilmesi.
- [ ] Gerekli procedure parametresinin atlanması; fazla positional argüman (fazla argüman tamamlandı).
- [ ] Scalar/table-valued function için argüman sayısı doğrulaması.
- [ ] `EXEC` sonucunu tüketen kodda beklenen result-set şeması doğrulaması
  (result-set çıkarımı eklendiğinde).

## Güvenlik, doğruluk ve performans uyarıları

- [ ] `WHERE` içinde indekslenmemiş sütun kullanımı (gerçek indeks metadatası gerekir).
- [x] Başta `%` olan sabit `LIKE` deseni için SARG/indeks-seek uyarısı.
- [ ] Diğer SARG edilemeyen filtreler: sütuna fonksiyon uygulama ve uyumsuz tür dönüşümü.
- [ ] Büyük tabloda filtresiz `SELECT`, `UPDATE` veya `DELETE` için bağlamsal uyarı.
- [ ] `NOT IN` ile nullable alt sorgu; `NOT EXISTS` önerisi.
- [x] `= NULL` / `<> NULL`; `IS NULL`/`IS NOT NULL` düzeltmesi.
- [x] `COUNT(column)` ve `COUNT(*)` farkının muhtemel yanlış kullanımı.
- [ ] Örtük dönüşüm ve olası hassasiyet kaybı: `float`, para, tarih/saat türleri.
- [ ] Transaction içinde `NOLOCK`, uzun transaction veya hata yönetimi olmadan
  `BEGIN TRANSACTION`.
- [x] Dinamik SQL'de string birleştirme ve parametresiz kullanıcı girdisi riski.
- [ ] `SELECT *` için olası hızlı düzeltme: indekslenmiş sütun listesiyle genişletme.

## İndeks/model yatırımları

- [ ] Tablo sütunlarına nullability, identity, computed/default, precision/scale ve
  collation bilgisini ekleme.
- [ ] Primary key, unique constraint, foreign key ve nonclustered index bilgisini toplama.
- [ ] View ve procedure için result-set sütunlarını çıkarma.
- [ ] Geçici tabloların ve table variable'ların oturum/kapsam içi şemasını çıkarma.
- [ ] SQL Server catalog/ScriptDom sürüm farklarını tanı çıktısına güven seviyesi olarak
  yansıtma.
- [ ] İndeks güncelliğini LSP tanılarına bağlama; eski indeks kaynaklı bulguları
  `information` seviyesine indirme.

## T-SQL dışındaki SQL lehçeleri

- [ ] Ayrıştırıcıyı lehçe sağlayıcıları üzerinden seçilebilir hale getirme; T-SQL
  davranışını varsayılan ve geriye uyumlu bırakma.
- [ ] PostgreSQL desteği: `public` şeması, `RETURNING`, `WITH RECURSIVE`, `ILIKE`,
  `ON CONFLICT`, dolar-quote stringler ve PostgreSQL'e özgü DDL.
- [ ] MySQL/MariaDB desteği: backtick tanımlayıcılar, `LIMIT`, `ON DUPLICATE KEY`,
  `DELIMITER`, prosedür gövdeleri ve MySQL DDL türleri.
- [ ] SQLite desteği: gevşek tür sistemi, `WITHOUT ROWID`, `UPSERT`, `PRAGMA` ve
  SQLite'in sınırlı `ALTER TABLE` davranışları.
- [ ] Oracle/PLSQL desteği: paketler, `BEGIN ... END`, `:bind` değişkenleri,
  `MERGE`, `DUAL` ve Oracle tanımlayıcı kuralları.
- [ ] Her lehçe için nesne, sütun, bağımlılık ve parse-hata eşlemesini ortak modele
  bağlamak; lehçeye özgü bilgiyi kaybetmemek.
- [ ] LSP `languageId` ve dosya uzantısından lehçeyi seçmek; kullanıcı ayarıyla
  açıkça override edebilmek.
- [ ] Ortak tanıları (`unknown-column`, `ambiguous-column`, bağımlılık grafiği)
  lehçeden bağımsız tutmak; yalnızca geçerli olduğu lehçelerde kural çalıştırmak.
- [ ] Her desteklenen lehçe için corpus, parse/indeks/LSP regresyon testleri ve
  tanı örnekleri eklemek.

## Kullanıcı deneyimi

- [ ] Güvenli LSP code action'ları: `= NULL` → `IS NULL`, `<> NULL`/`!= NULL` →
  `IS NOT NULL`, tablo adı yerine doğru alias ve belirsiz sütun için aday alias seçimi.
- [ ] `SELECT *` için indekslenmiş sütunlardan açık select-list üretme; değişiklikten
  önce kullanıcının gözden geçirebileceği quick fix olarak sunma.
- [ ] Benzer sütun adına dönüştürme, `INSERT` tamamlama ve `UPDATE`/`DELETE` için
  `WHERE` eklemeyi otomatik düzenleme yerine yalnızca öneri/şablon olarak sunma.
- [ ] Tanıda kaynak tablo, aday sütunlar ve benzer isim önerileri gösterme.
- [ ] Tekrarlanan düşük güvenli uyarıları `disable-next-line` ile susturma.
- [ ] Tanı kodlarını README'de örnekleriyle belgelemek.
- [ ] LSP istemci yapılandırmasında error/warning/info renkleri için öneri eklemek.
