using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ValveResourceFormat.Simulation;

namespace ValveResourceFormat.Renderer.Simulation;

/// <summary>
/// Integrates grenade trajectory simulation with VRF's Rubikon collision detection.
/// Simulates grenade trajectories bouncing off real CS2 map geometry.
/// </summary>
public class TrajectoryCalculator
{
    private readonly Rubikon physicsWorld;

    /// <summary>
    /// Optional debug log writer. When set, trajectory simulations will log
    /// step-by-step position, velocity, and collision data.
    /// </summary>
    public TextWriter? DebugLog
    {
        get => debugLog;
        set
        {
            debugLog = value;
            physicsWorld.TraceDebugLog = value;
        }
    }

    private TextWriter? debugLog;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrajectoryCalculator"/> class.
    /// </summary>
    /// <param name="physicsWorld">Rubikon physics world loaded from map geometry</param>
    public TrajectoryCalculator(Rubikon physicsWorld)
    {
        this.physicsWorld = physicsWorld ?? throw new ArgumentNullException(nameof(physicsWorld));
    }

    /// <summary>
    /// Calculate the complete trajectory of a grenade throw bouncing off map geometry.
    /// </summary>
    /// <param name="startPosition">Starting position where grenade is thrown from</param>
    /// <param name="launchVelocity">Initial launch velocity</param>
    /// <param name="grenadeType">Grenade behavior profile to simulate.</param>
    /// <param name="bounceSettings">Optional bounce-response override for debug tuning.</param>
    /// <returns>List of positions along the trajectory path</returns>
    public IReadOnlyList<Vector3> CalculateTrajectory(Vector3 startPosition, Vector3 launchVelocity, GrenadeType grenadeType = GrenadeType.Smoke, GrenadeBounceSettings? bounceSettings = null)
    {
        var collisionDetector = CreateRayTraceCollisionDetector();
        var simulator = CreateSimulator(grenadeType, bounceSettings);

        return simulator.SimulateFullTrajectory(
            startPosition,
            launchVelocity,
            collisionDetector,
            maxSteps: 10000
        );
    }

    /// <summary>
    /// Get the final resting position where the grenade detonates or comes to rest.
    /// </summary>
    /// <param name="startPosition">Starting position</param>
    /// <param name="launchVelocity">Launch velocity</param>
    /// <param name="grenadeType">Grenade behavior profile to simulate.</param>
    /// <param name="bounceSettings">Optional bounce-response override for debug tuning.</param>
    /// <returns>Final position vector</returns>
    public Vector3 GetFinalPosition(Vector3 startPosition, Vector3 launchVelocity, GrenadeType grenadeType = GrenadeType.Smoke, GrenadeBounceSettings? bounceSettings = null)
    {
        var path = CalculateTrajectory(startPosition, launchVelocity, grenadeType, bounceSettings);
        return path.Count > 0 ? path[^1] : startPosition;
    }

    /// <summary>
    /// Get information about the complete simulated throw, including final position and elapsed time.
    /// </summary>
    /// <param name="startPosition">Starting position</param>
    /// <param name="launchVelocity">Launch velocity</param>
    /// <param name="grenadeType">Grenade behavior profile to simulate.</param>
    /// <param name="bounceSettings">Optional bounce-response override for debug tuning.</param>
    /// <returns>Summary containing final state</returns>
    public TrajectoryResult GetTrajectoryResult(Vector3 startPosition, Vector3 launchVelocity, GrenadeType grenadeType = GrenadeType.Smoke, GrenadeBounceSettings? bounceSettings = null)
    {
        var collisionDetector = CreateRayTraceCollisionDetector();
        var simulator = CreateSimulator(grenadeType, bounceSettings);
        var path = simulator.SimulateFullTrajectory(
            startPosition,
            launchVelocity,
            collisionDetector,
            maxSteps: 10000
        );

        return new TrajectoryResult(
            Path: path,
            FinalPosition: simulator.CurrentPosition,
            ElapsedTime: simulator.ElapsedTime,
            HasDetonated: simulator.HasDetonated
        );
    }

    /// <summary>
    /// Create a collision detector delegate that uses Rubikon ray tracing.
    /// This is called internally by simulation methods.
    /// </summary>
    /// <returns>Collision detector function for the simulator</returns>
    private Func<Vector3, Vector3, CollisionInfo> CreateRayTraceCollisionDetector()
    {
        return (fromPos, toPos) =>
        {
            var trace = physicsWorld.TraceRay(fromPos, toPos);

            if (trace.Hit)
            {
                return new CollisionInfo(
                    Hit: true,
                    HitPosition: trace.HitPosition,
                    SurfaceNormal: trace.HitNormal
                );
            }

            return new CollisionInfo(Hit: false, Vector3.Zero, Vector3.Zero);
        };
    }

    private GrenadeSimulator CreateSimulator(GrenadeType grenadeType, GrenadeBounceSettings? bounceSettings)
    {
        return new GrenadeSimulator
        {
            DebugLog = debugLog,
            GrenadeType = grenadeType,
            BounceSettings = bounceSettings ?? GrenadeBounceSettings.Default,
        };
    }
}

/// <summary>
/// Result of a complete trajectory simulation.
/// </summary>
public record TrajectoryResult(
    IReadOnlyList<Vector3> Path,
    Vector3 FinalPosition,
    float ElapsedTime,
    bool HasDetonated
);
