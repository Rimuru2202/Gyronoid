using UnityEngine;

namespace Gironoid._Project.Code.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class PlayerShipController2D : MonoBehaviour
    {
        [Header("Movement (base)")]
        [SerializeField] private float _baseMaxSpeed = 6.0f;
        [SerializeField] private float _baseAcceleration = 16.0f;

        [Header("Clamp")]
        [SerializeField] private Camera _camera;
        [SerializeField] private float _padding = 0.45f;

        private Vector2 _vel;
        private float _maxSpeed;
        private float _accel;

        private bool _running;

        public void SetRunning(bool on)
        {
            _running = on;
            if (!on) _vel = Vector2.zero;
        }

        public void SetStats(float maxSpeed, float accel)
        {
            _maxSpeed = Mathf.Max(0.1f, maxSpeed);
            _accel = Mathf.Max(0.1f, accel);
        }

        private void Reset()
        {
            _camera = Camera.main;
        }

        private void Awake()
        {
            if (_camera == null)
                _camera = Camera.main;

            _maxSpeed = _baseMaxSpeed;
            _accel = _baseAcceleration;
        }

        private void Update()
        {
            if (!_running)
                return;

            var ix = Input.GetAxisRaw("Horizontal");
            var iy = Input.GetAxisRaw("Vertical");

            var input = new Vector2(ix, iy);
            if (input.sqrMagnitude > 1f)
                input.Normalize();

            var target = input * _maxSpeed;
            _vel = Vector2.MoveTowards(_vel, target, _accel * Time.deltaTime);

            var pos = (Vector2)transform.position + _vel * Time.deltaTime;

            transform.position = ClampToCamera(pos);
        }

        private Vector2 ClampToCamera(Vector2 pos)
        {
            if (_camera == null)
                return pos;

            if (!_camera.orthographic)
                return pos;

            var halfH = _camera.orthographicSize;
            var halfW = halfH * _camera.aspect;

            var minX = _camera.transform.position.x - halfW + _padding;
            var maxX = _camera.transform.position.x + halfW - _padding;

            var minY = _camera.transform.position.y - halfH + _padding;
            var maxY = _camera.transform.position.y + halfH - _padding;

            pos.x = Mathf.Clamp(pos.x, minX, maxX);
            pos.y = Mathf.Clamp(pos.y, minY, maxY);

            return pos;
        }
    }
}
