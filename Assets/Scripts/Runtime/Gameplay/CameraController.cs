using UnityEngine;

namespace Embervale.Client
{
    /// <summary>
    /// Elevated isometric-style follow camera. Q/E or middle-drag rotates around the player, the wheel zooms,
    /// arrow keys / W-S tilt a little. The camera is pulled in when terrain or a building wall would block it.
    /// </summary>
    public sealed class CameraController : MonoBehaviour
    {
        public float Yaw = 45f;
        public float Pitch = 52f;
        public float Distance = 18f;
        public float MinDistance = 7f, MaxDistance = 32f;
        public float MinPitch = 35f, MaxPitch = 72f;

        private GameSession _session;
        private Camera _cam;
        private Vector3 _focus;
        private float _targetDistance;
        private Vector2 _lastMouse;
        private bool _hasFocus;

        public Camera Cam => _cam;

        public void Init(GameSession session)
        {
            _session = session;
            _cam = GetComponent<Camera>();
            _cam.nearClipPlane = 0.3f;
            _cam.farClipPlane = 220f;
            _cam.fieldOfView = 38f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _targetDistance = Distance;
            _hasFocus = false;
        }

        private void LateUpdate()
        {
            if (_session == null || _session.State != ClientState.InWorld) return;
            var self = _session.SelfView;
            if (self == null) return;

            bool uiTyping = UIRoot.IsTyping;
            float dt = Time.unscaledDeltaTime;
            float sens = Settings.CameraSensitivity;
            if (!uiTyping)
            {
                if (InputAdapter.Held(InputAdapter.Key.Q) || InputAdapter.Held(InputAdapter.Key.Left)) Yaw += 90f * dt * sens;
                if (InputAdapter.Held(InputAdapter.Key.E) || InputAdapter.Held(InputAdapter.Key.Right)) Yaw -= 90f * dt * sens;
                if (InputAdapter.Held(InputAdapter.Key.Up)) Pitch += 40f * dt * sens;
                if (InputAdapter.Held(InputAdapter.Key.Down)) Pitch -= 40f * dt * sens;
            }
            var mouse = InputAdapter.MousePosition;
            if (InputAdapter.MouseHeld(2))
            {
                var d = mouse - _lastMouse;
                Yaw += d.x * 0.25f * sens;
                Pitch -= d.y * 0.15f * sens;
            }
            _lastMouse = mouse;
            if (!UIRoot.PointerOverUI)
            {
                float s = InputAdapter.Scroll;
                if (Mathf.Abs(s) > 0.01f) _targetDistance = Mathf.Clamp(_targetDistance * (1f - s * 0.12f), MinDistance, MaxDistance);
            }
            Pitch = Mathf.Clamp(Pitch, MinPitch, MaxPitch);
            Distance = Mathf.Lerp(Distance, _targetDistance, 1f - Mathf.Exp(-10f * dt));

            var target = self.transform.position + Vector3.up * 1.2f;
            _focus = _hasFocus ? Vector3.Lerp(_focus, target, 1f - Mathf.Exp(-12f * dt)) : target;
            _hasFocus = true;

            var rot = Quaternion.Euler(Pitch, Yaw, 0);
            var dir = rot * Vector3.back;
            float dist = Distance;
            // Keep the camera above the ground and outside walls.
            if (Physics.SphereCast(_focus, 0.4f, dir, out var hit, Distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<EntityView>() == null) dist = Mathf.Max(MinDistance * 0.5f, hit.distance - 0.3f);
            }
            var pos = _focus + dir * dist;
            if (_session.World != null)
            {
                float ground = _session.World.Terrain.SampleHeight(pos.x, pos.z) + 1.0f;
                if (pos.y < ground) pos.y = ground;
            }
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(_focus - pos));
        }

        /// <summary>Direction the camera faces flattened on the ground (used by the minimap compass).</summary>
        public float Heading => Yaw;
    }
}
