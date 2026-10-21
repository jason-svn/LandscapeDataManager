namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>One tree canopy in plan: centre in metres, radius in metres.</summary>
public readonly record struct CanopyCircle(double X, double Y, double Radius);

/// <summary>
/// The plan area covered by a set of tree canopies, overlaps counted once. Exact, not sampled: by
/// Green's theorem the union's area is ½∮(x dy − y dx) around its boundary, and that boundary is
/// made of the arcs of each circle no other circle covers. A circle inside another contributes no
/// arc; identical circles count once.
/// </summary>
public static class CanopyUnion
{
    private const double Epsilon = 1e-9;

    public static double AreaSquareMeters(IEnumerable<CanopyCircle> canopies)
    {
        var circles = canopies.Where(circle => circle.Radius > Epsilon && double.IsFinite(circle.X) && double.IsFinite(circle.Y)).ToList();
        if (circles.Count == 0)
        {
            return 0d;
        }

        // Neighbours through a grid of cells as wide as the largest canopy, so overlap checks only
        // look at nearby trees.
        var cellSize = 2d * circles.Max(circle => circle.Radius);
        var grid = new Dictionary<(long, long), List<int>>();
        (long, long) CellOf(CanopyCircle circle) => ((long)Math.Floor(circle.X / cellSize), (long)Math.Floor(circle.Y / cellSize));
        for (var index = 0; index < circles.Count; index++)
        {
            var cell = CellOf(circles[index]);
            if (!grid.TryGetValue(cell, out var members))
            {
                grid[cell] = members = [];
            }

            members.Add(index);
        }

        var area = 0d;
        var covered = new List<(double Start, double End)>();
        for (var i = 0; i < circles.Count; i++)
        {
            var circle = circles[i];
            covered.Clear();
            var hidden = false;
            var (cellX, cellY) = CellOf(circle);
            for (var dx = -1L; dx <= 1 && !hidden; dx++)
            {
                for (var dy = -1L; dy <= 1 && !hidden; dy++)
                {
                    if (!grid.TryGetValue((cellX + dx, cellY + dy), out var members))
                    {
                        continue;
                    }

                    foreach (var j in members)
                    {
                        if (j == i)
                        {
                            continue;
                        }

                        var other = circles[j];
                        var distance = Math.Sqrt(Math.Pow(other.X - circle.X, 2) + Math.Pow(other.Y - circle.Y, 2));
                        if (distance >= circle.Radius + other.Radius - Epsilon)
                        {
                            continue; // apart, or touching at a point
                        }

                        if (distance < Epsilon && Math.Abs(other.Radius - circle.Radius) < Epsilon)
                        {
                            // The same canopy twice: the first one draws the boundary.
                            if (j < i)
                            {
                                hidden = true;
                                break;
                            }

                            continue;
                        }

                        if (distance <= other.Radius - circle.Radius + Epsilon)
                        {
                            hidden = true; // inside the other canopy
                            break;
                        }

                        if (distance <= circle.Radius - other.Radius + Epsilon)
                        {
                            continue; // the other is inside this one: it covers none of this boundary
                        }

                        var towards = Math.Atan2(other.Y - circle.Y, other.X - circle.X);
                        var cosine = (circle.Radius * circle.Radius + distance * distance - other.Radius * other.Radius) / (2d * circle.Radius * distance);
                        var halfAngle = Math.Acos(Math.Clamp(cosine, -1d, 1d));
                        AddInterval(covered, towards - halfAngle, towards + halfAngle);
                    }
                }
            }

            if (hidden)
            {
                continue;
            }

            foreach (var (start, end) in ExposedArcs(covered))
            {
                area += ArcContribution(circle, start, end);
            }
        }

        return area / 2d;
    }

    /// <summary>x dy − y dx integrated along the arc from <paramref name="start"/> to <paramref name="end"/> (anticlockwise).</summary>
    private static double ArcContribution(CanopyCircle circle, double start, double end) =>
        circle.Radius * circle.Radius * (end - start) +
        circle.X * circle.Radius * (Math.Sin(end) - Math.Sin(start)) -
        circle.Y * circle.Radius * (Math.Cos(end) - Math.Cos(start));

    /// <summary>Adds an angular interval, split at 2π so every stored interval lies within [0, 2π).</summary>
    private static void AddInterval(List<(double Start, double End)> intervals, double start, double end)
    {
        const double fullTurn = 2d * Math.PI;
        var length = Math.Clamp(end - start, 0d, fullTurn);
        start %= fullTurn;
        if (start < 0)
        {
            start += fullTurn;
        }

        if (start + length <= fullTurn)
        {
            intervals.Add((start, start + length));
        }
        else
        {
            intervals.Add((start, fullTurn));
            intervals.Add((0d, start + length - fullTurn));
        }
    }

    private static IEnumerable<(double Start, double End)> ExposedArcs(List<(double Start, double End)> covered)
    {
        const double fullTurn = 2d * Math.PI;
        if (covered.Count == 0)
        {
            yield return (0d, fullTurn);
            yield break;
        }

        covered.Sort((first, second) => first.Start.CompareTo(second.Start));
        var cursor = 0d;
        foreach (var (start, end) in covered)
        {
            if (start > cursor)
            {
                yield return (cursor, start);
            }

            cursor = Math.Max(cursor, end);
        }

        if (cursor < fullTurn)
        {
            yield return (cursor, fullTurn);
        }
    }
}
