using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Text.Json;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: ScriptDomBenchmark <scriptdom-assembly> <corpus>");

    return 2;
}

var assemblyPath = Path.GetFullPath(args[0]);
var corpusPath = Path.GetFullPath(args[1]);

if (!File.Exists(assemblyPath))
{
    Console.Error.WriteLine($"ScriptDom assembly not found: {assemblyPath}");

    return 2;
}

if (!Directory.Exists(corpusPath))
{
    Console.Error.WriteLine($"Corpus not found: {corpusPath}");

    return 2;
}

var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
var parserType = assembly.GetType("Microsoft.SqlServer.TransactSql.ScriptDom.TSql160Parser");
var parserConstructor = parserType?.GetConstructor([typeof(bool)]);
var parseMethod = parserType?.GetMethods(BindingFlags.Public | BindingFlags.Instance)
    .SingleOrDefault(method => IsParseMethod(method));

if (parserConstructor is null || parseMethod is null)
{
    Console.Error.WriteLine("The assembly does not expose the expected TSql160Parser API.");

    return 2;
}

var exceptionCounts = new Dictionary<string, int>(StringComparer.Ordinal);
var firstChanceHandler = new EventHandler<FirstChanceExceptionEventArgs>((_, eventArgs) =>
{
    var name = eventArgs.Exception.GetType().FullName ?? eventArgs.Exception.GetType().Name;

    exceptionCounts[name] = exceptionCounts.GetValueOrDefault(name) + 1;
});

AppDomain.CurrentDomain.FirstChanceException += firstChanceHandler;

try
{
    var stopwatch = Stopwatch.StartNew();
    var allocatedBefore = GC.GetTotalAllocatedBytes(true);
    var files = Directory.EnumerateFiles(corpusPath, "*.sql", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .ToArray();
    var parseErrors = 0;
    var tokenCount = 0;
    var errorFiles = new List<FileError>();
    var parsedFragmentTypes = new Dictionary<string, int>(StringComparer.Ordinal);
    var parser = parserConstructor.Invoke([true]);

    foreach (var file in files)
    {
        using var reader = File.OpenText(file);
        var arguments = new object?[] { reader, null };
        var fragment = parseMethod.Invoke(parser, arguments);

        var fileParseErrors = CountItems(arguments[1]);
        parseErrors += fileParseErrors;
        tokenCount += CountPropertyItems(fragment, "ScriptTokenStream");

        if (fileParseErrors > 0)
            errorFiles.Add(new(file, fileParseErrors));

        if (fragment is not null)
        {
            var typeName = fragment.GetType().Name;
            parsedFragmentTypes[typeName] = parsedFragmentTypes.GetValueOrDefault(typeName) + 1;
        }
    }

    stopwatch.Stop();

    var result = new BenchmarkResult(
        files.Length,
        parseErrors,
        tokenCount,
        stopwatch.Elapsed.TotalMilliseconds,
        GC.GetTotalAllocatedBytes(true) - allocatedBefore,
        parsedFragmentTypes,
        exceptionCounts.OrderByDescending(pair => pair.Value)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        errorFiles.OrderByDescending(item => item.Count).Take(20).ToArray());

    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
}
finally
{
    AppDomain.CurrentDomain.FirstChanceException -= firstChanceHandler;
}

return 0;

static bool IsParseMethod(MethodInfo method)
{
    var parameters = method.GetParameters();

    return method.Name == "Parse"
        && method.ReturnType != typeof(void)
        && parameters.Length == 2
        && typeof(TextReader).IsAssignableFrom(parameters[0].ParameterType)
        && parameters[1].IsOut;
}

static int CountItems(object? value)
{
    return value is ICollection collection ? collection.Count : 0;
}

static int CountPropertyItems(object? value, string propertyName)
{
    return value?.GetType().GetProperty(propertyName)?.GetValue(value) is ICollection collection
        ? collection.Count
        : 0;
}

internal sealed record BenchmarkResult(
    int Files,
    int ParseErrors,
    int TokenCount,
    double ElapsedMilliseconds,
    long AllocatedBytes,
    IReadOnlyDictionary<string, int> FragmentTypes,
    IReadOnlyDictionary<string, int> Exceptions,
    IReadOnlyList<FileError> ErrorFiles);

internal sealed record FileError(string File, int Count);
