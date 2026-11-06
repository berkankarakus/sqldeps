# GO Batch Incremental Index Planı

Bu plan, değişen bir SQL dosyasındaki yalnızca değişen `GO` batch’lerini yeniden parse etmeyi
amaçlar. İlk sürümde güvenilirlik performanstan önce gelir; eşleşmesi kesin olmayan bir batch
yeniden parse edilir.

## Mevcut sınır

Cache şu anda dosya seviyesindedir. `CachedFile`, dosyanın hash’ini ve dosyanın ürettiği tüm
katkıları saklar. Dosyanın herhangi bir bölümü değiştiğinde `SqlFolderIndexer` bütün dosyayı tekrar
parse eder. Bu yaklaşım güvenlidir ancak büyük migration/procedure dosyalarında gereksiz parse işi
üretir.

## Önerilen cache modeli

Yeni `file_batches` tablosu her batch için şu alanları saklar:

- `source_file`
- `ordinal`
- `start_line`
- `text_hash`
- `payload`

`payload`, tek batch’in `IndexedFileContribution` verisini JSON olarak taşır. Mevcut nesne tablolarına
batch ordinal’i eklemek yerine katkıyı tek parça saklamak migration kapsamını ve yanlış eşleşme riskini
azaltır.

İlk uygulama sürümü yalnızca şu batch’leri yeniden kullanır:

1. Aynı dosya yolu.
2. Aynı batch ordinal’i.
3. Aynı batch metin hash’i.
4. Aynı başlangıç satırı.

Başlangıç satırı değişmişse batch yeniden parse edilir. Böylece ilk sürümde satır numarası rebasing’i
yapmadan stale diagnostic üretme riski ortadan kalkar. Daha sonra güvenilir bir line-delta modeli
ayrıca ölçülebilir.

## Index akışı

1. Dosyanın tamamı değişmemişse mevcut per-file cache hızlı yolu korunur.
2. Dosya değişmişse `BatchSplitter.Split` sonucu alınır.
3. Her batch için cache anahtarı hesaplanır.
4. Kesin eşleşen batch katkısı deserialize edilip merge edilir.
5. Eşleşmeyen batch normal ScriptDom parse/collect akışından geçer.
6. Yeni batch katkıları SQLite’a yazılır.
7. Eski ordinal’ler temizlenir; silinen batch’ler sonuçta kalmaz.

Batch katkıları dosya sırasına göre merge edilmelidir. Paralel indeksleme sırasında sonuç dizisi mevcut
dosya sırasını korumalıdır.

## Fail-closed kuralları

- Cache payload’ı bozuksa yalnızca ilgili batch yeniden parse edilir.
- `RulesVersion`, schema veya default schema değişmişse batch cache kullanılmaz.
- Aynı ordinal için hash eşleşmiyorsa eski katkı kullanılmaz.
- Başlangıç satırı değişmişse ilk sürümde reuse yapılmaz.
- Suppression dosyası veya repository metadata’sı değişmişse ilgili dosya katkıları yeniden hesaplanır.
- Incremental sonuç ile tam parse sonucu eşleşmiyorsa incremental sonuç yayınlanmaz; dosya tam parse edilir.

## Ölçülebilir kabul kriterleri

- İki batch’li fixture’da yalnızca ikinci batch değiştiğinde ilk batch için `reused_batch_count == 1`.
- Değişen batch için `reused_batch_count == 0`.
- Incremental ve full index sonuçları nesne, dependency, result column, problem, warning ve lint finding
  kümeleri bakımından eşit.
- Batch silindiğinde eski katkı sonuçtan çıkarılır.
- Batch satırları değiştiğinde stale line numarası üretilmez.
- Bozuk payload veya schema sürümünde güvenli full-file fallback çalışır.

## Uygulama sırası

1. `CachedBatch` ve deterministic contribution serializer ekle.
2. SQLite schema/cache load-write akışını ekle; schema/rules version artır.
3. `SqlFolderIndexer` içinde batch reuse ve fallback akışını ekle.
4. `reused_batch_count` ve `parsed_batch_count` ölçümlerini ekle.
5. Full-vs-incremental regression testleri ekle.
6. `fixtures` üzerinde smoke ve `local-corpus` üzerinde performans karşılaştırması yap.

