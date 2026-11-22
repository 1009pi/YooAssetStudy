using UnityEngine;
using Cysharp.Threading.Tasks;
using YooAsset;
using System.Collections;
using Game.AOT;

/// <summary>
/// 启动入口 初始化 - 清单 - 先下代码 - 加载程序集 - 下剩余资源 - 启动游戏
/// </summary>
public class BootTest : MonoBehaviour
{
    [SerializeField]
    private string _prefabLocation = "MainPanel";

    private AssetHandle _handler;

    private async UniTaskVoid Start()
    {
        if (await YooAssetBoot.InitializeAsync() == false)
            return;

        if (await ResourceUpdater.UpdateManifestAsync(Log) == false)
            return;

        //只下热更代码 保证脚本尽快可用
        if (await ResourceUpdater.DownloadAsync(new[] { "hotupdate" }, Log) == false)
            return;

        if(await HotUpdateLoader.LoadAsync(Log) == false)
            return;

        //再下剩余资源
        if (await ResourceUpdater.DownloadAsync(null, Log) == false)
            return;

        //进入热更逻辑
        if (HotUpdateLoader.StartGame() == false)
            return;

        Debug.Log($"[Boot] 进入游戏，资源版本 {YooAssetBoot.PackageVersion}");
    }

    private static void Log(string state, float progress)
    {
        Debug.Log($"[Boot] {state} {progress:P0}");
    }

    private void OnDestroy()
    {
        _handler?.Release();
        _handler = null;
    }
}
