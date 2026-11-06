# DBeaver SQL Editor plugin template

Bu dizin, sqldeps çalışma koduna bağlı olmayan, DBeaver SQL Editor eklentisi
başlatmak için kullanılabilir küçük bir Eclipse/OSGi iskeletidir. Template
özellikle DBeaver'ın public SQL Editor extension point'ini, ECJ tabanlı yerel
derlemeyi ve p2 publisher/director kurulum akışını korur.

## Kullanım

1. Bu dizini yeni eklentinin kök dizinine kopyalayın.
2. `org.example.dbeaver` paketini, `org.example.dbeaver` bundle kimliğini ve
   `TemplateSqlEditorAddIn` sınıfını kendi adlandırmanızla değiştirin.
3. `META-INF/MANIFEST.MF` ile `plugin.xml` içindeki sınıf ve bundle kimliklerini
   aynı değerlerle güncelleyin.
4. DBeaver kurulumunu ve Java çalıştırıcısını ortam değişkenleriyle belirtin:

```sh
DBEAVER_HOME=/Applications/DBeaver.app/Contents/Eclipse \
DBEAVER_JAVA=/Applications/DBeaver.app/Contents/Eclipse/jre/Contents/Home/bin/java \
./build-local.sh
```

Kurulum için:

```sh
DBEAVER_HOME=/Applications/DBeaver.app/Contents/Eclipse \
DBEAVER_JAVA=/Applications/DBeaver.app/Contents/Eclipse/jre/Contents/Home/bin/java \
./install-local.sh
```

DBeaver macOS dışındaki bir kurulumda kullanılıyorsa `DBEAVER_HOME` ve
`DBEAVER_JAVA` yolları o kuruluma göre verilmelidir. Dış bağımlılıklar geçici
bir dizine indirilir; template çalışma alanına, SQL kaynaklarına veya parola
gibi bağlantı bilgilerine erişmez.

## Başlangıç noktaları

- `plugin.xml`, `org.jkiss.dbeaver.sql.editorAddIns` extension point'i üzerinden
  `TemplateSqlEditorAddIn` sınıfını SQL Editor'e bağlar.
- `TemplateSqlEditorAddIn`, editör yaşam döngüsünü başlatmak için minimal bir
  `init`/`cleanup` kabuğudur. LSP4J iletişimi ve diagnostics davranışı bu
  template'e ürün özelliği olarak eklenmelidir.
- `build-local.sh`, DBeaver plugin jar'larını classpath'e ekleyerek Java 21
  kaynaklarını derler ve bundle jar'ı üretir.
- `install-local.sh`, bundle'ı geçici p2 deposuna yayımlar ve Equinox p2
  director ile DBeaver'a kurar.

Template, sqldeps'in diagnostic, index, code action veya MCP davranışlarını
kopyalamaz; bu ayrım eklentiyi başka bir LSP sunucusuna uyarlarken kapsamı
kontrollü tutar.

