using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Windows.Forms;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.Simulation;
using ValveResourceFormat.Simulation;

namespace GUI.Utils;

/// <summary>
/// Writes a lineup test config and launches CS2 to execute it.
/// </summary>
internal static class CS2Launcher
{
    private const int CS2AppId = 730;
    private const string CfgFileName = AppIdentity.CS2CfgFileName;

    /// <summary>
    /// Writes a .cfg file with the lineup setup commands and launches CS2.
    /// </summary>
    /// <param name="mapName">BSP map name (e.g. "de_dust2")</param>
    /// <param name="startPosition">Player position in world coordinates</param>
    /// <param name="pitchDegrees">Vertical look angle (negative = up in Source)</param>
    /// <param name="yawDegrees">Horizontal look angle</param>
    /// <param name="throwType">Throw type to execute once positioned.</param>
    /// <param name="grenadeType">Grenade weapon to equip before throwing.</param>
    /// <returns>Error message if something failed, null on success.</returns>
    public static string? LaunchLineupTest(string mapName, Vector3 startPosition, float pitchDegrees, float yawDegrees, ThrowType throwType, GrenadeType grenadeType)
    {
        var cfgPath = FindCfgFolder();

        if (cfgPath == null)
        {
            return "Could not find CS2 cfg folder. Is CS2 installed?";
        }

        var cfgContent = BuildCfgContent(startPosition, pitchDegrees, yawDegrees, throwType, grenadeType);
        var cfgFilePath = Path.Combine(cfgPath, CfgFileName);

        File.WriteAllText(cfgFilePath, cfgContent);

        LaunchCS2(mapName);

        MessageBox.Show(
            "CS2 is launching.\n\n" +
            "Once you're in the map, open the console (~) and type:\n\n" +
            $"    exec {Path.GetFileNameWithoutExtension(CfgFileName)}\n\n" +
            "This will teleport you to the throw position and execute the throw.",
            "Test Lineup in CS2",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        return null;
    }

    private static string BuildCfgContent(Vector3 startPosition, float pitchDegrees, float yawDegrees, ThrowType throwType, GrenadeType grenadeType)
    {
        var weaponCommand = GetWeaponCommand(grenadeType);

        // Source engine setpos uses X Y Z; setang uses PITCH YAW ROLL
        return string.Join('\n', [
            "sv_cheats 1",
            "mp_warmup_end",
            "mp_freezetime 0",
            "mp_roundtime_defuse 60",
            "bot_kick",
            "sv_infinite_ammo 1",
            // Wait for map to fully load before teleporting
            "wait 300",
            $"setpos {startPosition.X:F2} {startPosition.Y:F2} {startPosition.Z:F2}",
            $"setang {pitchDegrees:F2} {yawDegrees:F2} 0",
            $"give {weaponCommand}",
            $"use {weaponCommand}",
            ..BuildThrowCommands(throwType),
        ]);
    }

    private static string GetWeaponCommand(GrenadeType grenadeType) => grenadeType switch
    {
        GrenadeType.Flashbang => "weapon_flashbang",
        GrenadeType.HEGrenade => "weapon_hegrenade",
        GrenadeType.Decoy => "weapon_decoy",
        GrenadeType.Incendiary => "weapon_molotov",
        _ => "weapon_smokegrenade",
    };

    private static string[] BuildThrowCommands(ThrowType throwType) => throwType switch
    {
        ThrowType.ShortThrow =>
        [
            "wait 4",
            "+attack2",
            "wait 2",
            "-attack2",
        ],
        ThrowType.MidThrow =>
        [
            "wait 4",
            "+attack",
            "+attack2",
            "wait 2",
            "-attack",
            "-attack2",
        ],
        ThrowType.JumpThrow =>
        [
            "wait 4",
            "+jump",
            "wait 2",
            "+attack",
            "wait 2",
            "-attack",
            "wait 1",
            "-jump",
        ],
        _ =>
        [
            "wait 4",
            "+attack",
            "wait 2",
            "-attack",
        ],
    };

    private static string? FindCfgFolder()
    {
        var gameInfo = GameFolderLocator.FindSteamGameByAppId(CS2AppId);

        if (gameInfo == null)
        {
            return null;
        }

        // CS2 cfg is at: <GamePath>/game/csgo/cfg/
        var cfgPath = Path.Combine(gameInfo.Value.GamePath, "game", "csgo", "cfg");

        if (!Directory.Exists(cfgPath))
        {
            // Try creating — the folder might not exist yet
            Directory.CreateDirectory(cfgPath);
        }

        return cfgPath;
    }

    private static void LaunchCS2(string mapName)
    {
        // Launch via Steam protocol URI with +map to load the map on startup
        // The cfg is NOT passed here — user runs "exec lineup_test" manually in console
        Process.Start(new ProcessStartInfo
        {
            FileName = $"steam://rungameid/{CS2AppId}/+map {mapName}",
            UseShellExecute = true,
        });
    }

    /// <summary>
    /// Extract map name from a file path. Looks for patterns like "de_dust2" in the path.
    /// </summary>
    public static string? ExtractMapName(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return null;
        }

        // Try to find "maps/MAPNAME" in the path
        var fileName = Path.GetFileNameWithoutExtension(filePath);

        // Strip compiled suffix if present (e.g. "de_dust2.vmap_c" → "de_dust2.vmap" → "de_dust2")
        if (fileName.EndsWith(".vmap", StringComparison.OrdinalIgnoreCase))
        {
            fileName = Path.GetFileNameWithoutExtension(fileName);
        }

        // For VPK paths like "de_dust2_dir.vpk", try extracting map name
        if (fileName.EndsWith("_dir", StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName[..^4];
        }

        // If it looks like a standard map name, return it
        if (fileName.StartsWith("de_", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("cs_", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("ar_", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("gd_", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("dz_", StringComparison.OrdinalIgnoreCase))
        {
            return fileName;
        }

        // Fall back to whatever the filename is
        return fileName;
    }
}
