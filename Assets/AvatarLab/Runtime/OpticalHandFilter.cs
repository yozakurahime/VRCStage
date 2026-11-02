using UnityEngine;

namespace AvatarLab
{
    // Stateful signal filtering, independent of the Animator's pose each frame.
    public sealed class OpticalHandFilter
    {
        OpticalHand target;
        Vector3 candidate;
        int candidateCount;
        float lastGood=-100, lastStep=-100;
        bool present;
        public OpticalHand Pose { get; private set; }
        public OpticalHand Sample=>target;
        public float Weight { get; private set; }
        public int Dropouts { get; private set; }
        public int RejectedSpikes { get; private set; }
        public bool Fresh(float now)=>present && now-lastGood<.12f;
        public bool Usable=>Pose!=null && Weight>.001f;
        public void Observe(OpticalHand hand,float now,float threshold)
        {
            // Hysteresis avoids flickering as confidence oscillates around the acquire threshold.
            float minimum=target!=null && now-lastGood<.25f?threshold*.7f:threshold;
            if(hand==null || hand.confidence<minimum)
            {
                if(present) Dropouts++;
                present=false; return;
            }
            if(target!=null && now-lastGood<.12f && Vector3.Distance(hand.joints[0],target.joints[0])>.18f)
            {
                candidateCount=Vector3.Distance(candidate,hand.joints[0])<.06f?candidateCount+1:1;
                candidate=hand.joints[0];
                if(candidateCount<3) { RejectedSpikes++; return; }
            }
            candidateCount=0;
            target=hand; lastGood=now; present=true;
            if(Pose==null) Pose=Copy(hand);
        }
        static OpticalHand Copy(OpticalHand h)=>new OpticalHand {left=h.left,confidence=h.confidence,elbowValid=h.elbowValid,elbow=h.elbow,joints=(Vector3[])h.joints.Clone()};
        public void Step(float now,float response,float hold,float fade)
        {
            float dt=lastStep<0?1f/90:Mathf.Clamp(now-lastStep,0,.1f); lastStep=now;
            if(Pose==null || target==null) return;
            float age=now-lastGood;
            float desired=age<=hold?1:Mathf.Clamp01(1-(age-hold)/Mathf.Max(.05f,fade));
            // Re-acquire gradually; disappearance follows the time-based fade above.
            Weight=desired>Weight?Mathf.MoveTowards(Weight,desired,dt/.10f):desired;
            float alpha=1-Mathf.Exp(-Mathf.Max(1,response)*dt);
            for(int i=0;i<21;i++) Pose.joints[i]=Vector3.Lerp(Pose.joints[i],target.joints[i],alpha);
            Pose.elbow=Vector3.Lerp(Pose.elbow,target.elbow,1-Mathf.Exp(-Mathf.Max(1,response)*.6f*dt));
            Pose.elbowValid=target.elbowValid; Pose.confidence=target.confidence;
        }
        public string State(float now)=>!Usable?"sem sinal":Fresh(now)?"rastreada":Weight>.99f?"pose mantida (perda curta)":"retorno suave à animação";
        public void Reset()
        {
            target=null; Pose=null; Weight=0; lastGood=lastStep=-100; present=false; candidateCount=0; Dropouts=RejectedSpikes=0;
        }
    }
}
