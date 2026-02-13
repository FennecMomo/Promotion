using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Promotion.UI
{
    [HarmonyPatch(typeof(PlaySettings))]
    [HarmonyPatch("DoPlaySettingsGlobalControls")]
    public static class Patch_PlaySettingsControls
    {
        public static void Postfix(WidgetRow row, bool worldView)
        {
            if (worldView) return;
            
            bool show = Patch_PawnUIOverlay.ShowRankOverlay;
            string label = show ? "隐藏职级" : "显示职级";
            string tooltip = show ? "隐藏头顶的职级显示" : "显示头顶的职级标签";
            
            // 用文字按钮代替图标
            if (row.ButtonText(label, tooltip))
            {
                Patch_PawnUIOverlay.ShowRankOverlay = !show;
            }
        }
    }
}