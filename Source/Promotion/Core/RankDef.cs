using System.Collections.Generic;
using Verse;

namespace Promotion
{
    public class RankDef : Def
    {
        public int seniority;           // 职级高低（1=实习生，10=总监）
        public List<ClearanceLevel> clearances;  // 权限列表
        public bool universalAccess;    // 是否全图通行（总监特权）
        
        // 辅助方法：检查是否有某权限
        public bool HasClearance(ClearanceLevel level) 
        {
            return universalAccess || clearances?.Contains(level) == true;
        }
    }
    
    // 权限等级枚举（对应 XML 里的 <li>Production</li> 等）
    public enum ClearanceLevel
    {
        Public,      // 公共区域
        Production,  // 生产区
        Research,    // 科研区
        Command      // 指挥室
    }
}