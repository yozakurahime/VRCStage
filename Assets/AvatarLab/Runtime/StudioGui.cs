using System.Globalization;
using UnityEngine;

namespace AvatarLab
{
    public static class StudioGui
    {
        static readonly System.Collections.Generic.Dictionary<int, string> edits = new System.Collections.Generic.Dictionary<int, string>();
        public static float Number(string label, float value, float min = -10000, float max = 10000, float step = .1f)
        {
            GUILayout.BeginHorizontal(); GUILayout.Label(label, GUILayout.Width(80));
            int id = GUIUtility.GetControlID(FocusType.Passive); string control = "StudioNumber" + id;
            bool focused = GUI.GetNameOfFocusedControl() == control;
            if (!focused || !edits.TryGetValue(id, out var text)) text = value.ToString("0.###", CultureInfo.InvariantCulture);
            GUI.SetNextControlName(control); text = GUILayout.TextField(text); edits[id] = text;
            if (float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                && !float.IsNaN(parsed) && !float.IsInfinity(parsed)) value = Mathf.Clamp(parsed, min, max);
            if (GUILayout.Button("−", GUILayout.Width(28))) value = Mathf.Max(min, value - step);
            if (GUILayout.Button("+", GUILayout.Width(28))) value = Mathf.Min(max, value + step);
            GUILayout.EndHorizontal(); return value;
        }
        public static Vector3 Vector(string label, Vector3 v)
        {
            GUILayout.Label(label); v.x = Number("X", v.x); v.y = Number("Y", v.y); v.z = Number("Z", v.z); return v;
        }
        public static float Slider(string label, float value, float min, float max)
        {
            GUILayout.Label(label + "  " + value.ToString("0.00", CultureInfo.InvariantCulture));
            return GUILayout.HorizontalSlider(value, min, max);
        }
        public static Color Color(string label,Color value)
        {
            GUILayout.Label(label);value.r=Slider("Vermelho",value.r,0,1);value.g=Slider("Verde",value.g,0,1);value.b=Slider("Azul",value.b,0,1);value.a=1;return value;
        }
    }
}
