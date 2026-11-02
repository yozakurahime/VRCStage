using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace AvatarLab.Editor
{
    public static class PhysicsConverter
    {
        static SerializedProperty P(SerializedObject s,string name)=>s.FindProperty(name);
        static bool Flag(SerializedObject s,string name,bool fallback=false) { var p=P(s,name); return p==null?fallback:p.boolValue; }
        static float Float(SerializedObject s,string name,float fallback=0) { var p=P(s,name); return p==null?fallback:p.floatValue; }
        static int Int(SerializedObject s,string name,int fallback=0) { var p=P(s,name); return p==null?fallback:p.intValue; }
        static bool Permission(SerializedObject s,string name,string filter)
        {
            var p=P(s,name); if(p==null) return false;
            if(p.propertyType==SerializedPropertyType.Boolean) return p.boolValue;
            return p.intValue==1 || p.intValue==2 && (P(s,filter+".allowSelf")?.boolValue??true);
        }
        static void CopyFields(SerializedObject from,Object to,IEnumerable<string> names)
        {
            var target=new SerializedObject(to);
            foreach(string name in names)
            {
                var source=P(from,name); var dest=P(target,name); if(source==null || dest==null) continue;
                if(source.propertyType!=dest.propertyType && !(source.propertyType==SerializedPropertyType.Enum && dest.propertyType==SerializedPropertyType.Integer)) continue;
                switch(dest.propertyType)
                {
                    case SerializedPropertyType.Float: dest.floatValue=source.floatValue; break;
                    case SerializedPropertyType.Integer: dest.intValue=source.intValue; break;
                    case SerializedPropertyType.Boolean: dest.boolValue=source.boolValue; break;
                    case SerializedPropertyType.String: dest.stringValue=source.stringValue; break;
                    case SerializedPropertyType.Vector3: dest.vector3Value=source.vector3Value; break;
                    case SerializedPropertyType.Quaternion: dest.quaternionValue=source.quaternionValue; break;
                    case SerializedPropertyType.ObjectReference: dest.objectReferenceValue=source.objectReferenceValue; break;
                    case SerializedPropertyType.AnimationCurve:
                        var curve=source.animationCurveValue;
                        dest.animationCurveValue=new AnimationCurve(curve.keys){preWrapMode=curve.preWrapMode,postWrapMode=curve.postWrapMode}; break;
                }
            }
            target.ApplyModifiedPropertiesWithoutUndo();
        }
        public static void Convert(GameObject clone,List<string> notes)
        {
            var components=clone.GetComponentsInChildren<MonoBehaviour>(true).Where(c=>c!=null).ToArray();
            var map=new Dictionary<Object,AvatarPhysicsCollider>(); int chains=0,hands=0;
            foreach(var source in components.Where(c=>c.GetType().Name=="VRCPhysBoneCollider"))
            {
                var target=source.gameObject.AddComponent<AvatarPhysicsCollider>(); var s=new SerializedObject(source);
                CopyFields(s,target,new[]{"rootTransform","shapeType","insideBounds","radius","height","position","rotation","bonesAsSpheres"});
                if(target.rootTransform!=null && target.rootTransform!=clone.transform && !target.rootTransform.IsChildOf(clone.transform)) { notes.Add("Collider externo ignorado: "+source.name); Object.DestroyImmediate(target); continue; }
                target.globalCollision=Int(s,"globalCollision")==1 || Int(s,"globalCollision")==2 && Flag(s,"globalCollisionAllowSelf",true);
                target.allowSelf=Flag(s,"globalCollisionAllowSelf",true); target.enabled=source.enabled; map[source]=target;
            }
            foreach(var source in components.Where(c=>c.GetType().Name=="VRCPhysBone"))
            {
                var target=source.gameObject.AddComponent<AvatarPhysicsChain>(); var s=new SerializedObject(source);
                string[] floats={"pull","spring","stiffness","gravity","gravityFalloff","immobile","radius","maxAngleX","maxAngleZ","maxStretch","maxSquish","stretchMotion","grabMovement"};
                CopyFields(s,target,floats.Concat(floats.Where(n=>n!="grabMovement").Select(n=>n+"Curve")).Concat(new[]{"rootTransform","ignoreOtherPhysBones","endpointPosition","multiChildType","integrationType","version","immobileType","limitType","limitRotation","limitRotationXCurve","limitRotationYCurve","limitRotationZCurve","isAnimated","resetWhenDisabled","snapToHand","parameter"}));
                if(target.rootTransform!=null && target.rootTransform!=clone.transform && !target.rootTransform.IsChildOf(clone.transform)) { notes.Add("Cadeia externa ignorada: "+source.name); Object.DestroyImmediate(target); continue; }
                var ignores=P(s,"ignoreTransforms"); var ignored=new List<Transform>();
                if(ignores!=null && ignores.isArray) for(int i=0;i<ignores.arraySize;i++) { var t=ignores.GetArrayElementAtIndex(i).objectReferenceValue as Transform; if(t!=null) ignored.Add(t); }
                target.ignoreTransforms=ignored.ToArray();
                var list=P(s,"colliders"); var colliders=new List<AvatarPhysicsCollider>();
                if(list!=null && list.isArray) for(int i=0;i<list.arraySize;i++) if(map.TryGetValue(list.GetArrayElementAtIndex(i).objectReferenceValue??source,out var collider)) colliders.Add(collider);
                target.colliders=colliders.ToArray(); target.allowCollision=Permission(s,"allowCollision","collisionFilter"); target.allowGrabbing=Permission(s,"allowGrabbing","grabFilter"); target.allowPosing=Permission(s,"allowPosing","poseFilter"); target.enabled=source.enabled; chains++;
            }
            foreach(var descriptor in components.Where(c=>c.GetType().Name=="VRCAvatarDescriptor"))
            {
                var s=new SerializedObject(descriptor);
                foreach(string name in new[]{"collider_handL","collider_handR","collider_fingerIndexL","collider_fingerMiddleL","collider_fingerRingL","collider_fingerLittleL","collider_fingerIndexR","collider_fingerMiddleR","collider_fingerRingR","collider_fingerLittleR"})
                {
                    var p=P(s,name); if(p==null) continue;
                    var state=p.FindPropertyRelative("state"); if(state!=null && (state.propertyType==SerializedPropertyType.Enum?state.enumNames[state.enumValueIndex].IndexOf("Disabled",StringComparison.OrdinalIgnoreCase)>=0:state.intValue==2)) continue;
                    var t=p.FindPropertyRelative("transform")?.objectReferenceValue as Transform; if(t==null || !t.IsChildOf(clone.transform)) continue;
                    var go=new GameObject("AvatarLab standard "+name); go.transform.SetParent(t,false);
                    var c=go.AddComponent<AvatarPhysicsCollider>(); c.shapeType=1; c.radius=p.FindPropertyRelative("radius")?.floatValue??.015f; c.height=p.FindPropertyRelative("height")?.floatValue??.06f; c.position=p.FindPropertyRelative("position")?.vector3Value??Vector3.zero; c.rotation=p.FindPropertyRelative("rotation")?.quaternionValue??Quaternion.identity; c.globalCollision=true; c.allowSelf=true; hands++;
                }
            }
            foreach(var source in components.Where(c=>c.GetType().Name=="VRCPhysBone" || c.GetType().Name=="VRCPhysBoneCollider")) Object.DestroyImmediate(source);
            if(chains>0 || map.Count>0)
            {
                notes.Add("Física offline convertida: "+chains+" cadeias, "+map.Count+" colliders explícitos e "+hands+" colliders padrão de mãos/dedos.");
                notes.Add("Solver próprio aproximado: curvas, forças, limites, ramificações, esfera/cápsula/plano e interação local. Não equivale ao solver PhysBones do VRChat nem reproduz sincronização/versões numéricas exatamente.");
            }
        }
    }
}
