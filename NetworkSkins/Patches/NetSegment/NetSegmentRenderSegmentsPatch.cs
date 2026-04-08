using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace NetworkSkins.Patches.NetSegment
{
    /// <summary>
    /// Patches NetSegment.RenderSegments so that it iterates over
    /// NetSegmentRenderPatch.CurrentCustomSegments instead of info.m_segments
    /// when a skin is active for the current segment.
    /// Falls back to info.m_segments when CurrentCustomSegments is null (no skin).
    /// </summary>
    [HarmonyPatch]
    public static class NetSegmentRenderSegmentsPatch
    {
        public static MethodBase TargetMethod()
        {
            // private void RenderSegments(RenderManager.CameraInfo cameraInfo, NetInfo info, RenderManager.Instance data, float wOffsetParent, NetManager netManager)
            return typeof(global::NetSegment).GetMethod("RenderSegments", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var originalCodes = new List<CodeInstruction>(instructions);
            var codes = new List<CodeInstruction>(originalCodes);

            var netInfoSegmentsField = typeof(NetInfo).GetField("m_segments");
            var currentCustomSegmentsField = typeof(NetSegmentRenderPatch).GetField(nameof(NetSegmentRenderPatch.CurrentCustomSegments));
            var resolveMethod = typeof(NetSegmentRenderSegmentsPatch).GetMethod(nameof(ResolveSegments), BindingFlags.Static | BindingFlags.Public);

            if (netInfoSegmentsField == null || currentCustomSegmentsField == null || resolveMethod == null)
            {
                Debug.LogError("NetSegmentRenderSegmentsPatch: Necessary field not found. Cancelling transpiler!");
                return originalCodes;
            }

            // RenderSegments signature: instance=arg0, cameraInfo=arg1, info=arg2, data=arg3, wOffsetParent=arg4, netManager=arg5
            // Replace every occurrence of:
            //   ldarg.2 (info)
            //   ldfld NetInfo::m_segments
            // with:
            //   ldarg.2 (info)
            //   ldfld NetInfo::m_segments
            //   call ResolveSegments(info.m_segments)   ← returns CurrentCustomSegments ?? info.m_segments
            bool patched = false;
            for (int i = 0; i < codes.Count - 1; i++)
            {
                if (codes[i].opcode == OpCodes.Ldarg_2 && codes[i + 1].opcode == OpCodes.Ldfld && codes[i + 1].operand == netInfoSegmentsField)
                {
                    // Insert the call to ResolveSegments just after the ldfld, keeping labels intact
                    codes.Insert(i + 2, new CodeInstruction(OpCodes.Call, resolveMethod));
                    i += 2; // skip past the inserted instruction
                    patched = true;
                }
            }

            if (!patched)
            {
                Debug.LogError("NetSegmentRenderSegmentsPatch: Could not find info.m_segments reference in RenderSegments. Transpiler had no effect!");
            }

            return codes;
        }

        /// <summary>
        /// Returns CurrentCustomSegments if a skin is active for the current segment (non-null),
        /// otherwise falls back to the original info.m_segments array.
        /// </summary>
        public static NetInfo.Segment[] ResolveSegments(NetInfo.Segment[] infoSegments)
        {
            return NetSegmentRenderPatch.CurrentCustomSegments ?? infoSegments;
        }
    }
}
