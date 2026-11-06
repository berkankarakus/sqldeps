# sqldeps yol haritası

Bu dosya yalnızca açık işleri ve kısa karar notlarını tutar. Tamamlanmış ayrıntılı checklist’ler
git geçmişinde ve ilgili smoke/unit testlerinde bulunur.

## Öncelik sırasındaki açık işler

### İndeksleme ve performans

- [x] Değişen büyük dosyalarda `GO` batch’lerini bağımsız yeniden kullanmanın güvenli ilk sürümünü ekle. `CachedBatch`
  serializer’ı, kararlı ordinal/hash/başlangıç satırı eşleştirmesi, bozuk payload için fail-closed okuma,
  SQLite `file_batches` tablosu, reuse/parse metrikleri, silinen batch temizliği ve full-vs-incremental
  regression testleri tamamlandı. Tasarım ayrıntıları `INCREMENTAL_INDEX_PLAN.md` içinde.
- [ ] Mevcut local-corpus baseline’ıyla batch-level incremental parse kazanımını ölç; dosya-düzeyi lint
  baskılamaları, suppression metadata değişimi ve stale sonuç risklerini corpus üzerinde doğrula. Baseline (1 worker):
  parse-work `3,94s`, collect-work `0,91s`, toplam `6,59s`; dosya-düzeyi incremental yeniden indeksleme
  parse/collect işini `0,00s` seviyesine indiriyor, SQLite yazımı `0,75s` kalıyor.

### Paylaşılan Core kütüphanesi

- [ ] Class library public API sınırını belgeleyip tamamla; mevcut ilk sınırda refactor aday projeksiyonu,
  dependency impact çözümü ve lint suppression sözleşmeleri public, parser/SQLite iç yardımcıları internal kaldı.
- [ ] Core kütüphanesi için API compatibility testlerini genişlet; temel public assembly ve refactor projector
  sözleşmeleri `CoreApiCompatibilityTests` ile korunuyor. DBeaver adapter’ı doğrudan Core’a değil, LSP sözleşmesine
  bağlı tut.
- [ ] Java yapılandırmasını feature büyüdükçe ölç: önce `ui.index`, sonra gerekirse `ui.diagnostics` alt paketlerine
  ayır; yalnızca bağımlılık yönü daraldığında paket taşı.

### T-SQL doğruluk ve corpus incelemesi

- [ ] `local-corpus` içindeki NOLOCK dışı kuralları kural bazında sınıflandır; gerçek FN/FP örneklerini regression
  fixture’larına taşı.
- [ ] `unindexed-*`, tam kapsamlı DML ve dynamic SQL uyarıları için kalan örnekleri incele; yalnızca kanıtlanan
  false-positive’leri düzelt.

### Test ve refactor kapsamı

- [ ] P0 — JSON-RPC/LSP/MCP sınırları: malformed request, framing, stale version, diagnostics ve
  fail-closed code action dallarını doğrudan sözleşme testleriyle koru; smoke testlerini protokol/entegrasyon
  koruması olarak tut.
- [ ] LSP smoke süresini azalt: bağımsız senaryo izolasyonunu koruyarak mümkün olan testleri tek uzun ömürlü
  süreçte grupla veya önceden derlenmiş apphost kullan; tam suite’in süreç başlatma maliyetini ölç.
- [ ] P1 — Analiz doğruluğu: QueryColumnValidator, DependencyCollector, lint suppression ve procedure parameter
  dallarında kanıtlanmış FP/FN örneklerini küçük regression testlerine taşı. Mevcut FP/FN raporu `missing: 0`,
  `regressed: 0`; duplicate diagnostic kimliği artık kaynak aralığını da içeriyor ve tek NOLOCK ile aynı kuralın
  farklı sütunlardaki bulguları için regression smoke testleri var. Kalan iş range/confidence kanıtını yüksek riskli
  kurallarda güçlendirmek.
- [ ] P1 — Güvenli refactor/code action: her öneri için applicability, confidence/evidence, parse sonrası sonuç ve
  yazmama garantisini test et; Core projector tarafında geçerli edit taşıyan stale ve `writesFile=true` action’lar,
  DBeaver adapter tarafında stale/cross-document/overlap/UTF-16 WorkspaceEdit seam’leri korunuyor. Dynamic SQL
  parameterization için güvenli ve dinamik nesne adı içeren reddedilen LSP preview regression testleri eklendi;
  belirsiz metadata ve kalan code-action dallarını tamamla.
- [ ] P2 — DBeaver seam’leri: annotation/hover/navigation, tek-bulgu quick fix, stale sonuç ve lifecycle akışlarını
  config’siz Java testleriyle koru; SWTBot/PDE yalnızca bu seam’ler sabitlenince ekle.
- [ ] P2 — CLI sözleşmeleri: option parsing, JSON çıktı ve exit code dallarını doğrudan test et; pahalı full smoke’u
  push öncesine bırak.
- [ ] P3 — MCP sözleşmeleri: tool catalog, argument validation, read-only refactor preview ve structured output
  için unit/contract testleri ekle; gerçek stdio smoke testini son doğrulama olarak tut.
- [ ] Refactor edilen her katmanda CodeGraph çağrı grafiğini ve kaynak–test eşleşmesini yeniden doğrula.

### Analiz ve code action yol haritası

- [ ] SQL refactor advisor ekle: indeks ve LSP metadata’sından refactor adaylarını, etkilenen bağımlılıkları,
  confidence/evidence bilgisini ve parse-doğrulanmış preview’ı CLI JSON, LSP code action ve MCP read-only
  akışlarında ortak sözleşmeyle sun; stale/eksik metadata ve dynamic SQL’de fail-closed kal. İlk MCP
  `refactor_candidates` projection’ı mevcut güvenli LSP action’larından üretildi ve statik impact sonuçlarını
  taşıyor. `Core/RefactorCandidate.cs` typed sözleşmeyi sağlıyor; CLI/LSP adapter kullanımı ve aday başına
  diagnostic konumuna göre nesne eşlemesi eklendi; preview parse sonrası doğrulanıyor. LSP tarafında impact
  erişilemezse durum açıkça `unavailable` olarak işaretleniyor; seçili veya otomatik indeks varsa standart LSP
  code action akışında dependency impact dolduruluyor.

### DBeaver LSP entegrasyonu

- [ ] DBeaver UI test otomasyonu ekle: `DocumentSynchronizer`, lifecycle, `DiagnosticManager`, diagnostic
  navigation ve tek-bulgu aralık seçimi için config’siz Java seam testleri var; controller/annotation/navigation
  kararlarını ve stale sonuçları aynı katmanda tamamla. Ardından izole workspace ile SWTBot/PDE workbench test fragment’i
  kurup SQL Editor, annotation hover ve tek-bulgu quick-fix akışını koordinatsız doğrula. macOS Accessibility
  yalnızca son kurulum smoke testi olarak kalsın.
- [ ] Thread/RSS ölçümünü sandbox dışı yerel DBeaver çalıştırmasında doğrula.
- [ ] Yerel doğrulama sonrasında feature/p2 update-site paketlemesini ekle.

### En son — Diğer SQL lehçeleri

- Karar: T-SQL parser’ı olarak ScriptDom korunacak; PostgreSQL dahil yeni lehçelerde ANTLR tabanlı, lehçe başına ayrı
  parser/native-AST adapter’ı kullanılacak. Her adapter native AST’den ortak ve küçük bir analiz modeline
  (statement/batch, table/column reference, alias/scope, literal/type, source range) map edecek; lehçeye özgü
  node’lar ve kurallar native AST’de kalacak. Özel production corpus yoksa ilk doğrulama grammar/vendor örnekleri
  ve sentetik syntax fixture’larıyla yapılacak; metadata olmadan semantic doğruluk iddiasında bulunulmayacak.

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
