using System.Linq;

namespace ValveResourceFormat.Renderer.Utils;

internal static class PhysToolTextures
{
    private static readonly Dictionary<string, HashSet<string>> MultiTags = new()
    {
        ["clip"] = ["npcclip", "playerclip"],
        ["invisibleladder"] = ["ladder", "passbullets"],
    };

    public static string GetShortenedName(HashSet<string> interactAsStrings)
    {
        var texture = MultiTags.FirstOrDefault(x => x.Value.SetEquals(interactAsStrings)).Key;
        var tag = interactAsStrings.FirstOrDefault();
        texture ??= tag switch
        {
            "playerclip" or "npcclip" or "blocksound" => tag,
            "sky" => "skybox",
            "csgo_grenadeclip" => "grenadeclip",
            "ladder" => "invisibleladder",
            _ => "nodraw",
        };
        return texture;
    }

    public static string GetForEntity(string entityClassName)
    {
        return entityClassName switch
        {
            "env_cs_place" => "materials/tools/tools_cs_place.vmat",
            "func_nav_blocker" => "materials/tools/toolsnavattribute.vmat",
            "func_nav_markup" => "materials/tools/toolsnavattribute.vmat",
            "func_precipitation" => "materials/tools/toolsprecipitation.vmat",
            "post_processing_volume" => "materials/tools_postprocess_volume.vmat",
            "trigger_no_wards" => "materials/tools/tools_no_wards.vmat",
            _ => "materials/tools/toolstrigger.vmat",
        };
    }
}
