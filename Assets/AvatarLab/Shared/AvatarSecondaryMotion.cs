using System.Collections.Generic;
using UnityEngine;

namespace AvatarLab
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class AvatarSecondaryMotion : MonoBehaviour
    {
        [Tooltip("Sistema próprio experimental. Não interpreta configurações de PhysBones.")]
        public Transform root;
        [Range(0, 1)] public float stiffness = .35f;
        [Range(0, 1)] public float damping = .2f;
        public Vector3 gravity = new Vector3(0, -.2f, 0);
        public float radius = .015f;
        public SphereCollider[] colliders = new SphereCollider[0];
        sealed class Link { public Transform bone, child; public Vector3 tip, previous; public Quaternion animated; }
        readonly List<Link> links = new List<Link>();
        void Start()
        {
            var t = root != null ? root : transform;
            while (t != null && t.childCount == 1 && links.Count < 128)
            {
                var child = t.GetChild(0);
                links.Add(new Link { bone = t, child = child, tip = child.position, previous = child.position, animated = t.localRotation }); t = child;
            }
        }
        void Update() { foreach (var l in links) if (l.bone != null) l.bone.localRotation = l.animated; }
        void LateUpdate()
        {
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            if (dt <= 0) return;
            foreach (var l in links)
            {
                if (l.bone == null || l.child == null) continue;
                l.animated = l.bone.localRotation;
                var rest = l.child.position; float length = Vector3.Distance(l.bone.position, rest);
                var old = l.tip;
                l.tip += (l.tip - l.previous) * Mathf.Pow(1 - damping, dt * 60) + gravity * dt * dt;
                l.tip = Vector3.Lerp(l.tip, rest, 1 - Mathf.Pow(1 - stiffness, dt * 60));
                foreach (var c in colliders)
                {
                    if (c == null || !c.enabled) continue;
                    var center = c.transform.TransformPoint(c.center);
                    var scale = c.transform.lossyScale;
                    float r = c.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)) + radius;
                    var delta = l.tip - center;
                    if (delta.sqrMagnitude < r * r) l.tip = center + (delta.sqrMagnitude > 1e-8f ? delta.normalized : Vector3.up) * r;
                }
                var direction = l.tip - l.bone.position;
                if (direction.sqrMagnitude > 1e-8f && length > 1e-5f)
                {
                    l.tip = l.bone.position + direction.normalized * length;
                    l.bone.rotation = Quaternion.FromToRotation(rest - l.bone.position, direction) * l.bone.rotation;
                }
                l.previous = old;
            }
        }
        void OnDisable() { foreach (var l in links) if (l.bone != null) l.bone.localRotation = l.animated; }
    }
}
