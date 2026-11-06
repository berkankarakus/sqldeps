# sqldeps yol haritası

Bu dosya yalnızca açık işleri ve kısa karar notlarını tutar. Tamamlanmış ayrıntılı checklist’ler
git geçmişinde ve ilgili smoke/unit testlerinde bulunur.

## Öncelik sırasındaki açık işler

### İndeksleme ve performans

- [ ] Değişen büyük dosyalarda `GO` batch’lerini bağımsız yeniden kullanmayı ölç; bağımlılık kapsamı ve
  dosya-düzeyi lint baskılamaları yanlış sonuç üretmiyorsa batch-level incremental parse ekle.
- [ ] Refactor preview akışında `PreviewCodeActions` ile `DocumentAnalysis.Analyze` aynı SQL’i iki kez parse ediyor;
  ölçüm sonrası analiz sonucunu ve impact bağlamını paylaşarak duplicate parse’ı kaldır.

### Paylaşılan Core kütüphanesi

- [ ] Class library public API sınırını belgeleyip tamamla; mevcut ilk sınırda refactor aday projeksiyonu,
  dependency impact çözümü ve lint suppression sözleşmeleri public, parser/SQLite iç yardımcıları internal kaldı.
- [ ] Core kütüphanesi için API compatibility testlerini genişlet; temel public assembly ve refactor projector
  sözleşmeleri `CoreApiCompatibilityTests` ile korunuyor. DBeaver adapter’ı doğrudan Core’a değil, LSP sözleşmesine
  bağlı tut.
- [x] Java eklenti yerleşimi `editor`, `editor.actions`, `editor.diagnostics` ve `editor.index` paketlerine
  ayrıldı; LSP/commands sınırları korunuyor ve package hareketi sonrası Java/runtime smoke testleri geçiyor.

### T-SQL doğruluk ve corpus incelemesi

- [ ] `local-corpus` içindeki NOLOCK dışı kuralları kural bazında sınıflandır; gerçek FN/FP örneklerini regression
  fixture’larına taşı.
- [ ] `unindexed-*`, tam kapsamlı DML ve dynamic SQL uyarıları için kalan örnekleri incele; yalnızca kanıtlanan
  false-positive’leri düzelt.

### Test ve refactor kapsamı

- [ ] P0 — JSON-RPC/LSP/MCP sınırları: malformed request, framing, stale version, diagnostics ve
  fail-closed code action dallarını doğrudan sözleşme testleriyle koru; smoke testlerini protokol/entegrasyon
  koruması olarak tut.
- [ ] P1 — Analiz doğruluğu: QueryColumnValidator, DependencyCollector, lint suppression ve procedure parameter
  dallarında kanıtlanmış FP/FN örneklerini küçük regression testlerine taşı. Mevcut FP/FN raporu `missing: 0`,
  `regressed: 0`; tek NOLOCK diagnostic’i için duplicate regression smoke testi de var. Kalan iş range/confidence
  kanıtını yüksek riskli kurallarda güçlendirmek.
- [ ] P1 — Güvenli refactor/code action: her öneri için applicability, confidence/evidence, parse sonrası sonuç ve
  yazmama garantisini test et; Core projector tarafında geçerli edit taşıyan stale ve `writesFile=true` action’lar,
  DBeaver adapter tarafında stale/cross-document/overlap/UTF-16 WorkspaceEdit seam’leri korunuyor. Belirsiz metadata
  ve dynamic SQL dallarını tamamla.
- [x] P2 — DBeaver seam’leri: annotation/hover/navigation, tek-bulgu quick fix, stale sonuç ve lifecycle akışları
  config’siz Java testleriyle korunuyor; SWTBot/PDE ve macOS Accessibility son kurulum doğrulamasına bırakıldı.
- [x] P2 — CLI sözleşmeleri: `tests/smoke/cli.sh` option parsing, JSON çıktı ve exit code dallarını doğruluyor;
  pahalı full smoke push öncesine bırakılıyor.
- [x] P3 — MCP sözleşmeleri: tool catalog, argument validation, read-only refactor preview ve structured output
  unit/smoke testleriyle korunuyor; gerçek stdio smoke testi son doğrulama olarak tutuluyor.
- [ ] Refactor edilen her katmanda CodeGraph çağrı grafiğini ve kaynak–test eşleşmesini yeniden doğrula.

### Analiz ve code action yol haritası

- [x] SQL formatlama desteği: T-SQL için `Core.SqlFormatter` parse-doğrulanmış salt preview sağlıyor; LSP
  `textDocument/formatting` bunu tek-document TextEdit olarak sunuyor. Yorumlar/string literal’lar korunuyor,
  `GO` batch sınırları korunuyor; `GO n` güvenli round-trip desteklenene kadar fail-closed.
- [ ] SQL refactor advisor ekle: indeks ve LSP metadata’sından refactor adaylarını, etkilenen bağımlılıkları,
  confidence/evidence bilgisini ve parse-doğrulanmış preview’ı CLI JSON, LSP code action ve MCP read-only
  akışlarında ortak sözleşmeyle sun; stale/eksik metadata ve dynamic SQL’de fail-closed kal. İlk MCP
  `refactor_candidates` projection’ı mevcut güvenli LSP action’larından üretildi ve statik impact sonuçlarını
  taşıyor. `Core/RefactorCandidate.cs` typed sözleşmeyi sağlıyor; CLI/LSP adapter kullanımı ve aday başına
  diagnostic konumuna göre nesne eşlemesi eklendi; preview parse sonrası doğrulanıyor. LSP tarafında impact
  erişilemezse durum açıkça `unavailable` olarak işaretleniyor; seçili veya otomatik indeks varsa standart LSP
  code action akışında dependency impact dolduruluyor.

### DBeaver LSP entegrasyonu

- [x] DBeaver UI test otomasyonu için config’siz seam katmanı: `DocumentSynchronizer`, lifecycle, `DiagnosticManager`, diagnostic
  navigation ve tek-bulgu aralık seçimi için config’siz Java seam testleri var; controller/annotation/navigation
  kararları, stale sonuçlar ve WorkspaceEdit güvenliği korunuyor. Ardından izole workspace ile SWTBot/PDE workbench test fragment’i
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

- [ ] Parser/identifier/formatter seçimini sağlayıcı arayüzüne ayır; T-SQL varsayılanını geriye dönük koru. Ortak
  dialect AST sözleşmesi, küçük deneysel adapter ve upstream `antlr/grammars-v4` T-SQL lexer/parser snapshot’ı
  eklendi; upstream C# parser derleniyor ve parse-only adapter testleri geçiyor. Üretim analizine bağlanmadan
  önce semantic AST mapping ve adapter kapsamı tamamlanmalı.
- [ ] Upstream T-SQL grameri için corpus kabul oranını artır: `GO` batch sınırlarını dışarıda koru, hata kurtarma
  maliyetini sınırla, dependency çıkarımı için gerekli statement/identifier dallarını ziyaret et ve ScriptDom ile
  kabul edilen örneklerde AST eşleşmesini regression fixture’larıyla doğrula. Küçük yerel corpus denemesinde bile
  bazı dosyalar hata recovery sırasında pratik olmayacak kadar yavaşladı; batch başına hata bütçesi, parser giriş
  filtresi ve timeout/iptal davranışı eklenmeden ANTLR üretim analizine bağlanmayacak. ScriptDom üretim yolu
  korunacak.
- [ ] `languageId`/uzantı/ayar önceliğiyle lehçe seçimini ekle ve ortak diagnostic sözleşmesini sabitle.
- [ ] PostgreSQL sağlayıcısı: `public`, `RETURNING`, recursive CTE, `ILIKE`, dolar-quote, `ON CONFLICT`.
- [ ] MySQL/MariaDB sağlayıcısı: backtick, `LIMIT`, `ON DUPLICATE KEY`, `DELIMITER`.
- [ ] SQLite sağlayıcısı: gevşek tür sistemi, `WITHOUT ROWID`, `UPSERT`, `PRAGMA`.
- [ ] Oracle/PLSQL sağlayıcısı: paketler, bind değişkenleri, `DUAL`, `MERGE`.
- [ ] Her sağlayıcı için ayrı corpus ve LSP regression suite’i ekle.

## Sabit kararlar

- LSP ve MCP şimdilik aynı executable içinde kalacak; ayrı proje ayrımı bağımsız dağıtım veya izolasyon
  ihtiyacı oluşana kadar ertelendi.
- Rename desteği kapsam dışı; formatting yalnızca parse-doğrulanmış preview/WorkspaceEdit akışı olarak dahil edildi.
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
