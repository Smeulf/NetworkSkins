using HarmonyLib;
using NetworkSkins.Patches;
using NetworkSkins.Skins;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

[HarmonyPatch(typeof(global::NetNode), "UpdateJunctionTerrain")]
public static class NetNodeUpdateJunctionTerrainPatch
{
    public static IEnumerable<CodeInstruction> Transpiler(ILGenerator il, IEnumerable<CodeInstruction> instructions)
    {
        var originalInstructions = new List<CodeInstruction>(instructions);

        var netInfoCreatePavementField = typeof(NetInfo).GetField("m_createPavement");
        var netInfoFlattenTerrainField = typeof(NetInfo).GetField("m_flattenTerrain");
        var segmentSkinsField = typeof(NetworkSkinManager).GetField("SegmentSkins", BindingFlags.Static | BindingFlags.Public);
        var terrainSurfacePatcherApplyMethod = typeof(TerrainSurfacePatcher).GetMethod("Apply");
        var terrainSurfacePatcherRevertMethod = typeof(TerrainSurfacePatcher).GetMethod("Revert");

        if (netInfoCreatePavementField == null || netInfoFlattenTerrainField == null || segmentSkinsField == null || terrainSurfacePatcherApplyMethod == null || terrainSurfacePatcherRevertMethod == null)
        {
            Debug.LogError("NetNodeUpdateJunctionTerrainPatch: Necessary field and methods not found. Cancelling transpiler!");
            return originalInstructions;
        }

        var codes = new List<CodeInstruction>(originalInstructions);
        var patcherStateLocalVar = il.DeclareLocal(typeof(TerrainSurfacePatcherState));
        patcherStateLocalVar.SetLocalSymInfo("patcherState");

        int index = 0;

        CodeInstruction num14LocalVarLdLoc = null;
        CodeInstruction num13LocalVarLdLoc = null;
        CodeInstruction info4LocalVarLdLoc = null;
        CodeInstruction netInfo2LocalVarLdLoc = null;
        CodeInstruction segment7LocalVarLdLoc = null;
        CodeInstruction num6LocalVarLdLoc = null;

        // Cherche le pattern :
        // NetInfo netInfo2 = (num14 > num13 >> 1) ? netInfo54 : info4_43;
        // [786] ldloc.s 67  (num14)
        // [787] ldloc.s 64  (num13)
        // [788] ldc.i4.1
        // [789] shr
        // [790] bgt
        // [791] ldloc.s 43  (info4)
        // [792] br
        // [793] ldloc.s 54  (netInfo)
        // [794] stloc.s 68  (netInfo2)
        for (; index < codes.Count; index++)
        {
            if (codes[index].opcode == OpCodes.Shr
                && codes[index - 3].IsLdloc() //TranspilerUtils.IsLdLoc(codes[index - 3])
                && codes[index - 2].IsLdloc() //TranspilerUtils.IsLdLoc(codes[index - 2])
                && codes[index - 1].opcode == OpCodes.Ldc_I4_1
                && codes[index + 1].opcode == OpCodes.Bgt
                && codes[index + 2].IsLdloc() //TranspilerUtils.IsLdLoc(codes[index + 2])
                && codes[index + 3].opcode == OpCodes.Br
                && codes[index + 4].IsLdloc() //TranspilerUtils.IsLdLoc(codes[index + 4])
                && codes[index + 5].IsStloc()) //TranspilerUtils.IsStLoc(codes[index + 5]))
            {
                TranspilerUtils.LogDebug("Found NetInfo netInfo2 = (num14 > num13 >> 1) ? netInfo : info4;");
                num14LocalVarLdLoc = codes[index - 3].Clone();
                num13LocalVarLdLoc = codes[index - 2].Clone();
                info4LocalVarLdLoc = codes[index + 2].Clone();   // local 43
                var netInfoLocalVarLdLoc = codes[index + 4].Clone(); // local 54
                netInfo2LocalVarLdLoc = TranspilerUtils.BuildLdLocFromStLoc(codes[index + 5]); // local 68

                var findIndex = 0;
                segment7LocalVarLdLoc = FindSegment7LocalVar(codes, TranspilerUtils.BuildStLocFromLdLoc(info4LocalVarLdLoc), ref findIndex, index - 3);
                num6LocalVarLdLoc = FindNum6LocalVar(codes, TranspilerUtils.BuildStLocFromLdLoc(netInfoLocalVarLdLoc), ref findIndex, index - 3);
                break;
            }
        }

        if (num14LocalVarLdLoc == null || num13LocalVarLdLoc == null || info4LocalVarLdLoc == null || netInfo2LocalVarLdLoc == null || segment7LocalVarLdLoc == null || num6LocalVarLdLoc == null)
        {
            Debug.LogError("NetNodeUpdateJunctionTerrainPatch: Some local variables not found! Cancelling transpiler!");
            Debug.LogError($"num14: {num14LocalVarLdLoc}, num13: {num13LocalVarLdLoc}, info4: {info4LocalVarLdLoc}, netInfo2: {netInfo2LocalVarLdLoc}, segment7: {segment7LocalVarLdLoc}, num6: {num6LocalVarLdLoc}");
            return originalInstructions;
        }

        // --- Apply 1 ---
        // Before: bool flag8 = netInfo2.m_createPavement && ...
        // Insert: patcherState = TerrainSurfacePatcher.Apply(netInfo2, SegmentSkins[(num14 > num13 >> 1) ? num6 : segment7]);
        var apply1Inserted = false;
        for (; index < codes.Count; index++)
        {
            if (TranspilerUtils.IsSameInstruction(codes[index], netInfo2LocalVarLdLoc)
                && codes[index + 1].opcode == OpCodes.Ldfld && codes[index + 1].operand == netInfoCreatePavementField)
            {
                TranspilerUtils.LogDebug("Found netInfo2.m_createPavement (Apply 1)");

                var ldLocSegment7Label = il.DefineLabel();
                var ldElemtRefLabel = il.DefineLabel();

                var apply1Instructions = new[]
                {
                    new CodeInstruction(netInfo2LocalVarLdLoc),
                    new CodeInstruction(OpCodes.Ldsfld, segmentSkinsField),
                    new CodeInstruction(num14LocalVarLdLoc),
                    new CodeInstruction(num13LocalVarLdLoc),
                    new CodeInstruction(OpCodes.Ldc_I4_1),
                    new CodeInstruction(OpCodes.Shr),
                    new CodeInstruction(OpCodes.Bgt, ldLocSegment7Label),
                    new CodeInstruction(segment7LocalVarLdLoc),
                    new CodeInstruction(OpCodes.Br, ldElemtRefLabel),
                    new CodeInstruction(num6LocalVarLdLoc),
                    new CodeInstruction(OpCodes.Ldelem_Ref),
                    new CodeInstruction(OpCodes.Call, terrainSurfacePatcherApplyMethod),
                    new CodeInstruction(OpCodes.Stloc, patcherStateLocalVar)
                };
                apply1Instructions[0].labels.AddRange(codes[index].labels);
                codes[index].labels.Clear();
                apply1Instructions[9].labels.Add(ldLocSegment7Label);
                apply1Instructions[10].labels.Add(ldElemtRefLabel);

                codes.InsertRange(index, apply1Instructions);
                apply1Inserted = true;
                index += apply1Instructions.Length;
                break;
            }
        }

        if (!apply1Inserted)
        {
            Debug.LogError("NetNodeUpdateJunctionTerrainPatch: Apply Insertion 1 failed! Cancelling transpiler!");
            return originalInstructions;
        }

        // --- Revert 1 ---
        // Before: bool flag12 = netInfo2.m_flattenTerrain || ...
        // Insert: TerrainSurfacePatcher.Revert(netInfo2, patcherState);
        var revert1Inserted = false;
        for (; index < codes.Count; index++)
        {
            if (TranspilerUtils.IsSameInstruction(codes[index], netInfo2LocalVarLdLoc)
                && codes[index + 1].opcode == OpCodes.Ldfld && codes[index + 1].operand == netInfoFlattenTerrainField)
            {
                TranspilerUtils.LogDebug("Found netInfo2.m_flattenTerrain (Revert 1)");

                var revert1Instructions = new[]
                {
                    new CodeInstruction(netInfo2LocalVarLdLoc),
                    new CodeInstruction(OpCodes.Ldloc, patcherStateLocalVar),
                    new CodeInstruction(OpCodes.Call, terrainSurfacePatcherRevertMethod),
                };
                revert1Instructions[0].labels.AddRange(codes[index].labels);
                codes[index].labels.Clear();

                codes.InsertRange(index, revert1Instructions);
                revert1Inserted = true;
                index += revert1Instructions.Length;
                break;
            }
        }

        if (!revert1Inserted)
        {
            Debug.LogError("NetNodeUpdateJunctionTerrainPatch: Revert Insertion 1 failed! Cancelling transpiler!");
            return originalInstructions;
        }

        // --- Apply 2 ---
        // Before: bool flag13 = info4.m_createPavement && ...
        // Insert: patcherState = TerrainSurfacePatcher.Apply(info4, SegmentSkins[segment7]);
        var apply2Inserted = false;
        for (; index < codes.Count; index++)
        {
            if (TranspilerUtils.IsSameInstruction(codes[index], info4LocalVarLdLoc)
                && codes[index + 1].opcode == OpCodes.Ldfld && codes[index + 1].operand == netInfoCreatePavementField)
            {
                TranspilerUtils.LogDebug("Found info4.m_createPavement (Apply 2)");

                var apply2Instructions = new[]
                {
                    new CodeInstruction(info4LocalVarLdLoc),
                    new CodeInstruction(OpCodes.Ldsfld, segmentSkinsField),
                    new CodeInstruction(segment7LocalVarLdLoc),
                    new CodeInstruction(OpCodes.Ldelem_Ref),
                    new CodeInstruction(OpCodes.Call, terrainSurfacePatcherApplyMethod),
                    new CodeInstruction(OpCodes.Stloc, patcherStateLocalVar)
                };
                apply2Instructions[0].labels.AddRange(codes[index].labels);
                codes[index].labels.Clear();

                codes.InsertRange(index, apply2Instructions);
                apply2Inserted = true;
                index += apply2Instructions.Length;
                break;
            }
        }

        if (!apply2Inserted)
        {
            Debug.LogError("NetNodeUpdateJunctionTerrainPatch: Apply Insertion 2 failed! Cancelling transpiler!");
            return originalInstructions;
        }

        // --- Revert 2 ---
        // Before: bool flag17 = info4.m_flattenTerrain || ...
        // Insert: TerrainSurfacePatcher.Revert(info4, patcherState);
        var revert2Inserted = false;
        for (; index < codes.Count; index++)
        {
            if (TranspilerUtils.IsSameInstruction(codes[index], info4LocalVarLdLoc)
                && codes[index + 1].opcode == OpCodes.Ldfld && codes[index + 1].operand == netInfoFlattenTerrainField)
            {
                TranspilerUtils.LogDebug("Found info4.m_flattenTerrain (Revert 2)");

                var revert2Instructions = new[]
                {
                    new CodeInstruction(info4LocalVarLdLoc),
                    new CodeInstruction(OpCodes.Ldloc, patcherStateLocalVar),
                    new CodeInstruction(OpCodes.Call, terrainSurfacePatcherRevertMethod),
                };
                revert2Instructions[0].labels.AddRange(codes[index].labels);
                codes[index].labels.Clear();

                codes.InsertRange(index, revert2Instructions);
                revert2Inserted = true;
                index += revert2Instructions.Length;
                break;
            }
        }

        if (!revert2Inserted)
        {
            Debug.LogError("NetNodeUpdateJunctionTerrainPatch: Apply Insertion 2 failed! Cancelling transpiler!");
            return originalInstructions;
        }

        return codes;
    }

    private static CodeInstruction FindSegment7LocalVar(List<CodeInstruction> codes, CodeInstruction info4LocalVarStLoc, ref int index, int endIndex)
    {
        //if (!TranspilerUtils.IsStLoc(info4LocalVarStLoc))
        if (!info4LocalVarStLoc.IsStloc())
        {
            Debug.LogError("info4LocalVarStLoc is not stloc! Cancelling transpiler!");
            return null;
        }
        for (; index < endIndex; index++)
        {
            if (TranspilerUtils.IsSameInstruction(codes[index], info4LocalVarStLoc))
            {
                //if (TranspilerUtils.IsLdLoc(codes[index - 6]))
                if (codes[index - 6].IsLdloc())
                        return codes[index - 6].Clone();
            }
        }
        Debug.LogError("Unable to find segment7. Cancelling transpiler!");
        return null;
    }

    private static CodeInstruction FindNum6LocalVar(List<CodeInstruction> codes, CodeInstruction netInfoLocalVarStLoc, ref int index, int endIndex)
    {
        //if (!TranspilerUtils.IsStLoc(netInfoLocalVarStLoc))
        if (!netInfoLocalVarStLoc.IsStloc())
        {
            Debug.LogError("netInfoLocalVarStLoc is not stloc! Cancelling transpiler!");
            return null;
        }
        for (; index < endIndex; index++)
        {
            if (TranspilerUtils.IsSameInstruction(codes[index], netInfoLocalVarStLoc))
            {
                //if (TranspilerUtils.IsLdLoc(codes[index - 6]))
                if (codes[index - 6].IsLdloc())
                    return codes[index - 6].Clone();
            }
        }
        Debug.LogError("Unable to find num6. Cancelling transpiler!");
        return null;
    }
}