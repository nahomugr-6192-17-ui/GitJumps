using UnityEngine;

[RequireComponent(typeof(Camera))]
public class adaptiveCameraScript : MonoBehaviour
{
    [Header("Aspect & Orthographic Settings")]
    public float targetAspect = 16f / 9f;
    public float baselineOrthoSize = 10f;
    public float baselineCameraY = 0f;

    private Camera cam;
    private float lastAspect = -1f;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        UpdateFraming();
    }

    private void LateUpdate()
    {
        if (cam != null && Mathf.Abs(cam.aspect - lastAspect) > 0.001f)
        {
            UpdateFraming();
        }
    }

    public void UpdateFraming()
    {
        if (cam == null)
            cam = GetComponent<Camera>();

        if (cam == null)
            return;

        lastAspect = cam.aspect;
        float targetHalfWidth = baselineOrthoSize * targetAspect;

        if (cam.aspect < targetAspect)
        {
            float requiredOrtho = targetHalfWidth / cam.aspect;
            cam.orthographicSize = requiredOrtho;
            float deltaSize = requiredOrtho - baselineOrthoSize;
            transform.position = new Vector3(transform.position.x, baselineCameraY + deltaSize, transform.position.z);
        }
        else
        {
            cam.orthographicSize = baselineOrthoSize;
            transform.position = new Vector3(transform.position.x, baselineCameraY, transform.position.z);
        }
    }
}

