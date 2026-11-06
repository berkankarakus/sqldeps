# T-SQL ANTLR spike

Bu klasördeki grammar ve adapter, ScriptDom’un üretim yolunu değiştirmeyen deneysel bir karşılaştırma
katmanıdır. Şimdilik `SELECT` ve `CREATE TABLE` için küçük bir T-SQL alt kümesini parse eder; gerçek
indexleme, LSP diagnostics ve code action akışına bağlı değildir.

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

ANTLR’ın ara `.interp` ve `.tokens` dosyaları kaynak ağacına dahil edilmez. Generated C# dosyaları
kaynak kontrolünde tutulur; CI build’inin Java veya ANTLR jar’ına bağlı kalmaması amaçlanır.
