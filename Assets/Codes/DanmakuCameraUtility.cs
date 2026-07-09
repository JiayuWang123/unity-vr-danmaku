using UnityEngine;

public static class DanmakuCameraUtility
{
    public static Camera ResolveViewCamera()
    {
        Camera main = Camera.main;
        if (main != null && main.enabled && main.gameObject.activeInHierarchy)
            return main;

        Camera[] cameras = Camera.allCamerasCount > 0 ? new Camera[Camera.allCamerasCount] : null;
        if (cameras == null || cameras.Length == 0)
            return null;

        Camera.GetAllCameras(cameras);
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null && cameras[i].enabled && cameras[i].gameObject.activeInHierarchy)
                return cameras[i];
        }

        return null;
    }

    /// <summary>解析 VR 头显主相机（优先 Camera.main，其次 XR Origin 下的 Main Camera）。</summary>
    public static Transform ResolveHeadTransform()
    {
        Camera cam = ResolveViewCamera();
        if (cam != null)
            return cam.transform;

        var xrOrigin = GameObject.Find("XR Origin (XR Rig)");
        if (xrOrigin != null)
        {
            var xrCam = xrOrigin.GetComponentInChildren<Camera>(true);
            if (xrCam != null)
                return xrCam.transform;
        }

        return null;
    }
}
