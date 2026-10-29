# sqldeps

Bir klasördeki `.sql` dosyalarını Microsoft'un T-SQL ayrıştırıcısıyla (`ScriptDom`) okuyup "hangi nesne
hangi nesneyi hangi işlemle kullanıyor" bilgisini bir SQLite dosyasına yazar. Sonra düz SQL ile
sorulur.

```sh
./install.sh                              # ~/.local/bin/sqldeps
sqldeps index ~/Projects/sql-repo         # ~/.local/share/sqldeps/sql-repo.sqlite
sqldeps index ~/Projects/sql-repo --db deps.sqlite
sqldeps index ~/Projects/sql-repo --default-schema app
sqldeps index ~/Projects/sql-repo --jobs 4 # en çok dört dosyayı eşzamanlı işle
sqldeps index ~/Projects/sql-repo --full  # önbelleği yok say, her dosyayı yeniden oku
sqldeps status ~/Projects/sql-repo        # indeks güncel mi, kaç dosya/nesne/hata var?
sqldeps index ~/Projects/sql-repo --quiet # yalnızca hata durumunda çıktı ver
```

- **Artımlı dizinleme:** Aynı veritabanı dosyasına tekrar çalıştırınca yalnızca değişen, yeni ya da silinen
  dosyalar işlenir; her dosyanın SHA-256 özeti `files` tablosunda tutulur. Çıktıdaki
  `(N reused unchanged)` kaç dosyanın yeniden okunmadığını gösterir. Varsayılan şema değişince ya da
  araç kuralları değişince (`IndexCache.RulesVersion`) önbellek kullanılmaz.
- **Paralel ayrıştırma:** Dosyalar eşzamanlı ayrıştırılır; `--jobs <n>` ile işçi sayısı sınırlandırılabilir.
  SQLite indeksi önce geçici bir dosyada hazırlanır ve yalnızca başarıyla tamamlanırsa eski dosyanın yerini alır.
- **Kodlama:** BOM'lu UTF-8, UTF-16 ve UTF-32 doğru okunur. BOM yoksa önce UTF-8 denenir, geçerli
  değilse Türkçe Windows kod sayfası (Windows-1254) kullanılır (Türkçe Windows'ta SSMS böyle kaydeder).
  BOM'suz UTF-16 algılanmaz.
- **Varsayılan şema:** `--default-schema` ile şemasız yazılmış adların hangi şemada sayılacağı
  seçilir (varsayılan `dbo`).

## Tablolar ve görünümler

| Ad | İçerik |
|---|---|
| `dependencies` | `source_object`, `target_object`, `operation`, `source_file`, `line` |
| `objects` | `name`, `kind` (`table`, `view`, `procedure`, `function`, `trigger`), `source_file`, `line` |
| `parse_errors` | `source_file`, `line`, `message` |
| `files` | `path`, `sha256`: dizinlenen dosyalar ve özetleri (artımlı dizinleme için) |
| `meta` | `key`, `value`: dizinin hangi ayarlarla üretildiği (`settings`) |
| `parameters` | `object_name`, `ordinal`, `name`, `data_type`, `default_value`, `is_output`: yordam ve fonksiyon imzaları |
| `column_usage` | güvenle çözülebilen `SELECT`, `INSERT` ve `UPDATE` sütun kullanımları |
| `table_columns` | `CREATE TABLE`, `ALTER TABLE … ADD` ve `ALTER TABLE … ALTER COLUMN` ile gelen tablo sütunları ile açık sütun listeli görünüm sütunları (LSP tamamlaması için) |
| `reach` | `source_object`, `reached_object`, `depth`, `path`: bir nesnenin dolaylı olarak ulaştığı her şey, en yakın mesafeyle ve yoluyla (en çok 12 adım) |
| `cycles` | `object_name`, `depth`, `path`: kendini doğrudan ya da dolaylı çağıran nesneler ve en kısa dönüş yolu |
| `readers`, `writers`, `calls` | `dependencies`'in süzülmüş hâli: `SELECT`; `INSERT`/`UPDATE`/`DELETE`/`MERGE`/`TRUNCATE`; `EXEC`/`CALL` |
| `impact` | `origin`, `affected`, `depth`, `path`: `reach`'in ters okunuşu, "bu değişirse kimler etkilenir" |
| `undefined_objects` | kullanılan ama dizindeki dosyalarda tanımı olmayan nesneler (eksik betik, sistem nesnesi, başka veritabanı) |
| `unused_objects` | başka hiçbir nesnenin kullanmadığı nesneler (tetikleyiciler hariç) |
| `most_written` | en çok farklı nesnenin yazdığı tablolar |
| `object_degree` | her nesnenin kaç nesneye bağlı olduğu (`depends_on`) ve kaç nesnenin ona bağlı olduğu (`used_by`) |
| `duplicate_definitions` | birden çok yerde tanımlanan nesneler (sonradan gelen bir `ALTER` de sayılır) |

Adlar `schema.ad` biçimindedir (şemasız yazılmış ad `dbo` sayılır, veritabanı varsa `db.schema.ad`) ve
büyük/küçük harf ayrımı yapmadan karşılaştırılır. `dependencies` her kullanımı ayrı satır tutar, aynı
çift birden çok satırda geçebilir; tekrar istemezsen `SELECT DISTINCT` kullan.

`operation` değerleri: `SELECT`, `INSERT`, `UPDATE`, `DELETE`, `MERGE` (ayrıca eylemleri olan
`UPDATE`/`INSERT`/`DELETE`), `TRUNCATE`, `EXEC`, `CALL` (fonksiyon), `ON` (tetikleyicinin tablosu),
`EXEC_DYNAMIC` (hedefi bilinmeyen `EXEC (@sql)` ve `sp_executesql`; hedef `(dynamic sql)`). Sabit
metinli `EXEC ('SELECT ...')` ve `sp_executesql N'SELECT ...'` ise ayrıca ayrıştırılır; bu satırlarda
`dependencies.via_dynamic = 1` olur.

## Sorgular

İndekslemeden sonra günlük sorgular için SQLite yazmak gerekmez. Sorgu komutlarında `--db` verilmezse
çalışma klasörünün adaş varsayılan indeksi kullanılır. Sık kullandığınız indeksi bir kez etkinleştirirseniz,
hangi klasörde olursanız olun sorgular onu kullanır:

```sh
sqldeps use sql-repo                 # ~/.local/share/sqldeps/sql-repo.sqlite
sqldeps use /mutlak/yol/deps.sqlite  # özel bir indeks dosyası
sqldeps prompt                       # etkin indeksin prompt için kısa adı
sqldeps reindex                      # etkin indeksi kaynak klasöründen tamamen yenile
sqldeps indexes                      # yerel indeksleri ve kaynak klasörlerini listele
sqldeps doctor                       # etkin indeksin kaynak klasörünü ve güncelliğini denetle
sqldeps unuse                        # etkin indeks seçimini kaldır; dosyayı silmez
sqldeps drop sql-repo --yes          # indeks dosyasını sil; --yes zorunludur
sqldeps source UpdateProducts        # nesnenin tanım dosyasını ve satırını göster
sqldeps objects --limit 20            # indekslenen nesneleri listele
sqldeps find Products              # nesne adlarında büyük-küçük harfe duyarsız ara
sqldeps table-columns Products            # LSP'nin tamamlayabildiği tablo/görünüm sütunları
```

`--db` her zaman etkin seçimi geçersiz kılar. `status <klasör>` ise indeksle karşılaştırılacak SQL klasörünü
bildiği için etkin seçimden değil, kendi varsayılanından çalışır.

İndeksler kaynak klasörünü metadata'da saklar. Bu nedenle `reindex` argümansız çalıştığında etkin indeksin
kaynak klasörünü bulur ve tam yeniden indeksleme yapar. Eski indekslerde bu bilgi olmayabilir; bu durumda
`sqldeps reindex <folder> --db <file>` kullanın. `drop` geri alınamaz olduğundan açıkça `--yes` ister.

`prompt` yalnızca etkin indeksin dosya adını (uzantısız) yazar; etkin indeks yoksa sessizce çıkar. Shell
ayarını araç değiştirmez. İsterseniz zsh prompt'unuza şu isteğe bağlı bölümü ekleyebilirsiniz:

```zsh
setopt prompt_subst
sqldeps_prompt_segment() {
  local index
  index="$(command sqldeps prompt 2>/dev/null)"
  [[ -n "$index" ]] && print -n " [sqldeps: $index]"
}
PROMPT='%n@%m %1~$(sqldeps_prompt_segment) %# '
```

Nesne adları büyük/küçük harfe duyarsızdır: `dbo.Products`, `DBO.PRODUCTS` ve `dbo.upinfo` aynı sonucu verir.
`index` tamamlandığında indeksleme süresi de `Elapsed: 1.23s` biçiminde yazılır; `--quiet` bu satırı da gizler.
`Timing` satırı ayrıştırma, reachability hesabı ve SQLite yazım sürelerini ayrı gösterir; corpus üzerinde
darboğazı bulmak için kullanılabilir.
Sorgu sonuçları interaktif terminalde renkleri koruyan `less` içinde açılır; `q` ile çıkılır. Betiklerde veya
normal terminal çıktısında `--no-pager` kullanabilirsiniz. Pager yalnızca çıktı terminal yüksekliğini aştığında
çalışır. Nesne sorguları varsayılan olarak işlem ve tabloya
göre grupludur; kaynak dosya ve satırı aynı maddede gösterilir. Satır terminale sığmazsa sonu `…` ile
kısaltılır. Önceki satır-sütun tablosu görünümüne dönmek için `--table` kullanın.

```sh
sqldeps who-writes dbo.Products --db deps.sqlite
sqldeps who-updates dbo.Products price --db deps.sqlite
sqldeps who-reads dbo.OrderSummary --db deps.sqlite
sqldeps deps dbo.UpdateProducts --db deps.sqlite
sqldeps columns dbo.UpdateProducts --db deps.sqlite
sqldeps table-columns dbo.Products --db deps.sqlite
sqldeps impact dbo.Products --db deps.sqlite
sqldeps duplicates --db deps.sqlite
sqldeps errors --db deps.sqlite
sqldeps lint --db deps.sqlite
sqldeps impact dbo.Products --db deps.sqlite --json
sqldeps who-writes dbo.Products --db deps.sqlite --limit 10 --no-color
```

Gruplanmış çıktı varsayılandır; `--table` düz tabloyu, `--json` ise aynı veriyi betiklerin kullanabileceği JSON
dizisini yazar. `--limit`, tablo sonuçlarını sınırlar; `--no-color` otomasyonlar veya sade terminaller içindir.
`status`, indeksdeki dosya özetlerini diskteki dosyalarla karşılaştırır; eklenen, değişen veya silinen dosya
varsa `1` ile çıkar ve yeniden `index` çalıştırılmasını ister.

`lint`, indeksdeki T-SQL bulgularını tek akışta toplar: ayrıştırma hataları, çözümlenemeyen dinamik SQL,
yinelenen nesne tanımları, kullanılmayan nesneler ve veritabanları arası başvuruların yanında `SELECT *`,
`NOLOCK` ve `WHERE` içermeyen `UPDATE`/`DELETE` kalıplarını da gösterir. Bu bulgular kaynak kodu değiştirmez;
`error`, `warning` ve `info` önem dereceleriyle, kaynak konumu varsa onunla birlikte yazılır.

## LSP (deneysel)

`sqldeps lsp`, Neovim gibi bir LSP istemcisi tarafından stdio üzerinden başlatılır; terminalde doğrudan
çalıştırılmaz. Açık ama kaydedilmemiş SQL belgeleri için ayrıştırma hatalarını yayımlar; hatadan önceki geçerli
önek ayrıştırılabiliyorsa onun sembollerini kullanmaya devam eder. Belge/çalışma alanı
sembolleri, tanıma gitme, başvuruları bulma ve hover desteği sağlar. Başlatılırken çalışma alanını arka planda
indeksler; açık belgeler diskteki sürümün önüne geçer. Kaydetme ve kapatma, o dosyanın çalışma alanı anlık
görüntüsünü yeniler; böylece diğer dosyalardan yapılan gezinmeler eski diske yazılmış sürüme dönmez. Rename ve
formatting bilinçli olarak desteklenmez. Açık belgede ayrıştırma hatalarının yanında `lint` kuralları da
tanı olarak yayımlanır; örneğin `NOLOCK` uyarı, `SELECT *` bilgi seviyesindedir. `alias.` sonrasında tablo/görünüm sütunlarını; `dbo.` veya nesne adı
önekinde yordam, fonksiyon, görünüm ve tabloları tamamlar. Açık CTE sütun listeleri ve adı çıkarılabilen
türetilmiş alt sorgu sütunları da aynı belge içinde tamamlanır.

```sql
-- dbo.UpdateProducts neye ne yapıyor
SELECT target_object, operation, source_file
FROM dependencies
WHERE source_object = 'dbo.UpdateProducts';

-- dbo.Products'yu UPDATE eden kim
SELECT DISTINCT source_object, source_file
FROM dependencies
WHERE target_object = 'dbo.Products' AND operation = 'UPDATE';

-- dbo.Products değişirse kimler etkilenir (en yakından uzağa; path etkilenenden tabloya doğru gider)
SELECT affected, depth, path FROM impact WHERE origin = 'dbo.Products' ORDER BY depth, affected;

-- dbo.UpdateProducts'ın çağrı ağacı: dolaylı olarak ulaştığı her yordam ve tablo
SELECT reached_object, depth, path FROM reach WHERE source_object = 'dbo.UpdateProducts' ORDER BY depth;

-- kendini çağıran yordamlar
SELECT object_name, path FROM cycles;

-- hedefi bilinmeyen dinamik SQL kullanan nesneler (bağımlılıkları eksik olabilir)
SELECT DISTINCT source_object, source_file FROM dependencies WHERE operation = 'EXEC_DYNAMIC';

-- bir prosedürün parametreleri
SELECT ordinal, name, data_type, default_value, is_output
FROM parameters WHERE object_name = 'dbo.UpdateProducts' ORDER BY ordinal;

-- bir prosedürün hangi sütunları okuduğu veya yazdığı
SELECT table_name, column_name, operation
FROM column_usage WHERE source_object = 'dbo.UpdateProducts';

-- en çok yazılan tablolar, en çok bağlantısı olan nesneler
SELECT * FROM most_written LIMIT 10;
SELECT * FROM object_degree ORDER BY depends_on + used_by DESC LIMIT 10;
```

## Neleri kapsamaz

- Değişkenle veya hesaplanamayan bir ifadeyle gelen dinamik SQL (`EXEC (@sql)`) çözülemez ve yalnızca
  `EXEC_DYNAMIC` olarak işaretlenir. Sabit string ve sabit string birleştirmeleri ayrıştırılır.
- Çalışma zamanındaki veritabanına bağlanmaz; yalnızca dosyalardaki betiği okur.
- `unused_objects` yalnızca SQL tarafını görür: yalnızca C# koddan ya da bir iş zamanlayıcıdan çağrılan yordamlar da orada çıkar, "ölü kod kesinlikle" demek değildir.
- `reach` en çok 12 adım derinliğe gider; çok büyük ve çok bağlantılı depolarda dizinleme süresi buna göre uzar.
- Geçici tablolar (`#t`), CTE adları ve tetikleyicideki `inserted`/`deleted` bağımlılık sayılmaz.
- `column_usage`, yalnızca tablo hedefi kesin çözülebilen basit `SELECT` listelerini, `INSERT` sütun
  listelerini ve `UPDATE SET` hedeflerini kapsar; `*`, belirsiz join'ler ve karmaşık alt sorgular bilinçli
  olarak atlanır.
- Ayrıştırıcı SQL Server 2022 (`TSql160`) söz dizimini bilir. Okuyamadığı bölüm `parse_errors` tablosuna
  yazılır ve o grup (iki `GO` arası) dizine girmez, sonrakiler girer.
- C# kodundaki `SqlCommand("sp_ad")` çağrılarını bağlamaz.

## Test

```sh
./test.sh      # derler, fixtures/ klasörünü dizinler, 42 denetim yapar (sorgular, kodlamalar, artımlı dizinleme, varsayılan şema)
```
