using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace AvatarLab.Editor
{
    public static class ConstraintConverter
    {
        public static Type TargetType(Type type)
        {
            switch(type?.Name) {case "VRCParentConstraint":return typeof(AvatarParentConstraint);case "VRCPositionConstraint":return typeof(AvatarPositionConstraint);case "VRCRotationConstraint":return typeof(AvatarRotationConstraint);case "VRCScaleConstraint":return typeof(AvatarScaleConstraint);case "VRCAimConstraint":return typeof(AvatarAimConstraint);case "VRCLookAtConstraint":return typeof(AvatarLookAtConstraint);default:return null;}
        }
        public static void Convert(GameObject clone,List<string> notes)
        {
            int count=0;
            foreach(var c in clone.GetComponentsInChildren<MonoBehaviour>(true).Where(c=>c!=null).ToArray())
            {
                var type=TargetType(c.GetType()); if(type==null) continue;
                var target=(AvatarConstraint)c.gameObject.AddComponent(type); var source=new SerializedObject(c); var destination=new SerializedObject(target);
                var p=source.GetIterator(); bool enter=true;
                while(p.Next(enter))
                {
                    enter=true;
                    if(p.propertyPath.StartsWith("m_",StringComparison.Ordinal) || p.propertyType==SerializedPropertyType.Generic || p.propertyType==SerializedPropertyType.ArraySize) continue;
                    var d=destination.FindProperty(p.propertyPath); if(d==null) continue;
                    if(p.propertyType==SerializedPropertyType.ObjectReference)
                    {
                        var value=p.objectReferenceValue;
                        if(value is Transform t && t!=clone.transform && !t.IsChildOf(clone.transform)) { notes.Add("Constraint: referência externa ignorada em "+c.name+" / "+p.propertyPath); value=null; }
                        d.objectReferenceValue=value;
                    }
                    else if(p.propertyType==SerializedPropertyType.Boolean) d.boolValue=p.boolValue;
                    else if(p.propertyType==SerializedPropertyType.Integer || p.propertyType==SerializedPropertyType.Enum) d.intValue=p.intValue;
                    else if(p.propertyType==SerializedPropertyType.Float) d.floatValue=p.floatValue;
                    else if(p.propertyType==SerializedPropertyType.Vector3) d.vector3Value=p.vector3Value;
                }
                destination.ApplyModifiedPropertiesWithoutUndo();
                // Overflow source arrays need resizing before their entries can be copied.
                var overflow=source.FindProperty("Sources.overflowList");
                if(overflow!=null && overflow.isArray && overflow.arraySize>0)
                {
                    target.Sources.overflowList=new AvatarConstraintSource[overflow.arraySize];
                    for(int i=0;i<overflow.arraySize;i++)
                    {
                        var s=overflow.GetArrayElementAtIndex(i); target.Sources.overflowList[i]=new AvatarConstraintSource {SourceTransform=s.FindPropertyRelative("SourceTransform").objectReferenceValue as Transform,Weight=s.FindPropertyRelative("Weight").floatValue,ParentPositionOffset=s.FindPropertyRelative("ParentPositionOffset").vector3Value,ParentRotationOffset=s.FindPropertyRelative("ParentRotationOffset").vector3Value};
                    }
                }
                target.enabled=c.enabled; Object.DestroyImmediate(c); count++;
            }
            if(count>0) { var runner=clone.GetComponent<AvatarConstraintRunner>()??clone.AddComponent<AvatarConstraintRunner>(); runner.Rebuild(); notes.Add("Constraints convertidos: "+count+" (Parent/Position/Rotation/Scale/Aim/LookAt). Execução após tracking; dependências ordenadas; ciclos aproximados: "+runner.CyclicDependencies+"."); }
        }
    }
}
