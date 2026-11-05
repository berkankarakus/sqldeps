# sqldeps yol haritası

Bu dosya yalnızca açık işleri ve kısa karar notlarını tutar. Tamamlanmış ayrıntılı checklist’ler
git geçmişinde ve ilgili smoke/unit testlerinde bulunur.

## Öncelik sırasındaki açık işler

### T-SQL doğruluk ve corpus incelemesi

- [ ] `local-corpus` içindeki NOLOCK dışı kuralları kural bazında sınıflandır; gerçek FN/FP örneklerini regression fixture’larına taşı.
- [ ] `unindexed-*`, tam kapsamlı DML ve dynamic SQL uyarıları için kalan örnekleri incele; yalnızca kanıtlanan false-positive’leri düzelt.

### MCP etkileşim katmanı

- [x] İstemcinin gönderdiği SQL metnini, aktif indeks metadata’sıyla analiz eden salt-okunur `analyze` aracı ekle.
- [x] Dynamic SQL parametreleştirme ve procedure test iskeleti için dosya yazmayan MCP preview araçlarını ekle.
- [ ] Güvenli LSP code-action preview’lerini MCP üzerinden de döndür; otomatik dosya yazımı yapma.

### Güvenlik incelemesi

- [x] MCP araçlarını whitelist ile sınırla; indeks sorgularındaki kullanıcı değerlerini parametreli geçir.
- [x] MCP `analyze` ve dynamic SQL preview girdilerine boyut sınırı koy; güvenli biçimde çözümlenemeyen
  ifadeleri fail-closed reddet.
- [x] LSP URI/workspace sınırlarını ve dosya erişimi akışını denetle; non-file URI ve workspace dışı belge
  regression testleri mevcut.
- [x] Üretilen WorkspaceEdit aralıklarını, URI'lerini ve metin boyutlarını denetle; yalnızca aktif belgeye
  ait tek-belge değişikliklerini, geçerli UTF-16 konumlarını ve sınırlı adet/metin boyutunu kabul et.

### DBeaver LSP entegrasyonu

- [x] Performansın ilk adımı: hızlı belge değişikliklerinde eski diagnostic isteklerini coalescing/cancellation ile
  etkisizleştir; eski sonuçların güncel belgeye uygulanmasını engelle.
- [ ] Performansın ikinci adımı: her SQL/nested editör için ayrı LSP süreci ve indeks yüklemesini azalt; gerçek büyük
  prosedür üzerinde ölç. Baseline aracı `./test.sh dbeaver-baseline` ile 1/5/20 süreçte startup ve diagnostics
  sürelerini raporluyor; editör başına scheduler tekrarını kaldırdık, process/thread/RSS ölçümü ortak süreç
  havuzundan sonra eklenecek.
- [ ] Java adapter smoke suite'ini Java 21 CI koşusunda çalıştır ve dependency-free testleri doğrula.
- [x] `DocumentSynchronizer` için Eclipse `IDocument` fixture'ı ekle; debounce, close ve version geçişlerini test et.
- [x] `LspProcessManager` için gerçek `sqldeps lsp` süreciyle initialize/didOpen/diagnostic/shutdown smoke testi ekle.
- [x] Diagnostic request stale-response/cancellation gate'ini bağımsız test seam'iyle doğrula.
- [ ] `EditorLspSession` için sahte editör ve LSP süreciyle restart/stale-response/shutdown testleri ekle.
- [x] DBeaver performans baseline'ı çıkar: 1/5/20 süreç için startup ve diagnostic sürelerini ölçen
  `tests/perf/dbeaver-lsp-process-baseline.sh` aracını ekle; process/thread/RSS ölçümü sonraki havuz adımında.
- [x] DBeaver public `SQLEditorAddIn` extension point'ine dayalı bağımsız plugin iskeletini oluştur.
- [x] LSP4J stdio process/client yaşam döngüsünü, initialize handshake'ini ve stderr ayrımını ekle.
- [x] SQL Editor `didOpen`/debounce edilmiş tam metin `didChange`/pull diagnostics/`didClose` akışını ekle.
- [x] UTF-16 diagnostic range dönüşümünü ve eski belge sonuçlarını generation/version ile ele.
- [x] SQL Editor'dan erişilen standart `Analyze SQL` komutunu ekle.
- [x] DBeaver diagnostics bulgularını mesaj kutusu yerine SQL Editor ve nested/procedure editöründe
  annotation hover akışıyla göster; sol annotation ikonuna tıklayınca satır, severity ve mesaj
  okunabilir biçimde açılsın.
- [x] Diagnostics komutu ilk bulguyu editörde görünür konuma taşısın; aynı annotation/hover davranışı
  normal ve nested SQL Editor akışlarında korunup gerçek kurulumda doğrulansın.
- [x] Editör içindeki diagnostics bulguları arasında next/previous komutlarıyla gezinmeyi ekle.
- [x] Kendi DBeaver kurulumunda plugin'i derle, yükle ve gerçek SQL Editor diagnostics akışını doğrula.
- [x] Capability-gated `textDocument/codeAction` ve güvenli tek-belge WorkspaceEdit uygulamasını ekle.
- [x] Komut tabanlı code action'ları DBeaver'da güvenli whitelist ile çalıştır; query-builder preview'ını
  `workspace/executeCommand` üzerinden göster, dosyaya yazma.
- [x] DBeaver annotation Quick Fix command'ini sqldeps code action akışına bağla; salt-okunur belgelerde
  WorkspaceEdit uygulanmasını engelle.
- [x] DBeaver dialect aktarımını mevcut LSP desteğiyle değerlendirdim; sunucu T-SQL’e sabit ve standart
  configuration akışı sunmadığı için dialect bilgisi bilerek gönderilmiyor.
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
