# sqldeps yol haritası

Bu dosya yalnızca açık işleri ve kısa karar notlarını tutar. Tamamlanmış ayrıntılı checklist’ler
git geçmişinde ve ilgili smoke/unit testlerinde bulunur.

## Global uygulama sırası

1. **Kontrat ve test güvenliği:** P0 JSON-RPC/LSP/MCP sınırlarını, ardından CLI sözleşmelerini doğrudan testlerle
   sabitle. Her sonraki batch’te odak test + eşleşen smoke çalıştır; tam suite’i yalnızca mantıksal batch sonunda koş.
2. **T-SQL doğruluğu:** `local-corpus` içindeki NOLOCK dışı FP/FN örneklerini, özellikle subquery, table variable,
   temp table, dynamic SQL ve DML kapsamlarını kural bazında sınıflandırıp küçük regression fixture’larına taşı.
3. **DBeaver temel UX:** Aktif indeks göstergesi, no-index/stale yönlendirmesi ve LSP başlatma/yeniden başlatma
   durumlarını görünür yap. Bu akışlar sabitlenmeden daha büyük UI otomasyonuna geçme.
4. **Core API sınırı:** Public API dokümantasyonunu tamamla, compatibility testlerini genişlet ve CodeGraph ile
   kaynak–test/dependency yönünü doğrula. Java paketlerini yalnızca gerçek bağımlılık daralması varsa taşı.
5. **Güvenli analiz ve kod üretimi:** Refactor advisor’ı ortak metadata/evidence sözleşmesiyle ekle; query builder,
   code action ve MCP preview akışlarını parse-doğrulanmış, fail-closed ve otomatik dosya yazmayan şekilde genişlet.
6. **DBeaver entegrasyon derinliği:** Ana SQL Editor ve nested Source Editor parity’sini, code action/diagnostic
   önizlemelerini ve Java seam testlerini tamamla; sonra sınırlı SWTBot/PDE senaryolarını ekle.
7. **DBeaver gelişmiş UX ve kaynak ölçümü:** İndeks seçim dialogu, diagnostics filtreleme, context menu/kısayollar,
   dil tutarlılığı ve deneysel ayar kalıcılığını tamamla; ardından sandbox dışı thread/RSS ölçümü yap.
8. **Dağıtım:** Yerel doğrulama sonuçları sabitlenince feature/p2 update-site paketlemesini ekle.
9. **Diğer SQL lehçeleri:** T-SQL doğruluk, test kapsamı, performans ve dağıtım akışları oturduktan sonra provider
   arayüzü ve PostgreSQL → MySQL/MariaDB → SQLite → Oracle sırasıyla ilerle.

## Öncelik sırasındaki açık işler

### İndeksleme ve performans

### Paylaşılan Core kütüphanesi

- [ ] Class library public API sınırını belgeleyip tamamla; mevcut ilk sınırda refactor aday projeksiyonu,
  dependency impact çözümü ve lint suppression sözleşmeleri public, parser/SQLite iç yardımcıları internal kaldı.
- [ ] Core kütüphanesi için API compatibility testlerini genişlet; temel public assembly ve refactor projector
  sözleşmeleri `CoreApiCompatibilityTests` ile korunuyor. DBeaver adapter’ı doğrudan Core’a değil, LSP sözleşmesine
  bağlı tut.
- [ ] Java yapılandırmasını feature büyüdükçe ölç: önce `ui.index`, sonra gerekirse `ui.diagnostics` alt paketlerine
  ayır; yalnızca bağımlılık yönü daraldığında paket taşı.

### T-SQL doğruluk ve corpus incelemesi

- [ ] `local-corpus` için ayrı bir LSP document-analysis pass’i çalıştır; subquery, table variable, temp table,
  correlated scope ve alias diagnostics için gerçek FP/FN örneklerini küçük regression fixture’larına taşı. Static
  index-lint snapshot’ının kapsam dışı identifier kurallarını bu pass ile tamamla.
- [ ] `unindexed-*`, tam kapsamlı DML ve dynamic SQL uyarıları için kalan örnekleri incele; yalnızca kanıtlanan
  false-positive’leri düzelt.

### Test ve refactor kapsamı

- [ ] Test döngüsünü hızlandır: focused smoke/`--no-build` varsayılanını koru, tam suite’i batch sonuna bırak,
  VSTest’in sandbox TCP listener bağımlılığını ortadan kaldıracak yerel runner veya yapılandırma seçeneğini araştır.
- [x] P0 — JSON-RPC/LSP/MCP sınırları: malformed request, framing, stale version, diagnostics ve
  fail-closed code action dalları doğrudan unit/contract testleriyle korunuyor; smoke testleri protokol/entegrasyon
  koruması olarak tutuluyor.
- [ ] P1 — Analiz doğruluğu: QueryColumnValidator, DependencyCollector, lint suppression ve procedure parameter
  dallarında kanıtlanmış FP/FN örneklerini küçük regression testlerine taşı. Subquery/derived alias scope coverage’ı
  unit ve LSP smoke testleriyle korunuyor; named/positional procedure parameter dalları, missing-parameter
  severity/range sözleşmesi ve joined DELETE target alias binding de korunuyor. Kalan iş range/confidence kanıtını
  yüksek riskli kurallarda güçlendirmek.
- [ ] P1 — Güvenli refactor/code action: her öneri için applicability, confidence/evidence, parse sonrası sonuç ve
  yazmama garantisini test et; belirsiz metadata ve kalan code-action dallarını tamamla.
- [ ] P2 — DBeaver seam’leri: annotation/hover/navigation, tek-bulgu quick fix, stale sonuç ve lifecycle akışlarını
  config’siz Java testleriyle koru; SWTBot/PDE yalnızca bu seam’ler sabitlenince ekle.
- [ ] P2 — CLI sözleşmeleri: option parsing, JSON çıktı ve exit code dallarını doğrudan test et; pahalı full smoke’u
  push öncesine bırak.
- [x] P3 — MCP tool catalog ve read-only refactor preview kontratları; stdio smoke akışıyla birlikte korunuyor.
- [x] P3 — MCP argument validation ve structured output edge-case’leri doğrudan contract smoke testleriyle
  korunuyor; gerçek stdio smoke testi son doğrulama olarak tutuluyor.
- [ ] Refactor edilen her katmanda CodeGraph çağrı grafiğini ve kaynak–test eşleşmesini yeniden doğrula.

### Analiz ve code action yol haritası

- [x] Diagnostic açıklamalarını tüm kataloglanmış kurallar için LSP code action ve DBeaver preview akışında görünür
  yap; açıklama-only eylemler dosya yazmadan çalışır ve review-only kuralların mevcut adlarını korur.
- [x] Büyük LSP belgelerinde güvenli limited/partial analiz ekle; tamamlanan batch'leri koru, `partial-analysis`
  tanısını düşük güvenle bildir ve partial sonucu temiz raporlama.
- [x] Repository-aware SQL context ekle; seçili indexed object için uses, referenced-by ve reverse impact özetini
  LSP code action, executeCommand ve hover üzerinden salt-okunur sun.
- [x] SQL refactor advisor temel akışını ekle: indeks ve LSP metadata’sından refactor adaylarını, etkilenen
  bağımlılıkları, confidence/evidence bilgisini ve parse-doğrulanmış preview’ı CLI JSON, LSP code action ve MCP
  read-only akışında ortak sözleşmeyle sun; stale/eksik metadata ve partial dynamic-scope adaylarını fail-closed
  filtrele, güvenli sp_executesql preview’larını kanıtlı ayrı akışta koru.

### Güvenli refactor advisor alt görevleri

- [x] Advisor adaylarını rule, confidence, evidence ve etki alanına göre kararlı biçimde sırala; aynı adayı
  CLI, LSP ve MCP’de tek ortak metadata sözleşmesiyle göster.
- [x] Advisor preview’larını parse sonrası doğrula; stale indeks ve eksik impact durumlarında tüm istemci
  akışlarında otomatik refactor adayı üretimini kapat, partial dynamic-scope adaylarını filtrele.
- [x] Stale-index fail-closed davranışı için freshness unit testi ve CLI contract smoke eklendi.
- [x] Dynamic-scope impact’in `partial` işaretlendiğini ve güvenli advisor adayı üretmediğini LSP regression testiyle
  koru.
- [x] Eksik diagnostic source object ve indeks metadata’sında `unavailable` impact’in güvenli aday üretmediğini LSP
  regression testleriyle koru.
- [x] `table-name-after-alias` adayının current index ile parse-valid `alias.column` preview ürettiğini regression
  fixture’ıyla koru.
- [x] Derived table, CTE, correlated subquery ve duplicate-alias scope’ları için LSP contract smoke coverage’ını
  koru; yeni kanıtlanmış FP/FN örnekleri local-corpus incelemesine bırakıldı.
- [ ] CodeGraph refactor döngüsünü advisor değişikliklerinden sonra çağrı grafiği ve kaynak–test eşleşmesiyle
  zorunlu doğrulama adımı olarak koru.

### DBeaver LSP entegrasyonu

- [ ] DBeaver UI test otomasyonu ekle: `DocumentSynchronizer`, lifecycle, `DiagnosticManager`, diagnostic
  navigation ve tek-bulgu aralık seçimi için config’siz Java seam testleri var; controller/annotation/navigation
  kararlarını ve stale sonuçları aynı katmanda tamamla. Ardından izole workspace ile SWTBot/PDE workbench test fragment’i
  kurup SQL Editor, annotation hover ve tek-bulgu quick-fix akışını koordinatsız doğrula. macOS Accessibility
  yalnızca son kurulum smoke testi olarak kalsın.
- [ ] Thread/RSS ölçümünü sandbox dışı yerel DBeaver çalıştırmasında doğrula.
- [ ] Yerel doğrulama sonrasında feature/p2 update-site paketlemesini ekle.

### DBeaver UX iyileştirmeleri

- [x] P0 — Aktif indeks göstergesi ekle: editör status bar’ında indeks adı ile otomatik/manuel seçim modu
  görünür; dialogdaki `(active)` etiketi DBeaver’ın gerçek seçimiyle tutarlı.
- [x] P0 — Aktif indeks göstergesini stale/no-index durumuyla tamamla; LSP’nin `stale-index` tanısı status line’da
  eyleme dönük biçimde görünür.
- [x] P0 — Stale/no-index status line’ı `Reindex sqldeps Index` ve `Select sqldeps Index...` eylemlerine yönlendiriyor;
  yeniden indeksleme komutu aktif editör/nested editor oturumuna bağlandı.
- [x] P0 — LSP yaşam döngüsü geri bildirimi eklendi: başlatılıyor, analiz ediliyor, hazır, yeniden başlatılıyor,
  yeniden indeksleniyor ve hata durumları status bar’da gösteriliyor; indeks değişimi sessiz restart yapmıyor.
- [x] P0 — No-index ve stale-index akışları yönlendiriliyor: ilk kullanımda indeks seçimi, stale sonuçta reindex ve
  indeks seçme komutları status bar mesajında gösteriliyor.
- [ ] P1 — Ana SQL Editor ile nested Source Editor komutlarını eşitle; özellikle code actions, indeks seçimi,
  diagnostics ve aktif komut enablement davranışlarını aynı bağlam kurallarıyla sun.
- [x] P1 — İndeks seçim dialogunu zenginleştir: kaynak kökü, dosya/nesne sayısı, son güncelleme ve stale durumu
  göster; metadata okunamazsa unknown olarak göster.
- [x] P1 — Code action UX’ini güçlendir: tek eylemde bile kısa old/new önizleme ve geri alma görünürlüğü,
  listede rule/severity/confidence/evidence bilgisi ve salt-okunur preview ile gerçek edit ayrımı sağla.
- [x] P1 — Diagnostics görünümünü iyileştir: toplam sayı, severity ve rule adı özeti ile Problems/annotation
  navigasyonu; tanı yoksa açık “no findings” geri bildirimi ver.
- [ ] P2 — Komutları keşfedilebilir yap: SQL Editor context menu, toolbar/quick-access ve varsayılan kısayollar;
  yalnızca üst menüdeki `sqldeps` alt menüsüne bağımlılığı azalt.
- [ ] P2 — Dil ve terminolojiyi tutarlılaştır: kullanıcıya görünen İngilizce/Türkçe mesajları tek bir UI diliyle
  standardize et; teknik hata ile kullanıcı eylemi önerisini ayır.
- [ ] P2 — Deneysel öneri ayarını kalıcı ve açıklanabilir yap: tercih kaydı, güvenlik açıklaması, etkinlik kapsamı
  ve yeniden başlatma etkisi kullanıcıya gösterilsin.

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
