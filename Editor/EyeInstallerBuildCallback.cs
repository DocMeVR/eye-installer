#if VRC_SDK_VRCSDK3
using VRC.SDKBase.Editor.BuildPipeline;

namespace DocMe.EyeInstaller
{
    /// <summary>Puts the avatar back to its installed look before the VRChat SDK copies it for an upload: the live
    /// preview and "Show the original eyes" would otherwise end up in the upload. The SDK finds this class by itself.</summary>
    public class EyeInstallerBuildCallback : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => -10000;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            EyeInstallerWindow.EndTemporaryViewsEverywhere();
            return true;
        }
    }
}
#endif
