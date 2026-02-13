using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Promotion.UI
{
    [HarmonyPatch(typeof(PawnUIOverlay))]
    [HarmonyPatch("DrawPawnGUIOverlay")]
    public static class Patch_PawnUIOverlay
    {
        public static bool ShowRankOverlay = true;  // 默认开启
        
        // 扩展接口：以后给特定派系显示职级（如友好派系、特定 mod 派系）
        public static bool ShouldShowRankFor(Pawn pawn)
        {
            // 当前：仅玩家派系
            if (pawn.Faction == Faction.OfPlayer)
            {
                return true;
            }
            
            // TODO: 以后通过配置或接口给以下情况显示：
            // - 友好派系（ Hospitality mod 的访客）
            // - 特定故事背景（如"企业流亡者"派系自带职级）
            // - 俘虏（显示原职级用于招降谈判）
            
            return false;
        }
        
        public static void Postfix(PawnUIOverlay __instance, Pawn ___pawn)
        {
            if (!ShowRankOverlay) return;
            if (___pawn == null) return;
            if (!ShouldShowRankFor(___pawn)) return;  // 只显示玩家殖民者
            
            var comp = ___pawn.GetComp<PawnPromotionComp>();
            if (comp?.currentRank == null) return;
            
            // 计算头顶位置的坐标
            var headWorldPos = ___pawn.DrawPos;
            headWorldPos.z += 1;
            // 转换为屏幕坐标（自动处理相机远近缩放）
            var screenPos = headWorldPos.MapToUIPosition();
            
            string label = $"[{comp.currentRank.label}]";
            var color = new Color(0.8f, 0.6f, 0.2f);  // 金色
            
            GUI.color = color;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            
            float width = Text.CalcSize(label).x + 10f;
            Rect rect = new Rect(screenPos.x - width/2, screenPos.y, width, 20f);
            
            GUI.DrawTexture(rect, TexUI.GrayTextBG);  // 半透明背景
            Widgets.Label(rect, label);
            
            // 恢复
            GUI.color = Color.white;
            // Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }
    }
}