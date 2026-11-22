using Cysharp.Threading.Tasks;
using Game.AOT;
using UnityEngine;
using YooAsset;

namespace Game.HotUpdate
{
    public class GameEntry : IHotUpdateEntry
    {
        /// <summary>
        /// 用来验证代码热更，改变这个值 - 重新生成DLL - 只更新服务器资源
        /// 老安装包重启后应该打印新值
        /// </summary>
        public const string VersionTag = "hotupdate-v1.0.5";

        private const string PrefabLocation = "MainPanel";

        private AssetHandle _handle;

        public void OnStart()
        {
            Debug.Log($"[HotUpdate] 启动，VersionTag = {VersionTag}");
            LoadMainPanel().Forget();
        }

        private async UniTaskVoid LoadMainPanel()
        {
            var package = YooAssetBoot.Package;

            if(package.IsLocationValid(PrefabLocation) == false)
            {
                Debug.LogError($"[HotUpdate] 地址不存在：{PrefabLocation}");
                return;
            }

            _handle = package.LoadAssetAsync<GameObject>(PrefabLocation);
            await _handle;
            if (_handle.Status != EOperationStatus.Succeeded)
            {
                Debug.LogError($"[HotUpdate] 加载失败：{_handle.Error}");
                return;
            }

            var instantiateOp = _handle.InstantiateAsync();
            await instantiateOp;
            if (instantiateOp.Status != EOperationStatus.Succeeded) {
                Debug.LogError($"[HotUpdate] 实例化失败：{instantiateOp.Error}");
                return;
            }

            Object.DontDestroyOnLoad(instantiateOp.Result);
            Debug.Log($"[HotUpdate] {PrefabLocation} 加载成功");
        }
    }
}