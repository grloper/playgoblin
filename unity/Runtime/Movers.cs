using UnityEngine;
using UnityEngine.AI;

namespace PlayGoblin
{
    /// <summary>
    /// Implement this on your player if PlayGoblin's built-in movers don't fit your controller.
    /// MoveTowards is called every frame while PlayGoblin wants the player to walk somewhere.
    /// </summary>
    public interface IPlayGoblinMover
    {
        void MoveTowards(Vector3 destination);
        void Jump();
    }

    /// <summary>Optional: implement to control how PlayGoblin respawns the player after a fall or a stuck spot.</summary>
    public interface IPlayGoblinTeleporter
    {
        void Teleport(Vector3 position);
    }

    /// <summary>Optional: implement so PlayGoblin can tell the player to stand still between moves.</summary>
    public interface IPlayGoblinStoppable
    {
        void StopMoving();
    }

    internal sealed class NavMeshAgentMover : IPlayGoblinMover, IPlayGoblinTeleporter, IPlayGoblinStoppable
    {
        readonly NavMeshAgent _agent;
        Vector3 _lastDest;
        bool _hasDest;

        public NavMeshAgentMover(NavMeshAgent agent) { _agent = agent; }

        public void MoveTowards(Vector3 destination)
        {
            if (!_agent.enabled || !_agent.isOnNavMesh) return;
            if (_agent.isStopped) _agent.isStopped = false;
            if (!_hasDest || (destination - _lastDest).sqrMagnitude > 0.01f)
            {
                _agent.SetDestination(destination);
                _lastDest = destination;
                _hasDest = true;
            }
        }

        public void Jump() { }

        public void StopMoving()
        {
            if (_agent.enabled && _agent.isOnNavMesh) _agent.isStopped = true;
            _hasDest = false;
        }

        public void Teleport(Vector3 position)
        {
            // An agent can only be (re)enabled close to a NavMesh, so move it there first.
            _agent.enabled = false;
            _agent.transform.position = position;
            _agent.enabled = true;
            if (_agent.isOnNavMesh) _agent.Warp(position);
            _hasDest = false;
        }
    }

    /// <summary>Optional: a short description of the controller's state, added to stuck reports.</summary>
    public interface IPlayGoblinDiagnostics
    {
        string Describe();
    }

    internal sealed class CharacterControllerMover : IPlayGoblinMover, IPlayGoblinTeleporter, IPlayGoblinDiagnostics
    {
        readonly CharacterController _cc;
        readonly float _speed;
        float _vy;
        bool _jump;
        CollisionFlags _flags;
        Vector3 _lastStep;

        public string Describe() =>
            $"flags={_flags} grounded={_cc.isGrounded} enabled={_cc.enabled} vy={_vy:0.##} step={_lastStep.x:0.###},{_lastStep.z:0.###}";

        public CharacterControllerMover(CharacterController cc, float speed) { _cc = cc; _speed = speed; }

        public void MoveTowards(Vector3 destination)
        {
            var t = _cc.transform;
            var to = destination - t.position;
            to.y = 0;
            var step = to.sqrMagnitude > 0.0001f ? to.normalized * _speed : Vector3.zero;
            if (step != Vector3.zero) t.rotation = Quaternion.LookRotation(step);

            if (_cc.isGrounded)
            {
                _vy = _jump ? 5f : -1f;
                _jump = false;
            }
            else
            {
                _vy += Physics.gravity.y * Time.deltaTime;
            }
            step.y = _vy;
            _lastStep = step;
            _flags = _cc.Move(step * Time.deltaTime);
        }

        public void Jump() { _jump = true; }

        public void Teleport(Vector3 position)
        {
            _cc.enabled = false;
            _cc.transform.position = position;
            _cc.enabled = true;
            _vy = 0;
        }
    }

    internal sealed class RigidbodyMover : IPlayGoblinMover, IPlayGoblinTeleporter
    {
        readonly Rigidbody _rb;
        readonly float _speed;

        public RigidbodyMover(Rigidbody rb, float speed) { _rb = rb; _speed = speed; }

        public void MoveTowards(Vector3 destination)
        {
            var to = destination - _rb.position;
            to.y = 0;
            var v = to.sqrMagnitude > 0.0001f ? to.normalized * _speed : Vector3.zero;
#if UNITY_6000_0_OR_NEWER
            v.y = _rb.linearVelocity.y;
            _rb.linearVelocity = v;
#else
            v.y = _rb.velocity.y;
            _rb.velocity = v;
#endif
        }

        public void Jump() { _rb.AddForce(Vector3.up * 5f, ForceMode.VelocityChange); }

        public void Teleport(Vector3 position)
        {
#if UNITY_6000_0_OR_NEWER
            _rb.linearVelocity = Vector3.zero;
#else
            _rb.velocity = Vector3.zero;
#endif
            _rb.position = position;
            _rb.transform.position = position;
        }
    }

    internal sealed class TransformMover : IPlayGoblinMover, IPlayGoblinTeleporter
    {
        readonly Transform _t;
        readonly float _speed;

        public TransformMover(Transform t, float speed) { _t = t; _speed = speed; }

        public void MoveTowards(Vector3 destination)
        {
            var target = new Vector3(destination.x, _t.position.y, destination.z);
            _t.position = Vector3.MoveTowards(_t.position, target, _speed * Time.deltaTime);
        }

        public void Jump() { }

        public void Teleport(Vector3 position) { _t.position = position; }
    }
}
