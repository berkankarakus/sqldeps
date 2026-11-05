# AGENTS.md

`sqldeps`, T-SQL betiklerinden bağımlılık ve LSP bilgisi çıkaran bir .NET aracıdır. Çalışmaya başlamadan önce
`CONTRIBUTING.md` ve `TODO.md` dosyalarını oku, değişiklikten sonra `./test.sh` çalıştır.

## Kod ararken önce CodeGraph

Bu depoda `.codegraph/` indeksi var. Kodu bulmak ya da anlamak için `rg`, `grep`, `find` ya da dosya okumadan
**önce** `codegraph_explore` (MCP) ya da kabukta `codegraph explore "<sorgu>"` çağır. Bir sembolün kaynağı,
çağıranları ve etkilediği yerler tek çağrıda gelir.

CodeGraph SQL dosyalarını (`fixtures/*.sql`) indekslemez; onlar için `rg` ya da `sqldeps`'in kendisini kullan.
Yapılandırma ve belge dosyaları için de `rg`/okuma kalır.
