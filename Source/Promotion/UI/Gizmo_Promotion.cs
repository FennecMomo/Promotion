using System.Collections.Generic;
using System.Linq;
using Promotion.Social;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Promotion.UI
{
    public class Gizmo_Promotion : Command
    {
        private Pawn pawn;

        public Gizmo_Promotion(Pawn p)
        {
            this.pawn = p;
            this.defaultLabel = "晋升";
            this.defaultDesc = "为该殖民者举行晋升仪式";
            this.icon = ContentFinder<Texture2D>.Get("UI/Commands/LaunchReport", true)
                        ?? BaseContent.BadTex;

            // 检查是否有下一级职级
            var comp = pawn.GetComp<PawnPromotionComp>();
            if (comp?.currentRank != null)
            {
                var nextRank = GetNextRank(comp.currentRank);
                if (nextRank == null)
                {
                    this.Disable("已是最高职级");
                }
            }
            else
            {
                this.Disable("无当前职级");
            }
        }

        public override void ProcessInput(Event ev)
        {
            base.ProcessInput(ev);

            // 检查职级（原有逻辑保留）
            var comp = pawn.GetComp<PawnPromotionComp>();
            var currentRank = comp?.currentRank;
            if (currentRank == null) return;

            var nextRank = GetNextRank(currentRank);
            if (nextRank == null) return;

            // ========== 新增：自动寻找合规房间 ==========
            Building podium = FindBestCeremonyPodium(pawn);
            if (podium == null)
            {
                Messages.Message("没有找到可达的晋升讲台（需要放置在室内且可通行）", MessageTypeDefOf.RejectInput);
                return;
            }

            Room room = podium.Position.GetRoom(podium.Map);
            if (room == null || room.OutdoorsForWork)
            {
                Messages.Message("找到的讲台必须在室内", MessageTypeDefOf.RejectInput);
                return;
            }
            // ============================================

            // 检查是否已有进行中的仪式
            if (pawn.GetLord() != null)
            {
                Messages.Message($"{pawn.Name} 正在参与其他活动", MessageTypeDefOf.RejectInput);
                return;
            }

            // 创建仪式（使用找到的讲台）
            var participants = new List<Pawn> { pawn };

            // 找主持人（职级最高者）
            var highestRankPawn = pawn.Map.mapPawns.FreeColonists
                .Where(p => p != pawn && p.GetComp<PawnPromotionComp>()?.currentRank != null)
                .OrderByDescending(p => p.GetComp<PawnPromotionComp>().currentRank.seniority)
                .FirstOrDefault();
            Pawn organizer = highestRankPawn ?? pawn;
            if (organizer != pawn) participants.Add(organizer);

            // 找同事
            var colleagues = pawn.Map.mapPawns.FreeColonists
                .Where(p => p != pawn && p != organizer && p.relations.OpinionOf(pawn) > 0)
                .OrderByDescending(p => p.relations.OpinionOf(pawn))
                .Take(2);
            participants.AddRange(colleagues);

            // 创建 LordJob（使用找到的讲台）
            var lordJob = new LordJob_RankPromotion(pawn, nextRank, organizer, podium);
            LordMaker.MakeNewLord(pawn.Faction, lordJob, pawn.Map, participants);

            Messages.Message($"开始为 {pawn.Name} 举行晋升仪式，地点：{room.Role.label}", MessageTypeDefOf.NeutralEvent);
        }

// 新增方法：寻找最佳讲台
        private Building FindBestCeremonyPodium(Pawn pawn)
        {
            Building best = null;
            float bestDist = float.MaxValue;

            // 遍历所有晋升讲台
            foreach (var thing in pawn.Map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("PromotionPodium")))
            {
                if (thing is Building podium)
                {
                    // 必须在室内
                    Room room = podium.Position.GetRoom(podium.Map);
                    if (room == null || room.OutdoorsForWork) continue;

                    // 必须可达（路径检查）
                    if (!pawn.CanReach(podium, PathEndMode.Touch, Danger.Deadly)) continue;

                    // 选最近的（按路径成本或直线距离）
                    float dist = pawn.Position.DistanceToSquared(podium.Position);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = podium;
                    }
                }
            }

            return best;
        }

        private RankDef GetNextRank(RankDef current)
        {
            // 按 seniority 找下一级
            return DefDatabase<RankDef>.AllDefs
                .Where(r => r.seniority > current.seniority)
                .OrderBy(r => r.seniority)
                .FirstOrDefault();
        }
    }
}