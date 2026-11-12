# sqldeps roadmap

Bu dosya yalnızca tamamlanmamış işleri ve kalıcı kapsam kararlarını içerir. Tamamlanan işler ilgili
commit, unit test ve smoke test geçmişinde tutulur.

## Sıradaki işler

### Mimari ve bağımlılık yönü

- [ ] Gerçek assembly sınırlarını tamamla: `Domain` ve `SqlDeps.Analysis` artık bağımsız; `DocumentDiagnosticAnalyzer` ve `DocumentParseValidator` document diagnostics/parse validation için public Analysis sözleşmesi sağlıyor; `Core/SqlDeps.Core.csproj` Analysis'i proje referansıyla tüketiyor. `DependencyCollectionResult` ve `DependencyContributionCollector` collector sınırını sağlıyor; kalan iş, `DynamicSqlParameterizer` gibi Core'un doğrudan internal Analysis erişimlerini aynı şekilde daraltıp `InternalsVisibleTo("SqlDeps.Core")` kaydını kaldırmak ve wildcard compile sınırını ayrı batch'te incelemek.
- [ ] Refactor'ları regresyon kapılarıyla ilerlet: her batch tek bir davranışsal sınırı değiştirsin, önce hedef testleri sonra CodeGraph çağrı/test eşleşmesini ve batch sonunda tam suite'i çalıştır. Başarısız hedef testte sonraki refactor'a geçme.
- [ ] `ConstantVariableEnvironment` için üç fazlı ayrıştırma yap: (1) mevcut branch-state sözleşmesini `ConstantVariableAnalysisTests` ile dondur; bilinen/bilinmeyen `IF`, sıfır-kez-çalışabilen `WHILE`, `TRY/CATCH`, nested branch, temporal değişken ve bilinmeyen atama durumlarını koru; (2) `CaptureState`/`RestoreState`/`MergeStates` ve immutable state modelini visitor/literal çözümlemesinden ayır; (3) yalnızca testler sabit kaldıktan sonra branch traversal ile literal çözümlemeyi ayrı sorumluluklara böl. State birleşimi, fail-closed invalidation ve `StringComparer.OrdinalIgnoreCase` davranışı değişmeyecek.
- [ ] DBeaver normal/nested SQL editor refactor'ını yaşam döngüsünden ayır: önce normal editör ve nested bridge için init/activation, readiness, restart, diagnostics, code action, index selection ve cleanup karakterizasyonunu seam testleriyle sabitle; sonra yalnızca ortak instance operasyonlarını composition facade'ına taşı. Static registry, URI/workspace üretimi, gecikmeli nested activation ve cleanup ayrı adapter'larda kalacak; inheritance veya ortak registry yapılmayacak. Her iki host için smoke/runtime testleri aynı kalmadan ortaklaştırmayı ilerletme.
- [ ] `CodeActionController` ayrıştırmasını DBeaver host ortaklaştırmasından sonra yap: request/range seçimi, action normalizasyonu, UI dialog/preview ve WorkspaceEdit uygulamasını ayrı seam'lerle doğrula; UI görünümü değişmeden yalnızca sorumluluk sınırlarını taşı.
- [ ] Proje sınırları netleşmeden `Core/SqlDeps.Core.csproj` içindeki wildcard compile ile `Analysis`/`Indexing`/`Storage` bağımlılıklarını yeniden düzenleme; assembly sözleşmesi ve CodeGraph kaynak-test eşleşmesi hazır olduğunda ayrı batch olarak ele al.

### Performans ve parser

- [ ] ScriptDom parse/recovery yolunu ayrı bir benchmark ile incele; 4.397 dosya ve 14.880 batch içeren yerel corpus'ta parse hatası yokken exception recovery maliyeti yüksek. `jobs=4` en iyi ölçülen paralellik (3,53 sn); `jobs=8` daha hızlı değil (3,69 sn). Upstream fork'taki `perf/input-buffer` (`d5640b6`) ve CharQueue (`93a6966`) adayları source build ile doğrulandı; CharQueue, aynı çıktıyı koruyarak üç A/B koşulunda yaklaşık %2–3 daha hızlı ölçüldü. Paket eşdeğer build ve regression doğrulaması tamamlanmadan sqldeps'e bağlama.
- [ ] Phase-one kontrol akışındaki `PhaseOneBatchException`, `PhaseOnePartialAstException` ve `PhaseOneConstraintException` kullanımını exception dışı state/result akışına dönüştürme seçeneğini prototiple; yalnızca tam regression sonrası karar ver.
- [ ] Lexer'daki geçersiz karakter recovery yolunu incele; hata konumu ve diagnostics davranışı korunabiliyorsa `NoViableAltForCharException` sayısını azaltacak error-token yaklaşımını ölç.
- [ ] Parser/token-buffer instance reuse için thread-local havuz prototipi oluştur; state reset, token ömrü ve paralel index güvenliğini benchmark ve regression ile doğrula.

### CodeGraph destekli parser analizi

- [ ] Generated parser dosyaları 1 MB sınırını aştığında CodeGraph için method sınırlarından otomatik staging/chunk akışı ekle; mevcut TSql160 analizi geçici script ile doğrulandı.

### Analiz kapsamı

- [ ] Sabit ifade analizini genişlet: güvenli variable environment aynı batch'teki literal, bilinen değişkenden veya güvenli sayısal ifadeden gelen `DECLARE`/`SET` atamalarını, string ve temporal ifadeleri, aynı değerle birleşen branch'leri, bilinen koşullu searched `CASE` dallarını ve bilinen girdili simple `CASE` dallarını çözüyor; kalan iş, prosedür parametrelerini ve daha hassas branch provenance'ını false-positive üretmeden değerlendirmek.
- [ ] `INSERT ... SELECT` kaynak `WHERE`/`HAVING` koşulları ve inner `JOIN ON` için kesin yanlış koşul tanısı tamamlandı; dış birleşimlerde semantik değişiklik yaratabilecek koşul yayılımını ayrı değerlendir.
- [ ] UPDATE ve `INSERT ... SELECT` atamalarında aynı ailedeki kesin `CASE`, `CAST` ve sayısal aritmetik ifadelerinin tip bilgisini taşıma tamamlandı; kalan iş, daha fazla SQL Server dönüşümünü false-positive üretmeden kapsamaktır.
- [ ] Hesaplanan SELECT ifadeleri için yalnızca preview/WorkspaceEdit tabanlı code generation adaylarını değerlendir: eksik alias ekleme ve güvenli `ORDER BY` ordinal değiştirme; isim çakışması, aggregate ve belirsiz ifadelerde öneri verme.
- [ ] `select-ineffective-filter` için uygun zamanda güvenli rewrite adayı değerlendir: yalnızca parse-doğrulanmış `WHERE`/`HAVING` aralığı, yorum/hint ve üç-değerli mantık kontrolü, diff/preview ve açık experimental opt-in ile sun; otomatik dosya yazma yapma.
- [ ] Branch provenance analizini genişlet: kesin `IF`/`ELSE` koşullarında yalnızca erişilebilir branch, bilinmeyen koşullarda tüm yolların aynı literal değeri, bilinen koşullu searched `CASE` dalları ve `TRY/CATCH` birleşimi çözümleniyor; döngülerde sıfır-kez-çalışma ihtimali korunuyor. Kalan iş, nested path provenance ve prosedür parametrelerinde fail-closed davranışı tamamlamak.
- [ ] Nullability akışını genişlet: DML atamalarında `ISNULL` ilk/fallback argümanı ile `COALESCE` kesin non-null sonuçları, `NULLIF(..., NULL)` güvenli sonucu, eşdeğer `NULLIF` argümanlarının kesin NULL sonucu ve `NOT IN` alt sorgusundaki `IS NOT NULL` filtresi kullanılıyor; kalan iş, bu akışı diğer karşılaştırma/`JOIN` tanılarına üç-değerli mantığı koruyarak taşımak.
- [ ] Subquery ve CTE kapsamını derinleştir: correlated reference, derived-table çıktısı, kolon türü ve nullability bilgisini iç sorgudan dış sorguya güvenli biçimde aktar.
- [ ] DML veri akışını tamamla: `INSERT ... SELECT`, `UPDATE ... FROM`, `DELETE ... JOIN`, derived source/CTE ve sabit `MERGE` match/action koşulları artık etkisizliği veya erişilemezliği raporluyor; kalan iş correlated/nested write path'leri ve daha karmaşık join koşullarını aynı çiftlenmeyen tanı sözleşmesine taşımak.
- [ ] Implicit conversion ve SARGability analizini genişlet: veri tipi önceliği, kolon üzerindeki `CAST`/`CONVERT`/fonksiyon ve indeks aramasını engelleyen kalıpları kanıtla birlikte raporla.
- [ ] Kolon lineage ve çağrı hiyerarşisi çıkar: prosedür/CTE/alt sorgu kaynaklarını kolon düzeyinde izleyip LSP ve MCP için kanıt zinciri üret.
- [ ] Güvenlik analizini genişlet: dynamic SQL parametre akışı, kullanıcı girdisinin SQL'e eklenmesi, `EXEC` yerine `sp_executesql` kullanımı ve yetki/transaction risklerini fail-closed raporla.
- [ ] Statik metadata'dan indeks adayları üret: `WHERE`, `JOIN`, `ORDER BY` ve `GROUP BY` kolonlarını birleştir; önerileri kesin plan sonucu değil, kanıt ve belirsizlik seviyesiyle sun.

### Core API ve dağıtım

- [ ] DBeaver LSP sürecinde thread/RSS ölçümünü sandbox dışı yerel çalışmada doğrula. Baseline ölçümü geçti: tek paylaşımlı süreçte startup 239.7 ms, diagnostics 56.9 ms; 5 süreçte ortalamalar sırasıyla 45.3 ms ve 10.4 ms; 20 süreçte 11.2 ms ve 2.7 ms. Ortam `ps` erişimini engellediği için thread/RSS alanları hâlâ `unavailable`.

### DBeaver plugin, biçimlendirici ve kod kalitesi

- [ ] DBeaver UX backlog: code action dialogına arama/filtre ve daha belirgin klavye akışı ekle; diagnostics dialogında severity/rule filtreleme, kopyalama ve kaynak konumuna gitme aksiyonlarını değerlendir; sqldeps menüsünde aktif index, LSP durumu ve experimental durumunu görünür özetle.
- [ ] DBeaver'da `DiagnosticTag.Unnecessary` ile silikleştirilen kesin etkisiz/ulaşılamaz SQL annotation'ını açık/koyu gerçek temalarda görsel olarak doğrula.
- [ ] DBeaver runtime'ında `select-ineffective-filter` gibi bilgi seviyeli bulguların özel `org.sqldeps.dbeaver.info` annotation tipiyle editörde görünür olduğunu ve quick-fix akışının aynı aralığı seçtiğini elle doğrula.
- [ ] `TextResultDialog` ve `DiagnosticDialog` için DBeaver'ın gerçek açık/koyu temalarında görsel doğrulama yap.
- [ ] DBeaver runtime ortamında iki farklı workspace ile otomatik indeks seçiminin doğru kaynağı kullandığını elle doğrula.
- [ ] DBeaver runtime ortamında symlink’li workspace kökü ve symlink `.sqldeps` reddi elle doğrula.
- [ ] Biçimlendirici (`SqlLayout*`): tek SELECT öğesi ve WHERE `AND`/`OR` yerleşimi düzeltildi; `HAVING`, `GROUP BY`, `ORDER BY` ve `CASE` içindeki alt sorgular kural dışı, `CREATE FUNCTION`, `PIVOT`, `OPENJSON` ve dinamik SQL denenmedi. Gerçek, uzun prosedürlerde elle doğrula.

### Diğer SQL lehçeleri

- [ ] Parser/identifier/formatter seçimini sağlayıcı arayüzüne ayır; T-SQL varsayılanını koru.
- [ ] `languageId`/uzantı/ayar önceliğiyle lehçe seçimini ve ortak diagnostic sözleşmesini ekle.
- [ ] PostgreSQL, MySQL/MariaDB, SQLite ve Oracle/PLSQL sağlayıcılarını ayrı corpus ve LSP regression suite’leriyle ilerlet.

## Kalıcı kapsam kararları

- LSP code-action payload üretimi LSP adapter’ında kalır; Core yalnızca parse-doğrulanmış preview, ranking ve dependency evidence üretir.
- `IndexQueryService` tek SQLite backend’i varken `Storage/IndexDatabase` kullanır; ikinci backend ihtiyacı doğmadan yapay bir repository interface eklenmez.
- Domain, Analysis ve Storage tipleri kendi namespace'lerinde tutulur; bağımsız Indexing modelleri taşındıkça CLI/host katmanı bunları `using` ile tüketir.
- LSP ve MCP aynı executable içinde kalır; bağımsız dağıtım ihtiyacı oluşmadıkça ayrılmaz.
- Rename kapsam dışıdır. Formatting yalnızca `textDocument/formatting` isteği ve DBeaver'daki "Format SQL Script" komutuyla sınırlıdır; code action olarak sunulmaz ve biçimlendirme ayarı eklenmez. Neovim Lua yapılandırması ayrı projededir.
- Kod üretimi yalnızca parse-doğrulanmış preview/WorkspaceEdit/command olarak sunulur; otomatik dosya yazılmaz.
- Dynamic SQL, belirsiz kolon/ilişki, eksik metadata ve stale indeks durumlarında fail-closed davranılır.
- SWTBot/PDE UI otomasyonu kapsam dışıdır; Java seam testleri ve LSP smoke testleri kullanılır.
- Diğer SQL lehçeleri T-SQL doğruluk, test kapsamı, performans ve dağıtım akışları sabitlenmeden başlanmaz.
