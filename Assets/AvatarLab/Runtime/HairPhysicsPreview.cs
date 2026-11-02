using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace AvatarLab
{
    public sealed class HairPhysicsPreview : MonoBehaviour
    {
        [Serializable] public sealed class Settings
        {
            public bool enabled=true;
            public float pull=.18f,spring=.65f,gravity=.04f,radius=.004f;
        }
        sealed class Group { public AvatarPhysicsChain chain; public readonly List<SkinnedMeshRenderer> renderers=new List<SkinnedMeshRenderer>(); }
        public LabApp app;
        public Settings settings=new Settings();
        readonly List<Group> groups=new List<Group>();
        public AvatarPhysicsChain[] Generated=>groups.Where(g=>g.chain!=null).Select(g=>g.chain).ToArray();
        public string Status {get;private set;}="";
        GameObject attached;
        AvatarPhysicsCollider headCollider;
        string key;
        static bool HairName(string name)
        {
            string n=name.ToLowerInvariant(); return n.Contains("hair") || n.Contains("bang") || n.Contains("braid") || n.Contains("ponytail") || n.Contains("cabelo");
        }
        void Update()
        {
            if(attached!=app.currentAvatar) Attach();
            foreach(var group in groups) if(group.chain!=null)
            {
                bool enabled=settings.enabled && group.renderers.Any(r=>r!=null && r.enabled && r.gameObject.activeInHierarchy);
                if(group.chain.enabled!=enabled) group.chain.enabled=enabled;
                group.chain.pull=settings.pull; group.chain.spring=settings.spring; group.chain.gravity=settings.gravity;
                group.chain.radius=settings.radius/Scale(group.chain.Root);
            }
        }
        void Attach()
        {
            foreach(var group in groups) if(group.chain!=null) Destroy(group.chain);
            groups.Clear(); if(headCollider!=null) Destroy(headCollider); headCollider=null;
            attached=app.currentAvatar; if(attached==null) return;
            key="hair.preview."+app.AvatarId; settings=new Settings();
            if(PlayerPrefs.HasKey(key)) try { settings=JsonUtility.FromJson<Settings>(PlayerPrefs.GetString(key))??new Settings(); } catch { }
            var animator=app.driver?.Definition.animator; var head=animator!=null && animator.isHuman?animator.GetBoneTransform(HumanBodyBones.Head):null;
            if(head==null) { Status="Geração de cabelo requer um rig Humanoid com Head."; return; }
            var authored=attached.GetComponentsInChildren<AvatarPhysicsChain>(true);
            var candidates=new Dictionary<Transform,List<SkinnedMeshRenderer>>();
            foreach(var renderer in attached.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>HairName(r.name)))
                foreach(var bone in renderer.bones)
                {
                    if(bone==null || !bone.IsChildOf(head)) continue;
                    Transform root=null;
                    for(var t=bone;t!=null && t!=head;t=t.parent) if(HairName(t.name)) root=t;
                    if(root==null) continue;
                    if(!candidates.TryGetValue(root,out var meshes)) { meshes=new List<SkinnedMeshRenderer>(); candidates[root]=meshes; }
                    if(!meshes.Contains(renderer)) meshes.Add(renderer);
                }
            int covered=0;
            foreach(var pair in candidates)
            {
                var root=pair.Key;
                if(candidates.Keys.Any(other=>other!=root && root.IsChildOf(other))) continue;
                if(authored.Any(c=>c.Root==root || c.Root.IsChildOf(root) || root.IsChildOf(c.Root))) { covered++; continue; }
                if(headCollider==null)
                {
                    headCollider=head.gameObject.AddComponent<AvatarPhysicsCollider>(); headCollider.radius=.065f/Scale(head);
                    headCollider.position=head.InverseTransformVector(attached.transform.up*.045f); headCollider.globalCollision=false; headCollider.bonesAsSpheres=true;
                }
                var chain=root.gameObject.AddComponent<AvatarPhysicsChain>();
                chain.attachmentDepth=2; chain.lockAttachmentRotation=true;
                chain.multiChildType=2; chain.endpointPosition=Vector3.up*(.015f/Scale(root)); chain.isAnimated=true;
                chain.pull=settings.pull; chain.spring=settings.spring; chain.gravity=settings.gravity; chain.gravityFalloff=.35f; chain.immobile=.15f;
                chain.limitType=1; chain.maxAngleX=60; chain.radius=settings.radius/Scale(root); chain.allowGrabbing=true; chain.allowPosing=true;
                chain.colliders=new[]{headCollider};
                var group=new Group {chain=chain}; group.renderers.AddRange(pair.Value); groups.Add(group); chain.Rebuild();
            }
            Status=groups.Count>0?groups.Count+" raiz(es) com física de TESTE gerada; o avatar original não foi alterado.":covered>0?"Física de cabelo configurada no avatar encontrada e preservada.":"Nenhuma raiz de cabelo reconhecida neste avatar.";
            app.physicsStudio.Refresh();
        }
        static float Scale(Transform t) { var s=t.lossyScale; return Mathf.Max(.001f,Mathf.Max(Mathf.Abs(s.x),Mathf.Max(Mathf.Abs(s.y),Mathf.Abs(s.z)))); }
        public void DrawUI()
        {
            GUILayout.Label("Cabelo • física de teste"); GUILayout.Label(Status);
            if(groups.Count==0) return;
            var before=JsonUtility.ToJson(settings);
            settings.enabled=GUILayout.Toggle(settings.enabled,"Ativar física gerada do cabelo");
            settings.pull=StudioGui.Slider("Retorno do cabelo",settings.pull,.02f,.8f);
            settings.spring=StudioGui.Slider("Movimento / elasticidade",settings.spring,.1f,.95f);
            settings.gravity=StudioGui.Slider("Gravidade do cabelo",settings.gravity,-.1f,.2f);
            settings.radius=StudioGui.Slider("Raio dos fios (m)",settings.radius,.001f,.012f);
            GUILayout.Label("Base presa à cabeça; movimento aplicado às mechas após os pontos de fixação.");
            GUILayout.Label(groups.Sum(g=>g.chain!=null?g.chain.Count:0)+" pontos. Valores próprios para prévia, independentes do SDK.");
            if(GUILayout.Button("Testar balanço do cabelo")) foreach(var g in groups) if(g.chain!=null) g.chain.ApplyImpulse(new Vector3(.4f,0,.1f));
            if(before!=JsonUtility.ToJson(settings)) { PlayerPrefs.SetString(key,JsonUtility.ToJson(settings)); PlayerPrefs.Save(); }
        }
    }
}
