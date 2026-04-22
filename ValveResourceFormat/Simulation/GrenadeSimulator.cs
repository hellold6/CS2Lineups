using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace ValveResourceFormat.Simulation;

/// <summary>
/// Information about a collision detected between grenade positions.
/// </summary>
public record CollisionInfo(
    bool Hit,
    Vector3 HitPosition,
    Vector3 SurfaceNormal
);

/// <summary>
/// Grenade categories with different detonation behavior.
/// </summary>
public enum GrenadeType
{
    /// <summary>Smoke grenade that usually pops on walkable impact and otherwise air-bursts after a long delay.</summary>
    Smoke,
    /// <summary>Flashbang that detonates on a short timer regardless of impact.</summary>
    Flashbang,
    /// <summary>HE grenade that detonates on a fixed timer regardless of impact.</summary>
    HEGrenade,
    /// <summary>Decoy grenade that activates on first impact.</summary>
    Decoy,
    /// <summary>Molotov or incendiary grenade that ignites on walkable impact or air-bursts after a short delay.</summary>
    Incendiary,
}

/// <summary>
/// Bounce-response settings for grenade collision handling.
/// </summary>
public readonly record struct GrenadeBounceSettings(
    float BounceRestitution,
    float BounceFriction,
    float GroundBounceRestitution,
    float GroundBounceFriction)
{
    /// <summary>
    /// Default calibrated bounce settings.
    /// </summary>
    public static GrenadeBounceSettings Default => new(
        BounceRestitution: 0.45f,
        BounceFriction: 0.20f,
        GroundBounceRestitution: 0.50f,
        GroundBounceFriction: 0.28f);

    /// <summary>
    /// Clamp all bounce parameters to a physically sensible [0, 1] range.
    /// </summary>
    public GrenadeBounceSettings Clamp() => new(
        Math.Clamp(BounceRestitution, 0f, 1f),
        Math.Clamp(BounceFriction, 0f, 1f),
        Math.Clamp(GroundBounceRestitution, 0f, 1f),
        Math.Clamp(GroundBounceFriction, 0f, 1f));
}

/// <summary>
/// Simulates counter-strike grenade trajectories using a simple Euler integrator.
/// Handles gravity, air damping, collision detection, and bouncing.
/// All physics values are placeholders and will be calibrated against actual CS2 behaviour.
/// </summary>
public class GrenadeSimulator
{
    // CS2 grenade physics constants
    private const float Gravity = 324f;              // Effective grenade gravity, calibrated from CS2 flight data
    private const float AirDamping = 0.9992f;        // Slightly stronger per-tick drag to trim the remaining excess carry
    private const float MinBounceVelocity = 30f;     // Velocity below which grenade stops bouncing
    private const float FlashbangDetonationTime = 1.4f;
    private const float HEDetonationTime = 2f;
    private const float SmokeAirDetonationTime = 16f;
    private const float MolotovAirDetonationTime = 2f;
    private const float DecoyFallbackTime = 16f;
    private const float WalkableSurfaceNormalZ = 0.7f;
    /// <summary>Fixed simulation timestep in seconds (1/64s, matching Source 2 tick rate).</summary>
    public const float TimeStep = 0.015625f;
    private const float SurfaceNudge = 0.03125f;     // Small offset from surface after bounce (1/32 unit)

    // Simulation state
    private Vector3 currentPosition;
    private Vector3 currentVelocity;
    private float elapsedTime;
    private bool hasDetonated;
    private readonly List<Vector3> trajectoryPath;
    private int stepCount;

    /// <summary>
    /// Optional writer for debug output. When set, every simulation step logs
    /// position, velocity, and collision details. Set to <see cref="Console.Out"/>
    /// or a <see cref="StreamWriter"/> to capture output.
    /// </summary>
    public TextWriter? DebugLog { get; set; }

    /// <summary>
    /// Gets or sets which grenade type to simulate.
    /// </summary>
    public GrenadeType GrenadeType { get; set; } = GrenadeType.Smoke;

    /// <summary>
    /// Gets or sets the bounce response parameters used during collision handling.
    /// </summary>
    public GrenadeBounceSettings BounceSettings { get; set; } = GrenadeBounceSettings.Default;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrenadeSimulator"/> class.
    /// </summary>
    public GrenadeSimulator()
    {
        trajectoryPath = [];
        Reset();
    }

    /// <summary>
    /// Reset the simulator to initial state.
    /// </summary>
    public void Reset()
    {
        currentPosition = Vector3.Zero;
        currentVelocity = Vector3.Zero;
        elapsedTime = 0f;
        hasDetonated = false;
        stepCount = 0;
        trajectoryPath.Clear();
    }

    /// <summary>
    /// Initialize a grenade throw from a starting position with a launch velocity.
    /// </summary>
    /// <param name="startPosition">World position where the grenade is thrown from</param>
    /// <param name="launchVelocity">Initial velocity vector (direction and speed)</param>
    public void InitializeThrow(Vector3 startPosition, Vector3 launchVelocity)
    {
        Reset();
        currentPosition = startPosition;
        currentVelocity = launchVelocity;
        trajectoryPath.Add(currentPosition);

        if (DebugLog != null)
        {
            var bounceSettings = BounceSettings.Clamp();

            DebugLog.WriteLine("=== GrenadeSimulator Debug ===");
            DebugLog.WriteLine($"Grenade Type: {GrenadeType}");
            DebugLog.WriteLine($"Start Position: ({startPosition.X:F4}, {startPosition.Y:F4}, {startPosition.Z:F4})");
            DebugLog.WriteLine($"Launch Velocity: ({launchVelocity.X:F4}, {launchVelocity.Y:F4}, {launchVelocity.Z:F4})");
            DebugLog.WriteLine($"Launch Speed: {launchVelocity.Length():F4} u/s");
            DebugLog.WriteLine($"TimeStep: {TimeStep}, Gravity: {Gravity}, AirDamping: {AirDamping}, Restitution: {bounceSettings.BounceRestitution}, GroundRestitution: {bounceSettings.GroundBounceRestitution}, Friction: {bounceSettings.BounceFriction}, GroundFriction: {bounceSettings.GroundBounceFriction}, MaxTime: {GetMaxSimulationTime():F3}");
            DebugLog.WriteLine($"{"Step",5} {"Time",7} | {"Pos.X",10} {"Pos.Y",10} {"Pos.Z",10} | {"Vel.X",10} {"Vel.Y",10} {"Vel.Z",10} | {"Speed",8} | Event");
            DebugLog.WriteLine(new string('-', 110));
            DebugLog.WriteLine($"{0,5} {0f,7:F4} | {startPosition.X,10:F2} {startPosition.Y,10:F2} {startPosition.Z,10:F2} | {launchVelocity.X,10:F2} {launchVelocity.Y,10:F2} {launchVelocity.Z,10:F2} | {launchVelocity.Length(),8:F2} | INIT");
        }
    }

    /// <summary>
    /// Simulate one timestep of grenade motion.
    /// </summary>
    /// <param name="collisionDetector">
    /// Delegate that checks for collisions between two points.
    /// Returns collision info including hit position and surface normal.
    /// </param>
    /// <returns>False if grenade has detonated or stopped, true if simulation ongoing</returns>
    public bool SimulateStep(Func<Vector3, Vector3, CollisionInfo> collisionDetector)
    {
        if (hasDetonated)
        {
            return false;
        }

        stepCount++;

        // Apply gravity along the world up axis (Z in Source/VRF coordinates)
        currentVelocity.Z -= Gravity * TimeStep;

        // Apply a small amount of air damping so the grenade does not carry unrealistically far.
        currentVelocity *= AirDamping;

        // Predict next position using Euler integration
        var nextPosition = currentPosition + currentVelocity * TimeStep;

        // Check for collision along the path
        var collision = collisionDetector(currentPosition, nextPosition);
        if (collision.Hit)
        {
            if (ShouldDetonateOnImpact(collision.SurfaceNormal))
            {
                currentPosition = collision.HitPosition;
                elapsedTime += TimeStep;
                trajectoryPath.Add(currentPosition);

                if (DebugLog != null)
                {
                    DebugLog.WriteLine($"{stepCount,5} {elapsedTime,7:F4} | {currentPosition.X,10:F2} {currentPosition.Y,10:F2} {currentPosition.Z,10:F2} | {currentVelocity.X,10:F2} {currentVelocity.Y,10:F2} {currentVelocity.Z,10:F2} | {currentVelocity.Length(),8:F2} | DETONATE impact normal=({collision.SurfaceNormal.X:F3},{collision.SurfaceNormal.Y:F3},{collision.SurfaceNormal.Z:F3})");
                }

                hasDetonated = true;
                return false;
            }

            var preCollisionVelocity = currentVelocity;
            HandleCollision(collision.HitPosition, collision.SurfaceNormal);

            if (DebugLog != null)
            {
                var time = elapsedTime + TimeStep;
                DebugLog.WriteLine($"{stepCount,5} {time,7:F4} | {currentPosition.X,10:F2} {currentPosition.Y,10:F2} {currentPosition.Z,10:F2} | {currentVelocity.X,10:F2} {currentVelocity.Y,10:F2} {currentVelocity.Z,10:F2} | {currentVelocity.Length(),8:F2} | BOUNCE normal=({collision.SurfaceNormal.X:F3},{collision.SurfaceNormal.Y:F3},{collision.SurfaceNormal.Z:F3}) preVel=({preCollisionVelocity.X:F1},{preCollisionVelocity.Y:F1},{preCollisionVelocity.Z:F1})");
            }

            // If velocity is too low after bounce, grenade stops
            if (currentVelocity.Length() < MinBounceVelocity)
            {
                hasDetonated = true;
                DebugLog?.WriteLine($"  >>> STOPPED (velocity {currentVelocity.Length():F2} < {MinBounceVelocity})");
                return false;
            }
        }
        else
        {
            // No collision, update position
            currentPosition = nextPosition;
        }

        elapsedTime += TimeStep;
        trajectoryPath.Add(currentPosition);

        // Log every 8th step in normal flight to avoid excessive output
        if (DebugLog != null && !collision.Hit && stepCount % 8 == 0)
        {
            DebugLog.WriteLine($"{stepCount,5} {elapsedTime,7:F4} | {currentPosition.X,10:F2} {currentPosition.Y,10:F2} {currentPosition.Z,10:F2} | {currentVelocity.X,10:F2} {currentVelocity.Y,10:F2} {currentVelocity.Z,10:F2} | {currentVelocity.Length(),8:F2} |");
        }

        // Grenade detonates after its configured timer / air-burst threshold.
        var maxSimulationTime = GetMaxSimulationTime();
        if (elapsedTime >= maxSimulationTime)
        {
            hasDetonated = true;

            if (DebugLog != null)
            {
                DebugLog.WriteLine($"{stepCount,5} {elapsedTime,7:F4} | {currentPosition.X,10:F2} {currentPosition.Y,10:F2} {currentPosition.Z,10:F2} | {currentVelocity.X,10:F2} {currentVelocity.Y,10:F2} {currentVelocity.Z,10:F2} | {currentVelocity.Length(),8:F2} | DETONATE (timer {maxSimulationTime}s)");
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Simulate the entire grenade trajectory until it stops or detonates.
    /// </summary>
    /// <param name="startPosition">Starting position</param>
    /// <param name="launchVelocity">Launch velocity</param>
    /// <param name="collisionDetector">Collision detection delegate that returns collision info with surface normals</param>
    /// <param name="maxSteps">Maximum number of simulation steps to prevent infinite loops</param>
    /// <returns>Collection of positions along the trajectory path</returns>
    public IReadOnlyList<Vector3> SimulateFullTrajectory(
        Vector3 startPosition,
        Vector3 launchVelocity,
        Func<Vector3, Vector3, CollisionInfo> collisionDetector,
        int maxSteps = 10000)
    {
        InitializeThrow(startPosition, launchVelocity);

        var steps = 0;
        while (SimulateStep(collisionDetector) && steps < maxSteps)
        {
            steps++;
        }

        return trajectoryPath.AsReadOnly();
    }

    /// <summary>
    /// Handle a collision between the grenade and map geometry.
    /// Reflects velocity around the surface normal and applies energy loss based on bounce restitution.
    /// </summary>
    /// <param name="collisionPoint">Point of impact</param>
    /// <param name="surfaceNormal">Surface normal at the collision point (should be normalized)</param>
    private void HandleCollision(Vector3 collisionPoint, Vector3 surfaceNormal)
    {
        var bounceSettings = BounceSettings.Clamp();

        // Ensure surface normal is normalized
        surfaceNormal = Vector3.Normalize(surfaceNormal);

        // Decompose velocity into normal and tangential components
        var normalSpeed = Vector3.Dot(currentVelocity, surfaceNormal);
        var normalComponent = normalSpeed * surfaceNormal;
        var tangentialComponent = currentVelocity - normalComponent;

        var isWalkableSurface = surfaceNormal.Z >= WalkableSurfaceNormalZ;
        var friction = isWalkableSurface ? bounceSettings.GroundBounceFriction : bounceSettings.BounceFriction;
        var restitution = isWalkableSurface ? bounceSettings.GroundBounceRestitution : bounceSettings.BounceRestitution;

        // Reflect normal component and apply restitution (energy loss perpendicular to surface).
        // Apply friction to tangential component (energy loss parallel to surface).
        currentVelocity = tangentialComponent * (1f - friction) - normalComponent * restitution;

        // Position at collision point, nudged slightly away from the surface
        // to prevent the next gravity step from immediately re-colliding
        currentPosition = collisionPoint + surfaceNormal * SurfaceNudge;
    }

    private float GetMaxSimulationTime() => GrenadeType switch
    {
        GrenadeType.Flashbang => FlashbangDetonationTime,
        GrenadeType.HEGrenade => HEDetonationTime,
        GrenadeType.Smoke => SmokeAirDetonationTime,
        GrenadeType.Decoy => DecoyFallbackTime,
        GrenadeType.Incendiary => MolotovAirDetonationTime,
        _ => SmokeAirDetonationTime,
    };

    private bool ShouldDetonateOnImpact(Vector3 surfaceNormal)
    {
        var normalizedSurfaceNormal = Vector3.Normalize(surfaceNormal);

        return GrenadeType switch
        {
            GrenadeType.Decoy => true,
            GrenadeType.Incendiary => normalizedSurfaceNormal.Z >= WalkableSurfaceNormalZ,
            _ => false,
        };
    }

    // Properties for inspection and trajectory visualization
    /// <summary>
    /// Gets the current position of the grenade in world space.
    /// </summary>
    public Vector3 CurrentPosition => currentPosition;

    /// <summary>
    /// Gets the current velocity vector of the grenade.
    /// </summary>
    public Vector3 CurrentVelocity => currentVelocity;

    /// <summary>
    /// Gets the elapsed time since the grenade was thrown, in seconds.
    /// </summary>
    public float ElapsedTime => elapsedTime;

    /// <summary>
    /// Gets a value indicating whether the grenade has stopped or detonated.
    /// </summary>
    public bool HasDetonated => hasDetonated;

    /// <summary>
    /// Gets the complete trajectory path as a read-only list of positions.
    /// </summary>
    public IReadOnlyList<Vector3> TrajectoryPath => trajectoryPath.AsReadOnly();
}
