using Cysharp.Threading.Tasks;
using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    public sealed class LocalPlayerJobMover : MonoBehaviour
    {
        [SerializeField] private float moveDurationSeconds = 0.22f;
        [SerializeField] private bool keepZPosition = true;
        [SerializeField] private bool keepUprightRotation = true;

        private int _moveVersion;

        public void MoveTo(Vector3 worldPosition)
        {
            _moveVersion += 1;
            MoveToAsync(worldPosition, _moveVersion).Forget();
        }

        private async UniTaskVoid MoveToAsync(Vector3 targetPosition, int moveVersion)
        {
            var startPosition = transform.position;
            if (keepZPosition)
            {
                targetPosition.z = startPosition.z;
            }

            var duration = Mathf.Max(0.01f, moveDurationSeconds);
            var elapsed = 0f;
            while (elapsed < duration)
            {
                if (moveVersion != _moveVersion)
                {
                    return;
                }

                elapsed += Time.unscaledDeltaTime;
                var progress = Mathf.Clamp01(elapsed / duration);
                transform.position = Vector3.Lerp(startPosition, targetPosition, progress);
                if (keepUprightRotation)
                {
                    transform.rotation = Quaternion.identity;
                }

                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            if (moveVersion == _moveVersion)
            {
                transform.position = targetPosition;
                if (keepUprightRotation)
                {
                    transform.rotation = Quaternion.identity;
                }
            }
        }
    }
}
