using System.Collections.Generic;
using UnityEngine;

namespace AvatarLab
{
    // Independent positional two-bone solver. It deliberately does not claim VRChat IK parity.
    [DefaultExecutionOrder(100)]
    public sealed class BodySolver : MonoBehaviour
    {
        public Animator animator;
        public TrackingHub tracking;
        public bool Calibrated;
        public string Status = "Carregue um Humanoid e calibre em pé, braços abertos.";
        readonly Dictionary<string, Vector3> offsets = new Dictionary<string, Vector3>();
        readonly Dictionary<string, Quaternion> rotations = new Dictionary<string, Quaternion>();
        Vector3 headOffset;
        Quaternion headRotation;
        public void Calibrate()
        {
            Calibrated = false; offsets.Clear(); rotations.Clear();
            if (animator == null || !animator.isHuman || !tracking.HeadTracked) { Status = "É preciso um avatar Humanoid e headset rastreado."; return; }
            var h = animator.GetBoneTransform(HumanBodyBones.Head);
            if (h == null) { Status = "Avatar sem osso Head."; return; }
            float headsetHeight = tracking.head.position.y - tracking.origin.position.y;
            float avatarHeight = h.position.y - transform.position.y;
            if (headsetHeight < .5f || avatarHeight < .1f) { Status = "Altura inválida; configure o chão do SteamVR."; return; }
            float scale = Mathf.Clamp(headsetHeight / avatarHeight, .1f, 10);
            transform.localScale *= scale;
            transform.position += new Vector3(tracking.head.position.x - h.position.x, 0, tracking.head.position.z - h.position.z);
            headOffset = Quaternion.Inverse(tracking.head.rotation) * (h.position - tracking.head.position);
            headRotation = Quaternion.Inverse(tracking.head.rotation) * h.rotation;
            Capture("hand:left", tracking.leftHand, HumanBodyBones.LeftHand, tracking.LeftTracked);
            Capture("hand:right", tracking.rightHand, HumanBodyBones.RightHand, tracking.RightTracked);
            foreach (var p in tracking.points.Values)
            {
                var b = Bone(p.role);
                if (!p.Active || b == HumanBodyBones.LastBone) continue;
                var bone = animator.GetBoneTransform(b); if (bone == null) continue;
                offsets[p.id] = Quaternion.Inverse(p.rotation) * (bone.position - p.position);
                rotations[p.id] = Quaternion.Inverse(p.rotation) * bone.rotation;
            }
            Calibrated = true; Status = "Calibrado. IK experimental; recalibre após trocar avatar ou funções.";
        }
        void Capture(string id, Transform target, HumanBodyBones which, bool valid)
        {
            var bone = animator.GetBoneTransform(which); if (!valid || bone == null) return;
            offsets[id] = Quaternion.Inverse(target.rotation) * (bone.position - target.position);
            rotations[id] = Quaternion.Inverse(target.rotation) * bone.rotation;
        }
        static HumanBodyBones Bone(BodyRole role)
        {
            switch (role)
            {
                case BodyRole.Hips: return HumanBodyBones.Hips;
                case BodyRole.LeftFoot: return HumanBodyBones.LeftFoot;
                case BodyRole.RightFoot: return HumanBodyBones.RightFoot;
                case BodyRole.Chest: return HumanBodyBones.Chest;
                case BodyRole.LeftElbow: return HumanBodyBones.LeftLowerArm;
                case BodyRole.RightElbow: return HumanBodyBones.RightLowerArm;
                case BodyRole.LeftKnee: return HumanBodyBones.LeftLowerLeg;
                case BodyRole.RightKnee: return HumanBodyBones.RightLowerLeg;
                default: return HumanBodyBones.LastBone;
            }
        }
        TrackedPoint Role(BodyRole role)
        {
            foreach (var p in tracking.points.Values) if (p.role == role && p.Active && offsets.ContainsKey(p.id)) return p;
            return null;
        }
        Vector3 Position(TrackedPoint p) => p.position + p.rotation * offsets[p.id];
        void LateUpdate()
        {
            if(GetComponent<AvatarDefinition>()?.animator is Animator a && AvatarLayerControl.Hosts.TryGetValue(a,out var host) && host is AvatarDriver d && d.PoseOverride) return;
            if (!Calibrated || animator == null || !tracking.HeadTracked) return;
            var driver=GetComponent<AvatarDefinition>()!=null && AvatarLayerControl.Hosts.TryGetValue(animator,out var animationHost)?animationHost as AvatarDriver:null;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            var hip = Role(BodyRole.Hips);
            if(driver==null || driver.AllowsTracking("Hip"))
            {
                if (hip != null) { hips.position = Position(hip); hips.rotation = hip.rotation * rotations[hip.id]; }
                else hips.position += tracking.head.position + tracking.head.rotation * headOffset - head.position;
            }
            var chestPoint = Role(BodyRole.Chest);
            var chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (chestPoint != null && chest != null) chest.rotation = chestPoint.rotation * rotations[chestPoint.id];
            if(driver==null || driver.AllowsTracking("Head")) head.rotation = tracking.head.rotation * headRotation;
            if(driver==null || driver.AllowsTracking("LeftHand")) Hand("hand:left", tracking.leftHand, tracking.LeftTracked, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, Role(BodyRole.LeftElbow));
            if(driver==null || driver.AllowsTracking("RightHand")) Hand("hand:right", tracking.rightHand, tracking.RightTracked, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, Role(BodyRole.RightElbow));
            if(driver==null || driver.AllowsTracking("LeftFoot")) Foot(Role(BodyRole.LeftFoot), HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, Role(BodyRole.LeftKnee));
            if(driver==null || driver.AllowsTracking("RightFoot")) Foot(Role(BodyRole.RightFoot), HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, Role(BodyRole.RightKnee));
        }
        void Hand(string id, Transform target, bool valid, HumanBodyBones a, HumanBodyBones b, HumanBodyBones c, TrackedPoint hint)
        {
            if (valid && offsets.TryGetValue(id, out var offset)) Solve(animator.GetBoneTransform(a), animator.GetBoneTransform(b), animator.GetBoneTransform(c), target.position + target.rotation * offset, target.rotation * rotations[id], hint);
        }
        void Foot(TrackedPoint p, HumanBodyBones a, HumanBodyBones b, HumanBodyBones c, TrackedPoint hint)
        {
            if (p != null) Solve(animator.GetBoneTransform(a), animator.GetBoneTransform(b), animator.GetBoneTransform(c), Position(p), p.rotation * rotations[p.id], hint);
        }
        void Solve(Transform a, Transform b, Transform c, Vector3 target, Quaternion rotation, TrackedPoint hint)
        {
            if (a == null || b == null || c == null) return;
            float l1 = Vector3.Distance(a.position, b.position), l2 = Vector3.Distance(b.position, c.position);
            var direction = target - a.position;
            float distance = Mathf.Clamp(direction.magnitude, .001f, l1 + l2 - .0001f);
            if (direction.sqrMagnitude < 1e-8f || l1 < 1e-4f || l2 < 1e-4f) return;
            direction.Normalize();
            var bend = Vector3.ProjectOnPlane((hint != null ? Position(hint) : b.position) - a.position, direction);
            if (bend.sqrMagnitude < .00001f) bend = Vector3.ProjectOnPlane(transform.forward, direction);
            if (bend.sqrMagnitude < .00001f) bend = Vector3.ProjectOnPlane(transform.up, direction);
            bend.Normalize();
            float along = (l1 * l1 - l2 * l2 + distance * distance) / (2 * distance);
            var elbow = a.position + direction * along + bend * Mathf.Sqrt(Mathf.Max(0, l1 * l1 - along * along));
            a.rotation = Quaternion.FromToRotation(b.position - a.position, elbow - a.position) * a.rotation;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, target - b.position) * b.rotation;
            c.rotation = rotation;
        }
    }
}
