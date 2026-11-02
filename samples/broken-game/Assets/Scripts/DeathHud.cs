using UnityEngine;
using UnityEngine.UI;

namespace BrokenGame
{
    /// <summary>PLANTED BUG: after the player has fallen once, the HUD label is read every frame but was never assigned.</summary>
    public class DeathHud : MonoBehaviour
    {
        public Text label;
        bool _hasDied;

        void Update()
        {
            var cc = FindAnyObjectByType<CharacterController>();
            if (cc != null && cc.transform.position.y < -2f) _hasDied = true;
            if (_hasDied) label.text = "Respawned"; // NullReferenceException every frame when label is unassigned
        }
    }
}
