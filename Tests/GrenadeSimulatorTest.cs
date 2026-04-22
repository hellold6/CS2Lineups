using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using ValveResourceFormat.Simulation;

namespace ValveResourceFormat.Tests;

[TestFixture]
public class GrenadeSimulatorTest
{
    [Test]
    public void TestHorizontalThrowWithNoCollisions()
    {
        // Create a simulator
        var simulator = new GrenadeSimulator();

        // Create a passthrough collision detector (no collisions ever)
        Func<Vector3, Vector3, CollisionInfo> noCollisionDetector = (from, to) =>
            new CollisionInfo(Hit: false, Vector3.Zero, Vector3.Zero);

        // Launch horizontally from origin with velocity 500 u/s forward (+X)
        var startPosition = Vector3.Zero;
        var launchVelocity = new Vector3(500, 0, 0);

        // Simulate the trajectory
        var path = simulator.SimulateFullTrajectory(
            startPosition,
            launchVelocity,
            noCollisionDetector
        );

        // Print first 10 positions to inspect
        Console.WriteLine("=== Grenade Trajectory (Horizontal Launch, No Collisions) ===");
        Console.WriteLine($"Start position: {FormatVector3(startPosition)}");
        Console.WriteLine($"Launch velocity: {FormatVector3(launchVelocity)}");
        Console.WriteLine();
        Console.WriteLine("Position Index | X          | Y          | Z          | Distance from start");
        Console.WriteLine("---------------|------------|------------|------------|--------------------");

        var displayCount = Math.Min(10, path.Count);
        for (var i = 0; i < displayCount; i++)
        {
            var pos = path[i];
            var distFromStart = Vector3.Distance(startPosition, pos);
            Console.WriteLine(
                $"{i,14} | {pos.X,10:F2} | {pos.Y,10:F2} | {pos.Z,10:F2} | {distFromStart,18:F2}"
            );
        }

        Console.WriteLine();
        Console.WriteLine($"Total path length: {path.Count} positions");
        Console.WriteLine($"Final position: {FormatVector3(simulator.CurrentPosition)}");
        Console.WriteLine($"Final velocity: {FormatVector3(simulator.CurrentVelocity)}");
        Console.WriteLine($"Elapsed time: {simulator.ElapsedTime:F2} seconds");
        Console.WriteLine($"Has detonated: {simulator.HasDetonated}");

        // Sanity checks
        Assert.That(path.Count, Is.GreaterThan(10), "Should have simulated many steps");
        Assert.That(path[0].Z, Is.EqualTo(0), "First position should be at Z=0");
        
        // Grenade should have fallen due to gravity - later Z positions should be lower.
        Assert.That(path[5].Z, Is.LessThan(path[0].Z), "Gravity should pull grenade down (lower Z values)");
        
        // Grenade should have moved X forward consistently.
        Assert.That(simulator.CurrentPosition.X, Is.GreaterThan(0), "Should have moved in X direction");
    }

    [Test]
    public void TestVerticalThrowUpward()
    {
        // Test vertical upward throw to verify gravity pulling it back down
        var simulator = new GrenadeSimulator();

        Func<Vector3, Vector3, CollisionInfo> noCollisionDetector = (from, to) =>
            new CollisionInfo(Hit: false, Vector3.Zero, Vector3.Zero);

        var startPosition = Vector3.Zero;
        var launchVelocity = new Vector3(0, 0, 600); // Straight up at 600 u/s

        var path = simulator.SimulateFullTrajectory(
            startPosition,
            launchVelocity,
            noCollisionDetector
        );

        TestContext.WriteLine("=== Grenade Trajectory (Vertical Upward Launch, No Collisions) ===");
        TestContext.WriteLine($"Launch velocity: {FormatVector3(launchVelocity)}");
        TestContext.WriteLine();

        var displayCount = Math.Min(15, path.Count);
        TestContext.WriteLine("Step | Z Position  | Velocity Z");
        TestContext.WriteLine("-----|-------------|----------");
        
        for (var i = 0; i < displayCount; i++)
        {
            var pos = path[i];
            // Estimate velocity Z by looking at position difference.
            var velZ = i > 0 ? (path[i].Z - path[i - 1].Z) / GrenadeSimulator.TimeStep : 600f;
            TestContext.WriteLine($"{i,4} | {pos.Z,11:F2} | {velZ,8:F2}");
        }

        TestContext.WriteLine();
        TestContext.WriteLine($"Peak height: {path.Cast<Vector3>().Max(p => p.Z):F2}");
        TestContext.WriteLine($"Final Z: {simulator.CurrentPosition.Z:F2}");

        // Verify parabolic trajectory
        Assert.That(path.Count, Is.GreaterThan(10), "Should simulate multiple steps");
        
        // Should go up first, then come back down
        var peakIndex = -1;
        var maxZ = float.MinValue;
        for (var i = 0; i < path.Count; i++)
        {
            if (path[i].Z > maxZ)
            {
                maxZ = path[i].Z;
                peakIndex = i;
            }
        }
        
        Assert.That(peakIndex, Is.GreaterThan(0), "Should reach a peak height");
        Assert.That(peakIndex, Is.LessThan(path.Count - 1), "Should come back down after peak");
    }

    private static string FormatVector3(Vector3 v) => $"({v.X:F2}, {v.Y:F2}, {v.Z:F2})";
}
