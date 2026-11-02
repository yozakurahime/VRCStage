using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using Valve.VR;
using UnityEngine.InputSystem;
using CommonUsages = UnityEngine.XR.CommonUsages;
using InputDevices = UnityEngine.XR.InputDevices;

namespace AvatarLab
{
    public enum BodyRole { None, Hips, LeftFoot, RightFoot, Chest, LeftElbow, RightElbow, LeftKnee, RightKnee }
    public sealed class TrackedPoint
    {
        public string id, label;
        public BodyRole role;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public float lastSeen = -100;
        public bool Active => Time.unscaledTime - lastSeen < .5f;
    }
    public sealed class TrackingHub : MonoBehaviour
    {
        public readonly Dictionary<string, TrackedPoint> points = new Dictionary<string, TrackedPoint>();
        public bool VrActive => XRSettings.isDeviceActive;
        public string Status = "Desktop. Inicie SteamVR antes de ativar VR.";
        public string TrackerStatus = "Trackers SteamVR desligados.";
        public bool Starting;
        public Transform origin, head, leftHand, rightHand;
        public bool HeadTracked, LeftTracked, RightTracked;
        public float LeftGrip, RightGrip;
        CVRSystem system;
        bool ownsOpenVr;
        readonly TrackedDevicePose_t[] poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        UnityEngine.InputSystem.InputActionMap inputActions;
        void Awake()
        {
            inputActions = new UnityEngine.InputSystem.InputActionMap("AvatarLab tracking");
            foreach (var binding in new[] { "<XRHMD>/centerEyePosition", "<XRHMD>/centerEyeRotation", "<XRController>{LeftHand}/devicePosition", "<XRController>{LeftHand}/deviceRotation", "<XRController>{RightHand}/devicePosition", "<XRController>{RightHand}/deviceRotation", "<XRController>{LeftHand}/grip", "<XRController>{RightHand}/grip" })
                inputActions.AddAction("tracking" + inputActions.actions.Count, UnityEngine.InputSystem.InputActionType.Value, binding);
            inputActions.Enable();
        }
        public IEnumerator StartVr()
        {
            if (Starting || VrActive) yield break;
            Starting = true; Status = "Inicializando OpenXR...";
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager == null) { Status = "OpenXR não configurado neste build."; Starting = false; yield break; }
            yield return manager.InitializeLoader();
            if (manager.activeLoader != null)
            {
                origin.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                manager.StartSubsystems();
                var subsystems = new List<XRInputSubsystem>(); SubsystemManager.GetInstances(subsystems);
                foreach (var subsystem in subsystems) subsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                Status = "OpenXR iniciado. Verifique o headset.";
            }
            else Status = "OpenXR indisponível. Abra SteamVR e defina-o como runtime OpenXR ativo.";
            Starting = false;
        }
        public void StartTrackers()
        {
            if (system != null) return;
            try
            {
                var error = EVRInitError.None;
                system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Background);
                ownsOpenVr = system != null && error == EVRInitError.None;
                TrackerStatus = ownsOpenVr ? "SteamVR conectado; aguardando trackers válidos." : "SteamVR: " + error;
            }
            catch (Exception ex) { TrackerStatus = ex.Message; }
        }
        void Update()
        {
            HeadTracked = ReadNode(XRNode.Head, head, out _);
            LeftTracked = ReadNode(XRNode.LeftHand, leftHand, out LeftGrip);
            RightTracked = ReadNode(XRNode.RightHand, rightHand, out RightGrip);
            if (system == null) return;
            try
            {
                system.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0, poses);
                int count = 0;
                for (uint i = 0; i < poses.Length; i++)
                {
                    if (system.GetTrackedDeviceClass(i) != ETrackedDeviceClass.GenericTracker || !poses[i].bDeviceIsConnected || !poses[i].bPoseIsValid) continue;
                    var error = ETrackedPropertyError.TrackedProp_Success;
                    var serial = new StringBuilder(256);
                    system.GetStringTrackedDeviceProperty(i, ETrackedDeviceProperty.Prop_SerialNumber_String, serial, 256, ref error);
                    string id = "steam:" + (serial.Length > 0 ? serial.ToString() : i.ToString());
                    var m = poses[i].mDeviceToAbsoluteTracking;
                    // OpenVR right-handed coordinates to Unity left-handed coordinates.
                    var pos = new Vector3(m.m3, m.m7, -m.m11);
                    var forward = new Vector3(-m.m2, -m.m6, m.m10);
                    var up = new Vector3(m.m1, m.m5, -m.m9);
                    SetPoint(id, serial.ToString(), pos, Quaternion.LookRotation(forward, up)); count++;
                }
                TrackerStatus = "SteamVR: " + count + " tracker(s) com pose válida.";
            }
            catch (Exception ex) { TrackerStatus = ex.Message; }
        }
        bool ReadNode(XRNode node, Transform target, out float grip)
        {
            grip = 0; var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid || !device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked) return false;
            if (!device.TryGetFeatureValue(CommonUsages.devicePosition, out var position) || !device.TryGetFeatureValue(CommonUsages.deviceRotation, out var rotation)) return false;
            target.localPosition = position; target.localRotation = rotation;
            device.TryGetFeatureValue(CommonUsages.grip, out grip); return true;
        }
        public void SetPoint(string id, string label, Vector3 position, Quaternion rotation)
        {
            if (!points.TryGetValue(id, out var p))
            {
                p = new TrackedPoint { id = id, label = label, role = (BodyRole)PlayerPrefs.GetInt("role:" + id, 0) }; points[id] = p;
            }
            p.position = origin.TransformPoint(position); p.rotation = origin.rotation * rotation; p.lastSeen = Time.unscaledTime;
        }
        public void Assign(TrackedPoint point, BodyRole role)
        {
            foreach (var other in points.Values)
                if (role != BodyRole.None && other.role == role) { other.role = BodyRole.None; PlayerPrefs.SetInt("role:" + other.id, 0); }
            point.role = role; PlayerPrefs.SetInt("role:" + point.id, (int)role); PlayerPrefs.Save();
        }
        void OnDestroy()
        {
            inputActions?.Dispose();
            if (ownsOpenVr) OpenVR.Shutdown();
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager != null && manager.activeLoader != null) { manager.StopSubsystems(); manager.DeinitializeLoader(); }
        }
    }
}
