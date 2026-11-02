using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object=UnityEngine.Object;
namespace AvatarLab.Editor
{
    public static class PhysicsValidation
    {
        static void Check(bool value,string message) { if(!value) throw new Exception(message); }
        static Transform Bone(Transform parent,string name,Vector3 p) { var b=new GameObject(name).transform; b.SetParent(parent,false); b.localPosition=p; return b; }
        static void Step(AvatarPhysicsChain c,float dt) { c.SendMessage("Update"); c.Simulate(dt); }
        public static void Run()
        {
            try
            {
                Directory.CreateDirectory("TestOutput");
                var collider=new GameObject("Collider test").AddComponent<AvatarPhysicsCollider>(); collider.radius=.2f;
                Check(Mathf.Abs(collider.Project(Vector3.zero,.05f).magnitude-.25f)<1e-5f,"Sphere outside / coincident center");
                collider.insideBounds=true; Check(Mathf.Abs(collider.Project(Vector3.right,.05f).magnitude-.15f)<1e-5f,"Inside bounds radius");
                collider.insideBounds=false; collider.shapeType=1; collider.height=1;
                var hit=collider.Project(new Vector3(.05f,.25f,0),.05f); Check(Mathf.Abs(hit.x-.25f)<1e-5f,"Capsule side");
                collider.shapeType=2; collider.rotation=Quaternion.Euler(-90,0,0); hit=collider.Project(new Vector3(0,-1,0),.03f); Check(Mathf.Abs(hit.y-.03f)<1e-4f,"Plane normal / rotation / radius");
                collider.shapeType=0; collider.rotation=Quaternion.identity;
                hit=collider.ProjectBone(Vector3.left,Vector3.right,.05f); Check(Mathf.Abs(hit.z)>.1f,"Bone capsule crossing sphere not detected");
                collider.gameObject.SetActive(false);
                var root=new GameObject("Chain test"); var end=Bone(root.transform,"tip",Vector3.right);
                var chain=root.AddComponent<AvatarPhysicsChain>(); chain.pull=.2f; chain.spring=.7f; chain.gravity=.5f; chain.gravityFalloff=0; chain.radius=0; chain.Rebuild();
                for(int i=0;i<240;i++) Step(chain,1f/120);
                Check(chain.Point(1).y<-.02f,"Gravity did not move endpoint");
                Check(Mathf.Abs(Vector3.Distance(chain.Point(0),chain.Point(1))-1)<.0005f,"Unstretched length drift");
                Check(!float.IsNaN(chain.Point(1).x),"Nonfinite integration");
                chain.limitType=1; chain.maxAngleX=25; chain.gravity=0; chain.ResetSimulation(); chain.Grab(1,Vector3.left);
                for(int i=0;i<20;i++) Step(chain,1f/60);
                Check(Vector3.Angle(Vector3.right,chain.Point(1)-chain.Point(0))<25.1f,"Cone limit exceeded");
                chain.allowPosing=true; chain.Pose(); chain.Release(); Check(chain.IsPosed && !chain.IsGrabbed,"Pose / released grab state incorrect"); chain.ResetSimulation(); Check(!chain.IsPosed && !chain.IsGrabbed,"Reset retained pose");
                chain.limitType=0; chain.maxStretch=.5f; chain.maxSquish=.4f; chain.Grab(1,Vector3.right*3);
                for(int i=0;i<20;i++) Step(chain,1f/60);
                Check(chain.Point(1).magnitude<=1.501f && chain.Point(1).magnitude>1.4f,"Grab stretch bound"); chain.ClearPose(); chain.Release(); chain.ResetSimulation();
                root.transform.position=new Vector3(20,0,0); Step(chain,1f/60); Check(root.transform.position.x==20 && Vector3.Distance(chain.Point(1),end.position)<.001f,"Teleport reset");
                Object.DestroyImmediate(root);
                var branch=new GameObject("Branches"); var a=Bone(branch.transform,"A",Vector3.up*.2f); Bone(branch.transform,"B",Vector3.right*.2f);
                chain=branch.AddComponent<AvatarPhysicsChain>(); chain.endpointPosition=Vector3.up*.1f; chain.multiChildType=0; chain.Rebuild(); Check(chain.Count==5,"Branched graph / virtual endpoints");
                chain.ignoreTransforms=new[]{a}; chain.Rebuild(); Check(chain.Count==3,"Ignore subtree");
                Check(Mathf.Abs(AvatarPhysicsChain.Sample(.4f,AnimationCurve.Linear(0,.5f,1,2),1)-.8f)<1e-5f,"Depth curve multiplier"); Object.DestroyImmediate(branch); Object.DestroyImmediate(collider.gameObject);
                var movingHead=new GameObject("Moving head");
                var scalp=Bone(movingHead.transform,"Scalp",new Vector3(0,.08f,0));
                var baseBone=Bone(scalp,"HairBase",new Vector3(.03f,0,0));
                var strand=Bone(baseBone,"Strand attachment",new Vector3(.03f,0,0));
                var free=Bone(strand,"Free strand",new Vector3(0,-.15f,0));
                chain=scalp.gameObject.AddComponent<AvatarPhysicsChain>(); chain.attachmentDepth=2; chain.lockAttachmentRotation=true; chain.multiChildType=2; chain.isAnimated=true; chain.radius=0; chain.Rebuild();
                chain.ApplyImpulse(Vector3.right*.4f); bool freeMoved=false;
                for(int i=0;i<180;i++)
                {
                    movingHead.transform.SetPositionAndRotation(new Vector3(Mathf.Sin(i*.07f)*.2f,1.6f,0),Quaternion.Euler(Mathf.Sin(i*.05f)*30,i*.7f,Mathf.Sin(i*.1f)*15));
                    Step(chain,1f/90);
                    Check(Vector3.Distance(scalp.localPosition,new Vector3(0,.08f,0))<1e-5f && Quaternion.Angle(scalp.localRotation,Quaternion.identity)<.01f,"Scalp detached or rotated as a wig");
                    Check(Vector3.Distance(baseBone.localPosition,new Vector3(.03f,0,0))<1e-5f && Quaternion.Angle(baseBone.localRotation,Quaternion.identity)<.01f,"Hair base detached");
                    Check(Vector3.Distance(strand.localPosition,new Vector3(.03f,0,0))<1e-5f,"Strand attachment detached");
                    Check(Vector3.Distance(chain.Point(2),strand.position)<1e-5f,"Pinned simulation point did not follow head");
                    freeMoved|=Quaternion.Angle(strand.localRotation,Quaternion.identity)>.1f;
                }
                Check(freeMoved,"Attachment lock also froze free strands"); Object.DestroyImmediate(movingHead);
                LabBuild.Prepare(); var fixture=LabBuild.CreateSample(); var tip=Bone(fixture.transform,"PhysicsTip",Vector3.up*.2f);
                var rawCollider=fixture.AddComponent<Fixture.VRCPhysBoneCollider>(); rawCollider.shapeType=1; rawCollider.insideBounds=true;
                var raw=fixture.AddComponent<Fixture.VRCPhysBone>(); raw.rootTransform=tip; raw.colliders=new[]{rawCollider};
                string clipPath=AssetDatabase.GenerateUniqueAssetPath(LabBuild.Generated+"/PhysicsToggle.anim"); var clip=new AnimationClip{name="PhysicsToggle"};
                AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Fixture.VRCPhysBone),"m_Enabled"),AnimationCurve.Constant(0,1,1)); AssetDatabase.CreateAsset(clip,clipPath);
                var controller=fixture.GetComponent<AvatarDefinition>().fxController as AnimatorController; controller.layers[0].stateMachine.defaultState.motion=clip; AssetDatabase.SaveAssets();
                string before=AvatarArchive.Hash(File.ReadAllBytes(clipPath));
                string output=Path.GetFullPath("TestOutput/PhysicsFixture.alavatar"); AvatarExporter.Export(fixture,output);
                Check(fixture.GetComponent<Fixture.VRCPhysBone>()==raw && raw.colliders[0]==rawCollider,"Original component mutated"); Check(before==AvatarArchive.Hash(File.ReadAllBytes(clipPath)),"Original animation changed");
                var manifest=AvatarArchive.Read(output,out byte[] bytes); var bundle=AssetBundle.LoadFromMemory(bytes); var prefab=bundle.LoadAsset<GameObject>(manifest.prefab);
                var converted=prefab.GetComponent<AvatarPhysicsChain>(); var convertedCollider=prefab.GetComponent<AvatarPhysicsCollider>();
                Check(converted!=null && convertedCollider!=null && converted.colliders[0]==convertedCollider,"Converted collider references lost");
                Check(Mathf.Abs(converted.pull-.23f)<1e-5f && converted.pullCurve.length==2 && converted.multiChildType==2 && convertedCollider.insideBounds && convertedCollider.shapeType==1,"Serialized physics settings not preserved");
                Check(prefab.GetComponentsInChildren<MonoBehaviour>(true).All(b=>b==null || !b.GetType().Name.StartsWith("VRC")),"SDK fixture script leaked into player prefab");
                var copied=prefab.GetComponent<AvatarDefinition>().fxController;
                File.WriteAllLines("TestOutput/physics-imported-clips.txt",copied.animationClips.Select(c=>c.name));
                var importedClip=copied.animationClips.FirstOrDefault(c=>c.name.StartsWith("PhysicsToggle",StringComparison.Ordinal)); Check(importedClip!=null,"Converted physics clip missing.");
                var playback=Object.Instantiate(prefab); playback.GetComponent<AvatarPhysicsChain>().enabled=false;
                importedClip.SampleAnimation(playback,.5f);
                Check(playback.GetComponent<AvatarPhysicsChain>().enabled,"Runtime physics toggle animation did not enable converted component."); Object.DestroyImmediate(playback);
                bundle.Unload(true); Object.DestroyImmediate(fixture);
                File.WriteAllText("TestOutput/physics-editor-result.txt","PASS\nMoving/turning head: scalp and strand attachments fixed, free strands still move; sphere/capsule/rotated plane, inside bounds, segment collisions; gravity, length constraints, cone limits, pose/reset, grab stretch, teleport reset; branches/endpoints/ignored subtree; depth curves; export/import conversion and collider references, remapped enable animation, original components and clip unchanged, no SDK scripts.\n");
                Debug.Log("AVATARLAB_PHYSICS_EDITOR_PASS");
                if(Environment.GetCommandLineArgs().Contains("--build-physics-player")) LabBuild.Build(); else EditorApplication.Exit(0);
            }
            catch(Exception ex) { File.WriteAllText("TestOutput/physics-editor-result.txt","FAIL\n"+ex); Debug.LogException(ex); EditorApplication.Exit(1); }
        }
    }
}
