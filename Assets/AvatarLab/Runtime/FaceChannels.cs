using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AvatarLab
{
    public static class FaceChannels
    {
        public static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, string> LegacyUnified = new Dictionary<string, string>();
        public static readonly string[] Vowels = { "vowelA", "vowelE", "vowelI", "vowelO", "vowelU" };
        static FaceChannels()
        {
            Pair("jawOpen", "JawOpen", "Jaw_Open", "Mouth Open"); Pair("jawForward", "JawForward"); Pair("jawLeft", "JawLeft"); Pair("jawRight", "JawRight");
            Pair("mouthClose", "MouthClosed", "Mouth_Ape_Shape"); Pair("mouthFunnel", "MouthFunnel", "Mouth_Funnel"); Pair("mouthPucker", "MouthPucker", "Mouth_Pout");
            Pair("mouthLeft", "MouthLeft"); Pair("mouthRight", "MouthRight"); Pair("mouthRollLower", "MouthRollLower", "Mouth_Lower_Inside"); Pair("mouthRollUpper", "MouthRollUpper", "Mouth_Upper_Inside");
            Pair("mouthShrugLower", "MouthRaiserLower", "Mouth_Lower_Overlay"); Pair("mouthShrugUpper", "MouthRaiserUpper");
            Pair("browInnerUp", "BrowInnerUp"); Pair("cheekPuff", "CheekPuff"); Pair("tongueOut", "TongueOut");
            foreach (string side in new[] { "Left", "Right" })
            {
                string shortSide = side == "Left" ? "L" : "R";
                Pair("eyeBlink" + side, "EyeClosed" + side, "Eye_" + side + "_Blink", "Eye - Blink " + shortSide, "Eyes_Closed_" + shortSide, "eyeBlink_" + shortSide);
                Pair("eyeWide" + side, "EyeWide" + side, "Eye_" + side + "_Wide", "Eye - Wide " + shortSide);
                Pair("eyeSquint" + side, "EyeSquint" + side, "Eye_" + side + "_squeeze");
                foreach (string direction in new[] { "Up", "Down", "In", "Out" }) Pair("eyeLook" + direction + side, "EyeLook" + direction + side, "eyeLook" + direction + "_" + shortSide);
                Pair("mouthSmile" + side, "MouthSmile" + side, "Mouth_Smile_" + side, "mouthSmile_" + shortSide, "MouthCornerPuller" + side, "MouthCornerSlant" + side);
                Pair("mouthFrown" + side, "MouthFrown" + side, "Mouth_Frown_" + side, "mouthFrown_" + shortSide);
                foreach (string action in new[] { "Dimple", "Stretch", "Press", "LowerDown", "UpperUp" })
                    Pair("mouth" + action + side, "Mouth" + (action == "Dimple" ? "Dimpler" : action) + side, "Mouth_" + action + "_" + side, "mouth" + action + "_" + shortSide);
                Pair("browDown" + side, "BrowDown" + side, "browDown_" + shortSide);
                Pair("browOuterUp" + side, "BrowOuterUp" + side, "browOuterUp_" + shortSide);
                Pair("cheekSquint" + side, "CheekSquint" + side, "Cheek_Raiser_" + shortSide);
                Pair("noseSneer" + side, "NoseSneer" + side, "noseSneer_" + shortSide);
            }
            Pair("blinkBoth", "vrc.blink", "Blink", "EyesClosed");
            Pair("vowelA", "vrc.v_aa", "viseme_aa", "A", "AA", "Ah"); Pair("vowelE", "vrc.v_e", "viseme_e", "E", "Eh");
            Pair("vowelI", "vrc.v_ih", "viseme_ih", "I", "Ih"); Pair("vowelO", "vrc.v_oh", "viseme_oh", "O", "Oh"); Pair("vowelU", "vrc.v_ou", "viseme_ou", "U", "Ou");
        }
        static void Pair(string canonical, string legacy, params string[] aliases)
        {
            Aliases[Normalize(canonical)] = canonical; Aliases[Normalize(legacy)] = canonical; LegacyUnified[legacy] = canonical;
            foreach (string alias in aliases) Aliases[Normalize(alias)] = canonical;
        }
        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int slash = name.LastIndexOf('/'); if (slash >= 0) name = name.Substring(slash + 1);
            name = name.Replace("CTRL_expressions_", "").Replace("ctrl_expressions_", "");
            var output = new StringBuilder(); foreach (char c in name) if (char.IsLetterOrDigit(c)) output.Append(char.ToLowerInvariant(c)); return output.ToString();
        }
        public static string Canonical(string name) => Aliases.TryGetValue(Normalize(name), out var found) ? found : name;
        public static bool IsMouth(string name) => name.StartsWith("mouth", StringComparison.Ordinal) || name.StartsWith("jaw", StringComparison.Ordinal) || name.StartsWith("vowel", StringComparison.Ordinal) || name == "tongueOut";
    }
    public sealed class FacialFrame
    {
        public readonly Dictionary<string, float> values = new Dictionary<string, float>();
        public bool hasHead;
        public UnityEngine.Vector3 head;
        public bool hasLeftEye,hasRightEye;
        public UnityEngine.Vector3 leftEye,rightEye;
    }
    public static class IfacialCodec
    {
        // Official v1/v2 text UDP protocol. Values are 0..100; head angles are degrees.
        static readonly Regex Blend = new Regex(@"([A-Za-z][A-Za-z0-9_ .]*?)(?:&|-)(-?\d+(?:\.\d+)?)", RegexOptions.Compiled);
        public static FacialFrame Decode(string text)
        {
            if (text == null || text.Length > 65536) throw new FormatException("Frame iFacial inválido.");
            var frame = new FacialFrame();
            int head = text.IndexOf("head#", StringComparison.OrdinalIgnoreCase);
            string shapeText = head >= 0 ? text.Substring(0, head) : text;
            foreach (Match match in Blend.Matches(shapeText))
                if (float.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && !float.IsInfinity(v) && !float.IsNaN(v)) frame.values[FaceChannels.Canonical(match.Groups[1].Value.Trim())] = UnityEngine.Mathf.Clamp01(v / 100f);
            if (head >= 0)
            {
                int end = text.IndexOf('|', head); string raw = text.Substring(head + 5, (end < 0 ? text.Length : end) - head - 5);
                var parts = raw.Split(new[] { ',', '#' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                { if (!float.IsNaN(x) && !float.IsNaN(y) && !float.IsNaN(z) && !float.IsInfinity(x) && !float.IsInfinity(y) && !float.IsInfinity(z)) { frame.head = new UnityEngine.Vector3(x, y, z); frame.hasHead = true; } }
            }
            frame.hasLeftEye=ReadRotation(text,"leftEye#",out frame.leftEye);frame.hasRightEye=ReadRotation(text,"rightEye#",out frame.rightEye);
            if (frame.values.Count == 0 && !frame.hasHead && !frame.hasLeftEye && !frame.hasRightEye) throw new FormatException("Datagrama não é iFacialMocap v1/v2."); return frame;
        }
        static bool ReadRotation(string text,string key,out UnityEngine.Vector3 rotation)
        {
            rotation=UnityEngine.Vector3.zero;int start=text.IndexOf(key,StringComparison.OrdinalIgnoreCase);if(start<0) return false;start+=key.Length;int end=text.IndexOf('|',start);
            var values=text.Substring(start,(end<0?text.Length:end)-start).Split(new[]{',','#'},StringSplitOptions.RemoveEmptyEntries);if(values.Length<3) return false;
            var numbers=new float[3];for(int i=0;i<3;i++) if(!float.TryParse(values[i],NumberStyles.Float,CultureInfo.InvariantCulture,out numbers[i]) || float.IsNaN(numbers[i]) || float.IsInfinity(numbers[i])) return false;
            rotation=new UnityEngine.Vector3(numbers[0],numbers[1],numbers[2]);return true;
        }
    }
}
