# sqldeps roadmap

Bu dosya yalnızca tamamlanmamış işleri ve kalıcı kapsam kararlarını içerir. Tamamlanan işler ilgili
commit, unit test ve smoke test geçmişinde tutulur.

## Sıradaki işler

### Performans ve parser

- [ ] ScriptDom parse/recovery yolunu ayrı bir benchmark ile incele; 4.397 dosya ve 14.880 batch içeren yerel corpus'ta parse hatası yokken exception recovery maliyeti yüksek. `jobs=4` en iyi ölçülen paralellik (3,53 sn); `jobs=8` daha hızlı değil (3,69 sn). Upstream fork'taki `perf/input-buffer` (`d5640b6`) ve CharQueue (`93a6966`) adayları source build ile doğrulandı; CharQueue, aynı çıktıyı koruyarak üç A/B koşulunda yaklaşık %2–3 daha hızlı ölçüldü. Paket eşdeğer build ve regression doğrulaması tamamlanmadan sqldeps'e bağlama.
- [x] ScriptDom fork'unda thread-local guessing exception reuse değişikliğini aynı upstream commit'inden derlenmiş baseline ve tam regression ile doğrula; depth-bazlı havuzla 4.398 dosyada parse-error/token özeti korunurken yaklaşık %12 süre ve %27 tahsisat azalması ölçüldü. NuGet paket eşdeğerliği ayrıca doğrulanmadı.
- [ ] Exception taxonomy benchmark'ı ekle: `MismatchedTokenException`, `NoViableAltException`, `NoViableAltForCharException` ve phase-one exception'larını dosya, parser sürümü ve parse sonucu bazında raporla.
- [ ] En yoğun generated-parser syntactic predicate'lerini (`selectStarExpression`, `expressionWithClrElements`, `tableElement`) bounded lookahead ile yeniden değerlendir; `selectStarExpression` için ilk semantic-lookahead denemesi exception/parse-error sayısını değiştirmediği için geri alındı; yeni adaylarda AST, parse-error ve token akışını baseline ile karşılaştırmadan grammar değişikliğini kabul etme.
- [ ] Phase-one kontrol akışındaki `PhaseOneBatchException`, `PhaseOnePartialAstException` ve `PhaseOneConstraintException` kullanımını exception dışı state/result akışına dönüştürme seçeneğini prototiple; yalnızca tam regression sonrası karar ver.
- [ ] Lexer'daki geçersiz karakter recovery yolunu incele; hata konumu ve diagnostics davranışı korunabiliyorsa `NoViableAltForCharException` sayısını azaltacak error-token yaklaşımını ölç.
- [ ] Parser/token-buffer instance reuse için thread-local havuz prototipi oluştur; state reset, token ömrü ve paralel index güvenliğini benchmark ve regression ile doğrula.
- [x] Tek komutlu ScriptDom A/B benchmark'ı oluştur: `tests/perf/scriptdom-ab.sh` ve reflection tabanlı `ScriptDomBenchmark` parse süresi, allocated bytes, exception sayısı, parse-error sayısı ve fragment/token özetini karşılaştırıyor. NuGet 170 baseline ile forkun daha yeni upstream tabanı aynı corpus'ta parse-error/token farkı üretti; kabul ölçümü aynı upstream commit'inden derlenmiş baseline ile yapılmalı.

### CodeGraph destekli parser analizi

- [x] Generated parser kaynaklarını CodeGraph analizine dahil edecek geçici/ayrı indeks akışı oluştur; `/tmp` staging diziniyle doğrulandı, `obj/` dosyaları kalıcı repoya alınmadı.
- [x] `Parser.match` ve exception sınıfları için CodeGraph caller/callee zincirini generated parser metotlarıyla birleştir; staging indeksinde impact 2 sembolden 505 sembole, caller görünümü 400 generated çağrıya genişledi.
- [ ] CodeGraph hotpath çıktısını exception taxonomy benchmark'ıyla eşleştir; yalnızca iki araçta da sıcak görünen adayları grammar refactor listesine al. NuGet 170 karşılaştırması sürüm farkı nedeniyle yalnızca tanı amaçlı; önce aynı upstream commit'inden derlenmiş baseline ile davranış farkını sınıflandır.

### Core API ve dağıtım

- [ ] DBeaver LSP sürecinde thread/RSS ölçümünü sandbox dışı yerel çalışmada doğrula. Baseline ölçümü geçti: tek paylaşımlı süreçte startup 239.7 ms, diagnostics 56.9 ms; 5 süreçte ortalamalar sırasıyla 45.3 ms ve 10.4 ms; 20 süreçte 11.2 ms ve 2.7 ms. Ortam `ps` erişimini engellediği için thread/RSS alanları hâlâ `unavailable`.

### DBeaver plugin, biçimlendirici ve kod kalitesi

- [ ] `TextResultDialog` ve `DiagnosticDialog` için DBeaver'ın gerçek açık/koyu temalarında görsel doğrulama yap.
- [ ] `SqldepsQuickFixProcessor.computeQuickAssistProposals` hesaplama sırasında diyalog açıyor ve boş liste dönüyor. Hover ve lightbulb sorgularının bunu tetikleyip tetiklemediğini DBeaver'da doğrula; gerekirse gerçek bir proposal dön.
- [ ] DBeaver runtime ortamında iki farklı workspace ile otomatik indeks seçiminin doğru kaynağı kullandığını elle doğrula.
- [ ] DBeaver runtime ortamında symlink’li workspace kökü ve symlink `.sqldeps` reddi elle doğrula.
- [ ] Biçimlendirici (`SqlLayout*`): tek SELECT öğesi ve WHERE `AND`/`OR` yerleşimi düzeltildi; `HAVING`, `GROUP BY`, `ORDER BY` ve `CASE` içindeki alt sorgular kural dışı, `CREATE FUNCTION`, `PIVOT`, `OPENJSON` ve dinamik SQL denenmedi. Gerçek, uzun prosedürlerde elle doğrula.
- [ ] Büyük dosyaları ölçülebilir bir ihtiyaç doğunca böl: `Indexing/DependencyCollector.cs` (940), `Mcp/McpServer.cs` (628). `LanguageServer.cs` ana dosyası 39 satıra indirildi; `tests/smoke/lsp.sh` 166 satıra ayrıştırıldı.
- [ ] Geçmiş yeniden yazıldıktan sonra: `origin/main` için `git push --force-with-lease origin main` gerekiyor; `origin/master`, `origin/feature/antlr-tsql-parser` ve `origin/fix/dbeaver-editor-context` hâlâ eski adları ve Codex co-author satırlarını taşıyor. Hazır olunca yerel `refs/original/refs/heads/main` yedeğini sil.

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
