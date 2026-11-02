using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace AvatarLab.Editor
{
    public static class OpticalFilterValidation
    {
        static OpticalHand Hand(float x=0,float confidence=1)
        {
            var points=new Vector3[21]; for(int i=0;i<21;i++) points[i]=new Vector3(x,.2f,i*.002f);
            return new OpticalHand {left=true,joints=points,elbow=new Vector3(x,.1f,.2f),elbowValid=true,confidence=confidence};
        }
        static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
        static void Step(OpticalHandFilter f,float t)=>f.Step(t,18,.18f,.25f);
        public static void RunAndBuild()
        {
            Directory.CreateDirectory("TestOutput");
            try
            {
                const float dt=1f/90; float t=0; var filter=new OpticalHandFilter();
                for(int i=0;i<90;i++) { filter.Observe(Hand(),t,.5f); Step(filter,t); t+=dt; }
                Check(filter.Weight>.99f,"Tracking did not acquire");
                for(int i=0;i<90;i++)
                {
                    filter.Observe(i%2==0?null:Hand(),t,.5f); Step(filter,t); t+=dt;
                    Check(filter.Weight>.99f && filter.Pose.joints[0].y>.199f,"Single-frame dropout released the hand to animation");
                }
                for(int i=0;i<9;i++) { filter.Observe(null,t,.5f); Step(filter,t); t+=dt; Check(filter.Weight>.99f,"100ms dropout not held"); }
                filter.Observe(Hand(0,.4f),t,.5f); Step(filter,t); Check(filter.Fresh(t),"Confidence hysteresis failed after a brief absence"); t+=dt;
                filter.Observe(Hand(1),t,.5f); Step(filter,t); t+=dt;
                Check(filter.RejectedSpikes>0 && filter.Pose.joints[0].x<.001f,"Single-frame position spike accepted");
                filter.Observe(Hand(),t,.5f); Step(filter,t); t+=dt;
                double rawEnergy=0,filteredEnergy=0;
                for(int i=0;i<180;i++)
                {
                    float noise=(i%2==0?1:-1)*.004f; filter.Observe(Hand(noise),t,.5f); Step(filter,t); t+=dt;
                    rawEnergy+=noise*noise; filteredEnergy+=filter.Pose.joints[0].x*filter.Pose.joints[0].x;
                }
                Check(filteredEnergy<rawEnergy*.15,"High-frequency positional noise not attenuated");
                float last=filter.Weight;
                for(int i=0;i<55;i++)
                {
                    filter.Observe(null,t,.5f); Step(filter,t); t+=dt;
                    Check(filter.Weight<=last+.0001f && last-filter.Weight<.06f,"Loss fade discontinuity"); last=filter.Weight;
                }
                Check(!filter.Usable && filter.Weight==0,"Long tracking loss never returned to animation");
                filter.Observe(Hand(.03f),t,.5f); Step(filter,t); t+=dt;
                Check(filter.Weight>0 && filter.Weight<.2f,"Re-acquisition snapped to full tracking weight");
                for(int i=0;i<30;i++) { filter.Observe(Hand(.03f),t,.5f); Step(filter,t); t+=dt; }
                Check(filter.Weight>.99f && Mathf.Abs(filter.Pose.joints[0].x-.03f)<.001f,"Valid motion failed to converge");
                filter.Reset(); Check(!filter.Usable && !filter.Fresh(t) && filter.Pose==null,"Stop retained stale tracking");
                File.WriteAllText("TestOutput/optical-filter-result.txt","PASS\n90Hz deterministic sequences: alternating empty/valid frames and 100ms losses held without dropping application; confidence hysteresis; one-frame 1m spike rejected; 4mm alternating noise energy reduced below 15%; long loss smoothly released and re-acquisition blended; genuine movement converged; stop cleared state.\n");
                Debug.Log("AVATARLAB_OPTICAL_FILTER_PASS"); LabBuild.Build();
            }
            catch(Exception ex) { File.WriteAllText("TestOutput/optical-filter-result.txt","FAIL\n"+ex); Debug.LogException(ex); EditorApplication.Exit(1); }
        }
    }
}
