using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ValveResourceFormat.Simulation;

namespace ValveResourceFormat.Renderer.Simulation;

/// <summary>
/// CS2 grenade throw types with different initial velocities.
/// </summary>
public enum ThrowType
{
    /// <summary>Left click — standard full-power throw at 750 u/s.</summary>
    LongThrow,
    /// <summary>Right click — underhand lob at ~200 u/s.</summary>
    ShortThrow,
    /// <summary>Left + right click — medium power at ~500 u/s.</summary>
    MidThrow,
    /// <summary>Jump + left click — full power throw released during upward jump motion.</summary>
    JumpThrow,
}

/// <summary>
/// Searches for valid grenade lineups by brute-force sampling launch angles and velocities.
/// </summary>
public class LineupSearcher
{
    /// <summary>
    /// Approximate height of a standing player's hand in Source 2 units.
    /// Grenades are thrown from hand height, not floor level.
    /// </summary>
    private const float PlayerThrowHeight = 64f;
    private const float PlayerJumpImpulse = 301.993377f;

    /// <summary>
    /// Progress information for a running lineup search.
    /// </summary>
    /// <param name="CompletedCandidates">Number of candidates that have been evaluated so far.</param>
    /// <param name="TotalCandidates">Total number of candidates to evaluate for this search.</param>
    /// <param name="MatchesFound">Number of valid lineups found so far.</param>
    public record SearchProgress(long CompletedCandidates, long TotalCandidates, int MatchesFound);

    /// <summary>
    /// Trajectory visualization data reported during search for live rendering.
    /// </summary>
    /// <param name="Path">Sampled trajectory path points.</param>
    /// <param name="IsMatch">Whether this trajectory landed within tolerance of the target.</param>
    /// <param name="DistanceToTarget">Distance from the final position to the target.</param>
    public record SearchVisualization(IReadOnlyList<Vector3> Path, bool IsMatch, float DistanceToTarget);

    private readonly TrajectoryCalculator calculator;

    /// <summary>
    /// Gets the base launch speed for a throw type.
    /// </summary>
    public static float GetThrowSpeed(ThrowType throwType) => throwType switch
    {
        ThrowType.LongThrow => 750f,
        ThrowType.ShortThrow => 200f,
        ThrowType.MidThrow => 500f,
        ThrowType.JumpThrow => 750f,
        _ => 750f,
    };

    /// <summary>
    /// Gets the additional upward Z velocity for a throw type.
    /// Jump throws inherit the player's upward jump impulse at release.
    /// </summary>
    public static float GetThrowZBoost(ThrowType throwType) => throwType switch
    {
        ThrowType.JumpThrow => PlayerJumpImpulse,
        _ => 0f,
    };

    /// <summary>
    /// Gets the additional release height for a throw type.
    /// Jump throws currently use the same hand height as standing throws.
    /// The jump effect comes from inheriting the player's upward velocity.
    /// </summary>
    public static float GetThrowReleaseHeight(ThrowType throwType) => throwType switch
    {
        ThrowType.JumpThrow => 0f,
        _ => 0f,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="LineupSearcher"/> class.
    /// </summary>
    /// <param name="calculator">Trajectory calculator for simulating throws</param>
    public LineupSearcher(TrajectoryCalculator calculator)
    {
        this.calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
    }

    /// <summary>
    /// Result of a single grenade throw candidate from the search.
    /// </summary>
    public record LineupResult(
        Vector3 LaunchVelocity,
        Vector3 FinalPosition,
        float DistanceToTarget,
        float PitchAngleDegrees,
        float YawAngleDegrees,
        float VelocityMagnitude,
        ThrowType ThrowType,
        GrenadeType GrenadeType,
        Vector3 StartPosition
    );

    /// <summary>
    /// Result of the debug bounce auto-tuning pass.
    /// </summary>
    public record BounceTuningResult(
        GrenadeBounceSettings BounceSettings,
        IReadOnlyList<Vector3> Path,
        Vector3 FinalPosition,
        float DistanceToTarget
    );

    /// <summary>
    /// Configuration for the brute-force search over angle and velocity space.
    /// </summary>
    public record SearchParams(
        float MinPitch,
        float MaxPitch,
        float PitchStep,
        float MinYaw,
        float MaxYaw,
        float YawStep,
        float Tolerance,
        ThrowType ThrowType = ThrowType.LongThrow,
        GrenadeType GrenadeType = GrenadeType.Smoke,
        float MinVelocity = 0f,
        float MaxVelocity = 0f,
        float VelocityStep = 0f
    )
    {
        /// <summary>Whether this search iterates over a velocity range instead of using the throw type's fixed speed.</summary>
        public bool HasVelocityRange => VelocityStep > 0f && MaxVelocity > MinVelocity;

        /// <summary>
        /// Create default search parameters with reasonable ranges for CS2 grenade throws.
        /// </summary>
        public static SearchParams CreateDefault(ThrowType throwType = ThrowType.LongThrow, GrenadeType grenadeType = GrenadeType.Smoke) => new(
            MinPitch: -45f,
            MaxPitch: 85f,
            PitchStep: 2f,
            MinYaw: -180f,
            MaxYaw: 180f,
            YawStep: 5f,
            Tolerance: 50f,
            ThrowType: throwType,
            GrenadeType: grenadeType
        );

        /// <summary>
        /// Create a coarse search for quick results (good for prototyping).
        /// </summary>
        public static SearchParams CreateCoarse(ThrowType throwType = ThrowType.LongThrow, GrenadeType grenadeType = GrenadeType.Smoke) => new(
            MinPitch: -45f,
            MaxPitch: 85f,
            PitchStep: 5f,
            MinYaw: -180f,
            MaxYaw: 180f,
            YawStep: 10f,
            Tolerance: 75f,
            ThrowType: throwType,
            GrenadeType: grenadeType
        );

        /// <summary>
        /// Create a fine search for precise results (slower but more accurate).
        /// </summary>
        public static SearchParams CreateFine(ThrowType throwType = ThrowType.LongThrow, GrenadeType grenadeType = GrenadeType.Smoke) => new(
            MinPitch: -45f,
            MaxPitch: 85f,
            PitchStep: 1f,
            MinYaw: -180f,
            MaxYaw: 180f,
            YawStep: 2f,
            Tolerance: 30f,
            ThrowType: throwType,
            GrenadeType: grenadeType
        );

        /// <summary>
        /// Maximum-range debug search: huge tolerance, full pitch/yaw, velocity sweep 100–900.
        /// </summary>
        public static SearchParams CreateDebugWide(ThrowType throwType = ThrowType.LongThrow, GrenadeType grenadeType = GrenadeType.Smoke) => new(
            MinPitch: -90f,
            MaxPitch: 90f,
            PitchStep: 2f,
            MinYaw: -180f,
            MaxYaw: 180f,
            YawStep: 5f,
            Tolerance: 500f,
            ThrowType: throwType,
            GrenadeType: grenadeType,
            MinVelocity: 100f,
            MaxVelocity: 900f,
            VelocityStep: 50f
        );
    }

    /// <summary>
    /// Wraps the search results list and includes the closest miss for diagnostics.
    /// </summary>
    public record SearchResults(
        IReadOnlyList<LineupResult> Matches,
        LineupResult? ClosestMiss
    );

    /// <summary>
    /// Search for valid grenade lineups from a start position to a target.
    /// </summary>
    public IReadOnlyList<LineupResult> SearchLineups(
        Vector3 startPosition,
        Vector3 targetPosition,
        SearchParams searchParams)
    {
        var result = SearchLineupsWithDiagnostics(startPosition, targetPosition, searchParams, CancellationToken.None, progress: null);
        return result.Matches;
    }

    /// <summary>
    /// Search with full diagnostics including closest miss.
    /// </summary>
    public SearchResults SearchLineupsWithDiagnostics(
        Vector3 startPosition,
        Vector3 targetPosition,
        SearchParams searchParams,
        CancellationToken cancellationToken,
        IProgress<SearchProgress>? progress,
        IProgress<SearchVisualization>? visualization = null)
    {
        // Offset start position to player throw height (user picks ground position).
        // Jump throws release a short moment after takeoff, so the hand is slightly higher.
        var throwHeight = PlayerThrowHeight + GetThrowReleaseHeight(searchParams.ThrowType);
        var throwPosition = startPosition + new Vector3(0, 0, throwHeight);
        var zBoost = GetThrowZBoost(searchParams.ThrowType);

        var pitchValues = EnumerateValues(searchParams.MinPitch, searchParams.MaxPitch, searchParams.PitchStep);
        var yawValues = EnumerateValues(searchParams.MinYaw, searchParams.MaxYaw, searchParams.YawStep);

        // Either iterate a velocity range or use the throw type's fixed speed
        List<float> velocityValues;
        if (searchParams.HasVelocityRange)
        {
            velocityValues = EnumerateValues(searchParams.MinVelocity, searchParams.MaxVelocity, searchParams.VelocityStep);
        }
        else
        {
            velocityValues = [GetThrowSpeed(searchParams.ThrowType)];
        }

        var totalCandidates = (long)pitchValues.Count * yawValues.Count * velocityValues.Count;
        var results = new List<LineupResult>();
        var resultsLock = new object();
        long completedCandidates = 0;
        var matchesFound = 0;

        // Track the single closest miss across all threads
        LineupResult? globalClosestMiss = null;
        var closestMissLock = new object();

        // Throttle visualization to avoid flooding the UI thread
        var vizStopwatch = Stopwatch.StartNew();
        long lastVizMs = 0;
        const long VizIntervalMs = 40;

        progress?.Report(new SearchProgress(0, totalCandidates, 0));

        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
        };

        Parallel.ForEach(
            pitchValues,
            parallelOptions,
            () => (results: new List<LineupResult>(), closestMiss: (LineupResult?)null),
            (pitch, _, localState) =>
            {
                foreach (var yaw in yawValues)
                {
                    foreach (var velocity in velocityValues)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var launchVelocity = VelocityFromAngles(pitch, yaw, velocity);
                        launchVelocity.Z += zBoost;

                        // Check if this candidate should be visualized (time-throttled)
                        var shouldVisualize = false;
                        if (visualization != null)
                        {
                            var now = vizStopwatch.ElapsedMilliseconds;
                            var last = Interlocked.Read(ref lastVizMs);
                            if (now - last >= VizIntervalMs)
                            {
                                shouldVisualize = Interlocked.CompareExchange(ref lastVizMs, now, last) == last;
                            }
                        }

                        IReadOnlyList<Vector3>? path = null;
                        Vector3 finalPosition;

                        if (shouldVisualize)
                        {
                            path = calculator.CalculateTrajectory(throwPosition, launchVelocity, searchParams.GrenadeType);
                            finalPosition = path.Count > 0 ? path[^1] : throwPosition;
                        }
                        else
                        {
                            finalPosition = calculator.GetFinalPosition(throwPosition, launchVelocity, searchParams.GrenadeType);
                        }

                        var distanceToTarget = Vector3.Distance(finalPosition, targetPosition);
                        var candidate = new LineupResult(
                            LaunchVelocity: launchVelocity,
                            FinalPosition: finalPosition,
                            DistanceToTarget: distanceToTarget,
                            PitchAngleDegrees: pitch,
                            YawAngleDegrees: yaw,
                            VelocityMagnitude: velocity,
                            ThrowType: searchParams.ThrowType,
                            GrenadeType: searchParams.GrenadeType,
                            StartPosition: startPosition
                        );

                        // Track closest miss per thread
                        if (localState.closestMiss == null || distanceToTarget < localState.closestMiss.DistanceToTarget)
                        {
                            localState.closestMiss = candidate;
                        }

                        if (distanceToTarget <= searchParams.Tolerance)
                        {
                            path ??= calculator.CalculateTrajectory(throwPosition, launchVelocity, searchParams.GrenadeType);
                            visualization?.Report(new SearchVisualization(path, IsMatch: true, distanceToTarget));
                            localState.results.Add(candidate);
                        }
                        else if (shouldVisualize)
                        {
                            visualization!.Report(new SearchVisualization(path!, IsMatch: false, distanceToTarget));
                        }
                    }
                }

                var newCompleted = Interlocked.Add(ref completedCandidates, (long)yawValues.Count * velocityValues.Count);
                var newMatches = Interlocked.Add(ref matchesFound, localState.results.Count);
                progress?.Report(new SearchProgress(newCompleted, totalCandidates, newMatches));
                return localState;
            },
            localState =>
            {
                if (localState.results.Count > 0)
                {
                    lock (resultsLock)
                    {
                        results.AddRange(localState.results);
                    }
                }

                // Merge per-thread closest miss into global
                if (localState.closestMiss != null)
                {
                    lock (closestMissLock)
                    {
                        if (globalClosestMiss == null || localState.closestMiss.DistanceToTarget < globalClosestMiss.DistanceToTarget)
                        {
                            globalClosestMiss = localState.closestMiss;
                        }
                    }
                }
            });

        var sortedResults = results.OrderBy(r => r.DistanceToTarget).ToList();
        return new SearchResults(sortedResults, globalClosestMiss);
    }

    /// <summary>
    /// Search for the single best lineup (closest to target).
    /// </summary>
    /// <param name="startPosition">Position where the grenade is thrown from</param>
    /// <param name="targetPosition">Target position</param>
    /// <param name="searchParams">Search parameters</param>
    /// <returns>The best lineup result, or null if no valid lineup found within tolerance</returns>
    public LineupResult? SearchBestLineup(
        Vector3 startPosition,
        Vector3 targetPosition,
        SearchParams searchParams)
    {
        var results = SearchLineups(startPosition, targetPosition, searchParams);
        return results.Count > 0 ? results[0] : null;
    }

    /// <summary>
    /// Convenience wrapper that returns just the matches list (used by callers that don't need diagnostics).
    /// </summary>
    public IReadOnlyList<LineupResult> SearchLineups(
        Vector3 startPosition,
        Vector3 targetPosition,
        SearchParams searchParams,
        CancellationToken cancellationToken,
        IProgress<SearchProgress>? progress,
        IProgress<SearchVisualization>? visualization = null)
    {
        var result = SearchLineupsWithDiagnostics(startPosition, targetPosition, searchParams, cancellationToken, progress, visualization);
        return result.Matches;
    }

    /// <summary>
    /// Progress information for a reverse scan search.
    /// </summary>
    /// <param name="CompletedPositions">Number of start positions evaluated so far.</param>
    /// <param name="TotalPositions">Total number of start positions to evaluate.</param>
    /// <param name="MatchesFound">Number of valid lineups found across all positions.</param>
    public record ReverseScanProgress(int CompletedPositions, int TotalPositions, int MatchesFound);

    /// <summary>
    /// Search for valid lineups by sampling start positions across the map and throwing toward a known target.
    /// Casts rays downward to find floor positions on a grid, then runs the angle search from each valid position.
    /// </summary>
    /// <param name="targetPosition">Known target position (where the grenade should land).</param>
    /// <param name="searchParams">Angle search parameters and throw type.</param>
    /// <param name="physicsWorld">Physics world for floor ray casts.</param>
    /// <param name="gridStep">Horizontal spacing between sampled start positions (Source units).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="progress">Reports reverse scan progress (positions evaluated).</param>
    /// <param name="visualization">Optional trajectory visualization callback.</param>
    /// <returns>All valid lineups found across all sampled start positions, sorted by distance.</returns>
    public IReadOnlyList<LineupResult> SearchReverseLineups(
        Vector3 targetPosition,
        SearchParams searchParams,
        Rubikon physicsWorld,
        float gridStep,
        CancellationToken cancellationToken,
        IProgress<ReverseScanProgress>? progress,
        IProgress<SearchVisualization>? visualization = null)
    {
        var floorPositions = SampleFloorPositions(physicsWorld, gridStep, cancellationToken);

        if (floorPositions.Count == 0)
        {
            return [];
        }

        var allResults = new List<LineupResult>();
        var resultsLock = new object();
        var completedPositions = 0;
        var matchesFound = 0;

        progress?.Report(new ReverseScanProgress(0, floorPositions.Count, 0));

        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
        };

        Parallel.ForEach(
            floorPositions,
            parallelOptions,
            () => new List<LineupResult>(),
            (startPos, _, localResults) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var results = SearchLineups(startPos, targetPosition, searchParams, cancellationToken, progress: null, visualization);

                localResults.AddRange(results);

                var completed = Interlocked.Increment(ref completedPositions);
                var matches = Interlocked.Add(ref matchesFound, results.Count);
                progress?.Report(new ReverseScanProgress(completed, floorPositions.Count, matches));

                return localResults;
            },
            localResults =>
            {
                if (localResults.Count == 0)
                {
                    return;
                }

                lock (resultsLock)
                {
                    allResults.AddRange(localResults);
                }
            });

        return allResults.OrderBy(r => r.DistanceToTarget).ToList();
    }

    /// <summary>
    /// Sample floor positions across the map by casting rays downward on a grid.
    /// </summary>
    private static List<Vector3> SampleFloorPositions(Rubikon physicsWorld, float gridStep, CancellationToken cancellationToken)
    {
        // Compute map bounds from all physics shapes
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var mesh in physicsWorld.Meshes)
        {
            foreach (var vertex in mesh.VertexPositions)
            {
                min = Vector3.Min(min, vertex);
                max = Vector3.Max(max, vertex);
            }
        }

        foreach (var hull in physicsWorld.Hulls)
        {
            min = Vector3.Min(min, hull.Min);
            max = Vector3.Max(max, hull.Max);
        }

        if (min.X >= max.X || min.Y >= max.Y)
        {
            return [];
        }

        var positions = new List<Vector3>();
        var positionsLock = new object();
        var rayStartZ = max.Z + 100f;

        // Generate X values for parallel iteration
        var xValues = EnumerateValues(min.X, max.X, gridStep);

        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
        };

        Parallel.ForEach(
            xValues,
            parallelOptions,
            () => new List<Vector3>(),
            (x, _, localPositions) =>
            {
                for (var y = min.Y; y <= max.Y; y += gridStep)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var rayStart = new Vector3(x, y, rayStartZ);
                    var rayEnd = new Vector3(x, y, min.Z - 10f);
                    var hit = physicsWorld.TraceRay(rayStart, rayEnd);

                    if (hit.Hit && !hit.IsMinimalDistance)
                    {
                        localPositions.Add(hit.HitPosition);
                    }
                }

                return localPositions;
            },
            localPositions =>
            {
                if (localPositions.Count == 0)
                {
                    return;
                }

                lock (positionsLock)
                {
                    positions.AddRange(localPositions);
                }
            });

        return positions;
    }

    private static List<float> EnumerateValues(float min, float max, float step)
    {
        if (step <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(step), "Step size must be positive.");
        }

        var values = new List<float>();

        for (var value = min; value <= max + (step * 0.5f); value += step)
        {
            values.Add(value);
        }

        return values;
    }

    /// <summary>
    /// Convert pitch, yaw angles and velocity magnitude to a velocity vector.
    /// Uses the VRF/Source world coordinate system where Z is up and X/Y form the horizontal plane.
    /// </summary>
    /// <param name="pitchDegrees">Vertical launch angle (-90 = straight down, 0 = horizontal, 90 = straight up)</param>
    /// <param name="yawDegrees">Horizontal launch direction in the X/Y plane (0 = +X, 90 = +Y)</param>
    /// <param name="speedUnitsPerSecond">Magnitude of velocity vector (Source units/sec)</param>
    /// <returns>Velocity vector in world coordinates</returns>
    private static Vector3 VelocityFromAngles(float pitchDegrees, float yawDegrees, float speedUnitsPerSecond)
    {
        // Clamp pitch to reasonable values
        var clampedPitch = MathF.Max(-90f, MathF.Min(90f, pitchDegrees));

        // Convert degrees to radians
        var pitchRad = MathF.PI * clampedPitch / 180f;
        var yawRad = MathF.PI * yawDegrees / 180f;

        // Pre-compute trig functions for efficiency
        var cosPitch = MathF.Cos(pitchRad);
        var sinPitch = MathF.Sin(pitchRad);
        var cosYaw = MathF.Cos(yawRad);
        var sinYaw = MathF.Sin(yawRad);

        // Velocity decomposition using spherical-to-cartesian conversion in a Z-up world.
        var x = speedUnitsPerSecond * cosPitch * cosYaw;
        var y = speedUnitsPerSecond * cosPitch * sinYaw;
        var z = speedUnitsPerSecond * sinPitch;

        return new Vector3(x, y, z);
    }

    /// <summary>
    /// Debug method: simulate a single known throw and write the full trajectory to a TextWriter.
    /// Use this to validate the simulation against a known CS2 lineup.
    /// </summary>
    /// <param name="groundPosition">Player ground position (eye height will be added).</param>
    /// <param name="pitchDegrees">Our pitch convention: positive = up.</param>
    /// <param name="yawDegrees">Yaw angle (0 = +X).</param>
    /// <param name="speed">Launch speed in u/s.</param>
    /// <param name="targetPosition">Optional known target to show distance.</param>
    /// <param name="output">TextWriter to write results to.</param>
    /// <param name="grenadeType">Grenade behavior profile to simulate.</param>
    /// <param name="zBoost">Additional upward Z velocity (e.g. 100 for jump throw).</param>
    /// <returns>The simulated trajectory path points.</returns>
    public IReadOnlyList<Vector3> DebugSingleThrow(
        Vector3 groundPosition,
        float pitchDegrees,
        float yawDegrees,
        float speed,
        Vector3? targetPosition,
        System.IO.TextWriter output,
        GrenadeType grenadeType = GrenadeType.Smoke,
        float zBoost = 0f)
    {
        var throwHeight = PlayerThrowHeight + (zBoost > 0f ? GetThrowReleaseHeight(ThrowType.JumpThrow) : 0f);
        var throwPosition = groundPosition + new Vector3(0, 0, throwHeight);
        var launchVelocity = VelocityFromAngles(pitchDegrees, yawDegrees, speed);
        launchVelocity.Z += zBoost;

        output.WriteLine("=== Debug Single Throw ===");
        output.WriteLine($"Ground Position: ({groundPosition.X:F4}, {groundPosition.Y:F4}, {groundPosition.Z:F4})");
        output.WriteLine($"Throw Position (ground + {throwHeight:F4}): ({throwPosition.X:F4}, {throwPosition.Y:F4}, {throwPosition.Z:F4})");
        output.WriteLine($"Grenade Type: {grenadeType}");
        output.WriteLine($"Pitch: {pitchDegrees:F4}° (positive=up), Yaw: {yawDegrees:F4}°, Speed: {speed:F4} u/s");
        if (zBoost != 0f)
        {
            output.WriteLine($"Z Boost (jump throw): +{zBoost:F4} u/s");
        }
        output.WriteLine($"Launch Velocity: ({launchVelocity.X:F4}, {launchVelocity.Y:F4}, {launchVelocity.Z:F4})");
        output.WriteLine($"Launch Speed check: {launchVelocity.Length():F4} u/s");

        if (targetPosition.HasValue)
        {
            output.WriteLine($"Target Position: ({targetPosition.Value.X:F4}, {targetPosition.Value.Y:F4}, {targetPosition.Value.Z:F4})");
        }

        output.WriteLine();

        // Run the simulation with debug logging
        calculator.DebugLog = output;
        var path = calculator.CalculateTrajectory(throwPosition, launchVelocity, grenadeType);
        calculator.DebugLog = null;

        var finalPos = path.Count > 0 ? path[^1] : throwPosition;
        output.WriteLine();
        output.WriteLine($"=== Result ===");
        output.WriteLine($"Path points: {path.Count}");
        output.WriteLine($"Final Position: ({finalPos.X:F4}, {finalPos.Y:F4}, {finalPos.Z:F4})");

        if (targetPosition.HasValue)
        {
            output.WriteLine($"Distance to target: {Vector3.Distance(finalPos, targetPosition.Value):F4} units");
            var delta = finalPos - targetPosition.Value;
            output.WriteLine($"Delta (final - target): ({delta.X:F4}, {delta.Y:F4}, {delta.Z:F4})");
        }

        return path;
    }

    /// <summary>
    /// Progress information for the bounce auto-tuning sweep.
    /// </summary>
    /// <param name="CompletedCandidates">Number of parameter sets evaluated so far.</param>
    /// <param name="TotalCandidates">Total number of parameter sets to evaluate.</param>
    /// <param name="BestDistance">Best distance to target found so far.</param>
    /// <param name="Phase">Description of the current sweep phase.</param>
    public record BounceTuningProgress(long CompletedCandidates, long TotalCandidates, float BestDistance, string Phase);

    /// <summary>
    /// Automatically sweep bounce parameters and return the closest landing result for a known throw.
    /// </summary>
    public BounceTuningResult DebugSingleThrowAutoTuneBounce(
        Vector3 groundPosition,
        float pitchDegrees,
        float yawDegrees,
        float speed,
        Vector3 targetPosition,
        GrenadeType grenadeType = GrenadeType.Smoke,
        float zBoost = 0f,
        float visualizationTolerance = float.MaxValue,
        CancellationToken cancellationToken = default,
        IProgress<BounceTuningProgress>? progress = null,
        IProgress<SearchVisualization>? visualization = null)
    {
        var throwHeight = PlayerThrowHeight + (zBoost > 0f ? GetThrowReleaseHeight(ThrowType.JumpThrow) : 0f);
        var throwPosition = groundPosition + new Vector3(0, 0, throwHeight);
        var launchVelocity = VelocityFromAngles(pitchDegrees, yawDegrees, speed);
        launchVelocity.Z += zBoost;

        var coarseBest = FindBestBounceSettings(
            throwPosition,
            launchVelocity,
            targetPosition,
            grenadeType,
            CoarseWallRestitutions,
            CoarseWallFrictions,
            CoarseGroundRestitutions,
            CoarseGroundFrictions,
            "Coarse sweep",
            visualizationTolerance,
            cancellationToken,
            progress,
            visualization);

        cancellationToken.ThrowIfCancellationRequested();

        var fineBest = FindBestBounceSettings(
            throwPosition,
            launchVelocity,
            targetPosition,
            grenadeType,
            BuildCenteredValues(coarseBest.BounceSettings.BounceRestitution, 0.04f, 0.01f),
            BuildCenteredValues(coarseBest.BounceSettings.BounceFriction, 0.04f, 0.01f),
            BuildCenteredValues(coarseBest.BounceSettings.GroundBounceRestitution, 0.04f, 0.01f),
            BuildCenteredValues(coarseBest.BounceSettings.GroundBounceFriction, 0.04f, 0.01f),
            "Fine sweep",
            visualizationTolerance,
            cancellationToken,
            progress,
            visualization);

        cancellationToken.ThrowIfCancellationRequested();

        // Compute the best path for visualization
        var bestPath = calculator.CalculateTrajectory(throwPosition, launchVelocity, grenadeType, fineBest.BounceSettings);
        visualization?.Report(new SearchVisualization(bestPath, IsMatch: true, fineBest.DistanceToTarget));

        return fineBest with { Path = bestPath };
    }

    /// <summary>
    /// Write a detailed log for a completed bounce tuning result.
    /// </summary>
    public void WriteBounceTuningLog(
        System.IO.TextWriter output,
        Vector3 groundPosition,
        float pitchDegrees,
        float yawDegrees,
        float speed,
        Vector3 targetPosition,
        GrenadeType grenadeType,
        float zBoost,
        BounceTuningResult tuningResult)
    {
        var throwHeight = PlayerThrowHeight + (zBoost > 0f ? GetThrowReleaseHeight(ThrowType.JumpThrow) : 0f);
        var throwPosition = groundPosition + new Vector3(0, 0, throwHeight);
        var launchVelocity = VelocityFromAngles(pitchDegrees, yawDegrees, speed);
        launchVelocity.Z += zBoost;

        var defaultSettings = GrenadeBounceSettings.Default;
        var defaultFinal = calculator.GetFinalPosition(throwPosition, launchVelocity, grenadeType, defaultSettings);
        var defaultDistance = Vector3.Distance(defaultFinal, targetPosition);

        output.WriteLine("=== Auto Tune Bounce Parameters ===");
        output.WriteLine($"Ground Position: ({groundPosition.X:F4}, {groundPosition.Y:F4}, {groundPosition.Z:F4})");
        output.WriteLine($"Throw Position: ({throwPosition.X:F4}, {throwPosition.Y:F4}, {throwPosition.Z:F4})");
        output.WriteLine($"Target Position: ({targetPosition.X:F4}, {targetPosition.Y:F4}, {targetPosition.Z:F4})");
        output.WriteLine($"Launch Velocity: ({launchVelocity.X:F4}, {launchVelocity.Y:F4}, {launchVelocity.Z:F4})");
        output.WriteLine($"Grenade Type: {grenadeType}");
        output.WriteLine();
        output.WriteLine($"Baseline distance with defaults: {defaultDistance:F4} units");
        output.WriteLine($"Baseline settings: wall restitution={defaultSettings.BounceRestitution:F3}, wall friction={defaultSettings.BounceFriction:F3}, ground restitution={defaultSettings.GroundBounceRestitution:F3}, ground friction={defaultSettings.GroundBounceFriction:F3}");
        output.WriteLine();
        output.WriteLine("=== Best Tuned Bounce Settings ===");
        output.WriteLine($"Distance to target: {tuningResult.DistanceToTarget:F4} units");
        output.WriteLine($"Final Position: ({tuningResult.FinalPosition.X:F4}, {tuningResult.FinalPosition.Y:F4}, {tuningResult.FinalPosition.Z:F4})");
        output.WriteLine($"Wall restitution: {tuningResult.BounceSettings.BounceRestitution:F3}");
        output.WriteLine($"Wall friction: {tuningResult.BounceSettings.BounceFriction:F3}");
        output.WriteLine($"Ground restitution: {tuningResult.BounceSettings.GroundBounceRestitution:F3}");
        output.WriteLine($"Ground friction: {tuningResult.BounceSettings.GroundBounceFriction:F3}");
        output.WriteLine($"Improvement vs baseline: {defaultDistance - tuningResult.DistanceToTarget:F4} units");
        output.WriteLine();
        output.WriteLine("=== Detailed Trace For Best Tuned Settings ===");

        calculator.DebugLog = output;
        calculator.CalculateTrajectory(throwPosition, launchVelocity, grenadeType, tuningResult.BounceSettings);
        calculator.DebugLog = null;
    }

    private static readonly float[] CoarseWallRestitutions = [0.10f, 0.20f, 0.30f, 0.40f, 0.50f, 0.60f, 0.70f];
    private static readonly float[] CoarseWallFrictions = [0.10f, 0.20f, 0.30f, 0.40f, 0.50f, 0.60f, 0.70f];
    private static readonly float[] CoarseGroundRestitutions = [0.10f, 0.20f, 0.30f, 0.40f, 0.50f, 0.60f, 0.70f];
    private static readonly float[] CoarseGroundFrictions = [0.10f, 0.20f, 0.30f, 0.40f, 0.50f, 0.60f, 0.70f];

    private BounceTuningResult FindBestBounceSettings(
        Vector3 throwPosition,
        Vector3 launchVelocity,
        Vector3 targetPosition,
        GrenadeType grenadeType,
        IReadOnlyList<float> wallRestitutions,
        IReadOnlyList<float> wallFrictions,
        IReadOnlyList<float> groundRestitutions,
        IReadOnlyList<float> groundFrictions,
        string phaseName,
        float visualizationTolerance = float.MaxValue,
        CancellationToken cancellationToken = default,
        IProgress<BounceTuningProgress>? progress = null,
        IProgress<SearchVisualization>? visualization = null)
    {
        var totalCandidates = (long)wallRestitutions.Count * wallFrictions.Count * groundRestitutions.Count * groundFrictions.Count;
        var bestDistance = float.MaxValue;
        var bestFinalPosition = throwPosition;
        var bestSettings = GrenadeBounceSettings.Default;
        long candidateCount = 0;

        // Throttle visualization to avoid flooding the UI
        var vizStopwatch = Stopwatch.StartNew();
        long lastVizMs = 0;
        const long VizIntervalMs = 80;

        foreach (var wallRestitution in wallRestitutions)
        {
            foreach (var wallFriction in wallFrictions)
            {
                foreach (var groundRestitution in groundRestitutions)
                {
                    foreach (var groundFriction in groundFrictions)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        candidateCount++;

                        var settings = new GrenadeBounceSettings(
                            wallRestitution,
                            wallFriction,
                            groundRestitution,
                            groundFriction).Clamp();

                        var finalPosition = calculator.GetFinalPosition(throwPosition, launchVelocity, grenadeType, settings);
                        var distance = Vector3.Distance(finalPosition, targetPosition);
                        var isWithinTolerance = distance <= visualizationTolerance;

                        if (isWithinTolerance && distance < bestDistance)
                        {
                            bestDistance = distance;
                            bestFinalPosition = finalPosition;
                            bestSettings = settings;
                        }

                        // Visualize all candidates (time-throttled); matches within tolerance stay green
                        if (visualization != null)
                        {
                            var now = vizStopwatch.ElapsedMilliseconds;
                            if (isWithinTolerance || now - lastVizMs >= VizIntervalMs)
                            {
                                lastVizMs = now;
                                var path = calculator.CalculateTrajectory(throwPosition, launchVelocity, grenadeType, settings);
                                visualization.Report(new SearchVisualization(path, IsMatch: isWithinTolerance, distance));
                            }
                        }
                    }
                }

                progress?.Report(new BounceTuningProgress(candidateCount, totalCandidates, bestDistance, phaseName));
            }
        }

        progress?.Report(new BounceTuningProgress(totalCandidates, totalCandidates, bestDistance, phaseName));

        return new BounceTuningResult(bestSettings, [], bestFinalPosition, bestDistance);
    }

    private static IReadOnlyList<float> BuildCenteredValues(float center, float radius, float step)
    {
        var values = new List<float>();

        for (var value = center - radius; value <= center + radius + (step * 0.5f); value += step)
        {
            values.Add(Math.Clamp(MathF.Round(value * 1000f) / 1000f, 0f, 1f));
        }

        return values.Distinct().OrderBy(static value => value).ToList();
    }
}
