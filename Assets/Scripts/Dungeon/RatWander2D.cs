using UnityEngine;
using UnityEngine.EventSystems;
using SeekerDungeon.Audio;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SeekerDungeon.Dungeon
{
    /// <summary>
    /// Simple top-down rat wander: short burst moves + short pauses
    /// within a rectangular roam area.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
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
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Sprite deadSprite;
        [SerializeField] private Material deadMaterial;
        [SerializeField] private Camera inputCamera;
        [SerializeField] private Vector2 randomSfxIntervalSeconds = new(4.5f, 11f);
        [Range(0f, 1f)]
        [SerializeField] private float randomSfxChance = 0.6f;

        private Vector3 _roamCenter;
        private MoveState _state;
        private float _stateTimer;
        private float _moveSpeed;
        private Vector3 _moveTarget;
        private bool _isDead;
        private float _nextRandomSfxAt;
        private Collider2D _clickCollider;
        public event System.Action<RatWander2D, Vector3> Killed;

        private void Awake()
        {
            if (visualRoot == null)
            {
                visualRoot = transform;
            }

            if (spriteRenderer == null)
            {
                spriteRenderer = visualRoot.GetComponentInChildren<SpriteRenderer>();
                if (spriteRenderer == null)
                {
                    spriteRenderer = GetComponentInChildren<SpriteRenderer>();
                }
            }

            _clickCollider = GetComponent<Collider2D>();
            if (inputCamera == null)
            {
                inputCamera = Camera.main;
            }
        }

        private void OnEnable()
        {
            _roamCenter = transform.position;
            ScheduleNextRandomSfx();
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
            if (_isDead)
            {
                return;
            }

            TickRandomRatSfx();
            TryHandleTapOrClick();

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

        public void KillRat()
        {
            SetDead(true);
        }

        public void SetDead(bool notifyListeners)
        {
            if (_isDead)
            {
                return;
            }

            _isDead = true;
            _state = MoveState.Pausing;
            _stateTimer = 0f;

            if (spriteRenderer != null)
            {
                if (deadSprite != null)
                {
                    spriteRenderer.sprite = deadSprite;
                }

                if (deadMaterial != null)
                {
                    spriteRenderer.sharedMaterial = deadMaterial;
                }
            }

            if (notifyListeners)
            {
                Killed?.Invoke(this, transform.position);
            }
        }

        private void TickRandomRatSfx()
        {
            if (Time.unscaledTime < _nextRandomSfxAt)
            {
                return;
            }

            if (UnityEngine.Random.value <= randomSfxChance)
            {
                GameAudioManager.Instance?.PlayWorld(WorldSfxId.RatOneShot, transform.position);
            }

            ScheduleNextRandomSfx();
        }

        private void ScheduleNextRandomSfx()
        {
            var min = Mathf.Max(0.2f, Mathf.Min(randomSfxIntervalSeconds.x, randomSfxIntervalSeconds.y));
            var max = Mathf.Max(min, Mathf.Max(randomSfxIntervalSeconds.x, randomSfxIntervalSeconds.y));
            _nextRandomSfxAt = Time.unscaledTime + UnityEngine.Random.Range(min, max);
        }

        private void TryHandleTapOrClick()
        {
            if (_clickCollider == null)
            {
                return;
            }

#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null)
            {
                var touches = Touchscreen.current.touches;
                for (var index = 0; index < touches.Count; index += 1)
                {
                    var touch = touches[index];
                    if (!touch.press.wasReleasedThisFrame)
                    {
                        continue;
                    }

                    var touchId = touch.touchId.ReadValue();
                    if (IsPointerOverUi(touchId))
                    {
                        continue;
                    }

                    if (IsScreenPointOnRat(touch.position.ReadValue()))
                    {
                        KillRat();
                        return;
                    }
                }
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                if (IsPointerOverUi(-1))
                {
                    return;
                }

                if (IsScreenPointOnRat(Mouse.current.position.ReadValue()))
                {
                    KillRat();
                }
            }
#else
            if (Input.touchCount > 0)
            {
                for (var index = 0; index < Input.touchCount; index += 1)
                {
                    var touch = Input.GetTouch(index);
                    if (touch.phase != TouchPhase.Ended)
                    {
                        continue;
                    }

                    if (IsPointerOverUi(touch.fingerId))
                    {
                        continue;
                    }

                    if (IsScreenPointOnRat(touch.position))
                    {
                        KillRat();
                        return;
                    }
                }
            }

            if (Input.GetMouseButtonUp(0))
            {
                if (IsPointerOverUi(-1))
                {
                    return;
                }

                if (IsScreenPointOnRat(Input.mousePosition))
                {
                    KillRat();
                }
            }
#endif
        }

        private bool IsScreenPointOnRat(Vector2 screenPosition)
        {
            if (inputCamera == null)
            {
                inputCamera = Camera.main;
                if (inputCamera == null)
                {
                    return false;
                }
            }

            var worldPosition = inputCamera.ScreenToWorldPoint(screenPosition);
            var worldPoint2D = new Vector2(worldPosition.x, worldPosition.y);
            return _clickCollider.OverlapPoint(worldPoint2D);
        }

        private static bool IsPointerOverUi(int pointerId)
        {
            if (EventSystem.current == null)
            {
                return false;
            }

            return pointerId < 0
                ? EventSystem.current.IsPointerOverGameObject()
                : EventSystem.current.IsPointerOverGameObject(pointerId);
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
