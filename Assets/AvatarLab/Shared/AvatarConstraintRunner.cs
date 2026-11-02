using System.Collections.Generic;
using UnityEngine;
namespace AvatarLab
{
    [DefaultExecutionOrder(310)]
    public sealed class AvatarConstraintRunner : MonoBehaviour
    {
        public AvatarConstraint[] constraints;
        public int CyclicDependencies {get;private set;}
        [System.NonSerialized] public Camera debugCamera;
        [System.NonSerialized] public bool drawLinks;
        Material lines;
        public void Rebuild()
        {
            var all=GetComponentsInChildren<AvatarConstraint>(true); var result=new List<AvatarConstraint>(); var done=new HashSet<AvatarConstraint>(); var visiting=new HashSet<AvatarConstraint>(); CyclicDependencies=0;
            foreach(var c in all) Visit(c,all,result,done,visiting);
            constraints=result.ToArray();
        }
        void Visit(AvatarConstraint c,AvatarConstraint[] all,List<AvatarConstraint> result,HashSet<AvatarConstraint> done,HashSet<AvatarConstraint> visiting)
        {
            if(done.Contains(c)) return;
            if(!visiting.Add(c)) { CyclicDependencies++; return; }
            foreach(var other in all) if(other!=c)
            {
                bool depends=c.Target.parent!=null && (c.Target.parent==other.Target || c.Target.parent.IsChildOf(other.Target));
                for(int i=0;!depends && i<c.Sources.Count;i++) { var source=c.Sources.Get(i).SourceTransform; depends=source!=null && (source==other.Target || source.IsChildOf(other.Target)); }
                if(depends) Visit(other,all,result,done,visiting);
            }
            visiting.Remove(c); if(done.Add(c)) result.Add(c);
        }
        void OnEnable() { Rebuild(); }
        public void Evaluate() { using var timing=LabPerformance.Measure("Constraints"); if(constraints==null) Rebuild(); foreach(var c in constraints) if(c!=null) c.Evaluate(); }
        void LateUpdate() { Evaluate(); }
        void OnRenderObject()
        {
            if(!drawLinks || debugCamera==null || Camera.current!=debugCamera || constraints==null) return;
            if(lines==null) { var shader=Shader.Find("Hidden/Internal-Colored"); if(shader==null) return; lines=new Material(shader){hideFlags=HideFlags.HideAndDontSave}; lines.SetInt("_ZWrite",0); lines.SetInt("_ZTest",(int)UnityEngine.Rendering.CompareFunction.Always); }
            lines.SetPass(0); GL.PushMatrix(); GL.MultMatrix(Matrix4x4.identity); GL.Begin(GL.LINES);
            foreach(var c in constraints) if(c!=null && c.isActiveAndEnabled && c.IsActive)
            {
                GL.Color(c.FreezeToWorld?Color.magenta:Color.cyan);
                for(int i=0;i<c.Sources.Count;i++) { var source=c.Sources.Get(i); if(source.SourceTransform!=null && source.Weight>0) { GL.Vertex(c.Target.position); GL.Vertex(source.SourceTransform.position); } }
            }
            GL.End(); GL.PopMatrix();
        }
        void OnDestroy() { if(lines!=null) Destroy(lines); }
    }
}
