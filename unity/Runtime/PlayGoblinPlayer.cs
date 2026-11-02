using UnityEngine;
using UnityEngine.AI;

namespace PlayGoblin
{
    /// <summary>
    /// Add this to your player object. PlayGoblin uses, in order: a component on this object that implements
    /// <see cref="IPlayGoblinMover"/>, a NavMeshAgent, a CharacterController, a Rigidbody, or plain transform movement.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PlayGoblin/PlayGoblin Player")]
    public sealed class PlayGoblinPlayer : MonoBehaviour
    {
        [Tooltip("Walking speed for the CharacterController, Rigidbody and Transform movers. NavMeshAgent uses its own speed.")]
        public float moveSpeed = 3.5f;

        IPlayGoblinMover _mover;

        public IPlayGoblinMover Mover => _mover ??= CreateMover();

        public string MoverKind => Mover switch
        {
            NavMeshAgentMover _ => "NavMeshAgent",
            CharacterControllerMover _ => "CharacterController",
            RigidbodyMover _ => "Rigidbody",
            TransformMover _ => "Transform",
            _ => "Custom"
        };

        public Vector3 Position => transform.position;

        public string Diagnostics => Mover is IPlayGoblinDiagnostics d ? d.Describe() : "";

        /// <summary>World height of the bottom of the player's collider (or the pivot if it has none).</summary>
        public float FeetY => TryGetComponent(out Collider c) && c.enabled ? c.bounds.min.y : transform.position.y;

        /// <summary>Horizontal radius of the player's collider, 0.5 m when it has none.</summary>
        public float BodyRadius => TryGetComponent(out Collider c) && c.enabled ? Mathf.Max(0.1f, Mathf.Max(c.bounds.extents.x, c.bounds.extents.z)) : 0.5f;

        /// <summary>Move the player so its feet are at <paramref name="groundPosition"/>.</summary>
        public void Teleport(Vector3 groundPosition)
        {
            // NavMeshAgent.Warp already applies the agent's base offset.
            var p = Mover is NavMeshAgentMover ? groundPosition : groundPosition + Vector3.up * FootOffset();
            if (Mover is IPlayGoblinTeleporter t) t.Teleport(p);
            else transform.position = p;
        }

        /// <summary>Distance from the bottom of the player's collider up to its pivot.</summary>
        float FootOffset()
        {
            if (TryGetComponent(out Collider c) && c.enabled)
            {
                return Mathf.Max(0f, transform.position.y - c.bounds.min.y) + 0.05f;
            }
            return 0.05f;
        }

        public void StopMoving()
        {
            if (Mover is IPlayGoblinStoppable s) s.StopMoving();
        }

        IPlayGoblinMover CreateMover()
        {
            foreach (var mb in GetComponents<MonoBehaviour>())
            {
                if (mb is IPlayGoblinMover custom && !(mb is PlayGoblinPlayer)) return custom;
            }
            if (TryGetComponent(out NavMeshAgent agent)) return new NavMeshAgentMover(agent);
            if (TryGetComponent(out CharacterController cc)) return new CharacterControllerMover(cc, moveSpeed);
            if (TryGetComponent(out Rigidbody rb) && !rb.isKinematic) return new RigidbodyMover(rb, moveSpeed);
            return new TransformMover(transform, moveSpeed);
        }
    }
}
