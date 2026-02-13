using Verse;

namespace Promotion
{
    public class PawnPromotionComp : ThingComp
    {
        public RankDef currentRank;
        
        public override void PostExposeData()
        {
            Scribe_Defs.Look(ref currentRank, "currentRank");
        }
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            // 新生成的Pawn职级定为Intern (实习生)
            if (!respawningAfterLoad && currentRank == null)
            {
                currentRank = DefDatabase<RankDef>.GetNamed("Intern");
            }
        }
        
        // 辅助方法
        public bool HasRank => currentRank != null;
        public string RankLabel => currentRank?.label ?? "身份异常";
    }
}