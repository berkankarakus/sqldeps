# sqldeps yol haritası

Bu dosya yalnızca açık işleri ve kısa karar notlarını tutar. Tamamlanmış ayrıntılı checklist’ler
git geçmişinde ve ilgili smoke/unit testlerinde bulunur.

## Öncelik sırasındaki açık işler

### P2 — Kullanıcı deneyimi ve entegrasyon

- [ ] Canlı metadata sağlayıcısı için LSP’den bağımsız salt-okunur sınır ve veri sözleşmesini tasarla;
  mevcut `MetadataCatalog`/`WorkspaceMetadataSnapshot` akışını değiştirmeden önce bu sınırı sabitle.
- [ ] SQL Server sağlayıcısında yalnızca salt-okunur sorguları, zorunlu TLS doğrulamasını ve bağlantı
  zaman aşımını uygula; varsayılan davranış indeks kaynaklı kalmaya devam etsin.
- [ ] Neovim ayarlarında credential’ı repo dışından çöz; parolayı LSP loglarına, prompt’a veya JSON yanıtına
  yazma ve bağlantıyı açık bir kullanıcı tercihi olmadan başlatma.
- [ ] Canlı sağlayıcı için bağlantı hatası, eksik yetki ve stale metadata durumlarını fail-closed LSP testleriyle
  güvenceye al.

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
