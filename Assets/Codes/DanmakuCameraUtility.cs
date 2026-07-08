using UnityEngine;

public static class DanmakuCameraUtility
{
    public static Camera ResolveViewCamera()
    {
        Camera main = Camera.main;
        if (main != null && main.enabled)
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
}
