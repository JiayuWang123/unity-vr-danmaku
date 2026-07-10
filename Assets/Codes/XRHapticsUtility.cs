using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// 触发手柄震动的小工具。项目用的是 OpenXR + XR Interaction Toolkit（ActionBasedController），
/// 不是 Oculus OVRInput，所以震动统一走 XRBaseController.SendHapticImpulse。
/// </summary>
public static class XRHapticsUtility
{
    /// <summary>
    /// 找到场景里所有已启用的 ActionBasedController（通常就是左右手柄各一个）并触发一次震动脈冲。
    /// </summary>
    public static bool PulseAllControllers(float amplitude, float duration)
    {
        if (duration <= 0f || amplitude <= 0f)
            return false;

        ActionBasedController[] controllers = Object.FindObjectsOfType<ActionBasedController>();
        bool any = false;
        for (int i = 0; i < controllers.Length; i++)
        {
            ActionBasedController controller = controllers[i];
            if (controller == null || !controller.isActiveAndEnabled)
                continue;

            if (controller.SendHapticImpulse(amplitude, duration))
                any = true;
        }
        return any;
    }
}
