using System.Collections.Generic;

// 管理「两簇」弹幕（比赛信息 / 球员球队裁判）各自的堆叠槽位，
// 保证同一分类内正在显示的弹幕不会占用同一个槽位（不重叠），
// 槽位在弹幕淡出销毁后才会被释放、留给后来的弹幕复用。
public class TopClusterDanmakuLayout
{
    readonly Dictionary<DanmakuSemanticCategory, HashSet<int>> occupiedSlots = new Dictionary<DanmakuSemanticCategory, HashSet<int>>();

    public void Reset()
    {
        occupiedSlots.Clear();
    }

    public int AcquireSlot(DanmakuSemanticCategory category)
    {
        if (!occupiedSlots.TryGetValue(category, out HashSet<int> set))
        {
            set = new HashSet<int>();
            occupiedSlots[category] = set;
        }

        int slot = 0;
        while (set.Contains(slot))
            slot++;

        set.Add(slot);
        return slot;
    }

    public void ReleaseSlot(DanmakuSemanticCategory category, int slot)
    {
        if (occupiedSlots.TryGetValue(category, out HashSet<int> set))
            set.Remove(slot);
    }
}
