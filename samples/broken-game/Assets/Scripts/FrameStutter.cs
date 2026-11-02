using System.Threading;
using UnityEngine;

namespace BrokenGame
{
    /// <summary>PLANTED BUG: a section of the game runs at about 15 frames per second.</summary>
    public class FrameStutter : MonoBehaviour
    {
        public float startAt = 45f, seconds = 40f;
        public int sleepMs = 60;

        void Update()
        {
            var t = Time.timeSinceLevelLoad;
            if (t > startAt && t < startAt + seconds) Thread.Sleep(sleepMs);
        }
    }
}
