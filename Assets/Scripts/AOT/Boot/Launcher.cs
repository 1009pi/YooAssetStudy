using Cysharp.Threading.Tasks;
using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.AOT
{
    public class Launcher : MonoBehaviour
    {
        [SerializeField] private bool _showBootUI = true;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            if (_showBootUI)
                gameObject.AddComponent<BootUI>();
        }

        private async UniTaskVoid Start()
        {
            bool success = await BootFlow.RunAsync();
            if(success == false)
            {
                Debug.LogError("[Boot] 启动流程失败，游戏未进入");
                return;
            }

            Debug.Log($"[Boot] 进入游戏，版本 {YooAssetBoot.PackageVersion}，回退={BootFlow.UsedFallback}");
        }

        #region 已移除功能

        //private const string clientConfigUrl = "http://127.0.0.1:8080/clientConfig.json";

        //public static async UniTask<bool> UpdateClientConfig()
        //{
        //    string json = await DownloadTextAsync(clientConfigUrl);
        //    if (string.IsNullOrEmpty(json))
        //    {
        //        Debug.LogError("远程配置文件为null");
        //        return false;
        //    }

        //    ClientConfig config = null;

        //    try
        //    {
        //        config = JsonUtility.FromJson<ClientConfig>(json);
        //    }
        //    catch (Exception e)
        //    {
        //        Debug.LogError($"[Config] 配置解析失败：{e}");
        //    }

        //    if(config == null)
        //    {
        //        return false;
        //    }

        //    //赋值给CommonData
        //    CommonData.clientConfig = config;

        //    //返回成功
        //    return true;
        //}

        //private static async UniTask<string> DownloadTextAsync(string url, int timeout = 10)
        //{
        //    using (UnityWebRequest request = UnityWebRequest.Get(url))
        //    {
        //        request.timeout = timeout;
        //        await request.SendWebRequest();

        //        if (request.result != UnityWebRequest.Result.Success)
        //        {
        //            return null;
        //        }

        //        return request.downloadHandler.text;
        //    }
        //}
        #endregion
    }
}