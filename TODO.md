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

### T-SQL doğruluk ve corpus incelemesi

- [ ] `local-corpus` için ayrı bir LSP document-analysis pass’i çalıştır; subquery, table variable, temp table,
  correlated scope ve alias diagnostics için gerçek FP/FN örneklerini küçük regression fixture’larına taşı. Static
  index-lint snapshot’ı 4.397 dosyada 0 parse error/20 parse warning ile güncellendi; document-scope pass ve
  kapsam dışı identifier kurallarının kalan incelemesi devam ediyor.
- [ ] `unindexed-*`, tam kapsamlı DML ve dynamic SQL uyarıları için kalan örnekleri incele; yalnızca kanıtlanan
  false-positive’leri düzelt.

### Test ve refactor kapsamı

- [ ] Test döngüsünü hızlandır: focused smoke ve `unit --no-build` mevcut çıktıları yeniden kullanıyor,
  `-m:1` build maliyetini sınırlıyor; tam suite’i batch sonuna bırak. VSTest’in sandbox TCP listener bağımlılığını
  ortadan kaldıracak yerel runner veya yapılandırma seçeneğini araştır.
- [ ] P1 — Analiz doğruluğu: QueryColumnValidator, DependencyCollector, lint suppression ve procedure parameter
  dallarında kanıtlanmış FP/FN örneklerini küçük regression testlerine taşı. Subquery/derived alias scope coverage’ı
  unit ve LSP smoke testleriyle korunuyor; named/positional procedure parameter dalları, missing-parameter
  severity/range sözleşmesi, joined DELETE target alias binding ve DML condition findings’in severity/range
  sözleşmesi, QueryColumnValidator’ın unknown-table/ambiguous-table/ambiguous-column severity/range sözleşmesi,
  table variable/temp table/correlated alias scope coverage’ı ve
  DependencyCollector’ın unbound-identifier/table-name-after-alias/invalid-alias-reference range sözleşmesi ve lint
  suppression sonrası metadata korunumu da test ediliyor. Kalan iş
  range/confidence kanıtını diğer yüksek riskli kurallarda güçlendirmek.
- [ ] P1 — Güvenli refactor/code action: her öneri için applicability, confidence/evidence, parse sonrası sonuç ve
  yazmama garantisini test et; `WritesFile=true`, boş applicability, bilinmeyen confidence ve bozuk evidence
  references değerleri fail-closed filtreleniyor; alias/kolon ve temel SQL quick-fix’leri ortak candidate metadata
  taşıyor. Repository-context, result-set, suppression ve explanation-only eylemleri de applicability metadata’sı
  taşıyor; suppression eylemleri advisor adaylarından açıkça ayrı tutuluyor. Varsayılan kind/optional
  metadata, diagnostic-confidence önceliği, bilinmeyen confidence, non-array evidence ve explicit
  non-candidate action dalları fail-closed regression testleriyle korunuyor. Kalan belirsiz metadata ve code-action
  dallarını tamamla.
- [ ] Refactor edilen her katmanda CodeGraph çağrı grafiğini ve kaynak–test eşleşmesini yeniden doğrula. İlk okunabilirlik
  batch’inde `RefactorCandidatePreview` ve `LanguageServer.CodeActions` akışları adlandırılmış yardımcı metotlara ayrıldı;
  sonraki adaylar CodeGraph çıktısına göre seçilecek.

### Analiz ve code action yol haritası


### Güvenli refactor advisor alt görevleri

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

- [x] P2 — Komutları keşfedilebilir yap: SQL Editor context menu ve toolbar temel analiz, diagnostics ve code action
  komutlarını içeriyor; üç varsayılan kısayol ve Eclipse Quick Access komut adlarını görünür kılıyor.
- [ ] P2 — Deneysel öneri ayarını kalıcı ve açıklanabilir yap: tercih kaydı, güvenlik açıklaması, etkinlik kapsamı
  ve yeniden başlatma etkisi kullanıcıya gösterilsin.
- [x] Repo-geneli suppression code action LSP’de `.sqldeps` WorkspaceEdit olarak sunuluyor; DBeaver adapter’ı
  yalnızca mevcut, normal `.sqldeps` dosyasına güvenli preview/onay ve içerik değişikliği kontrolüyle uyguluyor.

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
- DBeaver Java paket sınırları ölçüldü: `editor.index`, `editor.diagnostics`, `editor.actions`, `lsp`
  ve `commands` ayrımı mevcut; bağımlılık yönünü daraltmadan yeni paket taşıması yapılmayacak.
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
