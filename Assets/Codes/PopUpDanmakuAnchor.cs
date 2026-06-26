using System.Collections.Generic;
using UnityEngine;

public class PopUpDanmakuAnchor : MonoBehaviour
{
    public PopUpDanmakuZone zone;
    public int slotIndex;
    public int maxConcurrent = 2;

    readonly List<PopUpDanmakuInstance> activeInstances = new List<PopUpDanmakuInstance>();

    public bool TrySpawn(PopUpDanmakuInstance prefab, PopUpDanmakuRecord record, PopUpDanmakuSettings settings, Transform parent, out PopUpDanmakuInstance instance)
    {
        instance = null;
        CleanupDestroyed();

        if (activeInstances.Count >= maxConcurrent)
            return false;

        instance = Instantiate(prefab, transform.position, transform.rotation, parent);
        instance.gameObject.SetActive(true);
        instance.Initialize(record, settings, zone, this);
        activeInstances.Add(instance);
        return true;
    }

    public void Release(PopUpDanmakuInstance instance)
    {
        activeInstances.Remove(instance);
    }

    void CleanupDestroyed()
    {
        for (int i = activeInstances.Count - 1; i >= 0; i--)
        {
            if (activeInstances[i] == null)
                activeInstances.RemoveAt(i);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = zone == PopUpDanmakuZone.Near
            ? Color.yellow
            : zone == PopUpDanmakuZone.Mid
                ? Color.cyan
                : Color.magenta;
        Gizmos.DrawWireSphere(transform.position, 0.08f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.15f);
    }
}
