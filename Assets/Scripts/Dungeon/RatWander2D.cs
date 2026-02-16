using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    /// <summary>
    /// Simple top-down rat wander: short burst moves + short pauses
    /// within a rectangular roam area.
    /// </summary>
    public sealed class RatWander2D : MonoBehaviour
    {
        private enum MoveState
        {
            Pausing,
            Moving
        }

        [SerializeField] private Transform visualRoot;
        [SerializeField] private Vector2 roamAreaSize = new(2.7f, 2.7f);
        [SerializeField] private Vector2 moveDurationRange = new(0.22f, 0.68f);
        [SerializeField] private Vector2 pauseDurationRange = new(0.18f, 0.9f);
        [SerializeField] private Vector2 speedRange = new(1.3f, 2.6f);
        [SerializeField] private bool rotateToMoveDirection = true;
        [SerializeField] private float rotationOffsetDegrees;
        [SerializeField] private bool flipXByMoveDirection;

        private Vector3 _roamCenter;
        private MoveState _state;
        private float _stateTimer;
        private float _moveSpeed;
        private Vector3 _moveTarget;

        private void Awake()
        {
            if (visualRoot == null)
            {
                visualRoot = transform;
            }
        }

        private void OnEnable()
        {
            _roamCenter = transform.position;
            EnterPause();
        }

        public void SetRoamArea(Vector3 centerWorldPosition, Vector2 size)
        {
            _roamCenter = centerWorldPosition;
            roamAreaSize = size;
            if (_state == MoveState.Moving)
            {
                _moveTarget = PickTargetPosition();
            }
        }

        private void Update()
        {
            _stateTimer -= Time.deltaTime;
            if (_state == MoveState.Pausing)
            {
                if (_stateTimer <= 0f)
                {
                    EnterMove();
                }
                return;
            }

            var toTarget = _moveTarget - transform.position;
            var step = _moveSpeed * Time.deltaTime;
            if (toTarget.sqrMagnitude <= step * step)
            {
                transform.position = _moveTarget;
                EnterPause();
                return;
            }

            if (_stateTimer <= 0f)
            {
                EnterPause();
                return;
            }

            var dir = toTarget.normalized;
            transform.position += dir * step;
            ApplyFacing(dir);
        }

        private void EnterPause()
        {
            _state = MoveState.Pausing;
            _stateTimer = RandomRange(pauseDurationRange);
        }

        private void EnterMove()
        {
            _state = MoveState.Moving;
            _stateTimer = RandomRange(moveDurationRange);
            _moveSpeed = RandomRange(speedRange);
            _moveTarget = PickTargetPosition();

            var moveDir = (_moveTarget - transform.position).normalized;
            if (moveDir.sqrMagnitude > 0.0001f)
            {
                ApplyFacing(moveDir);
            }
        }

        private Vector3 PickTargetPosition()
        {
            var halfX = Mathf.Max(0.01f, roamAreaSize.x * 0.5f);
            var halfY = Mathf.Max(0.01f, roamAreaSize.y * 0.5f);

            // Use random ranges for natural movement.
            var x = _roamCenter.x + Random.Range(-halfX, halfX);
            var y = _roamCenter.y + Random.Range(-halfY, halfY);
            return new Vector3(x, y, transform.position.z);
        }

        private void ApplyFacing(Vector3 moveDirection)
        {
            if (visualRoot == null || moveDirection.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            if (rotateToMoveDirection)
            {
                var angle = Mathf.Atan2(moveDirection.y, moveDirection.x) * Mathf.Rad2Deg + rotationOffsetDegrees;
                visualRoot.rotation = Quaternion.Euler(0f, 0f, angle);
            }

            if (flipXByMoveDirection)
            {
                var scale = visualRoot.localScale;
                var absX = Mathf.Abs(scale.x);
                scale.x = moveDirection.x >= 0f ? absX : -absX;
                visualRoot.localScale = scale;
            }
        }

        private float RandomRange(Vector2 range)
        {
            var min = Mathf.Min(range.x, range.y);
            var max = Mathf.Max(range.x, range.y);
            return Random.Range(min, max);
        }
    }
}
