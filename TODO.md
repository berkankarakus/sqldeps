# sqldeps yol haritası

Bu dosya yalnızca açık işleri ve kısa karar notlarını tutar. Tamamlanmış ayrıntılı checklist’ler
git geçmişinde ve ilgili smoke/unit testlerinde bulunur.

## Öncelik sırasındaki açık işler

### İndeksleme ve performans

- [ ] Değişen büyük dosyalarda `GO` batch’lerini bağımsız yeniden kullanmayı ölç; bağımlılık kapsamı ve
  dosya-düzeyi lint baskılamaları yanlış sonuç üretmiyorsa batch-level incremental parse ekle.

### Paylaşılan Core kütüphanesi

- [ ] `Core`, `Domain`, `Indexing` ve `Storage` kodunu ayrı bir .NET class library’ye çıkar; CLI/LSP/MCP
  executable’ı yalnızca giriş noktaları ve adapter orchestration olarak bırak.
- [ ] Class library public API sınırını belirle; parser, indeks modeli ve SQLite sözleşmesini desteklenen API
  olarak belgeleyip iç yardımcıları public yapma.
- [ ] Mevcut CLI/LSP/MCP davranışını değiştirmeden project reference geçişi yap ve paketleme/kurulum smoke
  testlerini koru.
- [ ] Core kütüphanesi için ayrı unit/API compatibility testleri ekle; DBeaver adapter’ı doğrudan Core’a değil,
  LSP sözleşmesine bağlı tut.
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
- [ ] P1 — Analiz doğruluğu: QueryColumnValidator, DependencyCollector, lint suppression ve procedure parameter
  dallarında kanıtlanmış FP/FN örneklerini küçük regression testlerine taşı.
- [ ] P1 — Güvenli refactor/code action: her öneri için applicability, confidence/evidence, parse sonrası sonuç ve
  yazmama garantisini test et; belirsiz metadata/dynamic SQL’de negatif testleri öncele.
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
  `refactor_candidates` projection’ı mevcut güvenli LSP action’larından üretildi; CLI/LSP ile ortak typed
  sözleşme ve etkilenen bağımlılık bilgisi sonraki adımda eklenecek.

### DBeaver LSP entegrasyonu

- [ ] DBeaver UI test otomasyonu ekle: önce controller/annotation/navigation kararlarını SWT bağımsız Java seam
  testleriyle kapsa; ardından izole workspace ile SWTBot/PDE workbench test fragment’i kurup SQL Editor,
  annotation hover ve tek-bulgu quick-fix akışını koordinatsız doğrula. macOS Accessibility yalnızca son
  kurulum smoke testi olarak kalsın.
- [ ] Thread/RSS ölçümünü sandbox dışı yerel DBeaver çalıştırmasında doğrula.
- [ ] Yerel doğrulama sonrasında feature/p2 update-site paketlemesini ekle.

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
