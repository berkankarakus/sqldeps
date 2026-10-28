# sqldeps

Bir klasördeki `.sql` dosyalarını Microsoft'un T-SQL ayrıştırıcısıyla (`ScriptDom`) okuyup "hangi nesne
hangi nesneyi hangi işlemle kullanıyor" bilgisini bir SQLite dosyasına yazar. Sonra düz SQL ile
sorulur.

```sh
./install.sh                              # ~/.local/bin/sqldeps
sqldeps index ~/Projects/sql-repo         # ~/.local/share/sqldeps/sql-repo.sqlite
sqldeps index ~/Projects/sql-repo --db deps.sqlite
sqldeps index ~/Projects/sql-repo --default-schema app
sqldeps index ~/Projects/sql-repo --full  # önbelleği yok say, her dosyayı yeniden oku
```

- **Artımlı dizinleme:** Aynı veritabanı dosyasına tekrar çalıştırınca yalnızca değişen, yeni ya da silinen
  dosyalar işlenir; her dosyanın SHA-256 özeti `files` tablosunda tutulur. Çıktıdaki
  `(N reused unchanged)` kaç dosyanın yeniden okunmadığını gösterir. Varsayılan şema değişince ya da
  araç kuralları değişince (`IndexCache.RulesVersion`) önbellek kullanılmaz.
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
`EXEC_DYNAMIC` (hedefi bilinmeyen `EXEC (@sql)` ve `sp_executesql`; hedef `(dynamic sql)`).

## Sorgular

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

-- en çok yazılan tablolar, en çok bağlantısı olan nesneler
SELECT * FROM most_written LIMIT 10;
SELECT * FROM object_degree ORDER BY depends_on + used_by DESC LIMIT 10;
```

## Neleri kapsamaz

- Dinamik SQL (`EXEC (@sql)`) içindeki bağımlılıklar görünmez; yalnızca `EXEC_DYNAMIC` olarak işaretlenir.
- Çalışma zamanındaki veritabanına bağlanmaz; yalnızca dosyalardaki betiği okur.
- `unused_objects` yalnızca SQL tarafını görür: yalnızca C# koddan ya da bir iş zamanlayıcıdan çağrılan yordamlar da orada çıkar, "ölü kod kesinlikle" demek değildir.
- `reach` en çok 12 adım derinliğe gider; çok büyük ve çok bağlantılı depolarda dizinleme süresi buna göre uzar.
- Geçici tablolar (`#t`), CTE adları ve tetikleyicideki `inserted`/`deleted` bağımlılık sayılmaz.
- Ayrıştırıcı SQL Server 2022 (`TSql160`) söz dizimini bilir. Okuyamadığı bölüm `parse_errors` tablosuna
  yazılır ve o grup (iki `GO` arası) dizine girmez, sonrakiler girer.
- C# kodundaki `SqlCommand("sp_ad")` çağrılarını bağlamaz.

## Test

```sh
./test.sh      # derler, fixtures/ klasörünü dizinler, 42 denetim yapar (sorgular, kodlamalar, artımlı dizinleme, varsayılan şema)
```
