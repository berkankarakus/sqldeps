# sqldeps roadmap

Bu dosya yalnızca tamamlanmamış işleri ve kalıcı kapsam kararlarını içerir. Tamamlanan işler ilgili
commit, unit test ve smoke test geçmişinde tutulur.

## Sıradaki işler

### Core API ve dağıtım

- [ ] DBeaver LSP sürecinde thread/RSS ölçümünü sandbox dışı yerel çalışmada doğrula. Bu ortamda startup/diagnostics baseline'ı çalıştı, ancak sandbox `ps` erişimini engellediği için thread/RSS alanları `unavailable` kaldı.
- [ ] LSP code-action payload üretimini Core'un protokol-bağımsız preview sözleşmesine taşı; doğrulama ve ranking artık Core'da.
- [ ] `IndexQueryService`'i `Storage/IndexDatabase` uygulamasına doğrudan bağlanmayacak bir Core okuma sözleşmesine ayır; önce gerçek ikinci implementasyon ihtiyacını doğrula.
- [ ] Storage ve Domain modellerinin namespace sınırlarını netleştir; kök `SqlDeps` modellerini taşımadan önce public API etkisini ölç.

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
