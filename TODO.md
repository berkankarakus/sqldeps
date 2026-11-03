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

## Öncelik sırasındaki açık işler

### P1 — Performans ölçümü ve hotpath

- [ ] Aynı sabit corpus üzerinde parse, collect, ScriptDom generator, SQLite ve LSP sürelerini
  önce/sonra karşılaştıran ölçüm raporu ekle.
- [ ] `AliasesIn` ve query/table/column finder tekrar taramalarını tek AST ziyaretinde birleştirmeyi
  benchmark’la; doğruluk regression’ları korunmuyorsa değişiklik yapma.
- [ ] `GenerateScript` için kaynak metin dilimi ve lazy metadata üretimini benchmark’la; yalnızca
  ölçülebilir kazanım varsa uygula.
- [ ] `BatchSplitter`/parse ara string kopyalarını ölç; `ReadOnlyMemory<char>` geçişini ancak kazanım
  kanıtlanırsa değerlendir.
- [ ] LSP `AllObjects`/`AllTableColumns` snapshot’larını workspace sürümüne göre önbelleklemenin
  allocation ve yanıt süresine etkisini ölç.
- [ ] Parse/collect/generator/SQLite/LSP metriklerini CI’da trend olarak sakla.

### P1 — Query builder kapsamı

- [ ] Temp table, table variable, parse-failure ve eksik metadata senaryolarını ayrı fixture’larla
  doğrula; belirsiz durumda preview üretme.
- [ ] Composite veya birden fazla FK ilişkisinde yalnızca açıklama/selection akışı sun; otomatik JOIN
  seçme.

### P1 — Doğruluk ve corpus

- [ ] `local-corpus` içindeki tüm tespitleri rule bazında incele; FP/FN örneklerini çıkar ve yalnızca
  güvenli alt kümeye alınabilecekleri regression fixture’larına taşı.
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
- [ ] MCP araçlarına query-builder preview, drift ve explain işlemlerini güvenli whitelist üzerinden ekle.

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
