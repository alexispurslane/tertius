using Tertius.Rendering;

namespace Tertius.ReflectionReference;

internal readonly record struct ReferenceQuadratureResult(
    DirectTerms Light,
    double? EstimatedAbsoluteError,
    int Evaluations,
    bool Converged
);

internal sealed partial class ReferenceIntegral
{
    private static readonly double[] ThreeNodes = [-0.7745966692414834, 0, 0.7745966692414834];
    private static readonly double[] ThreeWeights = [5.0 / 9, 8.0 / 9, 5.0 / 9];
    private static readonly double[] FiveNodes =
    [
        -0.9061798459386640,
        -0.5384693101056831,
        0,
        0.5384693101056831,
        0.9061798459386640,
    ];
    private static readonly double[] FiveWeights =
    [
        0.2369268850561891,
        0.4786286704993665,
        0.5688888888888889,
        0.4786286704993665,
        0.2369268850561891,
    ];

    private readonly record struct Panel(
        ReferenceFace Face,
        double Across,
        double Down,
        double Width,
        double Height,
        DirectTerms Light,
        double Error
    );

    public ReferenceQuadratureResult IntegrateEmitter(
        Double3 point,
        Double3 toView,
        double relativeTolerance,
        double absoluteTolerance,
        int maxEvaluations
    )
    {
        if (!double.IsFinite(relativeTolerance) || relativeTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(relativeTolerance));
        if (!double.IsFinite(absoluteTolerance) || absoluteTolerance <= 0)
            throw new ArgumentOutOfRangeException(nameof(absoluteTolerance));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEvaluations);
        var panels = new PriorityQueue<Panel, double>();
        DirectTerms total = default;
        double error = 0;
        int evaluations = 0;
        Double3 mirror = 2 * toView.Y * Double3.UnitY - toView;
        for (int index = 0; index < 6; index++)
        {
            ReferenceFace face = emitter.Face(index);
            if (Double3.Dot(point - face.Origin, face.Normal) <= 0)
                continue;
            double cosine = Double3.Dot(mirror, face.Normal);
            double distance = cosine == 0 ? -1 : Double3.Dot(face.Origin - point, face.Normal) / cosine;
            Double3 peak = distance > 0 ? point + distance * mirror - face.Origin : Double3.Zero;
            double footprint = distance > 0 ? 2 * material.Lobe.Alpha * distance / Math.Abs(cosine) : 0;
            double[] across = Cuts(
                Double3.Dot(peak, face.Across) / Double3.Dot(face.Across, face.Across),
                footprint / face.Across.Length
            );
            double[] down = Cuts(
                Double3.Dot(peak, face.Down) / Double3.Dot(face.Down, face.Down),
                footprint / face.Down.Length
            );
            for (int column = 0; column < across.Length - 1; column++)
            {
                for (int row = 0; row < down.Length - 1; row++)
                {
                    if (evaluations + 34 > maxEvaluations)
                        return new(
                            Light: total,
                            EstimatedAbsoluteError: null,
                            Evaluations: evaluations,
                            Converged: false
                        );
                    Panel panel = EvaluatePanel(
                        face: face,
                        point: point,
                        toView: toView,
                        across: across[column],
                        down: down[row],
                        width: across[column + 1] - across[column],
                        height: down[row + 1] - down[row]
                    );
                    panels.Enqueue(panel, -panel.Error);
                    total = Add(total, panel.Light);
                    error += panel.Error;
                    evaluations += 34;
                }
            }
        }
        while (error > absoluteTolerance + relativeTolerance * Magnitude(total))
        {
            if (evaluations + 136 > maxEvaluations || !panels.TryDequeue(out Panel parent, out _))
                return new(Light: total, EstimatedAbsoluteError: error, Evaluations: evaluations, Converged: false);
            double width = parent.Width / 2;
            double height = parent.Height / 2;
            total = Add(total, Scale(parent.Light, -1));
            error = Math.Max(0, error - parent.Error);
            for (int column = 0; column < 2; column++)
            {
                for (int row = 0; row < 2; row++)
                {
                    Panel child = EvaluatePanel(
                        face: parent.Face,
                        point: point,
                        toView: toView,
                        across: parent.Across + column * width,
                        down: parent.Down + row * height,
                        width: width,
                        height: height
                    );
                    panels.Enqueue(child, -child.Error);
                    total = Add(total, child.Light);
                    error += child.Error;
                    evaluations += 34;
                }
            }
        }
        return new(Light: total, EstimatedAbsoluteError: error, Evaluations: evaluations, Converged: true);
    }

    private static double[] Cuts(double center, double width)
    {
        var cuts = new SortedSet<double> { 0, 0.25, 0.5, 0.75, 1 };
        if (width > 0)
            foreach (double step in new double[] { -4, -1, 0, 1, 4 })
                if (center + step * width is > 0 and < 1 and var cut)
                    cuts.Add(cut);
        return [.. cuts];
    }

    private Panel EvaluatePanel(
        ReferenceFace face,
        Double3 point,
        Double3 toView,
        double across,
        double down,
        double width,
        double height
    )
    {
        DirectTerms coarse = IntegratePanel(
            face: face,
            point: point,
            toView: toView,
            across: across,
            down: down,
            width: width,
            height: height,
            nodes: ThreeNodes,
            weights: ThreeWeights
        );
        DirectTerms fine = IntegratePanel(
            face: face,
            point: point,
            toView: toView,
            across: across,
            down: down,
            width: width,
            height: height,
            nodes: FiveNodes,
            weights: FiveWeights
        );
        return new(
            Face: face,
            Across: across,
            Down: down,
            Width: width,
            Height: height,
            Light: fine,
            Error: Magnitude(Add(fine, Scale(coarse, -1)))
        );
    }

    private DirectTerms IntegratePanel(
        ReferenceFace face,
        Double3 point,
        Double3 toView,
        double across,
        double down,
        double width,
        double height,
        double[] nodes,
        double[] weights
    )
    {
        DirectTerms total = default;
        for (int column = 0; column < nodes.Length; column++)
        {
            for (int row = 0; row < nodes.Length; row++)
            {
                Double3 delta =
                    face.Position(across + (nodes[column] + 1) * width / 2, down + (nodes[row] + 1) * height / 2)
                    - point;
                double squaredDistance = Double3.Dot(delta, delta);
                Double3 direction = delta / Math.Sqrt(squaredDistance);
                double cosine = -Double3.Dot(face.Normal, direction);
                if (direction.Y <= 0 || cosine <= 0)
                    continue;
                DirectTerms response = material.ReflectedTerms(
                    normal: Double3.UnitY,
                    toView: toView,
                    toLight: direction,
                    tables: BrdfTables.Shared,
                    lightSine: 0
                );
                double weight =
                    weights[column] * weights[row] * face.Area * width * height * cosine / (4 * squaredDistance);
                total = Add(
                    total,
                    new(response.Diffuse * emitter.Radiance * weight, response.Specular * emitter.Radiance * weight)
                );
            }
        }
        return total;
    }

    private static DirectTerms Add(DirectTerms left, DirectTerms right) =>
        new(left.Diffuse + right.Diffuse, left.Specular + right.Specular);

    private static DirectTerms Scale(DirectTerms light, double scale) =>
        new(light.Diffuse * scale, light.Specular * scale);

    private static double Magnitude(DirectTerms light) =>
        Math.Max(
            Math.Max(Math.Abs(light.Diffuse.X), Math.Max(Math.Abs(light.Diffuse.Y), Math.Abs(light.Diffuse.Z))),
            Math.Max(Math.Abs(light.Specular.X), Math.Max(Math.Abs(light.Specular.Y), Math.Abs(light.Specular.Z)))
        );
}
