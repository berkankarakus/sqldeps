# Katkıda bulunma

`sqldeps`, SQL Server T-SQL betiklerinden bağımlılık ve LSP bilgisi çıkaran küçük bir araçtır. Katkılar,
özellikle gerçek bir SQL örneğini veya editör kullanımını iyileştiriyorsa memnuniyetle karşılanır.

## Başlamadan önce

```sh
git clone <repo-url>
cd sqldeps
dotnet restore
./test.sh
```

Geliştirme sırasında hızlı kontrol için `dotnet build -c Release --no-restore` kullanılabilir. İlişkili birkaç
değişiklik biriktiğinde `./test.sh` çalıştırılmalıdır; script fixture indeksleme, SQLite sorguları, CLI ve LSP
JSON-RPC akışını doğrular.

## Değişiklik ekleme

- T-SQL ayrıştırma ve bağımlılık davranışları `Indexing/` ve `Domain/` altındadır.
- Kalıcı indeks ve sorgu davranışları `Storage/` ile `Cli/` altında tutulur.
- Editör davranışları `Lsp/` altındadır; Neovim yapılandırması bu depoya dahil değildir.
- Yeni bir davranış eklemeden önce mevcut helper ve benzer akışı arayın; aynı mantığı kopyalamak yerine
  mevcut akışı genişletin.
- Karmaşık koşulları isimli metotlara ayırın ve kontrol akışını okunabilir tutun.
- Yeni bir SQL davranışı için mümkünse `fixtures/` veya test senaryosu ekleyin. Gerçek müşteri betiklerini,
  bağlantı bilgilerini veya gizli verileri commit etmeyin.

## LSP değişiklikleri

LSP stdio üzerinden JSON-RPC konuşur. Tanı, completion veya code action değişikliklerinde açık belge,
çalışma alanı ve parse hatası senaryolarını birlikte düşünün. LSP istemci yapılandırması ayrı bir ortamda
tutulduğu için bu depoya Neovim’e özel Lua dosyası eklemeyin.

## Commit ve pull request

Commit’ler tek bir mantıksal değişikliği anlatmalı ve kısa, eylem kipinde yazılmalıdır; örneğin
`feat: add column quick fix` veya `refactor: split dependency collector`. Pull request açıklamasında:

- neyin değiştiğini ve nedenini,
- hangi komutların çalıştırıldığını,
- kullanıcıya görünen çıktı veya LSP davranışı değişikliklerini,
- bilinen sınırlamaları

belirtin. Büyük ve ilgisiz refactor’ları aynı PR’da birleştirmeyin.

## Güvenlik

Güvenlik açığı, gerçek credential veya hassas SQL içeren bir örneği public issue olarak paylaşmayın. Önce
bakımcılarla özel bir kanal üzerinden iletişime geçin; raporda etkilenen sürümü, yeniden üretme adımlarını
ve mümkünse güvenli bir düzeltme önerisini belirtin.
