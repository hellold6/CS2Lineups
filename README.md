# CS2 Grenade Lineup Tool

A grenade lineup finder for Counter-Strike 2, built on top of [ValveResourceFormat (VRF)](https://github.com/ValveResourceFormat/ValveResourceFormat).

Load any CS2 map, pick a start and target position in the 3D viewport, and the tool will brute-force search for grenade trajectories that land where you want — with live Baritone-style visualization of the search in progress.

## Features

- **Map loading** — opens compiled CS2 `.vpk` maps with full 3D rendering via VRF's OpenGL renderer
- **Position picking** — click in the 3D viewport to set start and target positions on real map geometry (Rubikon physics ray tracing)
- **Lineup search** — parallel brute-force over pitch, yaw, and velocity space finds throws that land within tolerance of the target
- **Live search visualization** — watch trajectories being evaluated in real time: blue arcs cycle through candidates, green arcs mark matches as they're found
- **Result inspection** — select a result to see its full trajectory rendered as an orange arc, with the camera positioned at the throw position
- **Cancellation and progress** — progress bar with live candidate/match counts, cancel anytime

## How to Use

1. Open a CS2 map file (`.vpk` containing a `.vmap_c`)
2. Go to the **Grenade Lineups** tab
3. Click **Pick Start** → click a position in the 3D map view
4. Click **Pick Target** → click where you want the grenade to land
5. Click **Find Lineup** and watch the search visualize in the viewport
6. Select results from the list to inspect individual trajectories

## Building

Requires the latest .NET SDK.

```
dotnet build
dotnet run --project GUI
```

## Running Tests

```
dotnet test
```

## Project Structure

Folder | Description
-------|------------
`ValveResourceFormat/` | Source 2 resource parsing (maps, models, materials, physics) and `GrenadeSimulator`
`Renderer/` | OpenGL rendering engine, trajectory calculator, and lineup searcher
`GUI/` | WinForms map viewer application with grenade lineup UI
`Tests/` | NUnit tests for the grenade simulator

## Physics

The grenade simulator uses:
- Euler integration at 66.67 Hz (0.015s timestep)
- 800 u/s² gravity on the Z axis (Source 2 Z-up coordinates)
- Rubikon ray tracing for collision detection against real map geometry
- Velocity reflection with 0.4 bounce restitution
- Surface nudge after bounce to prevent re-collision
- 10 second max simulation time
- Player throw height offset of 64 units

Physics constants are placeholders and will be calibrated against actual CS2 values.

## Credits

Built on [ValveResourceFormat](https://github.com/ValveResourceFormat/ValveResourceFormat) — Valve's Source 2 resource file format parser, renderer, decompiler, and exporter.

## License

Contents of this repository are available under the [MIT license](LICENSE).

