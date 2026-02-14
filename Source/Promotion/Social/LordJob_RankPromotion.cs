using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Promotion.Social
{
    public class LordJob_RankPromotion : LordJob
    {
        private Pawn targetPawn;
        private RankDef targetRank;
        private Pawn organizer;
        private Building podium; // 新增：讲台引用

        public LordJob_RankPromotion()
        {
        }

        // 修改构造函数，接收 podium
        public LordJob_RankPromotion(Pawn target, RankDef newRank, Pawn org, Building podium)
        {
            this.targetPawn = target;
            this.targetRank = newRank;
            this.organizer = org;
            this.podium = podium; // 保存
        }

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();

            // 阶段1：集合（所有人走到目标身边）
            LordToil_GotoGathering gather = new LordToil_GotoGathering(targetPawn.Position);
            graph.AddToil(gather);

            // 阶段2：举行仪式
            LordToil_RankCeremony ceremony = new LordToil_RankCeremony(targetPawn, targetRank, this.podium);
            graph.AddToil(ceremony);

            // 阶段3：结束
            LordToil_End end = new LordToil_End();
            graph.AddToil(end);

            // 改为：时间触发 + 距离检查（人都到齐了就自动开始）
            Transition gatherToCeremony = new Transition(gather, ceremony);
            gatherToCeremony.AddTrigger(new Trigger_TicksPassed(2500));
            gatherToCeremony.AddPreAction(new TransitionAction_Message(
                $"{targetPawn.Name} 的晋升仪式开始！"));
            graph.AddTransition(gatherToCeremony);

            // 转换：仪式完成 → 结束
            Transition ceremonyToEnd = new Transition(ceremony, end);
            ceremonyToEnd.AddTrigger(new Trigger_TicksPassed(2500)); // 约1小时游戏时间
            ceremonyToEnd.AddPreAction(new TransitionAction_Custom(() => { DoPromotion(); }));
            graph.AddTransition(ceremonyToEnd);

            return graph;
        }

        private void DoPromotion()
        {
            var comp = targetPawn.GetComp<PawnPromotionComp>();
            if (comp != null)
            {
                var oldRank = comp.currentRank;
                comp.currentRank = targetRank;

                // 添加心情影响
                targetPawn.needs?.mood?.thoughts?.memories?.TryGainMemory(
                    ThoughtDef.Named("Promotion_Promoted"));

                Messages.Message(
                    $"{targetPawn.Name} 已正式晋升为 {targetRank.label}！",
                    targetPawn, MessageTypeDefOf.PositiveEvent);

                // 如果直属领导在场，领导也加心情
                if (organizer != null && organizer.Map == targetPawn.Map)
                {
                    organizer.needs?.mood?.thoughts?.memories?.TryGainMemory(
                        ThoughtDef.Named("Promotion_Witnessed"));
                }
            }
        }

        public override void ExposeData()
        {
            Scribe_References.Look(ref targetPawn, "targetPawn");
            Scribe_Defs.Look(ref targetRank, "targetRank");
            Scribe_References.Look(ref organizer, "organizer");
            Scribe_References.Look(ref podium, "podium"); // 新增：保存讲台引用
        }
    }

    // 仪式阶段行为
    public class LordToil_RankCeremony : LordToil
    {
        private Pawn target;
        private RankDef rank;
        private Building podium; // 必须保存

        public LordToil_RankCeremony(Pawn target, RankDef rank, Building podium)
        {
            this.target = target;
            this.rank = rank;
            this.podium = podium; // 确保赋值
        }

        public override void UpdateAllDuties()
        {
            // 获取 Duty（如果 null 则后续用 Idle 兜底）
            DutyDef hostDuty = DefDatabase<DutyDef>.GetNamed("RankCeremonyHost", false);
            DutyDef guestDuty = DefDatabase<DutyDef>.GetNamed("RankCeremonyGuest", false);

            if (podium == null)
            {
                Log.Error("[晋升] podium 为 null");
                return;
            }

            Room room = podium.Position.GetRoom(podium.Map);
            if (room == null || room.OutdoorsForWork)
            {
                // 室外应急：所有人站讲台周围 2 格
                foreach (var p in lord.ownedPawns)
                    p.mindState.duty = new PawnDuty(DutyDefOf.Idle, podium.Position, 2f);
                return;
            }

            // 获取可用椅子（严格在当前房间）
            List<Building> availableSeats = room.ContainedAndAdjacentThings
                .OfType<Building>()
                .Where(b => b.def.building?.isSittable == true)
                .Where(b => b.Position.GetFirstPawn(b.Map) == null && b.GetRoom() == room)
                .ToList();

            int seatIndex = 0;

            // 预定义 8 个环绕位置（防止角度计算失效）
            List<IntVec3> standingOffsets = new List<IntVec3>
            {
                new IntVec3(2, 0, 0), new IntVec3(1, 0, 1), new IntVec3(0, 0, 2),
                new IntVec3(-1, 0, 1), new IntVec3(-2, 0, 0), new IntVec3(-1, 0, -1),
                new IntVec3(0, 0, -2), new IntVec3(1, 0, -1)
            };
            int standingIndex = 0;

            foreach (var p in lord.ownedPawns)
            {
                if (p == target)
                {
                    // 主角：紧贴讲台（范围 0）
                    p.mindState.duty = new PawnDuty(hostDuty ?? DutyDefOf.Idle, podium.Position, 0f);
                }
                else
                {
                    if (seatIndex < availableSeats.Count)
                    {
                        // 有椅子：使用 guestDuty（修复：之前错误用了 DutyDefOf.Idle）
                        Building seat = availableSeats[seatIndex];
                        p.mindState.duty = new PawnDuty(guestDuty ?? DutyDefOf.Idle, seat.Position, 0f);
                        seatIndex++;
                    }
                    else
                    {
                        // 没椅子：使用预定义环绕位置
                        IntVec3 standPos = podium.Position;
                        int attempts = 0;

                        // 尝试找到有效的环绕位置
                        while (attempts < standingOffsets.Count)
                        {
                            int idx = (standingIndex + attempts) % standingOffsets.Count;
                            IntVec3 candidate = podium.Position + standingOffsets[idx];

                            if (candidate.IsValid && candidate.Walkable(p.Map) && candidate.GetRoom(p.Map) == room)
                            {
                                standPos = candidate;
                                standingIndex = (idx + 1) % standingOffsets.Count; // 下次从下一个开始
                                break;
                            }

                            attempts++;
                        }

                        p.mindState.duty = new PawnDuty(guestDuty ?? DutyDefOf.Idle, standPos, 0.5f);
                    }
                }
            }
        }
    }

    public class LordToil_GotoGathering : LordToil
    {
        private IntVec3 spot; // 讲台位置

        public LordToil_GotoGathering(IntVec3 spot)
        {
            this.spot = spot;
        }

        public override void UpdateAllDuties()
        {
            // 所有人走向讲台位置（范围 3 格，走到附近就停，不会挤在一个格子）
            foreach (var pawn in lord.ownedPawns)
            {
                pawn.mindState.duty = new PawnDuty(DutyDefOf.Goto, spot, 3f);
            }
        }
    }
}