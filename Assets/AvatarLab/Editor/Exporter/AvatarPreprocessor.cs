using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace AvatarLab.Editor
{
    public static class AvatarPreprocessor
    {
        public static void Run(GameObject clone,List<string> notes)
        {
            int fury=clone.GetComponentsInChildren<Component>(true).Count(c=>c!=null && c.GetType().Name=="VRCFury");
            if(fury==0) return;
            // The same local preprocess entry point used by VRCFury's Test Copy command.
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("VRC.SDKBase.Editor.BuildPipeline.VRCBuildPipelineCallbacks",false)).FirstOrDefault(t=>t!=null);
            var method=type?.GetMethod("OnPreprocessAvatar",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static,null,new[]{typeof(GameObject)},null);
            if(method==null) throw new InvalidOperationException("Avatar com VRCFury: instale SDK e VRCFury no projeto para processar a cópia antes da exportação.");
            try
            {
                if(!(method.Invoke(null,new object[]{clone}) is bool ok) || !ok) throw new InvalidOperationException("Um preprocessador recusou a preparação local do avatar. Consulte o Console; nenhum pacote foi substituído.");
            }
            catch(TargetInvocationException ex) { throw new InvalidOperationException("Falha na preparação local do VRCFury: "+(ex.InnerException?.Message??ex.Message),ex.InnerException??ex); }
            notes.Add("Preparação local concluída via preprocessadores do SDK/VRCFury, em uma cópia do avatar ("+fury+" componentes VRCFury na origem). Nenhum upload executado.");
        }
    }
}
