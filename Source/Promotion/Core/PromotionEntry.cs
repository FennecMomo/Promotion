using Verse;
using HarmonyLib;
using System.Reflection;
using RimWorld;

namespace Promotion
{
    [StaticConstructorOnStartup]
    public static class PromotionEntry
    {
        static PromotionEntry()
        {
            var harmony = new HarmonyLib.Harmony("FennecMomo.Promotion");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            
            Log.Message("[晋升体系] Promotion v0.1.0 加载成功");
        }
    }
    
    [HarmonyPatch(typeof(Pawn))]
    [HarmonyPatch("SpawnSetup")]
    public static class Patch_PawnSpawnSetup
    {
        static void Postfix(Pawn __instance)
        {
            // 所有人形pawn，如果没有组件，自动添加
            if (__instance.RaceProps.Humanlike && __instance.GetComp<PawnPromotionComp>() == null)
            {
                var comp = new PawnPromotionComp();
                comp.parent = __instance;
                __instance.AllComps.Add(comp);
            
                // 如果是玩家派系，自动设为实习生
                if (__instance.Faction == Faction.OfPlayer)
                {
                    comp.currentRank = DefDatabase<RankDef>.GetNamed("Intern", false);
                }
            }
        }
    }
}