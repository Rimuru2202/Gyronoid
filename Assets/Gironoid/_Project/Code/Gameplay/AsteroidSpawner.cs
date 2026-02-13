using System;
using UnityEngine;

namespace Gironoid._Project.Code.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class AsteroidSpawner : MonoBehaviour
    {
        [Header("Prefabs")]
        [SerializeField] private GameObject _asteroidPrefab;

        [Header("Spawn area (world)")]
        [SerializeField] private float _spawnY = 6.5f;
        [SerializeField] private float _minX = -4.5f;
        [SerializeField] private float _maxX = 4.5f;

        [Header("Base difficulty")]
        [SerializeField] private float _spawnInterval = 0.55f;
        [SerializeField] private float _spawnIntervalMin = 0.22f;

        [SerializeField] private float _hpBase = 3f;
        [SerializeField] private float _hpGrowthPerSecond = 0.015f;

        [SerializeField] private float _speedBase = 3.3f;
        [SerializeField] private float _speedGrowthPerSecond = 0.02f;

        [Header("Pool")]
        [SerializeField] private int _preload = 28;

        private SimplePool _pool;
        private Transform _poolRoot;

        private bool _running;
        private float _time;
        private float _nextSpawnAt;

        private Action<Asteroid, Asteroid.AsteroidDespawnReason> _onDespawn;

        public void Configure(Action<Asteroid, Asteroid.AsteroidDespawnReason> onDespawn)
        {
            _onDespawn = onDespawn;
        }

        public void SetRunning(bool on)
        {
            _running = on;
            if (!on)
            {
                _time = 0f;
                _nextSpawnAt = 0f;
            }
        }

        private void Awake()
        {
            if (_asteroidPrefab == null)
            {
                Debug.LogError("AsteroidSpawner: Asteroid prefab is not assigned.", this);
                enabled = false;
                return;
            }

            _poolRoot = new GameObject("AsteroidPool").transform;
            _poolRoot.SetParent(transform, false);

            _pool = new SimplePool(_asteroidPrefab, _poolRoot, _preload);
        }

        private void Update()
        {
            if (!_running)
                return;

            _time += Time.deltaTime;

            if (Time.time >= _nextSpawnAt)
            {
                SpawnOne();
                var interval = Mathf.Max(_spawnIntervalMin, _spawnInterval - (_time * 0.0025f));
                _nextSpawnAt = Time.time + interval;
            }
        }

        private void SpawnOne()
        {
            var x = UnityEngine.Random.Range(_minX, _maxX);
            var pos = new Vector3(x, _spawnY, 0f);

            var go = _pool.Spawn(pos, Quaternion.identity);
            if (go == null)
                return;

            var a = go.GetComponent<Asteroid>();
            if (a == null)
            {
                Debug.LogError("AsteroidSpawner: Asteroid component missing on prefab.", this);
                _pool.Despawn(go);
                return;
            }

            var hp = _hpBase + _time * _hpGrowthPerSecond;
            var spd = _speedBase + _time * _speedGrowthPerSecond;

            a.Activate(hp, spd, OnAsteroidDespawned);
        }

        private void OnAsteroidDespawned(Asteroid asteroid, Asteroid.AsteroidDespawnReason reason)
        {
            if (asteroid == null)
                return;

            _onDespawn?.Invoke(asteroid, reason);

            _pool.Despawn(asteroid.gameObject);
        }
    }
}
