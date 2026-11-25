using UnityEngine;

namespace Monlit
{
    [RequireComponent(typeof(Camera))]
    public sealed class WorkshopCamera : MonoBehaviour
    {
        private const float WorkingHalfHeight = 5.4f;
        [SerializeField, Min(0.1f)] private float minimumHalfHeight = 5.25f;
        [SerializeField, Min(0.1f)] private float minimumHalfWidth = 8.4f;

        private Camera workshopCamera;
        private WorkshopRobot robot;
        private Vector3 framedPosition;

        public void FrameDeck(float width, float depth, WorkshopRobot subject = null)
        {
            Vector2 halfDeck = new Vector2(width, depth) * .5f;
            minimumHalfWidth = halfDeck.x + 1.1f;
            minimumHalfHeight = Mathf.Abs(transform.forward.y) * halfDeck.y + 1.1f;
            // Back the lens away from the near edge on deep tables.
            float distance = Mathf.Max(15f, halfDeck.y * Mathf.Abs(transform.forward.z) + 6f);
            framedPosition = Vector3.up * .1f - transform.forward * distance;
            workshopCamera.farClipPlane = distance + (halfDeck.y + 35f) * Mathf.Abs(transform.forward.z) + 8f;
            robot = subject;
            FrameWorkshop();
        }

        public void ShowOverview()
        {
            robot = null;
            FrameWorkshop();
        }

        private void Awake()
        {
            workshopCamera = GetComponent<Camera>();
            framedPosition = transform.position;
            FrameWorkshop();
        }

        private void LateUpdate() => FrameWorkshop();

        private void FrameWorkshop()
        {
            if (!workshopCamera || workshopCamera.aspect <= 0f) return;
            bool following = robot && robot.gameObject.activeInHierarchy;
            float size = following ? WorkingHalfHeight : Mathf.Max(minimumHalfHeight, minimumHalfWidth / workshopCamera.aspect);
            workshopCamera.orthographicSize = size;
            Vector3 at = framedPosition;
            if (following)
            {
                float xRange = Mathf.Max(0f, minimumHalfWidth - size * workshopCamera.aspect);
                float zRange = Mathf.Max(0f, (minimumHalfHeight - size) / Mathf.Abs(transform.forward.y));
                at.x += Mathf.Clamp(robot.transform.position.x, -xRange, xRange);
                at.z += Mathf.Clamp(robot.transform.position.z, -zRange, zRange);
            }
            transform.position = at;
        }
    }
}
