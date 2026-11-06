# AGENTS.md

`sqldeps`, T-SQL betiklerinden bağımlılık ve LSP bilgisi çıkaran bir .NET aracıdır. Çalışmaya başlamadan önce
`CONTRIBUTING.md` ve `TODO.md` dosyalarını oku.

## Geliştirme, test ve refactor akışı

Her mantıksal batch için şu sırayı izle:

1. Bağımsız doğrulanabilecek en küçük davranış değişikliğini belirle.
2. Geliştirme sırasında ilgili hedef unit testi ve uygun `test.sh` smoke bölümünü çalıştır.
3. Kod taşıma, public contract veya bağımlılık yönü değişiyorsa düzenlemeden sonra CodeGraph ile çağrı yollarını
   ve kaynak-test eşleşmelerini kontrol et.
4. Yeni bulguları veya somut takip işlerini `TODO.md` içine ekle; tamamlanan maddeleri temizle.
5. Mantıksal batch tamamlandığında tam `./test.sh` çalıştır. Küçük her değişiklikten sonra tam suite çalıştırma.
6. Batch’i tek bir mantıksal conventional commit olarak kaydet; mikro commit ve ilgisiz değişiklik oluşturma.
   Repository sahibi açıkça istemedikçe push yapma.

Performans incelemesine ölçülebilir baseline veya somut aday olmadan yeniden başlama. Deneysel, SQL lehçesi ve
paketleme işlerini mevcut öncelik sırası değişmedikçe sona bırak.

## Kod ararken önce CodeGraph

Bu depoda `.codegraph/` indeksi var. Kodu bulmak ya da anlamak için `rg`, `grep`, `find` ya da dosya okumadan
**önce** `codegraph_explore` (MCP) ya da kabukta `codegraph explore "<sorgu>"` çağır. Bir sembolün kaynağı,
çağıranları ve etkilediği yerler tek çağrıda gelir.

CodeGraph SQL dosyalarını (`fixtures/*.sql`) indekslemez; onlar için `rg` ya da `sqldeps`'in kendisini kullan.
Yapılandırma ve belge dosyaları için de `rg`/okuma kalır.
