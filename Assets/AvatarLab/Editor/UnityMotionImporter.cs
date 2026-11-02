using UnityEditor;
namespace AvatarLab.Editor
{
    public sealed class UnityMotionImporter : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if(!assetPath.StartsWith("Assets/ThirdParty/UnityCharacterMotion/")) return;
            var importer=(ModelImporter)assetImporter;
            importer.animationType=ModelImporterAnimationType.Human;
            importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar=null;importer.importCameras=false;importer.importLights=false;
            importer.materialImportMode=ModelImporterMaterialImportMode.None;
            var clips=importer.clipAnimations;
            foreach(var clip in clips)
            {
                clip.events=new UnityEngine.AnimationEvent[0];
                clip.lockRootPositionXZ=true;clip.lockRootRotation=true;
                clip.keepOriginalPositionXZ=false;clip.keepOriginalOrientation=true;
                clip.keepOriginalPositionY=true;
            }
            importer.clipAnimations=clips;
        }
    }
}
