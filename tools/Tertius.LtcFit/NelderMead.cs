namespace Tertius.LtcFit;

/// <summary>The downhill simplex method of Nelder and Mead, with the standard coefficients.</summary>
internal static class NelderMead
{
    private const double Reflection = 1,
        Expansion = 2,
        Contraction = 0.5,
        Shrink = 0.5;

    /// <summary>
    /// The lowest point that the simplex finds from <paramref name="start"/>, with a first step of <paramref name="step"/>
    /// along each axis. It stops when the best and worst values differ by less than <paramref name="tolerance"/> of their
    /// mean, or after <paramref name="maxIterations"/> steps.
    /// </summary>
    public static double[] Minimize(
        double[] start,
        double step,
        double tolerance,
        int maxIterations,
        Func<double[], double> objective
    )
    {
        int dimensions = start.Length;
        var points = new double[dimensions + 1][];
        var values = new double[dimensions + 1];
        for (int i = 0; i <= dimensions; i++)
        {
            points[i] = (double[])start.Clone();
            if (i > 0)
                points[i][i - 1] += step;
            values[i] = objective(points[i]);
        }

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            (int lowest, int highest, int nextHighest) = Ranks(values);
            double low = Math.Abs(values[lowest]),
                high = Math.Abs(values[highest]);
            if (2 * Math.Abs(low - high) <= (low + high) * tolerance)
                break;

            double[] centroid = new double[dimensions];
            for (int i = 0; i <= dimensions; i++)
            {
                if (i == highest)
                    continue;
                for (int k = 0; k < dimensions; k++)
                    centroid[k] += points[i][k] / dimensions;
            }

            double[] Along(double factor) =>
                [.. Enumerable.Range(0, dimensions).Select(k => centroid[k] + factor * (centroid[k] - points[highest][k]))];

            double[] reflected = Along(Reflection);
            double reflectedValue = objective(reflected);
            if (reflectedValue < values[nextHighest])
            {
                if (reflectedValue < values[lowest])
                {
                    double[] expanded = Along(Expansion);
                    double expandedValue = objective(expanded);
                    if (expandedValue < reflectedValue)
                    {
                        (points[highest], values[highest]) = (expanded, expandedValue);
                        continue;
                    }
                }
                (points[highest], values[highest]) = (reflected, reflectedValue);
                continue;
            }

            double[] contracted = Along(-Contraction);
            double contractedValue = objective(contracted);
            if (contractedValue < values[highest])
            {
                (points[highest], values[highest]) = (contracted, contractedValue);
                continue;
            }

            for (int i = 0; i <= dimensions; i++)
            {
                if (i == lowest)
                    continue;
                for (int k = 0; k < dimensions; k++)
                    points[i][k] = points[lowest][k] + Shrink * (points[i][k] - points[lowest][k]);
                values[i] = objective(points[i]);
            }
        }

        return points[Ranks(values).Lowest];
    }

    /// <summary>The indices of the lowest, the highest and the second highest value, the first of equal values first.</summary>
    private static (int Lowest, int Highest, int NextHighest) Ranks(double[] values)
    {
        int lowest = 0,
            highest = 0;
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] < values[lowest])
                lowest = i;
            if (values[i] > values[highest])
                highest = i;
        }
        int nextHighest = highest == 0 ? 1 : 0;
        for (int i = 0; i < values.Length; i++)
            if (i != highest && values[i] > values[nextHighest])
                nextHighest = i;
        // One dimension has two points: the other one is both the lowest and the second highest.
        return (lowest, highest, Math.Min(nextHighest, values.Length - 1));
    }
}
