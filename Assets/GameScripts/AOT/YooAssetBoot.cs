using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace Game.AOT
{
    /// <summary>
    /// 资源系统启动器。
    /// 只负责"把资源包初始化好"，不负责版本/清单/下载（那些属于 ResourceUpdater）。
    /// </summary>
    public static class YooAssetBoot
    {
        /// <summary>当前使用的资源包</summary>
        public static ResourcePackage Package { get; private set; }

        /// <summary>当前资源版本，由 ResourceUpdater 在清单加载成功后写入</summary>
        public static string PackageVersion { get; internal set; }

        /// <summary>联机模式下使用的远端地址服务</summary>
        public static RemoteService RemoteService { get; private set; }

        public static async UniTask<bool> InitializeAsync()
        {
            // ① 初始化资源系统
            if (YooAssets.IsInitialized == false)
                YooAssets.Initialize();

            // ② 创建（或复用）资源包
            if (YooAssets.TryGetPackage(GameConsts.PackageName, out var existPackage))
                Package = existPackage;
            else
                Package = YooAssets.CreatePackage(GameConsts.PackageName);

            // ③ 已经初始化过就直接返回，避免重复初始化报错
            if (Package.InitializeStatus == EOperationStatus.Succeeded)
                return true;

            // ④ 按运行模式初始化文件系统（到这一步为止，不碰版本和清单）
            var options = CreateInitializeOptions();
            if (options == null)
                return false;

            var initOp = Package.InitializePackageAsync(options);
            await initOp;
            if (initOp.Status != EOperationStatus.Succeeded)
            {
                Debug.LogError($"[Boot] 资源包初始化失败: {initOp.Error}");
                return false;
            }

            Debug.Log($"[Boot] 资源包就绪：{GameConsts.PackageName}");
            return true;
        }

        private static InitializePackageOptions CreateInitializeOptions()
        {
#if UNITY_EDITOR
            if (GameConsts.UseEditorSimulateInEditor)
            {
                var simulateRoot = YooAssetBootHooks.EditorSimulatePackageRootProvider?.Invoke();
                if (string.IsNullOrEmpty(simulateRoot))
                {
                    Debug.LogError("[Boot] 编辑器模拟构建未就绪，检查 Game.AOT.Editor 是否编译成功。");
                    return null;
                }

                return new EditorSimulateModeOptions
                {
                    EditorFileSystemParameters =
                        FileSystemParameters.CreateDefaultEditorFileSystemParameters(simulateRoot),
                };
            }
#endif

            RemoteService = new RemoteService(GameConsts.HostServerRoot);
            return new HostPlayModeOptions
            {
                BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(),
                CacheFileSystemParameters = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(RemoteService),
            };
        }
    }
}