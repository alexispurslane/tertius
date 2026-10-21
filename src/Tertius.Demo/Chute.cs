namespace Tertius.Demo;

/// <summary>
/// A chute down a spoke of a spinning station: a trough on a back plate, with a rail on each side, along -X in the
/// station's axes at θ = 180 degrees (docs/plans/blue-danube-demo.md, "The exit", "The chute" and "The slide"). The
/// trough is open toward +Z, the spoke's middle. Every place is in the station's axes, in meters.
/// </summary>
/// <param name="BackFace">The z of the back plate's face toward the trough.</param>
/// <param name="HalfWidth">Half the width between the rails' inner faces, along Y.</param>
/// <param name="Top">The x of the chute's top, where its faces start: the deck.</param>
/// <param name="BrakeFrom">The x below which the brake run starts.</param>
/// <param name="End">The x of the trough's lower end.</param>
/// <param name="Friction">The friction coefficient of the trough above the brake run.</param>
/// <param name="BrakeFriction">The friction coefficient of the brake run.</param>
public readonly record struct Chute(double BackFace, double HalfWidth, double Top, double BrakeFrom, double End, double Friction, double BrakeFriction);
