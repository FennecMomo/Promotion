using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Promotion.Harmony
{
    // ==================== 路径上下文（用于传递 Pawn 到 PathGrid）====================
    public static class PathContext
    {
        public static Stack<Pawn> PawnStack = new Stack<Pawn>();
    }

    // ==================== 路径成本修改（软限制：让禁区像沼泽一样难走）====================
    [HarmonyPatch(typeof(Pawn_PathFollower))]
    [HarmonyPatch("StartPath")]
    public static class Patch_PawnPathFollower_StartPath
    {
        static void Prefix(Pawn ___pawn)
        {
            if (___pawn != null)
                PathContext.PawnStack.Push(___pawn);
        }
        
        static void Finalizer()
        {
            if (PathContext.PawnStack.Count > 0)
                PathContext.PawnStack.Pop();
        }
    }

    [HarmonyPatch(typeof(PathGrid))]
    [HarmonyPatch("CalculatedCostAt")]
    public static class Patch_PathGrid_CalculatedCostAt
    {
        // 地块权重：禁区额外成本（1000 = 像深水/厚沼泽，AI 会尽量避开）
        private const int RESTRICTED_TERRAIN_COST = 1000;
        
        static void Postfix(ref int __result, IntVec3 c)
        {
            if (__result >= 10000) return; // 已经是墙，不管
            if (PathContext.PawnStack.Count == 0) return; // 不是通过正常寻路调用的
            
            var pawn = PathContext.PawnStack.Peek();
            if (pawn == null) return;
            if (!pawn.RaceProps.Humanlike) return;
            if (pawn.Faction != Faction.OfPlayer) return;
            
            var comp = pawn.GetComp<PawnPromotionComp>();
            
            // 如果当前格子是禁区，增加路径成本
            if (Patch_Pawn_JobTracker_StartJob.IsRestricted(c, pawn, comp))
            {
                __result += RESTRICTED_TERRAIN_COST;
            }
        }
    }

    // ==================== 第一层：AI 分配过滤（防卡死）====================
    [HarmonyPatch(typeof(JobGiver_Work))]
    [HarmonyPatch("TryIssueJobPackage")]
    public static class Patch_JobGiver_Work
    {
        static void Postfix(ref ThinkResult __result, Pawn pawn, JobIssueParams jobParams)
        {
            if (__result.Job == null) return;
            if (pawn == null || !pawn.RaceProps.Humanlike || pawn.Faction != Faction.OfPlayer) return;
            if (pawn.Map == null) return;
            
            var job = __result.Job;
            var comp = pawn.GetComp<PawnPromotionComp>();
            
            List<IntVec3> targetCells = new List<IntVec3>();
            if (job.targetA.IsValid) targetCells.Add(job.targetA.Cell);
            if (job.targetB.IsValid) targetCells.Add(job.targetB.Cell);
            if (job.targetC.IsValid) targetCells.Add(job.targetC.Cell);
            
            IntVec3 currentCell = pawn.Position;
            
            foreach (var targetCell in targetCells)
            {
                if (!targetCell.IsValid) continue;
                
                bool targetRestricted = Patch_Pawn_JobTracker_StartJob.IsRestricted(targetCell, pawn, comp);
                bool currentRestricted = Patch_Pawn_JobTracker_StartJob.IsRestricted(currentCell, pawn, comp);
                
                if (targetRestricted && !currentRestricted)
                {
                    __result = ThinkResult.NoJob;
                    return;
                }
            }
        }
    }

    // ==================== 第二层：Job 启动拦截（硬限制+重定向）====================
    [HarmonyPatch(typeof(Pawn_JobTracker))]
    [HarmonyPatch("StartJob")]
    public static class Patch_Pawn_JobTracker_StartJob
    {
        private static Dictionary<Pawn, int> lastMessageTicks = new Dictionary<Pawn, int>();
        private const int COOLDOWN = 250;
        
        private static readonly HashSet<JobDef> uncheckedJobDefs = new HashSet<JobDef>
        {
            JobDefOf.GotoSafeTemperature,
            JobDefOf.Flee,
            JobDefOf.FleeAndCower,
            JobDefOf.FleeAndCowerShort,
            JobDefOf.GotoMindControlled,
            JobDefOf.Wait,
            JobDefOf.IdleWhileDespawned,
            JobDefOf.Wait_MaintainPosture,
            JobDefOf.Wait_Asleep,
            JobDefOf.Wait_Downed,
            JobDefOf.Wait_AsleepDormancy,
            JobDefOf.Wait_SafeTemperature,
            JobDefOf.Wait_Combat,
        };

        static bool Prefix(Pawn_JobTracker __instance, Job newJob, Pawn ___pawn)
        {
            if (newJob == null) return true;
            if (___pawn == null || !___pawn.RaceProps.Humanlike || ___pawn.Faction != Faction.OfPlayer) return true;
            if (___pawn.Map == null) return true;
            
            if (uncheckedJobDefs.Contains(newJob.def)) return true;
            
            var comp = ___pawn.GetComp<PawnPromotionComp>();
            
            List<IntVec3> targetCells = new List<IntVec3>();
            if (newJob.targetA.IsValid) targetCells.Add(newJob.targetA.Cell);
            if (newJob.targetB.IsValid) targetCells.Add(newJob.targetB.Cell);
            if (newJob.targetC.IsValid) targetCells.Add(newJob.targetC.Cell);
            
            IntVec3 currentCell = ___pawn.Position;
            
            foreach (IntVec3 targetCell in targetCells)
            {
                if (!targetCell.IsValid) continue;
                
                bool targetRestricted = IsRestricted(targetCell, ___pawn, comp);
                bool currentRestricted = IsRestricted(currentCell, ___pawn, comp);
                
                if (targetRestricted && !currentRestricted)
                {
                    int tick = Find.TickManager.TicksGame;
                    if (!lastMessageTicks.TryGetValue(___pawn, out int last) || tick - last > COOLDOWN)
                    {
                        lastMessageTicks[___pawn] = tick;
                        string rankName = comp?.currentRank?.label ?? "无职级/异常";
                        Messages.Message($"{___pawn.Name} ({rankName}) 无权限进入该区域，已自动转向！", 
                            MessageTypeDefOf.RejectInput, historical: false);
                    }
                    
                    // 如果是移动类Job，重定向到安全点
                    if (newJob.def == JobDefOf.Goto || newJob.def == JobDefOf.GotoWander)
                    {
                        IntVec3 safeCell = FindNearestSafeCell(___pawn, comp);
                        if (safeCell.IsValid)
                        {
                            newJob.targetA = new LocalTargetInfo(safeCell);
                            return true;
                        }
                    }
                    
                    return false;
                }
            }
            
            return true;
        }

        public static bool IsRestricted(IntVec3 cell, Pawn pawn, PawnPromotionComp comp)
        {
            if (pawn.Map == null) return false;
            
            Area area = pawn.Map.areaManager.AllAreas.FirstOrDefault(a => a[cell]);
            if (area == null) return false;
            
            ClearanceLevel required = GetRequiredClearance(area);
            if (required == ClearanceLevel.Public) return false;
            
            if (comp?.currentRank == null) return true;
            
            return !comp.currentRank.HasClearance(required) && !comp.currentRank.universalAccess;
        }
        
        public static ClearanceLevel GetRequiredClearance(Area area)
        {
            string label = area.Label.ToLower();
            if (label.Contains("科研") || label.Contains("research")) return ClearanceLevel.Research;
            if (label.Contains("生产") || label.Contains("production")) return ClearanceLevel.Production;
            if (label.Contains("指挥") || label.Contains("command")) return ClearanceLevel.Command;
            return ClearanceLevel.Public;
        }
        
        public static IntVec3 FindNearestSafeCell(Pawn pawn, PawnPromotionComp comp)
        {
            for (int radius = 1; radius <= 50; radius++)
            {
                foreach (var cell in GenRadial.RadialCellsAround(pawn.Position, radius, true))
                {
                    if (!cell.IsValid) continue;
                    if (!cell.Walkable(pawn.Map)) continue;
                    if (!IsRestricted(cell, pawn, comp))
                    {
                        return cell;
                    }
                }
            }
            return IntVec3.Invalid;
        }
    }

    // ==================== 第三层：自动离开禁区（备用保险）====================
    [HarmonyPatch(typeof(Pawn))]
    [HarmonyPatch("Tick")]
    public static class Patch_Pawn_Tick_LeaveRestrictedArea
    {
        private static readonly HashSet<JobDef> allowedInRestrictedDefs = new HashSet<JobDef>
        {
            JobDefOf.Wait_Downed,
            JobDefOf.Wait_Asleep,
            JobDefOf.Wait_AsleepDormancy,
            JobDefOf.Flee,
            JobDefOf.FleeAndCower,
            JobDefOf.GotoSafeTemperature,
        };

        static void Postfix(Pawn __instance)
        {
            if (__instance == null) return;
            if (!__instance.RaceProps.Humanlike) return;
            if (__instance.Faction != Faction.OfPlayer) return;
            if (__instance.Map == null) return;
            
            if (Find.TickManager.TicksGame % 120 != 0) return;
            
            var comp = __instance.GetComp<PawnPromotionComp>();
            
            if (!Patch_Pawn_JobTracker_StartJob.IsRestricted(__instance.Position, __instance, comp))
                return;
            
            if (__instance.CurJob != null)
            {
                if (allowedInRestrictedDefs.Contains(__instance.CurJob.def))
                    return;
                
                if (__instance.CurJob.def == JobDefOf.Goto)
                {
                    var targetCell = __instance.CurJob.targetA.Cell;
                    if (targetCell.IsValid && !Patch_Pawn_JobTracker_StartJob.IsRestricted(targetCell, __instance, comp))
                        return;
                }
            }
            
            IntVec3 exitCell = Patch_Pawn_JobTracker_StartJob.FindNearestSafeCell(__instance, comp);
            if (exitCell.IsValid)
            {
                Job leaveJob = JobMaker.MakeJob(JobDefOf.Goto, exitCell);
                __instance.jobs.TryTakeOrderedJob(leaveJob);
            }
        }
    }
}