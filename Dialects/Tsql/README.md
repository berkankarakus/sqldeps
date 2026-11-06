# T-SQL ANTLR spike

Bu klasördeki grammar ve adapter, ScriptDom’un üretim yolunu değiştirmeyen deneysel bir karşılaştırma
katmanıdır. Gerçek indexleme, LSP diagnostics ve code action akışına bağlı değildir.

`Upstream/` altında [antlr/grammars-v4 T-SQL grammar’ının](https://github.com/antlr/grammars-v4/tree/9fa9f94b20cd3225bd84f27554515da207ec39ff/sql/tsql)
9fa9f94b20cd3225bd84f27554515da207ec39ff commit’indeki kaynak snapshot’ı bulunur. Kaynak dosyaların
MIT lisans başlığı korunmuştur. Upstream README’si grammar’ın henüz tamamen eksiksiz olmadığını belirtir;
bu yüzden snapshot şu aşamada yalnızca geliştirme girdisidir, üretim parser’ı değildir.

Mevcut `TsqlSubset.g4` ve üretilmiş parser, küçük adapter testlerini koruyan geçici spike olarak kalır.
Upstream parser’a geçiş, grammar kapsamı ve hata kurtarma maliyeti ölçüldükten sonra ayrı bir batch’te
yapılacaktır.

Parser kaynakları ANTLR 4.13.2 ile üretilir. Runtime bağımlılığı `Antlr4.Runtime.Standard` üzerinden gelir.
Grammar değiştiğinde aşağıdaki komutla yalnızca kullanılan lexer/parser kaynaklarını yeniden üretin:

```sh
mkdir -p Dialects/Tsql/Generated
cd Dialects/Tsql
java -jar /path/to/antlr-4.13.2-complete.jar \
  -Dlanguage=CSharp \
  -no-visitor \
  -no-listener \
  -package SqlDeps.Dialects.Tsql.Generated \
  -o Generated \
  TsqlSubset.g4
```

Upstream snapshot’ı denemek için çıktıyı kaynak ağacına yazmadan geçici bir dizin kullanın:

```sh
tmp_dir="$(mktemp -d)"
cp Dialects/Tsql/Upstream/TSqlLexer.g4 "$tmp_dir/"
cp Dialects/Tsql/Upstream/TSqlParser.g4 "$tmp_dir/"
cd "$tmp_dir"
java -jar /path/to/antlr-4.13.2-complete.jar \
  -Dlanguage=CSharp \
  -no-visitor \
  -no-listener \
  -package SqlDeps.Dialects.Tsql.Upstream.Generated \
  -o generated \
  TSqlLexer.g4 \
  TSqlParser.g4
```

ANTLR’ın ara `.interp` ve `.tokens` dosyaları kaynak ağacına dahil edilmez. Generated C# dosyaları
kaynak kontrolünde tutulur; CI build’inin Java veya ANTLR jar’ına bağlı kalmaması amaçlanır.
