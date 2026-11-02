using UnityEngine;

namespace BrokenGame
{
    /// <summary>PLANTED BUG: a floor tile that gives way under the player, dropping them out of the level.</summary>
    public class Trapdoor : MonoBehaviour
    {
        public float standSeconds = 0.4f;
        float _timer;
        bool _open;

        void Update()
        {
            if (_open) return;
            var cc = FindAnyObjectByType<CharacterController>();
            if (cc == null) return;
            var b = GetComponent<Collider>().bounds;
            var p = cc.transform.position;
            var inside = p.x > b.min.x && p.x < b.max.x && p.z > b.min.z && p.z < b.max.z && p.y < b.max.y + 2f;
            _timer = inside ? _timer + Time.deltaTime : 0f;
            if (_timer < standSeconds) return;
            _open = true;
            GetComponent<Collider>().enabled = false;
            GetComponent<Renderer>().enabled = false;
        }
    }
}
