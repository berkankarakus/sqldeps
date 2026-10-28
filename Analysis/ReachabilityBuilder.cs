using SqlDeps.Domain;

namespace SqlDeps.Analysis;

/// <summary>Everything one object reaches, and how: "source reaches reached in depth steps along path".</summary>
public sealed record ReachRow(
    string Source,
    string Reached,
    int Depth,
    string Path
);

/// <summary>An object that ends up calling or using itself, with the shortest way round.</summary>
public sealed record CycleRow(
    string ObjectName,
    int Depth,
    string Path
);

public sealed record ReachResult(
    IReadOnlyList<ReachRow> Reach,
    IReadOnlyList<CycleRow> Cycles
);

/// <summary>
/// Follows the dependencies transitively. For every object a breadth-first walk lists each object it
/// reaches at the shortest distance, together with the path that gets there. The same table read the
/// other way round answers "what is affected if this changes".
/// </summary>
public static class ReachabilityBuilder
{
    private sealed record Edge(
        string Target,
        string Operation
    );

    public static ReachResult Build(
        IReadOnlyList<Dependency> dependencies,
        int maxDepth
    )
    {
        var edgesBySource = GroupEdges(dependencies);
        List<ReachRow> reach = [];
        List<CycleRow> cycles = [];

        foreach (var source in edgesBySource.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            Walk(source, edgesBySource, maxDepth, reach, cycles);
        }

        return new ReachResult(reach, cycles);
    }

    private static Dictionary<string, List<Edge>> GroupEdges(IReadOnlyList<Dependency> dependencies)
    {
        Dictionary<string, List<Edge>> edgesBySource = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (var dependency in dependencies)
        {
            if (IsNotAnObject(dependency.SourceObject) || IsNotAnObject(dependency.TargetObject))
            {
                continue;
            }

            var key = dependency.SourceObject + "|" + dependency.TargetObject + "|" + dependency.Operation;

            if (!seen.Add(key))
            {
                continue;
            }

            if (!edgesBySource.TryGetValue(dependency.SourceObject, out List<Edge>? value))
            {
                value = [];
                edgesBySource[dependency.SourceObject] = value;
            }

            value.Add(new Edge(dependency.TargetObject, dependency.Operation));
        }

        foreach (var edges in edgesBySource.Values)
        {
            edges.Sort(CompareEdges);
        }

        return edgesBySource;
    }

    // Loose statements outside any object and dynamic SQL are not nodes of the graph.
    private static bool IsNotAnObject(string name)
    {
        return name == "(batch)" || name == ObjectNames.DynamicSql;
    }

    private static int CompareEdges(
        Edge left,
        Edge right
    )
    {
        var byTarget = string.Compare(left.Target, right.Target, StringComparison.OrdinalIgnoreCase);

        if (byTarget != 0)
        {
            return byTarget;
        }

        return string.Compare(left.Operation, right.Operation, StringComparison.Ordinal);
    }

    private static void Walk(
        string source,
        Dictionary<string, List<Edge>> edgesBySource,
        int maxDepth,
        List<ReachRow> reach,
        List<CycleRow> cycles
    )
    {
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase) { source };
        Queue<(string Node, int Depth, string Path)> queue = new();
        queue.Enqueue((source, 0, source));
        var foundCycle = false;

        while (queue.Count > 0)
        {
            var (node, depth, path) = queue.Dequeue();

            if (depth >= maxDepth || !edgesBySource.TryGetValue(node, out var edges))
            {
                continue;
            }

            foreach (var edge in edges)
            {
                var nextPath = $"{path} -{edge.Operation}-> {edge.Target}";

                if (string.Equals(edge.Target, source, StringComparison.OrdinalIgnoreCase))
                {
                    if (!foundCycle)
                    {
                        cycles.Add(new CycleRow(source, depth + 1, nextPath));
                        foundCycle = true;
                    }

                    continue;
                }

                if (!visited.Add(edge.Target))
                {
                    continue;
                }

                reach.Add(new ReachRow(source, edge.Target, depth + 1, nextPath));
                queue.Enqueue((edge.Target, depth + 1, nextPath));
            }
        }
    }
}
