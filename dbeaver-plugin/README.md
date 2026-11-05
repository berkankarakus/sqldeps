# sqldeps DBeaver eklentisi

Bu klasör, mevcut `sqldeps lsp` sunucusunu DBeaver SQL Editor'a bağlayan bağımsız
Eclipse/OSGi eklentisini içerir. SQL analiz motoru veya LSP sunucusu burada yeniden
yazılmaz.

## Mevcut kapsam

- DBeaver'ın public `org.jkiss.dbeaver.sql.editorAddIns` extension point'i kullanılır.
- Her SQL Editor örneği için `sqldeps lsp` süreci stdio üzerinden başlatılır.
- DBeaver'ın prosedür/view kaynak sekmeleri `SQLEditorNested` olarak açıldığında da
  workbench part yaşam döngüsü üzerinden ayrı bir LSP köprüsü başlatılır; bu sayede
  Database Object Editor içindeki Source sekmesi de analiz edilir.
- LSP4J, `Content-Length` JSON-RPC framing'ini yönetir; sunucunun stdout'u protokol için
  ayrılmıştır, stderr ayrı bir akış olarak DBeaver loguna aktarılır.
- `didOpen`, debounce edilmiş tam metin `didChange`, pull `textDocument/diagnostic` ve
  `didClose` akışları uygulanır.
- Diagnostics, UTF-16 LSP konumlarından DBeaver annotation model offset'lerine çevrilir.
- `Analyze SQL` komutu standart `textDocument/diagnostic` isteğini tetikler.
- `Analyze SQL` ve `Show sqldeps Diagnostics` komutları SQL Editor ve nested Source
  editörlerinde çalışır.
- Diagnostics, DBeaver'ın standart annotation ve hover akışıyla SQL Editor'da gösterilir; komut ilk
  bulguyu görünür konuma taşır ve next/previous komutlarıyla bulgular arasında gezinilebilir.
- DBeaver oturumları varsayılan olarak indeks kullanmadan başlar.
- `Select sqldeps Index...` kayıtlı SQLite indekslerini adları ve yollarıyla listeler ve
  seçilen yolu yalnızca aktif SQL Editor LSP oturumuna gönderir. Kayıtlı indeks yoksa
  doğrudan SQLite dosya seçicisine düşer. `Use Automatic sqldeps Index` workspace eşleşmesi
  ve active-index fallback davranışını tekrar açar.
- Sunucu `codeActionProvider` ilan ederse `Show All sqldeps Code Actions...` komutu tüm diagnostic’ler için güvenli,
  yalnızca aktif belgeye ait WorkspaceEdit'leri kullanıcı onayından sonra uygular.
- Komut tabanlı code action'lar da whitelist ile desteklenir; `Preview SELECT` sonucu standart
  `workspace/executeCommand` isteğiyle alınır ve kullanıcıya gösterilir, dosyaya yazılmaz.
- Sunucu şu anda T-SQL/SQL Server odaklıdır. DBeaver dialect'i yalnızca yerel bağlam olarak
  gözlemlenir; sunucu standart bir dialect configuration akışı desteklemediği için aktarılmaz.
  sqldeps'in desteklemediği özel bir configuration metodu uydurulmaz ve bağlantı
  bilgileri/parolalar gönderilmez.

## Kendi DBeaver'ında deneme

macOS'ta kurulumun büyük bölümünü otomatikleştirmek için repo kökünden şu komutu
çalıştırabilirsin:

```bash
./dbeaver-plugin/install-local.sh
```

Script, DBeaver'ın Java 21 runtime'ını kullanarak bundle'ı derler, Maven Central'dan
LSP4J 0.24.0 ve Eclipse compiler bağımlılıklarını indirir, yerel p2 repository oluşturur
ve Eclipse p2 director ile DBeaver'ın mevcut `DefaultProfile`'ına kurar. DBeaver farklı
bir konumdaysa:

```bash
DBEAVER_HOME=/absolute/path/to/DBeaver.app/Contents/Eclipse \
  ./dbeaver-plugin/install-local.sh
```

Kurulumdan sonra DBeaver'ı bir kez `-clean` ile ve sqldeps executable yolunu vererek
başlat:

```bash
/Applications/DBeaver.app/Contents/MacOS/dbeaver \
  -clean \
  -vmargs \
  -Dsqldeps.lsp.path=/Users/berkan/.local/bin/sqldeps
```

Sadece bundle üretmek için `./dbeaver-plugin/build-local.sh` kullanılabilir. Üretilen
JAR ve yerel p2 repository geçici build dizinine yazılır; repo içine derleme çıktısı
eklenmez. Kurulum sırasında DBeaver kapalı olmalıdır.

Java adapter'ın bağımlılıksız smoke testlerini çalıştırmak için Java 21 JDK gerekir:

```bash
./test.sh dbeaver-java
```

Bu komut belge state'i, URI üretimi, indeks seçimi, debounce ve diagnostic request cancellation davranışını test eder.
DBeaver'ın kendi JRE'si plugin'i çalıştırmak için yeterli olabilir; test derlemek için
sistemde ayrıca `java` ve `javac` komutlarını sağlayan JDK bulunmalıdır.

Kurulu DBeaver'ın gerçek Eclipse/JFace sınıflarıyla belge senkronizasyonunu doğrulamak için:

```bash
./test.sh dbeaver-runtime
```

Gerçek `sqldeps lsp` süreciyle LSP4J initialize, `didOpen`, diagnostics ve shutdown akışını
doğrulamak için:

```bash
./test.sh dbeaver-lsp-runtime
```

1. DBeaver'ın kullandığı Eclipse sürümüne uygun Java/JDK'yı ve LSP4J 0.24.x bundle'larını
   PDE target platform'a ekle.
2. Bu klasörü Eclipse PDE'de plug-in project olarak aç ve `org.sqldeps.dbeaver` bundle'ını
   DBeaver'ın `org.jkiss.dbeaver.ui.editors.sql` bundle'ını içeren target platform'a karşı
   derle.
3. DBeaver çalışırken `sqldeps` komutunun PATH üzerinde olduğundan emin ol. Farklı bir
   executable kullanacaksan DBeaver başlatılırken şu JVM property'yi ver:

   ```text
   -Dsqldeps.lsp.path=/absolute/path/to/sqldeps
   ```

   Eklenti bu executable'ı `lsp` argümanıyla başlatır.
4. PDE ile `org.sqldeps.dbeaver` bundle'ını DBeaver installation'ına deploy et veya
   Eclipse Application üzerinden DBeaver product'ını çalıştır.
5. SQL Editor açıp SQL metnini değiştir. Diagnostics annotation olarak görünmeli. Manuel
   istek için ana menüde `sqldeps > Analyze SQL` komutunu kullan.

## Bilinen sınırlar

- Bu ilk dilim genel p2 update-site üretmiyor; yerel DBeaver doğrulaması tamamlandıktan sonra
  feature/repository projeleri eklenecek.
- DBeaver ve LSP4J bundle sürümleri target platform'a göre doğrulanmalıdır; bu makinede Java
  sistem PATH'inde olmasa da kurulu DBeaver 21.0.8 JRE'si mevcut. LSP4J bundle'ları DBeaver
  kurulumunda bulunmadığı için PDE target platform'a ayrıca eklenmelidir.

