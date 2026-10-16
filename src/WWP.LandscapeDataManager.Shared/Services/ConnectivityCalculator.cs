namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>A point in plan, in metres.</summary>
public readonly record struct PlanPoint(double X, double Y);

/// <summary>
/// One habitat patch for the connectivity network: a tree's canopy circle (<see cref="Outline"/> null)
/// or a planting area's plan outline. <see cref="AreaSquareMeters"/> is the patch's "attribute" aᵢ in
/// the PC index — canopy area for a tree, floor area for a planting area.
/// </summary>
public sealed record ConnectivityPatch(
    string Id,
    double AreaSquareMeters,
    PlanPoint Center,
    double RadiusMeters,
    IReadOnlyList<PlanPoint>? Outline = null);

/// <summary>
/// <paramref name="DispersalDistancesMeters"/> are the distances at which direct dispersal between two
/// patches has probability <paramref name="ProbabilityAtDistance"/> (Conefor's convention, 0.5 = median
/// dispersal distance) — each gives its own k = −ln(p)/d. Running several (e.g. 50, 250, 1000 m for
/// beetles, slow-worms and woodland birds) averages their scores and flags patches whose band swings.
/// Paths whose probability falls below <paramref name="NegligibleProbability"/> at the longest distance
/// are ignored, which keeps the network sparse without measurably changing PC.
/// </summary>
public sealed record ConnectivityOptions(
    IReadOnlyList<double> DispersalDistancesMeters,
    double ProbabilityAtDistance = 0.5,
    double NegligibleProbability = 1e-4);

/// <summary>
/// One patch's result. <see cref="DpcByDistance"/> is dPC as a fraction (0.05 = removing the patch
/// loses 5% of PC) and <see cref="ScoreByDistance"/> its percentile rank, both per dispersal distance;
/// <see cref="Score"/> is the mean percentile and <see cref="Band"/> its 1–4 band.
/// <see cref="Uncertain"/> is true when the per-distance bands are two or more bands apart.
/// </summary>
public sealed record ConnectivityPatchResult(
    string Id,
    IReadOnlyList<double> DpcByDistance,
    IReadOnlyList<double> ScoreByDistance,
    IReadOnlyList<int> BandByDistance,
    double Score,
    int Band,
    bool Uncertain)
{
    public double MeanDpcPercent => DpcByDistance.Count == 0 ? 0 : DpcByDistance.Average() * 100d;
}

/// <summary><see cref="EquivalentConnectedAreaByDistance"/> is EC = √(Σᵢ Σⱼ aᵢ·aⱼ·p*ᵢⱼ) in m², per dispersal distance — the size of a single patch that would give the same connectivity.</summary>
public sealed record ConnectivityRunResult(
    IReadOnlyList<ConnectivityPatchResult> Patches,
    IReadOnlyList<double> EquivalentConnectedAreaByDistance);

/// <summary>
/// Structural connectivity by the Probability of Connectivity index (Saura &amp; Pascual-Hortal 2007,
/// as in Conefor): PC = Σᵢ Σⱼ aᵢ·aⱼ·p*ᵢⱼ / A_L², with pᵢⱼ = exp(−k·dᵢⱼ) on edge-to-edge distance and
/// p*ᵢⱼ the maximum-product path. Each patch's importance is dPCₖ = (PC − PC without k) / PC; A_L
/// cancels out of that ratio, so the landscape area is never needed. Patches are then ranked by dPC
/// into percentile scores and the four bands.
/// </summary>
/// <remarks>
/// Because −ln(exp(−k·d)) = k·d, the maximum-product path is simply the shortest path in metres and
/// is the same for every k — so one pass of shortest paths serves every dispersal distance. Removing
/// patch k only changes p* for pairs whose shortest path runs through k: for each source, those are
/// exactly k's descendants in the source's shortest-path tree, and only they are re-searched (with k
/// removed). That keeps dPC exact without re-running all-pairs shortest paths once per patch.
/// </remarks>
public static class ConnectivityCalculator
{
    public const int LowBand = 1;
    public const int ModerateBand = 2;
    public const int StrongBand = 3;
    public const int CriticalBand = 4;

    public static int ToBand(double score) => score switch
    {
        >= 0.8 => CriticalBand,
        >= 0.5 => StrongBand,
        >= 0.2 => ModerateBand,
        _ => LowBand
    };

    public static string BandName(int band) => band switch
    {
        CriticalBand => "Critical stepping-stone",
        StrongBand => "Strong connector",
        ModerateBand => "Moderate",
        _ => "Low"
    };

    public static string BandMeaning(int band) => band switch
    {
        CriticalBand => "Removing it (or not building it) fragments the network",
        StrongBand => "Important but substitutable",
        ModerateBand => "Local value only",
        _ => "Contributes little to landscape connectivity"
    };

    /// <summary>Parses "50, 250, 1000" (commas, semicolons or spaces) into positive distances; null if any entry is invalid or none are given.</summary>
    public static IReadOnlyList<double>? ParseDistances(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var values = new List<double>();
        foreach (var part in text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value) || value <= 0)
            {
                return null;
            }

            values.Add(value);
        }

        return values.Count == 0 ? null : values.Distinct().OrderBy(value => value).ToList();
    }

    public static ConnectivityRunResult Calculate(
        IReadOnlyList<ConnectivityPatch> patches,
        ConnectivityOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (options.DispersalDistancesMeters.Count == 0 || options.DispersalDistancesMeters.Any(distance => !(distance > 0)))
        {
            throw new ArgumentException("Give at least one positive dispersal distance.", nameof(options));
        }

        if (!(options.ProbabilityAtDistance is > 0 and < 1))
        {
            throw new ArgumentException("The dispersal probability must be between 0 and 1.", nameof(options));
        }

        var count = patches.Count;
        var distanceCount = options.DispersalDistancesMeters.Count;
        if (count == 0)
        {
            return new ConnectivityRunResult([], new double[distanceCount]);
        }

        var decay = options.DispersalDistancesMeters.Select(distance => -Math.Log(options.ProbabilityAtDistance) / distance).ToArray();
        var cutoff = Math.Log(1d / options.NegligibleProbability) / decay.Min();
        var areas = patches.Select(patch => Math.Max(0d, patch.AreaSquareMeters)).ToArray();
        var adjacency = BuildAdjacency(patches, cutoff, cancellationToken);

        // Per patch: the ordered pairs it takes part in (term "intra + flux"), and the loss in other
        // pairs' p* when it's removed (term "connector"), each per dispersal distance.
        var ownPairs = new double[count * distanceCount];
        var connector = new double[count * distanceCount];
        var pcNumerator = new double[distanceCount];
        var gate = new object();
        var completed = 0;

        Parallel.For(
            0,
            count,
            new ParallelOptions { CancellationToken = cancellationToken },
            () => new SourceWorkspace(count, distanceCount),
            (source, _, workspace) =>
            {
                ProcessSource(source, adjacency, areas, decay, cutoff, workspace, ownPairs);
                var done = Interlocked.Increment(ref completed);
                if (progress is not null && (done % 16 == 0 || done == count))
                {
                    progress.Report((double)done / count);
                }

                return workspace;
            },
            workspace =>
            {
                lock (gate)
                {
                    for (var index = 0; index < connector.Length; index++)
                    {
                        connector[index] += workspace.Connector[index];
                    }

                    for (var m = 0; m < distanceCount; m++)
                    {
                        pcNumerator[m] += workspace.PcNumerator[m];
                    }
                }
            });

        var dpc = new double[distanceCount][];
        var scores = new double[distanceCount][];
        for (var m = 0; m < distanceCount; m++)
        {
            dpc[m] = new double[count];
            for (var k = 0; k < count; k++)
            {
                var loss = ownPairs[k * distanceCount + m] + connector[k * distanceCount + m];
                dpc[m][k] = pcNumerator[m] > 0 ? Math.Clamp(loss / pcNumerator[m], 0d, 1d) : 0d;
            }

            scores[m] = PercentileScores(dpc[m]);
        }

        var results = new List<ConnectivityPatchResult>(count);
        for (var k = 0; k < count; k++)
        {
            var patchDpc = new double[distanceCount];
            var patchScores = new double[distanceCount];
            var patchBands = new int[distanceCount];
            for (var m = 0; m < distanceCount; m++)
            {
                patchDpc[m] = dpc[m][k];
                patchScores[m] = scores[m][k];
                patchBands[m] = ToBand(patchScores[m]);
            }

            var score = patchScores.Average();
            results.Add(new ConnectivityPatchResult(
                patches[k].Id,
                patchDpc,
                patchScores,
                patchBands,
                score,
                ToBand(score),
                patchBands.Max() - patchBands.Min() >= 2));
        }

        return new ConnectivityRunResult(results, pcNumerator.Select(Math.Sqrt).ToArray());
    }

    /// <summary>
    /// Percentile rank (rank − 1) / (N − 1), lowest 0 and highest 1. Tied values share their average
    /// rank, so equally isolated patches get the same score instead of an arbitrary order.
    /// </summary>
    public static double[] PercentileScores(IReadOnlyList<double> values)
    {
        var count = values.Count;
        var scores = new double[count];
        if (count == 1)
        {
            scores[0] = 1d;
            return scores;
        }

        var order = Enumerable.Range(0, count).OrderBy(index => values[index]).ToArray();
        var start = 0;
        while (start < count)
        {
            var end = start;
            while (end + 1 < count && AreTied(values[order[end + 1]], values[order[start]]))
            {
                end++;
            }

            // Ranks start..end (0-based) share their average.
            var score = (start + end) / 2d / (count - 1);
            for (var position = start; position <= end; position++)
            {
                scores[order[position]] = score;
            }

            start = end + 1;
        }

        return scores;
    }

    private static bool AreTied(double a, double b) =>
        Math.Abs(a - b) <= 1e-12 * Math.Max(1d, Math.Max(Math.Abs(a), Math.Abs(b)));

    private sealed class SourceWorkspace
    {
        public SourceWorkspace(int count, int distanceCount)
        {
            Distance = new double[count];
            Parent = new int[count];
            Settled = new List<int>(count);
            FirstChild = new int[count];
            NextSibling = new int[count];
            Mark = new int[count];
            Recomputed = new double[count];
            Subtree = new List<int>(count);
            Stack = new Stack<int>();
            Queue = new PriorityQueue<int, double>();
            Connector = new double[count * distanceCount];
            PcNumerator = new double[distanceCount];
        }

        public double[] Distance { get; }
        public int[] Parent { get; }
        public List<int> Settled { get; }
        public int[] FirstChild { get; }
        public int[] NextSibling { get; }
        public int[] Mark { get; }
        public int Stamp { get; set; }
        public double[] Recomputed { get; }
        public List<int> Subtree { get; }
        public Stack<int> Stack { get; }
        public PriorityQueue<int, double> Queue { get; }
        public double[] Connector { get; }
        public double[] PcNumerator { get; }
    }

    private static void ProcessSource(
        int source,
        (int To, double Weight)[][] adjacency,
        double[] areas,
        double[] decay,
        double cutoff,
        SourceWorkspace workspace,
        double[] ownPairs)
    {
        var count = areas.Length;
        var distanceCount = decay.Length;
        var distance = workspace.Distance;
        var parent = workspace.Parent;
        var settled = workspace.Settled;
        var queue = workspace.Queue;

        Array.Fill(distance, double.PositiveInfinity);
        Array.Fill(parent, -1);
        settled.Clear();
        queue.Clear();
        distance[source] = 0;
        queue.Enqueue(source, 0);
        while (queue.TryDequeue(out var node, out var nodeDistance))
        {
            if (nodeDistance > distance[node])
            {
                continue;
            }

            settled.Add(node);
            foreach (var (to, weight) in adjacency[node])
            {
                var candidate = nodeDistance + weight;
                if (candidate <= cutoff && candidate < distance[to])
                {
                    distance[to] = candidate;
                    parent[to] = node;
                    queue.Enqueue(to, candidate);
                }
            }
        }

        // Row sums Σⱼ aᵢ·aⱼ·p*ᵢⱼ (j = i included, p*ᵢᵢ = 1). Each source owns its own ownPairs slot.
        var sourceArea = areas[source];
        for (var m = 0; m < distanceCount; m++)
        {
            var rowSum = 0d;
            foreach (var node in settled)
            {
                rowSum += sourceArea * areas[node] * Math.Exp(-decay[m] * distance[node]);
            }

            workspace.PcNumerator[m] += rowSum;
            // Ordered pairs involving the source: (i,i) once, (i,j) and (j,i) for each j ≠ i.
            ownPairs[source * distanceCount + m] = 2 * rowSum - sourceArea * sourceArea;
        }

        // Shortest-path tree as child lists.
        var firstChild = workspace.FirstChild;
        var nextSibling = workspace.NextSibling;
        foreach (var node in settled)
        {
            firstChild[node] = -1;
        }

        for (var index = settled.Count - 1; index >= 1; index--)
        {
            var node = settled[index];
            var nodeParent = parent[node];
            nextSibling[node] = firstChild[nodeParent];
            firstChild[nodeParent] = node;
        }

        foreach (var removed in settled)
        {
            if (removed == source || firstChild[removed] < 0)
            {
                continue;
            }

            AccumulateRemovalLoss(source, removed, adjacency, areas, decay, cutoff, workspace);
        }
    }

    /// <summary>
    /// Re-searches the descendants of <paramref name="removed"/> in the source's shortest-path tree
    /// with that patch taken out, adding the drop in Σ aᵢ·aⱼ·p*ᵢⱼ to its connector term. Every other
    /// patch keeps its distance — its shortest path never went through the removed patch.
    /// </summary>
    private static void AccumulateRemovalLoss(
        int source,
        int removed,
        (int To, double Weight)[][] adjacency,
        double[] areas,
        double[] decay,
        double cutoff,
        SourceWorkspace workspace)
    {
        var distance = workspace.Distance;
        var mark = workspace.Mark;
        var recomputed = workspace.Recomputed;
        var subtree = workspace.Subtree;
        var stack = workspace.Stack;
        var queue = workspace.Queue;
        var stamp = ++workspace.Stamp;

        subtree.Clear();
        stack.Clear();
        mark[removed] = stamp;
        for (var child = workspace.FirstChild[removed]; child >= 0; child = workspace.NextSibling[child])
        {
            stack.Push(child);
        }

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            mark[node] = stamp;
            subtree.Add(node);
            for (var child = workspace.FirstChild[node]; child >= 0; child = workspace.NextSibling[child])
            {
                stack.Push(child);
            }
        }

        // Seed each orphaned patch from neighbours that keep their distance, then settle within the subtree.
        queue.Clear();
        foreach (var node in subtree)
        {
            var best = double.PositiveInfinity;
            foreach (var (from, weight) in adjacency[node])
            {
                if (mark[from] != stamp && double.IsFinite(distance[from]))
                {
                    best = Math.Min(best, distance[from] + weight);
                }
            }

            recomputed[node] = best <= cutoff ? best : double.PositiveInfinity;
            if (double.IsFinite(recomputed[node]))
            {
                queue.Enqueue(node, recomputed[node]);
            }
        }

        while (queue.TryDequeue(out var node, out var nodeDistance))
        {
            if (nodeDistance > recomputed[node])
            {
                continue;
            }

            foreach (var (to, weight) in adjacency[node])
            {
                if (mark[to] != stamp || to == removed)
                {
                    continue;
                }

                var candidate = nodeDistance + weight;
                if (candidate <= cutoff && candidate < recomputed[to])
                {
                    recomputed[to] = candidate;
                    queue.Enqueue(to, candidate);
                }
            }
        }

        var sourceArea = areas[source];
        var distanceCount = decay.Length;
        var offset = removed * distanceCount;
        foreach (var node in subtree)
        {
            if (recomputed[node] <= distance[node])
            {
                continue;
            }

            var pairWeight = sourceArea * areas[node];
            for (var m = 0; m < distanceCount; m++)
            {
                workspace.Connector[offset + m] += pairWeight *
                    (Math.Exp(-decay[m] * distance[node]) - Math.Exp(-decay[m] * recomputed[node]));
            }
        }
    }

    private static (int To, double Weight)[][] BuildAdjacency(
        IReadOnlyList<ConnectivityPatch> patches,
        double cutoff,
        CancellationToken cancellationToken)
    {
        var count = patches.Count;
        var shapes = patches.Select(PatchShape.From).ToArray();
        var edges = new List<(int To, double Weight)>[count];
        for (var index = 0; index < count; index++)
        {
            edges[index] = [];
        }

        // Sort by bounding-box left edge so each patch only compares against those within reach in X.
        var order = Enumerable.Range(0, count).OrderBy(index => shapes[index].MinX).ToArray();
        for (var a = 0; a < count; a++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var first = shapes[order[a]];
            for (var b = a + 1; b < count; b++)
            {
                var second = shapes[order[b]];
                if (second.MinX - first.MaxX > cutoff)
                {
                    break;
                }

                if (second.MinY - first.MaxY > cutoff || first.MinY - second.MaxY > cutoff)
                {
                    continue;
                }

                var gap = PatchShape.EdgeToEdgeDistance(first, second);
                if (gap <= cutoff)
                {
                    edges[order[a]].Add((order[b], gap));
                    edges[order[b]].Add((order[a], gap));
                }
            }
        }

        return edges.Select(list => list.ToArray()).ToArray();
    }

    /// <summary>Edge-to-edge plan distance between two patches in metres; 0 where they touch or overlap.</summary>
    public static double EdgeToEdgeDistance(ConnectivityPatch first, ConnectivityPatch second) =>
        PatchShape.EdgeToEdgeDistance(PatchShape.From(first), PatchShape.From(second));

    private sealed class PatchShape
    {
        private PatchShape(PlanPoint center, double radius, PlanPoint[]? outline)
        {
            Center = center;
            Radius = radius;
            Outline = outline;
            if (outline is null)
            {
                MinX = center.X - radius;
                MaxX = center.X + radius;
                MinY = center.Y - radius;
                MaxY = center.Y + radius;
            }
            else
            {
                MinX = outline.Min(point => point.X);
                MaxX = outline.Max(point => point.X);
                MinY = outline.Min(point => point.Y);
                MaxY = outline.Max(point => point.Y);
            }
        }

        public PlanPoint Center { get; }
        public double Radius { get; }
        public PlanPoint[]? Outline { get; }
        public double MinX { get; }
        public double MaxX { get; }
        public double MinY { get; }
        public double MaxY { get; }

        public static PatchShape From(ConnectivityPatch patch) =>
            patch.Outline is { Count: >= 3 } outline
                ? new PatchShape(patch.Center, 0, outline.ToArray())
                : new PatchShape(patch.Center, Math.Max(0, patch.RadiusMeters), null);

        public static double EdgeToEdgeDistance(PatchShape first, PatchShape second)
        {
            if (first.Outline is null && second.Outline is null)
            {
                return Math.Max(0, Distance(first.Center, second.Center) - first.Radius - second.Radius);
            }

            if (first.Outline is null)
            {
                return Math.Max(0, PointToPolygon(first.Center, second.Outline!) - first.Radius);
            }

            if (second.Outline is null)
            {
                return Math.Max(0, PointToPolygon(second.Center, first.Outline) - second.Radius);
            }

            return PolygonToPolygon(first.Outline, second.Outline);
        }

        private static double Distance(PlanPoint a, PlanPoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private static double PointToPolygon(PlanPoint point, PlanPoint[] polygon)
        {
            if (Contains(polygon, point))
            {
                return 0;
            }

            var best = double.PositiveInfinity;
            for (var index = 0; index < polygon.Length; index++)
            {
                best = Math.Min(best, PointToSegment(point, polygon[index], polygon[(index + 1) % polygon.Length]));
            }

            return best;
        }

        private static double PolygonToPolygon(PlanPoint[] first, PlanPoint[] second)
        {
            if (Contains(second, first[0]) || Contains(first, second[0]))
            {
                return 0;
            }

            var best = double.PositiveInfinity;
            for (var i = 0; i < first.Length; i++)
            {
                var a1 = first[i];
                var a2 = first[(i + 1) % first.Length];
                for (var j = 0; j < second.Length; j++)
                {
                    var b1 = second[j];
                    var b2 = second[(j + 1) % second.Length];
                    if (SegmentsIntersect(a1, a2, b1, b2))
                    {
                        return 0;
                    }

                    best = Math.Min(best, Math.Min(
                        Math.Min(PointToSegment(a1, b1, b2), PointToSegment(a2, b1, b2)),
                        Math.Min(PointToSegment(b1, a1, a2), PointToSegment(b2, a1, a2))));
                }
            }

            return best;
        }

        private static double PointToSegment(PlanPoint point, PlanPoint start, PlanPoint end)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var lengthSquared = dx * dx + dy * dy;
            var t = lengthSquared == 0 ? 0 : Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0, 1);
            return Distance(point, new PlanPoint(start.X + t * dx, start.Y + t * dy));
        }

        private static bool SegmentsIntersect(PlanPoint a1, PlanPoint a2, PlanPoint b1, PlanPoint b2)
        {
            static double Cross(PlanPoint o, PlanPoint a, PlanPoint b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
            var d1 = Cross(b1, b2, a1);
            var d2 = Cross(b1, b2, a2);
            var d3 = Cross(a1, a2, b1);
            var d4 = Cross(a1, a2, b2);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        private static bool Contains(PlanPoint[] polygon, PlanPoint point)
        {
            var inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                if ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y) &&
                    point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X)
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }
}
