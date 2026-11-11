# sqldeps roadmap

Bu dosya yalnızca tamamlanmamış işleri ve kalıcı kapsam kararlarını içerir. Tamamlanan işler ilgili
commit, unit test ve smoke test geçmişinde tutulur.

## Sıradaki işler

### Mimari ve bağımlılık yönü

- [ ] Gerçek assembly sınırlarını ayır: `Domain` artık `SqlDeps.Domain` olarak bağımsız; `Core/SqlDeps.Core.csproj` hâlâ `Analysis`, `Indexing` ve `Storage` kaynaklarını linkliyor. Analysis bağımsız projeye çıkarılamadı çünkü `DocumentAnalysis` doğrudan `Indexing/DependencyCollector`, `BatchSplitter` ve `Finders` implementasyonlarına bağlı; önce parser/collector sorumluluklarını ayırmak gerekiyor.
- [x] `Storage` katmanının `Analysis` bağımlılığını kaldır; özellikle `ReachResult` ve indeks yazma snapshot'ı için persistence sözleşmelerini analiz modellerinden ayır.
- [x] `Core/IndexQueryService` içindeki SQLite sorgu ve `IndexCache` erişimini Core'dan çıkarıp storage/application sınırına taşı; Core'un doğrudan SQLite ve fiziksel indeks bilgisine bağımlılığını azalt. `Storage/IndexQueryService`, `Domain` metadata sözleşmeleri ve `Preview/IndexPreviewService` ayrıldı.
- [x] `Preview/PreviewService -> Lsp/LanguageServer` yönünü düzelt; code-action preview kompozisyonunu `Lsp/CodeActionPreviewService` altında ortaklaştırıp MCP/CLI adapter'larının aynı servisi kullanmasını sağla.
- [x] İzin verilen namespace/assembly yönlerini test eden düşük maliyetli bir architecture test ekle; Domain dışarı bağımlı olmasın, adapter katmanları ortak servisleri tüketebilsin.
- [ ] Refactor sırasında geniş partial sınıfları davranış sınırlarına göre değerlendir: `LanguageServer` ve `QueryColumnValidator` için public contract değişmeden seam'leri küçült. `IndexQueryService`, `LanguageServer` diagnostics cache/document analyzer ve `QueryColumnValidator` metadata/ColumnVisitor seam’leri tamamlandı.
- [x] `BatchSplitter` sahipliğini `Indexing` dışına taşı; `SqlBatch` Domain sözleşmesi korunarak `Analysis/BatchSplitter.cs` altına alındı ve `BatchSplitterTests` ile LSP/indexleme akışları doğrulandı. Bağımsız `Analysis` assembly'si sonraki adımda ele alınacak.
- [ ] `DocumentAnalysis` için `Indexing/DependencyCollector` bağımlılığını daralt; tam indeksleme visitor'ları yerine analiz sonuçlarını üreten ayrı bir collector sınırı oluştur.
- [x] Ortak ScriptDom finder'larını `Analysis/ScriptDomFinders.cs` altında topladık; Analysis artık Indexing'e bağlanmadan aynı visitor'ları kullanıyor.
- [ ] Gerçek `Analysis/SqlDeps.Analysis.csproj` oluştur; Core'daki `../Analysis/**/*.cs` source-link yaklaşımını kaldır, test/host proje referanslarını güncelle ve bağımsız derlemeyi doğrula.
- [ ] Assembly ayrımından sonra root host'taki gereksiz doğrudan ScriptDom/SQLite paket referanslarını temizle; CLI, LSP ve MCP'nin yalnızca gerekli adapter/application referanslarını tuttuğunu doğrula.
- [ ] Namespace tabanlı architecture testine ek olarak `.csproj` project-reference ve source-link yönü testi ekle; Core'un Analysis/Indexing/Storage kaynaklarını linklemesini ve beklenmeyen katman yönlerini engelle.
- [x] `LanguageServer.Protocol.cs` içindeki JSON-RPC transport/dispatch ile sembol/diagnostic payload dönüşümlerini `LanguageServer.ProtocolTypes.cs` seam'ine ayır; mevcut LSP smoke testleri korunuyor.
- [x] `LanguageServer.DependencyActions.cs` içindeki seçim/payload üretimini `LanguageServer.DependencyActions.cs`te, indeks sorgulama ve impact çıktılarını `LanguageServer.DependencyQueries.cs`te ayır; dependency/impact smoke regression'larını koru.
- [x] `QueryColumnValidator` partial'larını davranış sınırlarına göre ayır; `OrderByDiagnostics`, `TableResolution`, `InsertLiteralDiagnostics`, `InsertSelectNullabilityDiagnostics`, `InsertExecuteDiagnostics`, `InsertCountDiagnostics`, `InsertSelectTypeDiagnostics`, `JoinTypeDiagnostics`, `TableOutputResolution`, `QueryScopeDiagnostics`, `ScopeTableResolution` ve `ImplicitFilterDiagnostics` seam'leri ayrıldı. Kalan `TypeDiagnostics` (132 satır) ve `ColumnDiagnostics` (167 satır) tek birincil sorumlulukta tutuldu; ortak `ColumnVisitor` state'i çoğaltılmadı.

### Performans ve parser

- [ ] ScriptDom parse/recovery yolunu ayrı bir benchmark ile incele; 4.397 dosya ve 14.880 batch içeren yerel corpus'ta parse hatası yokken exception recovery maliyeti yüksek. `jobs=4` en iyi ölçülen paralellik (3,53 sn); `jobs=8` daha hızlı değil (3,69 sn). Upstream fork'taki `perf/input-buffer` (`d5640b6`) ve CharQueue (`93a6966`) adayları source build ile doğrulandı; CharQueue, aynı çıktıyı koruyarak üç A/B koşulunda yaklaşık %2–3 daha hızlı ölçüldü. Paket eşdeğer build ve regression doğrulaması tamamlanmadan sqldeps'e bağlama.
- [x] ScriptDom fork'unda thread-local guessing exception reuse değişikliğini aynı upstream commit'inden derlenmiş baseline ve tam regression ile doğrula; depth-bazlı havuzla 4.398 dosyada parse-error/token özeti korunurken yaklaşık %12 süre ve %27 tahsisat azalması ölçüldü. NuGet paket eşdeğerliği ayrıca doğrulanmadı.
- [x] Exception taxonomy benchmark'ı ekle: `ScriptDomBenchmark` parser assembly/version, toplam exception türleri, parse sonucu ve en yoğun dosyaları JSON çıktısında raporluyor. Phase-one exception'ları corpus'ta oluşmadığında sıfır görünür; sentetik phase-one fixture'ı ayrıca eklenebilir.
- [x] En yoğun generated-parser syntactic predicate'lerini (`selectStarExpression`, `expressionWithClrElements`, `tableElement`) bounded lookahead ile yeniden değerlendir; `expressionWithClrElements` için CLR type predicate'i tüm grammar sürümlerinde semantic lookahead'e taşındı ve aynı 28 parse-error/3.782.226 token özetiyle yaklaşık %7 ek süre kazanımı ölçüldü. `selectStarExpression` adayı davranışı değiştirmediği için geri alındı; `tableElement` düşük katkı nedeniyle bekletiliyor.
- [ ] Phase-one kontrol akışındaki `PhaseOneBatchException`, `PhaseOnePartialAstException` ve `PhaseOneConstraintException` kullanımını exception dışı state/result akışına dönüştürme seçeneğini prototiple; yalnızca tam regression sonrası karar ver.
- [ ] Lexer'daki geçersiz karakter recovery yolunu incele; hata konumu ve diagnostics davranışı korunabiliyorsa `NoViableAltForCharException` sayısını azaltacak error-token yaklaşımını ölç.
- [ ] Parser/token-buffer instance reuse için thread-local havuz prototipi oluştur; state reset, token ömrü ve paralel index güvenliğini benchmark ve regression ile doğrula.
- [x] Tek komutlu ScriptDom A/B benchmark'ı oluştur: `tests/perf/scriptdom-ab.sh` ve reflection tabanlı `ScriptDomBenchmark` parse süresi, allocated bytes, exception sayısı, parse-error sayısı ve fragment/token özetini karşılaştırıyor. NuGet 170 baseline ile forkun daha yeni upstream tabanı aynı corpus'ta parse-error/token farkı üretti; kabul ölçümü aynı upstream commit'inden derlenmiş baseline ile yapılmalı.

### CodeGraph destekli parser analizi

- [x] Generated parser kaynaklarını CodeGraph analizine dahil edecek geçici/ayrı indeks akışı oluştur; `/tmp` staging diziniyle doğrulandı, `obj/` dosyaları kalıcı repoya alınmadı.
- [x] `Parser.match` ve exception sınıfları için CodeGraph caller/callee zincirini generated parser metotlarıyla birleştir; staging indeksinde impact 2 sembolden 505 sembole, caller görünümü 400 generated çağrıya genişledi.
- [x] CodeGraph hotpath çıktısını exception taxonomy benchmark'ıyla eşleştir; aynı upstream baseline ile davranışı koruyan adaylar listelendi. `expressionWithClrElements` (`97.459` mismatch) → `expressionUnary` → `expressionBinaryPri1/2`, `selectStarExpression` (`47.872`) → `selectColumnOrStarExpression`/`querySpecification`, `tableElement` (`3.919`) → `tableDefinition` zincirleri öne çıkıyor. NuGet 170 karşılaştırması sürüm farkı nedeniyle yalnızca tanı amaçlı kaldı.
- [ ] Generated parser dosyaları 1 MB sınırını aştığında CodeGraph için method sınırlarından otomatik staging/chunk akışı ekle; mevcut TSql160 analizi geçici script ile doğrulandı.

### Analiz kapsamı

- [ ] Sabit ifade analizine güvenli bir variable environment ekle: yalnızca aynı scope'ta tek kez atanmış literal `DECLARE`/`SET` değerlerini kullan; reassignment, branch, prosedür parametresi ve dynamic SQL durumlarında bilinmeyen kabul et.
- [ ] Kesin yanlış koşul bilgisini `INSERT ... SELECT` bağlamına güvenli biçimde taşı; `JOIN ON` ve dış birleşimlerde semantik değişiklik yaratabilecek yayılımı ayrı değerlendir.
- [ ] UPDATE atamalarında `CASE` ifadelerinin tip bilgisini güvenli biçimde taşı; `INSERT ... SELECT` ve UPDATE için doğrudan kolon nullability desteği tamamlandı.
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
