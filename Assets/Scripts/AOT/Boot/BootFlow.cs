using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Game.AOT
{
    /// <summary>
    /// 启动加载流程
    /// </summary>
    public static class BootFlow
    {
        public static event Action<BootProgress> ProgressChanged;
        public static event Action<BootFailure> Failed;
        public static event Action<bool> Finished;

        ///<summary>本次启动是否使用了回退版本</summary>
        public static bool UsedFallback { get; private set; }

        private static UniTaskCompletionSource<BootDecision> _decisionSource;

        public static async UniTask<bool> RunAsync()
        {
            UsedFallback = false;
            bool success = false;

            try
            {
                success = await RunStepsAsync();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Boot] 启动流程异常: {e}");
            }

            Finished?.Invoke(success);
            return success;
        }

        private static async UniTask<bool> RunStepsAsync()
        {
            if (await RunStepAsync(BootState.Initializing, "初始化资源系统",
                _ => YooAssetBoot.InitializeAsync(), 0f, 0.05f, false) == false)
                return false;

            if (await RunStepAsync(BootState.CheckingVersion, "检查更新",
                report => ResourceUpdater.UpdateManifestAsync(report), 0.05f, 0.15f, true) == false)
                return false;

            if (await RunStepAsync(BootState.DownloadingCode, "下载热更代码",
                report => ResourceUpdater.DownloadAsync(new[] { CommonData.HotAssembliesTag }, report),
                0.15f, 0.45f, true) == false)
                return false;

            if (await RunStepAsync(BootState.LoadingScripts, "加载热更程序集",
                    report => HotAssemblyLoader.LoadAsync(report), 0.45f, 0.55f, false) == false)
                return false;

            if (await RunStepAsync(BootState.DownloadingResources, "下载游戏资源",
                report => ResourceUpdater.DownloadAsync(null, report), 0.55f, 0.95f, false) == false)
                return false;

            // 全部成功才把当前版本标记为"可用"
            if (UsedFallback == false)
                ResourceUpdater.MarkVersionUsable();

            Report(BootState.EnteringGame, 0.97f, "启动游戏");
            if (HotAssemblyLoader.StartGame() == false)
            {
                Report(BootState.Failed, 1f, "启动游戏失败");
                return false;
            }

            Report(BootState.Completed, 1f, "完成");
            return true;
        }

        /// <summary>
        /// 执行一个步骤，失败就询问决策。
        /// canSkipUpdate=true 只用于"还没加载任何热更代码"的阶段。
        /// </summary>
        private static async UniTask<bool> RunStepAsync(
            BootState state, string title,
            Func<Action<string, float>, UniTask<bool>> action,
            float from, float to, bool canSkipUpdate)
        {
            Report(state, from, title);

            while (true)
            {
                Action<string, float> report = (message, local) =>
                    Report(state, Mathf.Lerp(from, to, Mathf.Clamp01(local)), message);

                if (await action(report))
                    return true;

                bool hasFallback = canSkipUpdate
                    && string.IsNullOrEmpty(ResourceUpdater.LastGoodVersion) == false;

                BootDecision decision = await AskAsync(
                    new BootFailure(state, title + "失败", hasFallback));

                if (decision == BootDecision.Retry)
                    continue;

                if (decision == BootDecision.SkipUpdate && hasFallback)
                {
                    if (await RollbackAsync())
                        return true;
                    continue;
                }

                Report(BootState.Failed, 1f, title + "失败");
                return false;
            }
        }

        private static async UniTask<bool> RollbackAsync()
        {
            Report(BootState.CheckingVersion, 0.05f, "回退到上一个可用版本");

            bool ok = await ResourceUpdater.RollbackToLastGoodAsync(
                (message, local) =>
                    Report(BootState.CheckingVersion, Mathf.Lerp(0.05f, 0.15f, local), message));

            if (ok)
            {
                UsedFallback = true;
                Debug.Log($"[Boot] 已回退到版本 {YooAssetBoot.PackageVersion}");
            }
            else
            {
                Debug.LogError("[Boot] 回退失败");
            }
            return ok;
        }

        private static async UniTask<BootDecision> AskAsync(BootFailure failure)
        {
            // 没有 UI 时不能卡死，直接中止
            if (Failed == null)
            {
                Debug.LogError($"[Boot] {failure.Message}（无界面订阅，自动中止）");
                return BootDecision.Abort;
            }

            _decisionSource = new UniTaskCompletionSource<BootDecision>();
            Failed.Invoke(failure);

            BootDecision decision = await _decisionSource.Task;
            _decisionSource = null;
            return decision;
        }

        /// <summary>由 UI 调用：回传用户选择</summary>
        public static void Answer(BootDecision decision)
        {
            _decisionSource?.TrySetResult(decision);
        }

        private static void Report(BootState state, float value, string message)
        {
            ProgressChanged?.Invoke(new BootProgress(state, value, message));
        }

    }
}