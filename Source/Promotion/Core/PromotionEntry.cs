using Verse;
using HarmonyLib;
using System.Reflection;

namespace Promotion
{
    [StaticConstructorOnStartup]
    public static class PromotionEntry
    {
        static PromotionEntry()
        {
            var harmony = new Harmony("YourName.Promotion");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            
            Log.Message("[晋升体系] Promotion v0.1.0 加载成功");
        }
    }
}