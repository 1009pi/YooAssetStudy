using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace Game.AOT
{
    public static class ResourceUpdater
    {
        ///<summary>上次完整可用版本</summary>
        private const string LastGoodVersionKey = "yoo_last_good_version";

        public static string LastGoodVersion => PlayerPrefs.GetString(LastGoodVersionKey, string.Empty);

        ///<summary>请求远端版本；失败时回退到上次可用版本</summary>
        public static async UniTask<(bool ok, string version)> RequestVersionAsync()
        {
            var versionOp = YooAssetBoot.Package.RequestPackageVersionAsync(
                new RequestPackageVersionOptions(appendTimeTicks: true, timeout: 10));
            await versionOp;

            if (versionOp.Status == EOperationStatus.Succeeded)
                return (true, versionOp.PackageVersion);

            Debug.LogWarning($"[Update] 请求版本失败: {versionOp.Error}");

            string local = LastGoodVersion;
            if (string.IsNullOrEmpty(local))
                return (false, string.Empty);

            Debug.LogWarning($"[Update] 使用上次可用版本 {local}");
            return (true, local);
        }

        ///<summary>只加载清单 不改写可用版本记录</summary>
        public static async UniTask<bool> LoadManifestAsync(string version, Action<string, float> onProgress)
        {
            if(string.IsNullOrEmpty(version))
            {
                Debug.LogError("[Update] 版本号为空");
                return false;
            }

            //远端 URL 依赖版本目录 必须在加载清单前设置
            if(YooAssetBoot.RemoteService != null)
                YooAssetBoot.RemoteService.Version = version;

            onProgress?.Invoke($"加载清单 {version}", 0.3f);
            var manifestOp = YooAssetBoot.Package.LoadPackageManifestAsync(
                new LoadPackageManifestOptions(version, 60));
            await manifestOp;

            if (manifestOp.Status != EOperationStatus.Succeeded)
            {
                Debug.LogError($"[Update] 加载清单失败: {manifestOp.Error}");
                return false;
            }

            YooAssetBoot.PackageVersion = version;
            Debug.Log($"[Update] 清单就绪，版本 {version}");
            return true;
        }

        ///<summary>正常更新入口：请求版本 + 加载清单</summary>
        public static async UniTask<bool> UpdateManifestAsync(Action<string, float> onProgress)
        {
            var (ok, version) = await RequestVersionAsync();
            if (ok == false)
            {
                Debug.LogError("[Update] 无法获取版本（无网络且无本地版本）");
                return false;
            }

            onProgress?.Invoke("检查资源清单", 0f);
            return await LoadManifestAsync(version, onProgress);
        }

        ///<summary>回退到上次完整可用的版本</summary>
        public static async UniTask<bool> RollbackToLastGoodAsync(Action<string, float> onProgress)
        {
            string version = LastGoodVersion;
            if(string.IsNullOrEmpty(version))
            {
                Debug.LogWarning("[Update] 没有可回退的版本");
                return false;
            }

            Debug.Log($"[Update] 回退到版本 {version}");
            return await LoadManifestAsync(version, onProgress);
        }

        ///<summary>整个流程成功后调用，记录这个版本是完整的</summary>
        public static void MarkVersionUsable()
        {
            string version = YooAssetBoot.PackageVersion;
            if (string.IsNullOrEmpty(version))
                return;

            PlayerPrefs.SetString(LastGoodVersionKey, version);
            PlayerPrefs.Save();
            Debug.Log($"[Update] 标记可用版本 {version}");
        }

        ///<summary>按标签下载，tag = null 表示全部</summary>
        public static async UniTask<bool> DownloadAsync(string[] tags, Action<string, float> onProgress)
        {
            var package = YooAssetBoot.Package;
            var options = tags == null
                ? new ResourceDownloaderOptions(maximumConcurrency: 10, retryCount: 3)
                : new ResourceDownloaderOptions(tags, maximumConcurrency: 10, retryCount: 3);

            var downloader = package.CreateResourceDownloader(options);

            string label = tags == null ? "all resources" : string.Join(",", tags);
            if(downloader.TotalDownloadCount == 0)
            {
                onProgress?.Invoke($"{label}已是最新", 1f);
                return true;
            }

            float totalMB = downloader.TotalDownloadBytes / 1024f / 1024f;
            Debug.Log($"[Update] {label}: {downloader.TotalDownloadCount} 个文件，{totalMB:F2} MB");

            downloader.DownloadProgressChanged += args =>
                onProgress?.Invoke($"{label} {args.CurrentDownloadCount}/{args.TotalDownloadCount}", args.Progress);
            downloader.DownloadError += args =>
                Debug.LogError($"[Update] 下载失败 {args.FileName}: {args.ErrorInfo}");

            downloader.StartDownload();
            await downloader;

            if (downloader.Status != EOperationStatus.Succeeded)
            {
                Debug.LogError($"[Update] {label} 下载未完成: {downloader.Error}");
                return false;
            }
            return true;
        }
    }
}