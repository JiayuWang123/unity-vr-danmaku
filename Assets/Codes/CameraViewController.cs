using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

/// <summary>
/// Desktop: hold right mouse button to look around.
/// VR: re-parents to XR rig and enables Tracked Pose Driver when a headset is active.
/// </summary>
[DefaultExecutionOrder(-150)]
public class CameraViewController : MonoBehaviour
{
    [Header("XR Rig")]
    public GameObject xrOrigin;
    public Transform vrCameraMount;
    public Behaviour trackedPoseDriver;

    [Header("Desktop Look")]
    public float mouseSensitivity = 2f;
    public float minPitch = -85f;
    public float maxPitch = 85f;
    public bool requireRightMouseButton = true;

    float pitch;
    float yaw;
    bool desktopLookActive = true;

    public bool IsDesktopMode => desktopLookActive;

    void Awake()
    {
        ResolveReferences();
        SyncRotationFromTransform();
        EnterDesktopMode();
        StartCoroutine(InitializeModeWhenReady());
    }

    void ResolveReferences()
    {
        if (xrOrigin == null)
        {
            var originGo = GameObject.Find("XR Origin (XR Rig)");
            if (originGo != null)
                xrOrigin = originGo;
        }

        if (vrCameraMount == null && xrOrigin != null)
        {
            var mount = xrOrigin.transform.Find("Camera Offset");
            if (mount != null)
                vrCameraMount = mount;
        }

        if (trackedPoseDriver == null)
        {
            foreach (var behaviour in GetComponents<Behaviour>())
            {
                if (behaviour == null || behaviour == this)
                    continue;

                var typeName = behaviour.GetType().Name;
                if (typeName == "TrackedPoseDriver")
                {
                    trackedPoseDriver = behaviour;
                    break;
                }
            }
        }
    }

    IEnumerator InitializeModeWhenReady()
    {
#if !UNITY_EDITOR
        yield return TryInitializeXr();
#else
        yield return null;
#endif

        if (XRSettings.enabled && XRSettings.isDeviceActive)
            EnterVrMode();
    }

#if !UNITY_EDITOR
    static IEnumerator TryInitializeXr()
    {
        var settings = XRGeneralSettings.Instance;
        if (settings == null || settings.Manager == null)
            yield break;

        if (settings.Manager.activeLoader != null)
            yield break;

        yield return settings.Manager.InitializeLoader();
        if (settings.Manager.activeLoader != null)
            settings.Manager.StartSubsystems();
    }
#endif

    public void SyncRotationFromTransform()
    {
        var euler = transform.eulerAngles;
        pitch = NormalizeAngle(euler.x);
        yaw = euler.y;
    }

    public void LookAtPoint(Vector3 worldPoint)
    {
        transform.LookAt(worldPoint);
        SyncRotationFromTransform();
    }

    static float NormalizeAngle(float angle)
    {
        if (angle > 180f)
            angle -= 360f;
        return angle;
    }

    void EnterDesktopMode()
    {
        desktopLookActive = true;

        if (trackedPoseDriver != null)
            trackedPoseDriver.enabled = false;

        if (xrOrigin != null)
            xrOrigin.SetActive(false);

        transform.SetParent(null, true);
    }

    void EnterVrMode()
    {
        desktopLookActive = false;

        if (xrOrigin != null)
            xrOrigin.SetActive(true);

        if (vrCameraMount != null)
        {
            transform.SetParent(vrCameraMount, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }

        if (trackedPoseDriver != null)
            trackedPoseDriver.enabled = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Update()
    {
        if (!desktopLookActive)
            return;

        if (requireRightMouseButton && !Input.GetMouseButton(1))
            return;

        var mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        var mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        yaw += mouseX;
        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }
}
