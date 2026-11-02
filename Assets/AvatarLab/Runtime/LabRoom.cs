using UnityEngine;

namespace AvatarLab
{
    public sealed class LabRoom : MonoBehaviour
    {
        public Camera view;
        public Transform orbit;
        public Light key, fill, rim;
        public Material litTemplate, mirrorTemplate;
        public float yaw = 160, pitch = 8, distance = 3.5f;
        public Vector3 focus = new Vector3(0, 1.1f, 0);
        public bool gameLook, clampToRoom = true;
        Camera mirrorCamera;
        RenderTexture mirrorTexture;
        Material mirrorMaterial;
        bool mirrorEnabled = true;
        const float MirrorZ = 2.8f;
        public void Create()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.28f, .31f, .38f);
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
            Object("Floor", PrimitiveType.Cube, new Vector3(0, -.08f, 0), new Vector3(12, .15f, 12), new Color(.065f, .08f, .105f));
            for (int i = -6; i <= 6; i++)
            {
                Object("Grid", PrimitiveType.Cube, new Vector3(i, .002f, 0), new Vector3(.009f, .004f, 12), new Color(.13f, .19f, .23f));
                Object("Grid", PrimitiveType.Cube, new Vector3(0, .002f, i), new Vector3(12, .004f, .009f), new Color(.13f, .19f, .23f));
            }
            Object("Wall", PrimitiveType.Cube, new Vector3(0, 2, 3), new Vector3(12, 4, .1f), new Color(.075f, .095f, .13f));
            for (int i = -1; i <= 1; i += 2) Object("Accent", PrimitiveType.Cube, new Vector3(i * 2.3f, 1.5f, 2.85f), new Vector3(.03f, 2.8f, .02f), new Color(.1f, .8f, .85f), true);
            key = Lamp("Key", new Vector3(-2, 3, -2), new Color(1, .9f, .8f), 1.3f);
            fill = Lamp("Fill", new Vector3(2, 2, -1), new Color(.6f, .8f, 1), .7f);
            rim = Lamp("Rim", new Vector3(0, 3, 2), new Color(.5f, .9f, 1), 1.4f);
            var mirror = GameObject.CreatePrimitive(PrimitiveType.Quad);
            mirror.name = "Inspection Mirror"; mirror.layer = 30;
            mirror.transform.SetParent(transform);
            mirror.transform.position = new Vector3(0, 1.5f, MirrorZ); mirror.transform.localScale = new Vector3(4.3f, 2.7f, 1);
            Destroy(mirror.GetComponent<Collider>());
            mirrorTexture = new RenderTexture(1024, 1024, 24) { name = "AvatarLab Mirror" };
            mirrorMaterial = mirrorTemplate != null ? new Material(mirrorTemplate) : new Material(Shader.Find("Unlit/Texture"));
            mirrorMaterial.mainTexture = mirrorTexture;
            mirror.GetComponent<Renderer>().sharedMaterial = mirrorMaterial;
            mirrorCamera = new GameObject("Mirror Camera").AddComponent<Camera>(); mirrorCamera.enabled = false;
            mirrorCamera.targetTexture = mirrorTexture;
        }
        GameObject Object(string label, PrimitiveType type, Vector3 position, Vector3 scale, Color color, bool emissive = false)
        {
            var go = GameObject.CreatePrimitive(type); go.name = label; go.transform.SetParent(transform);
            go.transform.position = position; go.transform.localScale = scale;
            var material = litTemplate != null ? new Material(litTemplate) : new Material(Shader.Find("Standard")); material.color = color;
            material.SetFloat("_Glossiness", .25f);
            if (emissive) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * 2); }
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (label == "Grid") Destroy(go.GetComponent<Collider>());
            return go;
        }
        Light Lamp(string label, Vector3 position, Color color, float intensity)
        {
            var light = new GameObject(label).AddComponent<Light>(); light.transform.SetParent(transform); light.transform.position = position;
            light.transform.LookAt(new Vector3(0, 1, 0)); light.type = LightType.Spot;
            light.spotAngle = 100; light.range = 12; light.color = color; light.intensity = intensity * 3;
            light.shadows = LightShadows.Soft; return light;
        }
        public void Preset(int i)
        {
            key.color = i == 2 ? new Color(1, .35f, .15f) : new Color(1, .93f, .85f);
            fill.color = i == 2 ? new Color(.25f, .25f, 1) : new Color(.65f, .8f, 1);
            key.intensity = i == 1 ? .3f : 4; fill.intensity = i == 1 ? .15f : 2;
            rim.intensity = i == 1 ? 1 : 4;
            RenderSettings.ambientLight = i == 1 ? new Color(.025f, .025f, .04f) : new Color(.28f, .31f, .38f);
        }
        public void Frame(GameObject avatar)
        {
            var renderers = avatar.GetComponentsInChildren<Renderer>();
            var bounds = new Bounds(avatar.transform.position + Vector3.up, Vector3.one * .2f);
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            focus = bounds.center; distance = Mathf.Clamp(bounds.size.magnitude * 1.2f, .6f, 10);
        }
        public void SetMirror(bool enabled) { mirrorEnabled = enabled; }
        public void Orbit(bool vr)
        {
            if (vr) return;
            if (Input.GetMouseButton(1) || gameLook) { yaw += Input.GetAxis("Mouse X") * 4; pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 3, -70, 80); }
            if (Input.mousePosition.x > 440) distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y * .2f, .3f, 15);
            if (Input.GetMouseButton(2)) focus += (view.transform.right * -Input.GetAxis("Mouse X") + view.transform.up * -Input.GetAxis("Mouse Y")) * .02f * distance;
            orbit.position = focus + Quaternion.Euler(pitch, yaw, 0) * Vector3.back * distance;
            if (clampToRoom && orbit.position.z > MirrorZ - .2f) orbit.position = new Vector3(orbit.position.x, orbit.position.y, MirrorZ - .2f);
            orbit.LookAt(focus);
        }
        void LateUpdate()
        {
            using var timing=LabPerformance.Measure("Mirror");
            if (!mirrorEnabled || view == null || mirrorCamera == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            mirrorCamera.CopyFrom(view); mirrorCamera.enabled = false; mirrorCamera.targetTexture = mirrorTexture;
            mirrorCamera.cullingMask = view.cullingMask & ~(1 << 30);
            mirrorCamera.stereoTargetEye = StereoTargetEyeMask.None;
            var reflection = Matrix4x4.identity; reflection.m22 = -1; reflection.m23 = 2 * MirrorZ;
            mirrorCamera.worldToCameraMatrix = view.worldToCameraMatrix * reflection;
            var plane = new Vector4(0, 0, -1, MirrorZ - .01f);
            var cameraPlane = mirrorCamera.worldToCameraMatrix.inverse.transpose * plane;
            mirrorCamera.projectionMatrix = view.CalculateObliqueMatrix(cameraPlane);
            bool old = GL.invertCulling;
            try { GL.invertCulling = !old; mirrorCamera.Render(); }
            finally { GL.invertCulling = old; }
        }
        void OnDestroy()
        {
            if (mirrorTexture != null) { mirrorTexture.Release(); Destroy(mirrorTexture); }
            if (mirrorMaterial != null) Destroy(mirrorMaterial);
            if (mirrorCamera != null) Destroy(mirrorCamera.gameObject);
        }
    }
}
