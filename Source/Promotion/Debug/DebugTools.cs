using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace Promotion.Debug
{
    public static class DebugTools
    {
        [DebugAction("晋升", "测试晋升选中殖民者")]
        private static void TestPromote()
        {
            var pawn = Find.CurrentMap.mapPawns.FreeColonists.FirstOrDefault();
            if (pawn != null)
            {
                Messages.Message($"{pawn.Name} 晋升测试成功！", MessageTypeDefOf.PositiveEvent);
            }
        }

        private static bool TryGetSelectedPawn(out Pawn pawn)
        {
            pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (pawn == null)
            {
                Messages.Message("请先选中一个殖民者！", MessageTypeDefOf.NegativeEvent);
                return false;
            }

            return true;
        }
        
        // 获取选中的殖民者职级组件
        private static bool TryGetPromotion(out PawnPromotionComp comp, Pawn pawn)
        {
            comp = null;

            if (pawn == null)
            {
                return false;
            }
                
            comp = pawn.GetComp<PawnPromotionComp>();
            if (comp == null)
            {
                Messages.Message($"{pawn.Name} 没有职级组件！", MessageTypeDefOf.NegativeEvent);
                return false;
            }
            
            return true;
        }

        // 设置选中者为总监
        [DebugAction("晋升", "设为总监(Director)")]
        private static void SetDirector()
        {
            if (!TryGetSelectedPawn(out var pawn))
            {
                return;
            }

            if (!TryGetPromotion(out var comp, pawn))
            {
                return;
            }

            // 设置职级为总监
            comp.currentRank = DefDatabase<RankDef>.GetNamed("Director");
            Messages.Message($"{pawn.Name} 被任命为 [{comp.currentRank.label}]！", MessageTypeDefOf.PositiveEvent);
        }
        
        // 查看当前职级
        [DebugAction("晋升", "查看当前职级")]
        private static void CheckRank()
        {
            if (!TryGetSelectedPawn(out var pawn))
            {
                return;
            }

            if (!TryGetPromotion(out var comp, pawn))
            {
                return;
            }
            
            string rankName = comp.HasRank ? comp.currentRank.label : "无";
            Messages.Message($"{pawn.Name} 当前职级: {rankName}", MessageTypeDefOf.NeutralEvent);
        }
    }
}