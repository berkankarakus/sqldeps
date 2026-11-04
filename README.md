# sqldeps

[![CI](https://github.com/berkankarakus/sqldeps/actions/workflows/ci.yml/badge.svg)](https://github.com/berkankarakus/sqldeps/actions/workflows/ci.yml)

Katkı göndermek için [CONTRIBUTING.md](CONTRIBUTING.md) rehberine bakabilirsiniz.

Bir klasördeki `.sql` dosyalarını Microsoft'un T-SQL ayrıştırıcısıyla (`ScriptDom`) okuyup "hangi nesne
hangi nesneyi hangi işlemle kullanıyor" bilgisini bir SQLite dosyasına yazar. Sonra düz SQL ile
sorulur.

```sh
sqldeps --help                            # komutların kısa kullanımını göster (help/-h de geçerli)
sqldeps --version                         # kurulu sürümü göster (version de geçerli)
./install.sh                              # ~/.local/bin/sqldeps
# HOME yoksa DOTNET_CLI_HOME kullanılabilir; SQLDEPS_INSTALL_ROOT/SQLDEPS_BIN_DIR ile hedefler değiştirilebilir.
sqldeps index ~/Projects/sql-repo         # ~/.local/share/sqldeps/sql-repo.sqlite
sqldeps index ~/Projects/sql-repo --db deps.sqlite
sqldeps index ~/Projects/sql-repo --default-schema app
sqldeps index ~/Projects/sql-repo --jobs 4 # en çok dört dosyayı eşzamanlı işle
sqldeps index ~/Projects/sql-repo --full  # önbelleği yok say, her dosyayı yeniden oku
sqldeps index ~/Projects/sql-repo --json  # otomasyon için sabit alanlı özet JSON'u
sqldeps status ~/Projects/sql-repo        # indeks güncel mi, kaç dosya/nesne/hata var?
sqldeps index ~/Projects/sql-repo --quiet # yalnızca hata durumunda çıktı ver
sqldeps lint --fail-on warning             # warning veya error bulgusunda 1 ile çık
```

- **Artımlı dizinleme:** Aynı veritabanı dosyasına tekrar çalıştırınca yalnızca değişen, yeni ya da silinen
  dosyalar işlenir; her dosyanın SHA-256 özeti `files` tablosunda tutulur. Çıktıdaki
  `(N reused unchanged)` kaç dosyanın yeniden okunmadığını gösterir. Varsayılan şema değişince ya da
  araç kuralları değişince (`IndexCache.RulesVersion`) önbellek kullanılmaz.
- Etkileşimli terminalde indeksleme, reachability ve SQLite aşamalarını tek satır progress bar ile gösterir;
  `--json`, `--quiet`, CI ve yönlendirilmiş çıktılarda progress bar kapalıdır.
- **Paralel ayrıştırma:** Dosyalar eşzamanlı ayrıştırılır; `--jobs <n>` ile işçi sayısı açıkça
  sınırlandırılabilir. Varsayılan worker limiti `min(CPU sayısı, 4)`’tür; büyük SQL corpus’larında
  parser CPU’su ile thread-pool beklemesi arasındaki denge için seçilmiştir.
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
| `table_columns` | `CREATE TABLE`, `ALTER TABLE … ADD` ve `ALTER TABLE … ALTER COLUMN` ile gelen tablo sütunları ile açık sütun listeli görünüm sütunları (LSP tamamlaması için); `is_nullable`, `is_identity`, `default_value`, `is_computed`, `precision`, `scale`, `collation` metadata’sı da tutulur |
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
çalışma klasörünün adını taşıyan varsayılan indeksi kullanır. Sık kullandığınız indeksi bir kez etkinleştirirseniz,
hangi klasörde olursanız olun sorgular onu kullanır:

```sh
sqldeps use sql-repo                 # ~/.local/share/sqldeps/sql-repo.sqlite
sqldeps use /mutlak/yol/deps.sqlite  # özel bir indeks dosyası
sqldeps use sql-repo --json          # seçimi otomasyon için JSON yaz
sqldeps prompt                       # etkin indeksin prompt için kısa adı
sqldeps prompt --json                # etkin indeks durumunu JSON yaz
sqldeps reindex                      # etkin indeksi kaynak klasöründen tamamen yenile
sqldeps indexes                      # yerel indeksleri ve kaynak klasörlerini listele
sqldeps indexes --json                # indeks listesini makine-okunabilir JSON olarak yaz
sqldeps doctor                       # etkin indeksin kaynak klasörünü ve güncelliğini denetle
sqldeps drift                        # indeks ile kaynak klasörü arasındaki dosya farklarını listele
sqldeps report                       # indeks durumu, drift ve lint özetini tek raporda göster
sqldeps unuse                        # etkin indeks seçimini kaldır; dosyayı silmez
sqldeps unuse --json                 # seçimin kaldırıldığını JSON ile bildir
sqldeps drop sql-repo                   # [y/N] onayıyla indeks dosyasını sil
sqldeps drop sql-repo --yes             # betikler için onayı atla
sqldeps drop sql-repo --yes --json      # silme sonucunu JSON yaz
sqldeps source UpdateProducts           # nesnenin tanım dosyasını ve satırını göster
sqldeps objects --limit 20              # indekslenen nesneleri listele
sqldeps find Products                   # nesne adlarında büyük-küçük harfe duyarsız ara
sqldeps table-columns Products          # LSP'nin tamamlayabildiği tablo/görünüm sütunları
sqldeps query-builder Products          # güvenli, tek kaynaklı SELECT preview'ı üret
sqldeps query-builder OrderLines --join # tek foreign-key ilişkisi varsa JOIN preview'ı üret
sqldeps test-skeleton RunReport         # procedure için güvenli EXEC test iskeleti üret
sqldeps explain select-star             # bir tanı kuralının nedenini ve yönünü göster
sqldeps mcp --db deps.sqlite            # MCP araçlarını stdio üzerinden sun
```

`--db` her zaman etkin seçimi geçersiz kılar. `status <klasör>` ise indeksle karşılaştırılacak SQL klasörünü
bildiği için etkin seçimden değil, kendi varsayılanından çalışır.

`sqldeps --help` komutları günlük kullanım, ileri bakım/otomasyon ve entegrasyon başlıklarıyla gruplar.
İleri komutlar kaldırılmadı; mevcut script ve alias kullanımları geriye dönük uyumludur.

`report`, indeksin güncelliğini, dosya drift listesini ve lint bulgularının önem derecesine göre özetini
tek çıktıda birleştirir. `--json` makine-okunur bir nesne döndürür; indeks güncel değilse `1`, aksi hâlde
`0` çıkış kodu verir.

İndeksler kaynak klasörünü metadata'da saklar. Bu nedenle `reindex` argümansız çalıştığında etkin indeksin
kaynak klasörünü bulur ve tam yeniden indeksleme yapar. Eski indekslerde bu bilgi olmayabilir; bu durumda
`sqldeps reindex <folder> --db <file>` kullanın. `drop` geri alınamaz olduğundan varsayılan olarak `[y/N]`
onayı ister; otomasyonlarda `--yes` bu adımı atlar. Başarılı silme sonrasında silinen indeksin adı ve dosya yolu yazılır.

SQLite indeks formatı sürümlü değildir; indeksleme kuralları veya tablo şeması değiştiğinde `RulesVersion`
değeri artırılır ve eski indeks artımlı önbellek olarak kullanılmaz. Yeni indeks geçici SQLite dosyasına
yazılır ve işlem sonunda atomik olarak hedefe taşınır. Bu nedenle migration yerine `reindex` güvenli ve
desteklenen geçiş yoludur.

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

Nesne adları büyük/küçük harfe duyarsızdır: `dbo.Products`, `DBO.PRODUCTS` ve `dbo.products` aynı sonucu verir.
`index` tamamlandığında indeksleme süresi de `Elapsed: 1.23s` biçiminde yazılır; `--quiet` bu satırı da gizler.
`Timing` satırı ayrıştırma, reachability hesabı ve SQLite yazım sürelerini ayrı gösterir; corpus üzerinde
darboğazı bulmak için kullanılabilir.
`Work` satırı, paralel worker'ların toplam ScriptDom parse/collector işini gösterir; bu değer duvar saatinden
büyük olabilir. `GenerateScript` satırı generator çağrılarını ve kaynak metin dilimi kullanımını raporlar.
`./test.sh perf`, indeksleme ve tekrarlı LSP istekleri için bu baseline ölçümlerini üretir.
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
sqldeps query-builder dbo.Products --db deps.sqlite --json
sqldeps impact dbo.Products --db deps.sqlite
sqldeps duplicates --db deps.sqlite
sqldeps errors --db deps.sqlite
sqldeps lint --db deps.sqlite
sqldeps lint --db deps.sqlite --fail-on warning  # CI için warning/error bulgularında başarısız ol
sqldeps impact dbo.Products --db deps.sqlite --json
sqldeps who-writes dbo.Products --db deps.sqlite --limit 10 --no-color
```

Gruplanmış çıktı varsayılandır; `--table` düz tabloyu, `--json` ise aynı veriyi betiklerin kullanabileceği JSON
dizisini yazar. `--limit`, tablo sonuçlarını sınırlar; `--no-color` otomasyonlar veya sade terminaller içindir.
`lint --fail-on <severity>` kullanıldığında `error`, `warning` veya `info` eşiğindeki bulgu varsa komut `1`
ile çıkar; varsayılan davranış bulgular olsa da `0` ile çıkmaktır. `indexes --json` yerel indeksleri ve kaynak
klasörlerini aynı JSON satır yapısıyla verir. `--help`/`-h` komut adından sonra da kullanılabilir.

`query-builder`, yalnızca indekste tanımlı tablo ve görünümler için kolon listesini metadata’dan çıkararak
okunabilir bir `SELECT` preview’ı üretir. Nesne ve kolon adları SQL Server köşeli parantezleriyle quote edilir,
`t` alias’ı kullanılır ve sonuç doğrudan dosyaya yazılmaz. `--json` çıktısında `object_name`, `object_kind`,
`columns` ve `sql` alanları bulunur. Eksik veya belirsiz metadata’da komut preview üretmez.

## MCP (deneysel)

`sqldeps mcp`, etkin indeksi veya `--db` ile verilen SQLite dosyasını salt-okunur MCP araçları olarak stdio
üzerinden sunar. MCP istemcisi her JSON-RPC mesajını tek satırda gönderir; sunucu da yanıtları tek satırlık
JSON olarak yazar. Keyfi SQL çalıştırılmaz; araçlar yalnızca `IndexQueryService` içindeki parametreli sorguları
kullanır.

```sh
sqldeps mcp                         # etkin indeksi kullan
sqldeps mcp --db deps.sqlite        # belirli indeksi kullan
```

Sunulan araçlar `find_objects`, `objects`, `source`, `table_columns`, `deps`, `impact`, `who_writes`,
`who_reads`, `lint`, `query_builder`, `explain` ve `drift`'tir. Listeleme/sorgu araçları hem `content` içindeki JSON metni,
hem de makine tüketimi için `structuredContent.rows` alanı olarak döner. `table_columns` satırlarında
`primary_key` ve `foreign_key` metadata'sı korunur. `query_builder`, `table` ve isteğe bağlı `join`
argümanlarıyla CLI ile aynı parse-doğrulamalı, salt-okunur preview sözleşmesini döndürür.
`explain`, CLI ve LSP’nin paylaştığı tanı kataloğundan kural özeti ve önerilen yönü döndürür.
`drift`, indeksin kaydettiği kaynak klasörüyle dosya hash’lerini karşılaştırır; keyfi bir klasör taramaz.

CLI çıkış kodları sabittir:

| Kod | Anlamı |
|---:|---|
| `0` | İşlem başarılı; `lint` için eşik aşılmadı. |
| `1` | İndeks güncel değil veya `lint --fail-on` eşiği aşıldı. |
| `2` | Kullanım hatası, bilinmeyen seçenek/komut veya açılamayan indeks. |

`index --json` çıktısı `sql_files`, `reused_files`, `objects`, `dependencies`, `parse_errors`, `warnings`,
`reach_pairs`, `cycles` ve `database` alanlarını taşır. `status`/`doctor --json` çıktıları `sql_files`, `indexed_files`, `added`, `changed`, `removed`, `objects`,
`dependencies`, `parse_errors` ve `is_current` alanlarını taşır. `indexes --json` ise `name`, `active` ve
`source_root` alanlarını taşır; `active` boolean’dır. Sorgu komutları SQL sonuç kolonlarını JSON anahtarları olarak korur.
`status`, indeksteki dosya özetlerini diskteki dosyalarla karşılaştırır; eklenen, değişen veya silinen dosya
varsa `1` ile çıkar ve yeniden `index` çalıştırılmasını ister.
`drift` aynı karşılaştırmayı dosya bazında `added`, `changed` ve `removed` durumlarıyla listeler; fark varsa
CI kullanımında `1` çıkış kodu döndürür.

`lint`, indeksteki T-SQL bulgularını tek akışta toplar: ayrıştırma hataları, çözümlenemeyen dinamik SQL,
yinelenen nesne tanımları, kullanılmayan nesneler ve veritabanları arası başvuruların yanında `SELECT *`,
`NOLOCK`/`READUNCOMMITTED`, filtresiz `SELECT`, `MERGE` ve `WHERE` içermeyen `UPDATE`/`DELETE` kalıplarını da gösterir. Bu bulgular kaynak kodu değiştirmez;
`error`, `warning` ve `info` önem dereceleriyle, kaynak konumu varsa onunla birlikte yazılır.
`lint --json` çıktısı LSP ile ortak `source`, `confidence` ve `stale` alanlarını da taşır. `source`, güncel indeks için
`fresh-index`, dosyaları değişmiş indeks için `stale-index` olur; stale bulgular `confidence: low` ve `stale: true`
ile işaretlenir.
İndekste doğrulanamayan şemasız table/view/procedure/function referansları `information` seviyesinde ve düşük
confidence ile; açıkça şemalı fakat bulunamayan referanslar `warning` seviyesinde ve orta confidence ile raporlanır.
Parser kaynaklı hatalar, ScriptDom’un SQL Server 2022 (`TSql160`) söz dizimi sınırı nedeniyle `confidence: low`
ve `confidenceReason: scriptdom-tsql160-parser` taşır. Catalog bağlantısı olmadan şeması çözülemeyen nesneler,
varsa `confidenceReason: source-index-no-catalog` ile raporlanır.

Sık kullanılan tanı kodları: `unknown-column`/`ambiguous-column` sütun çözümleme hatalarıdır;
`unknown-procedure`, `unknown-function`, `unknown-procedure-parameter` ve `missing-procedure-parameter`
yordam/fonksiyon çağrılarını denetler;
`ambiguous-table`, `cte-alias-shadowing`, `select-star`, `nolock`, `merge`, `leading-wildcard-like`, `non-sargable-function`,
`implicit-filter-conversion`, `unfiltered-select`
ve `update-without-where`
ise kalite/performans uyarılarıdır. `parse-error` ayrıştırılamayan batch’i, `dynamic-sql` ise bağımlılığı
tamamlanamayan dinamik SQL’i gösterir.

Bilerek kullanılan bir kalıbı yalnızca takip eden satır için gizlemek istersen, onun hemen üstüne kuralı yaz:

```sql
-- sqldeps: disable-next-line=merge
MERGE dbo.Target AS target USING dbo.Source AS source ON target.Id = source.Id
WHEN MATCHED THEN UPDATE SET Value = source.Value;
```

Virgülle birden çok kural ya da `all` kullanılabilir. Bastırma hem `lint` hem LSP tanıları için geçerlidir.
LSP istemcisi kod eylemlerini destekliyorsa, tanı üzerindeki **Disable sqldeps … for this line** quick fix'i bu
yorumu otomatik ekler.
Belirsiz sütun tanılarında aday tablo alias’larını seçen, benzer sütun adlarında ise alias’ı koruyan replacement
quick fix’ler de sunulur.

## LSP (deneysel)

`sqldeps lsp`, Neovim gibi bir LSP istemcisi tarafından stdio üzerinden başlatılır; terminalde doğrudan
çalıştırılmaz. Açık ama kaydedilmemiş SQL belgeleri için ayrıştırma hatalarını yayımlar; hatadan önceki geçerli
önek ayrıştırılabiliyorsa onun sembollerini kullanmaya devam eder. Belge/çalışma alanı
sembolleri, tanıma gitme, başvuruları bulma ve hover desteği sağlar. Nesne üzerinde hover tanımı ve parametre
imzasını gösterir; lint tanısının üzerinde hover yapıldığında kuralı, mesajı, güven seviyesini, kaynağını ve
etkilenen nesneyi açıklar. Başlatılırken çalışma alanını arka planda
indeksler; açık belgeler diskteki sürümün önüne geçer. Kaydetme ve kapatma, o dosyanın çalışma alanı anlık
görüntüsünü yeniler; böylece diğer dosyalardan yapılan gezinmeler eski diske yazılmış sürüme dönmez. Rename ve
formatting bilinçli olarak desteklenmez. Açık belgede ayrıştırma hatalarının yanında `lint` kuralları da
tanı olarak yayımlanır; örneğin `NOLOCK` uyarı, `SELECT *` bilgi seviyesindedir. Lint tanıları mümkün olduğunda
tüm satır yerine doğrudan ilgili SQL parçasını vurgular. `alias.` sonrasında tablo/görünüm sütunlarını; `dbo.` veya nesne adı
önekinde yordam, fonksiyon, görünüm ve tabloları tamamlar. Açık CTE sütun listeleri ve adı çıkarılabilen
türetilmiş alt sorgu sütunları da aynı belge içinde tamamlanır. `DECLARE @name TABLE (...)`
bildirimleriyle tanımlanan table variable sütunları da aynı sorgu kapsamındaki tanılarda kullanılır.
`CREATE TABLE #name (...)` ve `ALTER TABLE #name ADD ...` ile tanımlanan geçici tablo sütunları,
alt sorgu ve UNION çıktılarıyla birlikte aynı belge içindeki sütun tamamlama ve doğrulamada kullanılır.
İndeksin bildiği bir tabloda yazım hatasıyla kullanılan sütun için, tek ve yakın bir ad varsa tanı bunu önerir;
LSP istemcisi kod eylemlerini destekliyorsa alias'ı koruyan **Replace with …** quick fix'i de sunulur.
Kesin sözdizimi düzeltmeleri için negatif `OFFSET` literal'ı `0`'a, sıfır `FETCH` literal'ı `1`'e çeviren
preview editleri de sunulur; değişken veya hesaplanan ifadeler için doğrudan edit üretilmez.
Güvenli yapısal code action'lar da yalnızca metadata ve kapsam kesin olduğunda üretilir: eksik zorunlu
procedure parametreleri tek bir `NULL` placeholder edit'iyle, `INSERT` hedefindeki eksik kolonlar ise
bilinen yerleşik tiplerde `CAST(NULL AS <type>)` değerleriyle tamamlanabilir. Eşit şekilli çok satırlı
`INSERT ... VALUES` ve belirli `INSERT ... SELECT` akışları desteklenir; `*`, `UNION`, output parametresi,
belirsiz tip veya eşit olmayan satır şekli gibi durumlarda edit yerine açıklama ya da hiç öneri verilmez.
Seçili indekslenmiş tablo/view üzerinde dosyayı değiştirmeyen **Preview SELECT** code action’ı da
`sqldeps.query-builder` komutunu çağırır.
`test-skeleton` indekslenmiş procedure parametrelerinden yalnızca adlandırılmış `NULL` argümanlarla bir
`EXEC` preview’ı üretir; `OUTPUT` parametreleri veya eksik metadata görüldüğünde üretimi reddeder.
`explain` desteklenen tanı kurallarının kısa neden, varsayılan önem ve önerilen yön bilgisini insan-okunur
veya JSON biçiminde gösterir; LSP diagnostic hover aynı katalogdan yararlanır.
Güvenli alt kümedeki `EXEC(...)` string birleşimleri de kullanıcı onaylı `sp_executesql` preview edit'ine
dönüştürülebilir; dinamik nesne adları ve tipi çözülemeyen değişkenler bilinçli olarak reddedilir.
İstemci renklerini yapılandırırken `error` seviyesini kırmızı, `warning` seviyesini sarı ve `info` seviyesini
mavi/soluk bir vurgu olarak eşlemek okunabilir bir varsayılan sağlar; LSP sunucusu bu seviyeleri standart
diagnostic severity değerleriyle gönderir.
İstemci veya entegrasyon araçları `textDocument/diagnostic` ile açık belgenin, `workspace/diagnostic` ile
açık olmayan indekslenmiş SQL dosyalarının ayrıştırma ve lint tanılarını standart rapor biçiminde alabilir.
İstemci `initialize` içinde pull diagnostic capability’sini bildirirse sunucu aynı belge için ayrıca
`publishDiagnostics` push bildirimi göndermez; böylece istemci tanıyı iki kez göstermez.

### Canlı veritabanı istemcisi

Neovim’de canlı sorgu çalıştırmak veya gerçek veritabanı şemasını gezmek için Vim-Dadbod gibi ayrı bir istemci
kullanılabilir. Sorumluluklar bilinçli olarak ayrıdır:

- `sqldeps lsp`: kaynak dosyalarından statik bağımlılık, lint, tamamlamalar ve güvenli preview’lar.
- Vim-Dadbod: canlı bağlantı, sorgu çalıştırma ve veritabanı oturumu.

`sqldeps` bağlantı bilgisi veya parola saklamaz; canlı istemcinin sonuçlarını indeks metadata’sıyla birleştirmez.
Bu nedenle iki araç aynı SQL dosyasını açabilir, ancak biri diğerinin credential veya bağlantı ayarına bağımlı değildir.

Kaynak tüketimini sınırlamak için indeksleyici 64 MiB’den büyük SQL dosyalarını ve 16 MiB’den büyük batch’leri
ayrıştırmaz. LSP de 16 MiB’den büyük açık belgeleri ayrıştırmak yerine boyut tanısı yayımlar.

```sql
-- dbo.UpdateProducts neye ne yapıyor
SELECT target_object, operation, source_file
FROM dependencies
WHERE source_object = 'dbo.UpdateProducts';

-- dbo.Products'ı UPDATE eden kim
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

## Kod yapısı

Araç iki aynı veri toplama yolunu kullanır: komut satırındaki indeksleme diskteki `.sql` dosyalarını kalıcı
SQLite indeksine dönüştürür; LSP ise açık belgenin bellekteki sürümünü aynı kurallarla analiz eder. Genel akış
şöyledir:

```text
CLI / LSP belgesi → ScriptDom ayrıştırma → DependencyCollector → model listeleri → SQLite / LSP yanıtı
```

LSP canlı bir SQL Server bağlantısı açmaz; tamamlamalar ve tanılar indekslenmiş kaynak dosyalarından
ve açık belgelerin anlık görünümünden üretilir. Canlı metadata sağlayıcısı bu projenin kapsamı dışındadır.

Canlı veritabanı sorguları ve şema gezintisi için Vim-Dadbod gibi ayrı bir Neovim istemcisi kullanılabilir.
Bu proje bağlantı açmaz, credential saklamaz ve dış istemcinin sorgu sonuçlarını statik analiz metadata’sı
yerine geçirmez.

- `Program.cs` komutları ve terminal çıktısını yönetir. `Indexing/SqlFolderIndexer.cs`, klasördeki SQL
  dosyalarını bulur, değişmeyenleri `IndexCache` üzerinden yeniden kullanır ve dosyaları paralel ayrıştırır.
- `Indexing/DependencyCollector*.cs`, ScriptDom ağacını dolaşan asıl T-SQL bilgisidir. Ana dosya okumaları ve
  ortak yardımcıları; `Calls`, `Writes`, `Definitions` ve `DynamicSql` parçaları sırasıyla çağrıları,
  DML yazmalarını, nesne tanımlarını ve sabit metinli dinamik SQL'i toplar. `DynamicSqlText.cs` yalnızca
  çözümlenebilen sabit SQL ifadesini ayırır.
- `Storage/` SQLite şemasını, indeks yazımını ve sorgu yardımcılarını içerir. `dependencies`, `objects`,
  sütunlar, parametreler ve lint bulguları burada kalıcı hâle gelir; reachability ve rapor görünümleri de bu
  katmanda üretilir.
- `Core/IndexQueryService.cs`, CLI ve MCP'nin paylaştığı whitelist'li, parametreli indeks sorgularını sağlar.
- `Mcp/McpServer.cs`, aynı executable içindeki salt-okunur MCP stdio protokolünü yürütür; aktif indeks yolunu
  `Storage/IndexPathResolver.cs` ile CLI ile birlikte çözer.
- `Lsp/DocumentAnalysis.cs`, kaydedilmemiş belgeyi batch batch ayrıştırır, geçerli öneki hata sonrasında da
  kullanmayı dener ve collector sonucunu `AnalysisResult` içinde tutar. `QueryColumnValidator` ile
  `ProcedureParameterValidator` bu ortak ayrıştırma sonucunu paylaşır.
- `Lsp/LanguageServer.cs` JSON-RPC isteklerini yönlendirir. `LanguageServer.Completion.cs` tamamlamayı,
  `LanguageServer.CodeActions.cs` lint quick fix'lerini taşır; diğer gezinme istekleri çalışma alanı ve açık
  belge anlık görünümünü birlikte sorgular.
- LSP ve MCP bilinçli olarak şimdilik aynı `sqldeps` executable’ı içindedir. `IndexQueryService` ortak ve
  güvenli sınırı sağladığı için iki ayrı `.csproj` eklemek şu aşamada dağıtım veya bakım değerine sahip değil;
  bağımsız dağıtım, sürümleme ya da izolasyon ihtiyacı oluşursa ayrıştırma yeniden değerlendirilecektir.

Davranış değiştirirken önce verinin hangi katmanda üretildiğini seçmek gerekir: yeni T-SQL bilgisi için
`DependencyCollector`, kalıcı sorgu/rapor için `Storage`, editör davranışı için `Lsp` doğru başlangıç noktasıdır.
`./test.sh` uçtan uca CLI senaryolarını çalıştırır; ortak smoke yardımcıları ve JSON-RPC framing’i
`tests/smoke/common.sh` içinde, indeksleme, CLI, LSP, hedefli quick-fix ve FP/FN senaryoları ise
`tests/smoke/index.sh`, `tests/smoke/cli.sh`, `tests/smoke/lsp.sh`, `tests/smoke/mcp.sh`, `tests/smoke/quick-fix.sh` ve
`tests/smoke/fp-fn.sh` altında tutulur. `tests/smoke/fp-fn-report.sh`, üretimde yayılan her kuralın corpus
referansını ve assertion matrisini raporlar; `fp-fn-coverage.baseline` içindeki minimum kapsamın altına
düşüşte CI'ı durdurur. Güvenli alt kümeye alınmayan FN/temkin kararları
`tests/smoke/fp-fn-decisions.md` içinde tutulur.
LSP değişiklikleri için aynı fixture'lar ve JSON-RPC akışı ayrıca gözden geçirilmelidir.

Geliştirme döngüsünde hızlı kontrol için `dotnet build -c Release --no-restore` veya hedefli test komutları
kullanılabilir. `./test.sh unit`, `./test.sh index`, `./test.sh cli`, `./test.sh lsp`, `./test.sh mcp`,
`./test.sh quick-fix`, `./test.sh perf` ve `./test.sh fp-fn`
yalnızca ilgili katmanı çalıştırır. Push öncesinde `./test.sh` (veya `./test.sh all`) derleme, fixture indeksleme,
CLI sorguları ve LSP/FP-FN JSON-RPC senaryolarını birlikte doğrulamalıdır.

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
./test.sh             # tüm smoke suite'lerini çalıştırır
./test.sh unit        # yalnızca .NET unit testleri
./test.sh lsp         # yalnızca LSP smoke suite'i
./test.sh mcp         # yalnızca MCP smoke suite'i
./test.sh quick-fix   # yalnızca quick-fix kabul matrisi
./test.sh perf        # sabit corpus üzerinde indeksleme evrelerini ölçer
./test.sh fp-fn       # yalnızca FP/FN corpus'u ve kapsam raporu
./test.sh report      # mevcut FP/FN kapsam raporunu yeniden üretir
SQLDEPS_CORPUS_DB=~/.local/share/sqldeps/local-corpus.sqlite bash tests/smoke/local-corpus-fp-fn.sh
```
