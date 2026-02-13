using System.Collections.Generic;
using UnityEngine;

namespace Gironoid._Project.Code.Gameplay
{
    public sealed class SimplePool
    {
        private readonly Stack<GameObject> _stack = new Stack<GameObject>(64);
        private readonly GameObject _prefab;
        private readonly Transform _root;

        public SimplePool(GameObject prefab, Transform root, int preload)
        {
            _prefab = prefab;
            _root = root;

            if (_prefab != null && preload > 0)
                Preload(preload);
        }

        public void Preload(int count)
        {
            if (_prefab == null) return;

            for (int i = 0; i < count; i++)
            {
                var go = Object.Instantiate(_prefab, _root);
                go.SetActive(false);
                _stack.Push(go);
            }
        }

        public GameObject Spawn(Vector3 position, Quaternion rotation)
        {
            if (_prefab == null) return null;

            GameObject go;
            if (_stack.Count > 0)
            {
                go = _stack.Pop();
                if (go == null)
                    go = Object.Instantiate(_prefab, _root);
            }
            else
            {
                go = Object.Instantiate(_prefab, _root);
            }

            go.transform.SetPositionAndRotation(position, rotation);
            go.SetActive(true);
            return go;
        }

        public void Despawn(GameObject go)
        {
            if (go == null) return;

            go.SetActive(false);
            go.transform.SetParent(_root, worldPositionStays: false);
            _stack.Push(go);
        }

        public void Clear()
        {
            while (_stack.Count > 0)
            {
                var go = _stack.Pop();
                if (go != null)
                    Object.Destroy(go);
            }
        }
    }
}