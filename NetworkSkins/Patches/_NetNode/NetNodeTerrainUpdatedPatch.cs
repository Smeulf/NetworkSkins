using HarmonyLib;
using NetworkSkins.Patches;
using NetworkSkins.Skins;

[HarmonyPatch(typeof(global::NetNode), "TerrainUpdated")]
public static class NetNodeTerrainUpdatedPatch
{
    public static void Prefix(ref global::NetNode __instance, ushort nodeID, out TerrainSurfacePatcherState __state)
    {
        __state = TerrainSurfacePatcher.Apply(__instance.Info, NetworkSkinManager.NodeSkins[nodeID]);
    }

    public static void Postfix(ref global::NetNode __instance, TerrainSurfacePatcherState __state)
    {
        TerrainSurfacePatcher.Revert(__instance.Info, __state);
    }
}