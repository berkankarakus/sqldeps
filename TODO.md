# sqldeps roadmap

Bu dosya yalnızca tamamlanmamış işleri ve kalıcı kapsam kararlarını içerir. Tamamlanan işler ilgili
commit, unit test ve smoke test geçmişinde tutulur.

## Sıradaki işler

### Güvenli SQL refactor advisor’ı

- [ ] Yeni aday türleri eklendikçe parse-valid, stale-index, evidence ve `writes_file=false` sınırlarını kapsayan regression testlerini genişlet. `select-star` için gerçek indeks/impact baseline'ı mevcut.

### T-SQL doğruluğu ve corpus incelemesi

- [ ] `local-corpus` içindeki NOLOCK dışı FP/FN örneklerini kural bazında sınıflandır.
- [ ] Subquery, CTE, table variable, temp table, dynamic SQL ve DML kapsamları için küçük regression fixture’ları ekle.

### Core API ve dağıtım

- [ ] Public Core API dokümantasyonunu ve compatibility testlerini tamamla.
- [ ] Yerel DBeaver doğrulaması sonrasında feature/p2 update-site paketlemesini ekle.
- [ ] DBeaver LSP sürecinde thread/RSS ölçümünü sandbox dışı yerel çalışmada doğrula.

### Diğer SQL lehçeleri

- [ ] Parser/identifier/formatter seçimini sağlayıcı arayüzüne ayır; T-SQL varsayılanını koru.
- [ ] `languageId`/uzantı/ayar önceliğiyle lehçe seçimini ve ortak diagnostic sözleşmesini ekle.
- [ ] PostgreSQL, MySQL/MariaDB, SQLite ve Oracle/PLSQL sağlayıcılarını ayrı corpus ve LSP regression suite’leriyle ilerlet.

## Kalıcı kapsam kararları

- LSP ve MCP aynı executable içinde kalır; bağımsız dağıtım ihtiyacı oluşmadıkça ayrılmaz.
- Rename ve formatting kapsam dışıdır; Neovim Lua yapılandırması ayrı projededir.
- Kod üretimi yalnızca parse-doğrulanmış preview/WorkspaceEdit/command olarak sunulur; otomatik dosya yazılmaz.
- Dynamic SQL, belirsiz kolon/ilişki, eksik metadata ve stale indeks durumlarında fail-closed davranılır.
- SWTBot/PDE UI otomasyonu kapsam dışıdır; Java seam testleri ve LSP smoke testleri kullanılır.
- Diğer SQL lehçeleri T-SQL doğruluk, test kapsamı, performans ve dağıtım akışları sabitlenmeden başlanmaz.
