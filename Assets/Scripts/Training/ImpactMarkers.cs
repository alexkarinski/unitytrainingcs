using System.Collections.Generic;
using CsAim.Core;
using UnityEngine;

namespace CsAim.Training
{
    /// <summary>
    /// sv_showimpacts equivalent: a small square at every bullet impact that lives for
    /// sv_showimpacts_time seconds (default 4). Red = where the bullet hit, yellow = an accurate
    /// first shot. Like the game, markers stay in the world even if the target moves on.
    /// </summary>
    public sealed class ImpactMarkers : MonoBehaviour
    {
        public bool Enabled = true;
        public float Lifetime = 4f;           // sv_showimpacts_time
        public float Size = 2.2f;             // source units

        struct Marker { public GameObject go; public float dieAt; }
        readonly Queue<Marker> _live = new Queue<Marker>();
        readonly Stack<GameObject> _pool = new Stack<GameObject>();
        Material _red, _firstShot;

        void Awake()
        {
            _red = Materials.Unlit(new Color(1f, 0.15f, 0.1f));
            _firstShot = Materials.Unlit(new Color(1f, 0.85f, 0.1f));
        }

        public void Add(Vector3 point, Vector3 normal, bool firstShot)
        {
            if (!Enabled) return;

            GameObject go = _pool.Count > 0 ? _pool.Pop() : CreateQuad();
            go.SetActive(true);
            go.transform.position = point + normal * 0.15f;
            go.transform.rotation = Quaternion.LookRotation(-normal);
            go.transform.localScale = Vector3.one * Size;
            go.GetComponent<Renderer>().sharedMaterial = firstShot ? _firstShot : _red;
            _live.Enqueue(new Marker { go = go, dieAt = Time.time + Lifetime });
        }

        public void Clear()
        {
            while (_live.Count > 0) Recycle(_live.Dequeue().go);
        }

        GameObject CreateQuad()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Impact";
            go.layer = 2;   // Ignore Raycast
            DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        void Recycle(GameObject go)
        {
            go.SetActive(false);
            _pool.Push(go);
        }

        void Update()
        {
            while (_live.Count > 0 && _live.Peek().dieAt <= Time.time)
                Recycle(_live.Dequeue().go);
        }
    }
}
