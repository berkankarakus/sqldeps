# sqldeps yol haritası

Bu dosya yalnızca açık işleri ve kısa karar notlarını tutar. Tamamlanmış ayrıntılı checklist’ler
git geçmişinde ve ilgili smoke/unit testlerinde bulunur.

## Tamamlanan ana işler

- T-SQL ScriptDom tabanlı indeksleme, dependency/reachability ve SQLite metadata modeli.
- Parse hataları, alias/kolon çözümleme, subquery/CTE/derived table, table variable ve temp table kapsamı.
- Lint tanıları: DML güvenliği, NULL/tür/collation, JOIN/WHERE/ORDER BY indeks kanıtı, MERGE,
  dynamic SQL, procedure/function sözleşmeleri ve result-set doğrulaması.
- LSP: diagnostics, hover, completion, signature help, definitions, references, semantic tokens,
  workspace diagnostics, stale-index ayrımı ve güvenli code action’lar.
- CLI: index/status/reindex/drop/use/indexes/doctor/drift, lint, objects/source/find/table-columns,
  query-builder, test-skeleton, explain ve MCP komutları.
- Güvenli kod üretimi: bounded edit, confidence kontrolü, yeniden parse, alias/kolon doğrulaması,
  `INSERT`/procedure/dynamic SQL preview’ları; doğrudan dosyaya yazma yok.
- Query builder: tek tablo SELECT, tekil FK JOIN, LSP preview action’ı ve ScriptDom parse doğrulaması.
- Tanı açıklama kataloğu: CLI `explain` ve LSP hover aynı metadata kaynağını kullanıyor.
- Install/publish, temiz HOME, `DOTNET_CLI_HOME` fallback’i ve GitHub Actions smoke akışı.
- FP/FN corpus, normalize contract snapshot’ları, LSP fixture ayrımı ve hedefli smoke suite’leri.
- Etkileşimli indeksleme progress bar’ı; JSON/quiet/CI çıktılarında güvenli biçimde kapalı.

## Öncelik sırasındaki açık işler

### P1 — Performans ölçümü ve hotpath

- [x] Aynı sabit corpus üzerinde parse, collect, ScriptDom generator, SQLite ve LSP sürelerini
  ölçmek için `./test.sh perf` baseline komutunu ekle.
- [x] Parse, collect, ScriptDom generator, SQLite ve LSP için `./test.sh perf` altında
  tekrar edilebilir baseline raporu ekle; optimizasyon öncesi/sonrası değerleri aynı tabloda
  karşılaştırmak sonraki CI trend işine bırakıldı.
- [x] Query/table finder tekrar taramalarını tek AST ziyaretinde birleştir; wall-clock kazanımı
  sınırlı kalsa da aynı `FromClause` yürüyüşü ortadan kaldırıldı ve index smoke doğrulandı.
- [x] `GenerateScript` çağrı/süre ve kaynak dilimi kullanımını benchmark’la; generator işi düşük
  (`--jobs 4` corpus ölçümünde yaklaşık 0,27 s, 9 kaynak dilimi) olduğu için ek optimizasyon uygulanmadı.
- [x] `BatchSplitter` satır dizisi ve ara string kopyalarını ölç; span tabanlı tarama ölçülebilir
  kazanım verdiği için uygulandı. `ReadOnlyMemory<char>` geçişi ayrıca gerekirse değerlendirilecek.
- [x] LSP `AllObjects`/`AllTableColumns` ve kardeş metadata akışlarını workspace belge sürümüne göre
  önbellekle; `tests/perf/lsp-baseline.sh` ile tekrarlı istek baseline’ı ekle.
- [ ] Parse/collect/generator/SQLite/LSP metriklerini CI’da trend olarak sakla.

### P1 — Query builder kapsamı

- [x] Temp table, table variable, parse-failure ve eksik metadata senaryolarını ayrı fixture’larla
  doğrula; belirsiz durumda preview üretme.
- [x] Composite veya birden fazla FK ilişkisinde yalnızca açıklama/selection akışı sun; otomatik JOIN
  seçme; query-builder smoke testi güvenli biçimde reddediyor.

### P1 — Doğruluk ve corpus

- [x] `local-corpus` bulgularını rule bazında özetleyen yeniden üretilebilir rapor ekle; corpus dosyaları
  Git dışında kalır.
- [x] `OPENQUERY`/uzak tablo alias’larının ürettiği 41 `unbound-identifier` FP’sini gider ve regression
  fixture’ına taşı.
- [ ] Kalan düşük hacimli correctness bulgularını (özellikle CTE/derived DML ve unbound scope) tek tek
  incele; yalnızca güvenli alt kümeye alınabilecekleri regression fixture’larına taşı.
- [ ] Yeni bir diagnostic rule eklenirken pozitif fixture, negatif fixture veya bilinçli FN kararı ekle.

### P2 — Refactor ve bakım

- [ ] Performans ölçümleri tamamlandıktan sonra codegraph ile yeni hotpath/refactor adaylarını çıkar.
- [ ] Büyük metotlarda koşul gruplarını anlamlı yardımcı metotlara ayır; davranış değişikliği olmadan,
  gereksiz mikro-soyutlamadan kaçın.
- [ ] README’de mimari, komut sözleşmeleri ve LSP/CLI sınırları değiştikçe güncel tut.

### P2 — Kullanıcı deneyimi ve entegrasyon

- [ ] Neovim canlı veritabanı bağlantısını sqldeps LSP’sinden ayrı katman olarak değerlendir;
  salt-okunur kimlik, TLS doğrulaması ve repo dışı credential yönetimi şartlarını belgeleyin.
- [ ] Watch mode veya PR raporu: drift, lint ve stale indeks sonuçlarını tek makine-okunur raporda birleştir.
- [x] MCP’ye `query_builder` preview aracını güvenli whitelist ve CLI ile aynı `IndexQueryService` sınırı
  üzerinden ekle.
- [x] MCP’ye `explain` aracını CLI/LSP ile ortak tanı kataloğu üzerinden ekle.
- [x] MCP’ye drift işlemini yalnızca indeksin kayıtlı kaynak klasörünü tarayan güvenli whitelist aracı
  olarak ekle.

### En son — Diğer SQL lehçeleri

- [ ] Parser/identifier/formatter seçimini sağlayıcı arayüzüne ayır; T-SQL varsayılanını geriye dönük koru.
- [ ] `languageId`/uzantı/ayar önceliğiyle lehçe seçimini ekle ve ortak diagnostic sözleşmesini sabitle.
- [ ] PostgreSQL sağlayıcısı: `public`, `RETURNING`, recursive CTE, `ILIKE`, dolar-quote, `ON CONFLICT`.
- [ ] MySQL/MariaDB sağlayıcısı: backtick, `LIMIT`, `ON DUPLICATE KEY`, `DELIMITER`.
- [ ] SQLite sağlayıcısı: gevşek tür sistemi, `WITHOUT ROWID`, `UPSERT`, `PRAGMA`.
- [ ] Oracle/PLSQL sağlayıcısı: paketler, bind değişkenleri, `DUAL`, `MERGE`.
- [ ] Her sağlayıcı için ayrı corpus ve LSP regression suite’i ekle.

## Sabit kararlar

- LSP ve MCP şimdilik aynı executable içinde kalacak; ayrı proje ayrımı bağımsız dağıtım veya izolasyon
  ihtiyacı oluşana kadar ertelendi.
- Rename ve formatting desteği kapsam dışı; Neovim için Lua yapılandırması ayrı projede tutuluyor.
- Kod üretimi yalnızca preview/WorkspaceEdit/command olarak sunulacak; otomatik dosya yazımı yapılmayacak.
- Dynamic SQL, belirsiz kolon/ilişki, eksik metadata ve stale indeks durumlarında fail-closed davranılacak.
- Diğer SQL lehçeleri T-SQL doğruluk ve performans sınırları sabitlenmeden başlanmayacak.

## Sık kullanılan doğrulamalar

```sh
./test.sh spec
./test.sh lsp
./test.sh query-builder
./test.sh test-skeleton
./test.sh explain
./test.sh drift
./test.sh install
```
