using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using NetworkSkins.Skins;
using UnityEngine;

namespace NetworkSkins.Patches.NetSegment
{
    /// <summary>
    /// Common transpiler logic for NetSegment.RenderInstance, NetSegment.CalculateGroupData and NetSegment.PopulateGroupData
    /// </summary>
    public static class NetSegmentRenderPatch
    {
        /// <summary>
        /// Holds the custom segments array for the current segment being rendered,
        /// so that the RenderSegments patch can pick it up without requiring a signature change.
        /// ThreadStatic ensures thread safety (Cities: Skylines renders on multiple threads).
        /// Reset to null at the start of every RenderInstance call, and only set when a skin exists,
        /// so RenderSegments can distinguish "no skin" (null) from "skin with custom segments".
        /// </summary>
        [ThreadStatic]
        public static NetInfo.Segment[] CurrentCustomSegments;

        public static bool PatchLanesAndSegments(ILGenerator il, List<CodeInstruction> codes, CodeInstruction infoLdInstruction, CodeInstruction segmentIdLdInstruction, ref int index)
        {
            var netInfoLanesField = typeof(NetInfo).GetField("m_lanes");
            var netInfoSegmentsField = typeof(NetInfo).GetField("m_segments");
            var segmentSkinsField = typeof(NetworkSkinManager).GetField("SegmentSkins", BindingFlags.Static | BindingFlags.Public);
            var networkSkinLanesField = typeof(NetworkSkin).GetField("m_lanes");
            var networkSkinSegmentsField = typeof(NetworkSkin).GetField("m_segments");
            var currentCustomSegmentsField = typeof(NetSegmentRenderPatch).GetField(nameof(CurrentCustomSegments));
            if (netInfoLanesField == null || netInfoSegmentsField == null || segmentSkinsField == null || networkSkinLanesField == null || networkSkinSegmentsField == null || currentCustomSegmentsField == null)
            {
                Debug.LogError("Necessary field not found. Cancelling transpiler!");
                return false;
            }

            var beginLabel = il.DefineLabel();
            codes[index].labels.Add(beginLabel);

            var customLanesLocalVar = il.DeclareLocal(typeof(NetInfo.Lane[]));
            customLanesLocalVar.SetLocalSymInfo("customLanes");

            var customSegmentsLocalVar = il.DeclareLocal(typeof(NetInfo.Segment[]));
            customSegmentsLocalVar.SetLocalSymInfo("customSegments");

            var customLanesInstructions = new[]
            {
                // NetSegmentRenderPatch.CurrentCustomSegments = null;  ← toujours, en premier, avant tout branchement
                new CodeInstruction(OpCodes.Ldnull),
                new CodeInstruction(OpCodes.Stsfld, currentCustomSegmentsField),

                // NetInfo.Lane[] customLanes = info.m_lanes;
                new CodeInstruction(infoLdInstruction), // info
                new CodeInstruction(OpCodes.Ldfld, netInfoLanesField),
                new CodeInstruction(OpCodes.Stloc, customLanesLocalVar),

                // NetInfo.Segment[] customSegments = info.m_segments;
                new CodeInstruction(infoLdInstruction), // info
                new CodeInstruction(OpCodes.Ldfld, netInfoSegmentsField),
                new CodeInstruction(OpCodes.Stloc, customSegmentsLocalVar),

                // if (SegmentSkinManager.SegmentSkins[segmentID] != null) {
                new CodeInstruction(OpCodes.Ldsfld, segmentSkinsField),
                new CodeInstruction(segmentIdLdInstruction), // segmentID
                new CodeInstruction(OpCodes.Ldelem_Ref),
                new CodeInstruction(OpCodes.Brfalse_S, beginLabel),

                // customLanes = SegmentSkinManager.SegmentSkins[segmentID].m_lanes;
                new CodeInstruction(OpCodes.Ldsfld, segmentSkinsField),
                new CodeInstruction(segmentIdLdInstruction), // segmentID
                new CodeInstruction(OpCodes.Ldelem_Ref),
                new CodeInstruction(OpCodes.Ldfld, networkSkinLanesField),
                new CodeInstruction(OpCodes.Stloc, customLanesLocalVar),

                // customSegments = SegmentSkinManager.SegmentSkins[segmentID].m_segments;
                new CodeInstruction(OpCodes.Ldsfld, segmentSkinsField),
                new CodeInstruction(segmentIdLdInstruction), // segmentID
                new CodeInstruction(OpCodes.Ldelem_Ref),
                new CodeInstruction(OpCodes.Ldfld, networkSkinSegmentsField),
                new CodeInstruction(OpCodes.Stloc, customSegmentsLocalVar),

                // NetSegmentRenderPatch.CurrentCustomSegments = customSegments;  ← seulement si skin
                new CodeInstruction(OpCodes.Ldloc, customSegmentsLocalVar),
                new CodeInstruction(OpCodes.Stsfld, currentCustomSegmentsField),
                // }
            };
            codes.InsertRange(index, customLanesInstructions);

            index += customLanesInstructions.Length;

            // Replace all occurences of:
            //
            // info.m_lanes
            // -- with --
            // customLanes
            //
            // info.m_segments
            // -- with --
            // customSegments
            for (; index < codes.Count; index++)
            {
                if (TranspilerUtils.IsSameInstruction(codes[index], infoLdInstruction) && codes[index + 1].opcode == OpCodes.Ldfld)
                {
                    if (codes[index + 1].operand == netInfoLanesField)
                    {
                        // It is important that we copy the labels from the existing instruction!
                        // Otherwise "Label not marked" exception
                        codes[index] = new CodeInstruction(codes[index])
                        {
                            opcode = OpCodes.Ldloc,
                            operand = customLanesLocalVar
                        };
                        codes.RemoveAt(index + 1);
                    }
                    else if (codes[index + 1].operand == netInfoSegmentsField)
                    {
                        // It is important that we copy the labels from the existing instruction!
                        // Otherwise "Label not marked" exception
                        codes[index] = new CodeInstruction(codes[index])
                        {
                            opcode = OpCodes.Ldloc,
                            operand = customSegmentsLocalVar
                        };
                        codes.RemoveAt(index + 1);
                    }
                }
            }

            return true;
        }
    }
}
