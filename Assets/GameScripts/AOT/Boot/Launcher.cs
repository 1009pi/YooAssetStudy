using Cysharp.Threading.Tasks;
using System;
using System.Collections;
using UnityEngine;

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
    }
}