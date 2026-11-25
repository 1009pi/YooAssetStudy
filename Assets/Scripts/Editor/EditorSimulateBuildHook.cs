using Game.AOT;
using UnityEditor;
using YooAsset;
using YooAsset.Editor;

namespace Game.AOT.Editor
{
    /// <summary>
    /// Editor启动时把 模拟构建 注入到运行时启动器
    /// [InitializeOnLoad] 会在编辑器加载 / 脚本重编译 / 进入播放模式时执行静态构造函数
    /// </summary>
    [InitializeOnLoad]
    internal static class EditorSimulateBuildHook
    {
        static EditorSimulateBuildHook()
        {
            YooAssetBootHooks.EditorSimulatePackageRootProvider = BuildSimulatePackage;
        }

        /// <summary>
        /// 执行编辑器模拟构建 返回清单所在目录
        /// 输出位置：Bundle/{平台}/{包名}/Simulate
        /// </summary>
        /// <returns></returns>
        private static string BuildSimulatePackage()
        {
            var buildParam = new PackageBuildParameters(CommonData.PackageName);
            buildParam.BuildPipelineName = EBuildPipeline.EditorSimulateBuildPipeline.ToString();
            buildParam.BuildBundleType = (int)EBundleType.VirtualAssetBundle;

            var result = BundleSimulateBuilder.SimulateBuild(buildParam);
            return result.PackageRootDirectory;
        }
    }
}