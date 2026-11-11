# sqldeps roadmap

Bu dosya yalnızca tamamlanmamış işleri ve kalıcı kapsam kararlarını içerir. Tamamlanan işler ilgili
commit, unit test ve smoke test geçmişinde tutulur.

## Sıradaki işler

### Mimari ve bağımlılık yönü

- [ ] Gerçek assembly sınırlarını tamamla: `Domain` ve `SqlDeps.Analysis` artık bağımsız; `Core/SqlDeps.Core.csproj` Analysis'i proje referansıyla tüketiyor. `DependencyCollectionResult` ve `DependencyContributionCollector` collector sınırını sağlıyor; kalan iş, LSP/Core'un `AnalysisResult` ve validator internals'ına olan friend-assembly erişimini dar bir analiz sözleşmesine indirmek.

### Performans ve parser

- [ ] ScriptDom parse/recovery yolunu ayrı bir benchmark ile incele; 4.397 dosya ve 14.880 batch içeren yerel corpus'ta parse hatası yokken exception recovery maliyeti yüksek. `jobs=4` en iyi ölçülen paralellik (3,53 sn); `jobs=8` daha hızlı değil (3,69 sn). Upstream fork'taki `perf/input-buffer` (`d5640b6`) ve CharQueue (`93a6966`) adayları source build ile doğrulandı; CharQueue, aynı çıktıyı koruyarak üç A/B koşulunda yaklaşık %2–3 daha hızlı ölçüldü. Paket eşdeğer build ve regression doğrulaması tamamlanmadan sqldeps'e bağlama.
- [ ] Phase-one kontrol akışındaki `PhaseOneBatchException`, `PhaseOnePartialAstException` ve `PhaseOneConstraintException` kullanımını exception dışı state/result akışına dönüştürme seçeneğini prototiple; yalnızca tam regression sonrası karar ver.
- [ ] Lexer'daki geçersiz karakter recovery yolunu incele; hata konumu ve diagnostics davranışı korunabiliyorsa `NoViableAltForCharException` sayısını azaltacak error-token yaklaşımını ölç.
- [ ] Parser/token-buffer instance reuse için thread-local havuz prototipi oluştur; state reset, token ömrü ve paralel index güvenliğini benchmark ve regression ile doğrula.

### CodeGraph destekli parser analizi

- [ ] Generated parser dosyaları 1 MB sınırını aştığında CodeGraph için method sınırlarından otomatik staging/chunk akışı ekle; mevcut TSql160 analizi geçici script ile doğrulandı.

### Analiz kapsamı

- [ ] Sabit ifade analizini genişlet: güvenli variable environment aynı batch'teki tek literal `DECLARE`/`SET` atamasını ve güvenli toplama/çıkarma/çarpma ifadelerini çözüyor; kalan iş, temporal ifadeleri, prosedür parametrelerini ve daha hassas branch provenance'ını false-positive üretmeden değerlendirmek.
- [ ] Kesin yanlış koşul bilgisini `INSERT ... SELECT` bağlamına güvenli biçimde taşı; `JOIN ON` ve dış birleşimlerde semantik değişiklik yaratabilecek yayılımı ayrı değerlendir.
- [ ] UPDATE ve `INSERT ... SELECT` atamalarında aynı ailedeki kesin `CASE`, `CAST` ve sayısal aritmetik ifadelerinin tip bilgisini taşıma tamamlandı; kalan iş, daha fazla SQL Server dönüşümünü false-positive üretmeden kapsamaktır.
- [ ] `INSERT ... SELECT` ve UPDATE atamalarında `CAST`, aritmetik ve `CASE` gibi ifadelerin bilinen hedef/kaynak tiplerini, SQL Server dönüşüm önceliğini tahmin etmeden yalnızca kesin risklerde raporla; doğrudan kolon tür ailesi uyumsuzlukları tamamlandı.
- [ ] Hesaplanan SELECT ifadeleri için yalnızca preview/WorkspaceEdit tabanlı code generation adaylarını değerlendir: eksik alias ekleme ve güvenli `ORDER BY` ordinal değiştirme; isim çakışması, aggregate ve belirsiz ifadelerde öneri verme.

### Core API ve dağıtım

- [ ] DBeaver LSP sürecinde thread/RSS ölçümünü sandbox dışı yerel çalışmada doğrula. Baseline ölçümü geçti: tek paylaşımlı süreçte startup 239.7 ms, diagnostics 56.9 ms; 5 süreçte ortalamalar sırasıyla 45.3 ms ve 10.4 ms; 20 süreçte 11.2 ms ve 2.7 ms. Ortam `ps` erişimini engellediği için thread/RSS alanları hâlâ `unavailable`.

### DBeaver plugin, biçimlendirici ve kod kalitesi

- [ ] DBeaver UX backlog: code action dialogına arama/filtre ve daha belirgin klavye akışı ekle; diagnostics dialogında severity/rule filtreleme, kopyalama ve kaynak konumuna gitme aksiyonlarını değerlendir; sqldeps menüsünde aktif index, LSP durumu ve experimental durumunu görünür özetle.
- [ ] DBeaver'da `DiagnosticTag.Unnecessary` ile silikleştirilen kesin etkisiz/ulaşılamaz SQL annotation'ını açık/koyu gerçek temalarda görsel olarak doğrula.
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
