using System;
using System.Reflection;
using Cysharp.Threading.Tasks;
using HybridCLR;
using JetBrains.Annotations;
using UnityEngine;
using YooAsset;

namespace Game.AOT
{
    /// <summary>
    /// 热更新程序集加载器：补充AOT元数据 - 加载热更DLL - 反射调用入口
    /// 所有DLL都是YooAsset里的TextAsset (.dll.bytes)
    /// </summary>
    public static class HotUpdateLoader
    {
        /// <summary>
        /// 需要补充元数据的 AOT 程序集（资源地址=文件名 含.dll）
        /// 按需填写：只有热更代码里用到了这些程序集里的泛型实例化时才需要
        /// 文件不存在会自动跳过
        /// </summary>
        private static readonly string[] AOTMetaAssemblies =
        {
            "mscorlib.dll",
            "System.dll",
            "System.Core.dll",
        };

        public static Assembly HotUpdateAssembly { get; private set; }

        public static async UniTask<bool> LoadAsync(Action<string, float> onProgress)
        {
            var package = YooAssetBoot.Package;

            //补充元数据：必须在加载热更程序集之前完成
            for (int i = 0; i < AOTMetaAssemblies.Length; i++)
            {
                string location = AOTMetaAssemblies[i];
                if (package.IsLocationValid(location) == false)
                {
                    Debug.Log($"[HotUpdate] 跳过未收集的补充元数据：{location}");
                    continue;
                }

                onProgress?.Invoke($"补充元数据 {location}", (float)i / (AOTMetaAssemblies.Length + 1));

                var metaHandle = package.LoadAssetAsync<TextAsset>(location);
                await metaHandle;
                if (metaHandle.Status != EOperationStatus.Succeeded)
                {
                    Debug.LogError($"[HotUpdate] 读取元数据失败 {location}：{metaHandle.Error}");
                    return false;
                }

                byte[] metaBytes = metaHandle.GetAssetObject<TextAsset>().bytes;
                metaHandle.Release();

                //编辑器下这个方法是个空实现（直接返回OK），真机才会真实生效
                LoadImageErrorCode err =
                    RuntimeApi.LoadMetadataForAOTAssembly(metaBytes, HomologousImageMode.SuperSet);
                if (err != LoadImageErrorCode.OK)
                {
                    Debug.LogError($"[HotUpdate] 补充元数据失败 {location}: {err}");
                    return false;
                }
            }
            //加载热更主程序集
            onProgress?.Invoke("加载热更程序集", 0.9f);

            var dllHandle = package.LoadAssetAsync<TextAsset>(GameConsts.HotUpdateDllLocation);
            await dllHandle;
            if (dllHandle.Status != EOperationStatus.Succeeded)
            {
                Debug.LogError($"[HotUpdate] 读取DLL失败：{dllHandle.Error}");
                return false;
            }

            byte[] dllBytes = dllHandle.GetAssetObject<TextAsset>().bytes;
            dllHandle.Release();

            if (dllBytes == null || dllBytes.Length == 0)
            {
                Debug.LogError("[HotUpdate] DLL 内容为空，检查Assets/GameRes/HotUpdate 下的 .dll.bytes");
                return false;
            }

            //Editor 里 Unity 已经吧 Game.HotUpdate 编译并加载过了
            //若这里再Assemble.Load 会出现两个同名程序集
            //热更类型无法转换成 AOT 侧的 IHotUpdateEntry

            HotUpdateAssembly = FindLoadedAssembly(GameConsts.HotUpdateAssenbleName);
            if (HotUpdateAssembly == null)
            {
                HotUpdateAssembly = Assembly.Load(dllBytes);
                Debug.Log($"[HotUpdate] 从字节码加载程序集 {dllBytes.Length} bytes");
            }
            else
            {
                Debug.Log("[HotUpdate] 复用Editor已加载的程序集");
            }

            onProgress?.Invoke("热更程序集就绪", 1f);
            return true;
        }

        /// <summary>
        /// 反射调用热更入口
        /// </summary>
        /// <returns></returns>
        public static bool StartGame()
        {
            if(HotUpdateAssembly == null)
            {
                Debug.LogError("[HotUpdate] 程序集未加载，先调用 LaodAsync");
                return false;
            }

            Type entryType = HotUpdateAssembly.GetType(GameConsts.HotUpdateEntryTypeName);
            if(entryType == null)
            {
                Debug.LogError($"[HotUpdate] 找不到入口类型：{GameConsts.HotUpdateEntryTypeName}");
                return false;
            }

            if(typeof(IHotUpdateEntry).IsAssignableFrom(entryType) == false)
            {
                Debug.LogError($"[HotUpdate] {entryType.FullName} 未实现 IHotUpdateEntry");
                return false;
            }

            var entry = (IHotUpdateEntry)Activator.CreateInstance(entryType);
            entry.OnStart();
            return true;
        }

        private static Assembly FindLoadedAssembly(string assemblyName)
        {
            foreach(var assemby in AppDomain.CurrentDomain.GetAssemblies())
            {
                if(assemby.GetName().Name == assemblyName)
                {
                    return assemby;
                }
            }
            return null;
        }
    }
}