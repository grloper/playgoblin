using UnityEngine;

namespace BrokenGame
{
    /// <summary>PLANTED BUG: the game camera renders nothing for a few seconds (black screen).</summary>
    [RequireComponent(typeof(Camera))]
    public class CameraBlackout : MonoBehaviour
    {
        public float startAt = 25f, seconds = 8f;
        int _mask;

        void Start() => _mask = GetComponent<Camera>().cullingMask;

        void Update()
        {
            var cam = GetComponent<Camera>();
            var on = Time.timeSinceLevelLoad > startAt && Time.timeSinceLevelLoad < startAt + seconds;
            cam.clearFlags = on ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
            cam.backgroundColor = Color.black;
            cam.cullingMask = on ? 0 : _mask;
        }
    }
}
