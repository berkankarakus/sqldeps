namespace SqlDeps;

internal static partial class CommandLine
{
    private static CommandOptions Parse(string[] args, bool requireValue, bool allowSchema = false, bool allowFull = false, bool allowJobs = false, bool allowFailOn = false, int maximumValues = 1, bool allowJoin = false, bool allowSarif = false, bool allowProfile = false, bool allowGraphFormat = false)
    {
        ParseState state = new();

        for (var index = 0; index < args.Length; index++)
        {
            ParseArgument(args, ref index, state, allowSchema, allowFull, allowJobs, allowFailOn, maximumValues, allowJoin, allowSarif, allowProfile, allowGraphFormat);
        }

        state.Validate(requireValue);

        return state.ToOptions();
    }

    private static void ParseArgument(string[] args, ref int index, ParseState state, bool allowSchema, bool allowFull, bool allowJobs, bool allowFailOn, int maximumValues, bool allowJoin, bool allowSarif, bool allowProfile, bool allowGraphFormat)
    {
        var argument = args[index];

        if (TryParsePathOption(args, ref index, state, allowSchema))
        {
            return;
        }

        if (TryParseSwitchOption(argument, state, allowFull, allowJoin, allowSarif))
        {
            return;
        }

        if (TryParseNumberOption(args, ref index, state, allowJobs))
        {
            return;
        }

        if (TryParseFailOnOption(args, ref index, state, allowFailOn))
        {
            return;
        }

        if (TryParseProfileOption(args, ref index, state, allowProfile))
        {
            return;
        }

        if (TryParseGraphFormatOption(args, ref index, state, allowGraphFormat))
        {
            return;
        }

        if (TryParsePositional(argument, state, maximumValues))
        {
            return;
        }

        throw new ArgumentException("Unknown or incomplete option: " + argument + "\n" + Usage);
    }

    private static bool TryParsePathOption(string[] args, ref int index, ParseState state, bool allowSchema)
    {
        var argument = args[index];

        if (argument == "--db")
        {
            if (!HasFollowingArgument(args, index))
            {
                return false;
            }

            state.DatabasePath = Path.GetFullPath(args[++index]);

            return true;
        }

        if (argument == "--default-schema")
        {
            if (!allowSchema || !HasFollowingArgument(args, index))
            {
                return false;
            }

            state.DefaultSchema = args[++index];

            return true;
        }

        return false;
    }

    private static bool TryParseSwitchOption(string argument, ParseState state, bool allowFull, bool allowJoin, bool allowSarif)
    {
        if (argument == "--full" && allowFull)
        {
            state.FullReindex = true;

            return true;
        }

        if (argument == "--json")
        {
            state.Json = true;

            return true;
        }

        if (argument == "--sarif" && allowSarif)
        {
            state.Sarif = true;

            return true;
        }

        if (argument == "--quiet")
        {
            state.Quiet = true;

            return true;
        }

        if (argument == "--no-color")
        {
            state.NoColor = true;

            return true;
        }

        if (argument == "--no-pager")
        {
            state.NoPager = true;

            return true;
        }

        if (argument == "--table")
        {
            state.Table = true;

            return true;
        }

        if (argument == "--join" && allowJoin)
        {
            state.Join = true;

            return true;
        }

        return false;
    }

    private static bool TryParseNumberOption(string[] args, ref int index, ParseState state, bool allowJobs)
    {
        var argument = args[index];

        if (argument == "--limit" && TryReadPositiveInt(args, ref index, out var limit))
        {
            state.Limit = limit;

            return true;
        }

        if (argument == "--jobs" && allowJobs && TryReadPositiveInt(args, ref index, out var jobs))
        {
            state.Jobs = jobs;

            return true;
        }

        return false;
    }

    private static bool TryReadPositiveInt(string[] args, ref int index, out int value)
    {
        value = 0;

        if (!HasFollowingArgument(args, index))
        {
            return false;
        }

        if (!int.TryParse(args[index + 1], out value) || value <= 0)
        {
            return false;
        }

        index++;

        return true;
    }

    private static bool TryParseFailOnOption(string[] args, ref int index, ParseState state, bool allowFailOn)
    {
        if (!CanParseFailOnOption(args, index, allowFailOn))
        {
            return false;
        }

        var severity = args[++index].ToLowerInvariant();

        if (severity is not ("info" or "warning" or "error"))
        {
            throw new ArgumentException("--fail-on must be one of: info, warning, error\n" + Usage);
        }

        state.FailOn = severity;

        return true;
    }

    private static bool TryParseProfileOption(string[] args, ref int index, ParseState state, bool allowProfile)
    {
        if (args[index] != "--profile" || !allowProfile || !HasFollowingArgument(args, index))
        {
            return false;
        }

        state.Profile = args[++index];

        return true;
    }

    private static bool TryParseGraphFormatOption(string[] args, ref int index, ParseState state, bool allowGraphFormat)
    {
        if (args[index] != "--format" || !allowGraphFormat || !HasFollowingArgument(args, index))
        {
            return false;
        }

        string format = args[++index].ToLowerInvariant();

        if (format is not ("json" or "dot"))
        {
            throw new ArgumentException("--format must be one of: json, dot\n" + Usage);
        }

        state.GraphFormat = format;

        return true;
    }

    private static bool CanParseFailOnOption(string[] args, int index, bool allowFailOn)
    {
        return args[index] == "--fail-on"
            && allowFailOn
            && HasFollowingArgument(args, index);
    }

    private static bool TryParsePositional(string argument, ParseState state, int maximumValues)
    {
        if (!IsPositionalArgument(argument))
        {
            return false;
        }

        if (state.Value is null)
        {
            state.Value = argument;

            return true;
        }

        if (maximumValues > 1 && state.SecondValue is null)
        {
            state.SecondValue = argument;

            return true;
        }

        return false;
    }

    private static bool HasFollowingArgument(string[] args, int index) => index + 1 < args.Length;

    private static bool IsPositionalArgument(string argument) => !argument.StartsWith('-');

    private sealed class ParseState
    {
        public string? Value
        {
            get; set;
        }
        public string? SecondValue
        {
            get; set;
        }
        public string? DatabasePath
        {
            get; set;
        }
        public string? DefaultSchema
        {
            get; set;
        }
        public bool FullReindex
        {
            get; set;
        }
        public bool Json
        {
            get; set;
        }
        public bool Sarif
        {
            get; set;
        }
        public bool Quiet
        {
            get; set;
        }
        public bool NoColor
        {
            get; set;
        }
        public bool NoPager
        {
            get; set;
        }
        public bool Table
        {
            get; set;
        }
        public bool Join
        {
            get; set;
        }
        public int? Limit
        {
            get; set;
        }
        public int? Jobs
        {
            get; set;
        }
        public string? FailOn
        {
            get; set;
        }
        public string? Profile
        {
            get; set;
        }
        public string? GraphFormat
        {
            get; set;
        }
        public void Validate(bool requireValue)
        {
            if (requireValue && Value is null)
            {
                throw new ArgumentException(Usage);
            }
        }
        public CommandOptions ToOptions() => new(Value, SecondValue, DatabasePath, DefaultSchema, FullReindex, Json, Sarif, Quiet, NoColor, NoPager, Table, Join, Limit, Jobs, FailOn, Profile, GraphFormat);
    }

    private sealed record CommandOptions(string? Value, string? SecondValue, string? DatabasePath, string? DefaultSchema, bool FullReindex, bool Json, bool Sarif, bool Quiet, bool NoColor, bool NoPager, bool Table, bool Join, int? Limit, int? Jobs, string? FailOn, string? Profile, string? GraphFormat);
}
