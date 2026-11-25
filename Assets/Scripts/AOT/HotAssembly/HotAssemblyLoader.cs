using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using HybridCLR;
using UnityEngine;
using YooAsset;

namespace Game.AOT
{
    /// <summary>
    /// 热更程序集加载器：读取程序集清单 - 补充AOT元数据 - 按清单顺序加载热更DLL - 反射调用入口
    /// 清单和DLL都是YooAsset里的资源 (.dll.bytes 是 TextAsset)
    /// </summary>
    public static class HotAssemblyLoader
    {
        /// <summary>当前客户端支持的清单协议版本</summary>
        private const int SupportedProtocolVersion = 1;

        /// <summary>定义了入口类型的程序集</summary>
        public static Assembly EntryAssembly { get; private set; }

        public static Dictionary<string, Assembly> loadedAssemblyDic = new Dictionary<string, Assembly>();

        public static async UniTask<bool> LoadAsync(Action<string, float> onProgress)
        {
            ResourcePackage package = YooAssetBoot.Package;
            if (package == null)
            {
                Debug.LogError("[HotAssembly] 资源包未初始化，先调用 YooAssetBoot.InitializeAsync");
                return false;
            }

            //读取程序集清单
            onProgress?.Invoke("读取程序集清单", 0.1f);

            var manifestHandle = package.LoadAssetAsync<TextAsset>(CommonData.HotAssembliesManifestLocation);
            await manifestHandle;
            if (manifestHandle.Status != EOperationStatus.Succeeded)
            {
                Debug.LogError($"[HotAssembly] 清单加载失败 {CommonData.HotAssembliesManifestLocation}：{manifestHandle.Error}");
                return false;
            }

            HotAssemblyManifest manifest = ParseManifest(manifestHandle.GetAssetObject<TextAsset>());
            manifestHandle.Release();
            if (manifest == null)
                return false;

            //补充元数据：必须在加载热更程序集之前完成
            if (await LoadAOTMetadataAsync(package, manifest.aotMetadata, onProgress) == false)
                return false;

            //按清单顺序加载：被依赖的程序集排在前面
            var loadedAssemblies = new List<Assembly>(manifest.assemblies.Length);
            for (int i = 0; i < manifest.assemblies.Length; i++)
            {
                HotAssembly info = manifest.assemblies[i];
                onProgress?.Invoke($"加载程序集 {info.name}", 0.6f + 0.4f * (i + 1) / manifest.assemblies.Length);

                Assembly assembly = await LoadAssemblyAsync(package, info);
                if (assembly == null)
                    return false;

                loadedAssemblies.Add(assembly);
            }

            //入口程序集 = 定义了入口类型的那个程序集
            for (int i = 0; i < loadedAssemblies.Count; i++)
            {
                if (loadedAssemblies[i].GetType(CommonData.HotAssemblyEntryTypeName) != null)
                {
                    EntryAssembly = loadedAssemblies[i];
                    break;
                }
            }

            if (EntryAssembly == null)
            {
                Debug.LogError($"[HotAssembly] 清单里的程序集都不包含入口类型：{CommonData.HotAssemblyEntryTypeName}");
                return false;
            }

            onProgress?.Invoke("热更程序集就绪", 1f);
            return true;
        }

        /// <summary>
        /// 加载程序集
        /// </summary>
        /// <param name="dllLocation"></param>
        /// <returns></returns>
        public static async UniTask<bool> LoadAssembleAsync(string dllLocation)
        {
            if (string.IsNullOrEmpty(dllLocation))
            {
                //传递的dllLocation为空
                return false;
            }
            if (loadedAssemblyDic.ContainsKey(dllLocation))
            {
                //已加载的Assembly
                return false;
            }

            ResourcePackage package = YooAssetBoot.Package;
            if (package == null) return false;

            var dllHandle = package.LoadAssetAsync<TextAsset>(dllLocation);
            await dllHandle;
            if (dllHandle.Status != EOperationStatus.Succeeded)
            {
                Debug.LogError($"[LoadAssemble] dllHandle 加载失败: {dllHandle.Error}");
                return false;
            }

            byte[] dllBytes = dllHandle.GetAssetObject<TextAsset>().bytes;
            dllHandle.Release();

            if (dllBytes == null || dllBytes.Length == 0)
            {
                Debug.Log($"DLL内容为空：{dllLocation}");
                return false;
            }

            Assembly assembly = FindLoadedAssembly(dllLocation);
            if (assembly == null)
            {
                assembly = Assembly.Load(dllBytes);
                Debug.Log($"从字节码加载程序集 {dllLocation}:{dllBytes.Length} bytes");
                //加入字典中
                loadedAssemblyDic[dllLocation] = assembly;
            }
            else
            {
                //程序集已被加载 Editor模式则已自动加载
                //也加入字典中
                if (!loadedAssemblyDic.ContainsKey(dllLocation))
                {
                    loadedAssemblyDic[dllLocation] = assembly;
                }
            }

            return true;
        }

        /// <summary>
        /// 反射调用热更入口
        /// </summary>
        /// <returns></returns>
        public static bool StartGame()
        {
            if (EntryAssembly == null)
            {
                Debug.LogError("[HotAssembly] 程序集未加载，先调用 LoadAsync");
                return false;
            }

            Type entryType = EntryAssembly.GetType(CommonData.HotAssemblyEntryTypeName);
            if (entryType == null)
            {
                Debug.LogError($"[HotAssembly] 找不到入口类型：{CommonData.HotAssemblyEntryTypeName}");
                return false;
            }

            if (typeof(IHotAssemblyEntry).IsAssignableFrom(entryType) == false)
            {
                Debug.LogError($"[HotAssembly] {entryType.FullName} 未实现 IHotAssemblyEntry");
                return false;
            }

            var entry = (IHotAssemblyEntry)Activator.CreateInstance(entryType);
            entry.OnStart();
            return true;
        }

        private static HotAssemblyManifest ParseManifest(TextAsset asset)
        {
            if (asset == null)
            {
                Debug.LogError($"[HotAssembly] 清单不是 TextAsset：{CommonData.HotAssembliesManifestLocation}");
                return null;
            }

            HotAssemblyManifest manifest = null;
            try
            {
                manifest = JsonUtility.FromJson<HotAssemblyManifest>(asset.text);
            }
            catch (Exception e)
            {
                Debug.LogError($"[HotAssembly] 清单解析失败：{e}");
                return null;
            }

            if (manifest == null || manifest.assemblies == null || manifest.assemblies.Length == 0)
            {
                Debug.LogError("[HotAssembly] 清单为空或没有 assemblies 字段");
                return null;
            }

            if (manifest.protocolVersion > SupportedProtocolVersion)
            {
                Debug.LogError($"[HotAssembly] 清单协议版本 {manifest.protocolVersion} 高于当前客户端支持的 {SupportedProtocolVersion}，请更新安装包");
                return null;
            }

            return manifest;
        }

        private static async UniTask<bool> LoadAOTMetadataAsync(
            ResourcePackage package, HotAssembly[] entries, Action<string, float> onProgress)
        {
            if (entries == null || entries.Length == 0)
            {
                Debug.LogWarning("[HotAssembly] 清单里没有 aotMetadata，跳过补充元数据");
                return true;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                HotAssembly info = entries[i];
                onProgress?.Invoke($"补充元数据 {info.name}", 0.2f + 0.3f * (i + 1) / entries.Length);

                //缺元数据只会在真机运行到对应泛型时才崩，所以这里直接失败
                if (package.IsLocationValid(info.location) == false)
                {
                    Debug.LogError($"[HotAssembly] 清单里的元数据地址不存在：{info.location}");
                    return false;
                }

                var metaHandle = package.LoadAssetAsync<TextAsset>(info.location);
                await metaHandle;
                if (metaHandle.Status != EOperationStatus.Succeeded)
                {
                    Debug.LogError($"[HotAssembly] 读取元数据失败 {info.location}：{metaHandle.Error}");
                    return false;
                }

                byte[] metaBytes = metaHandle.GetAssetObject<TextAsset>().bytes;
                metaHandle.Release();

                //编辑器下这个方法是个空实现（直接返回OK），真机才会真实生效
                LoadImageErrorCode err =
                    RuntimeApi.LoadMetadataForAOTAssembly(metaBytes, HomologousImageMode.SuperSet);
                if (err != LoadImageErrorCode.OK)
                {
                    Debug.LogError($"[HotAssembly] 补充元数据失败 {info.location}: {err}");
                    return false;
                }
            }

            return true;
        }

        private static async UniTask<Assembly> LoadAssemblyAsync(ResourcePackage package, HotAssembly info)
        {
            if (string.IsNullOrEmpty(info.name) || string.IsNullOrEmpty(info.location))
            {
                Debug.LogError("[HotAssembly] 清单条目的 name 或 location 为空");
                return null;
            }

            //Editor 里 Unity 已经把热更程序集编译并加载过
            //若这里再 Assembly.Load 会出现两个同名程序集，热更类型无法转换成 AOT 侧的 IHotAssemblyEntry
            Assembly assembly = FindLoadedAssembly(info.name);
            if (assembly != null)
            {
                Debug.Log($"[HotAssembly] 复用Editor已加载的程序集 {info.name}");
                return assembly;
            }

            if (package.IsLocationValid(info.location) == false)
            {
                Debug.LogError($"[HotAssembly] 清单里的程序集地址不存在：{info.location}");
                return null;
            }

            var dllHandle = package.LoadAssetAsync<TextAsset>(info.location);
            await dllHandle;
            if (dllHandle.Status != EOperationStatus.Succeeded)
            {
                Debug.LogError($"[HotAssembly] 读取DLL失败 {info.location}：{dllHandle.Error}");
                return null;
            }

            byte[] dllBytes = dllHandle.GetAssetObject<TextAsset>().bytes;
            dllHandle.Release();

            if (dllBytes == null || dllBytes.Length == 0)
            {
                Debug.LogError($"[HotAssembly] DLL 内容为空：{info.location}");
                return null;
            }

            assembly = Assembly.Load(dllBytes);
            Debug.Log($"[HotAssembly] 从字节码加载程序集 {info.name}：{dllBytes.Length} bytes");
            return assembly;
        }

        private static Assembly FindLoadedAssembly(string assemblyName)
        {
            foreach (var assemby in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assemby.GetName().Name == assemblyName)
                {
                    return assemby;
                }
            }
            return null;
        }
    }

    /// <summary>热更程序集清单，对应 hotupdate_manifest.json</summary>
    [Serializable]
    public sealed class HotAssemblyManifest
    {
        /// <summary>清单协议版本，高于客户端支持的版本时拒绝加载</summary>
        public int protocolVersion;

        /// <summary>需要补充元数据的 AOT 程序集，在热更程序集之前加载</summary>
        public HotAssembly[] aotMetadata;

        /// <summary>按加载顺序排列，被依赖的排前面</summary>
        public HotAssembly[] assemblies;
    }

    /// <summary>清单里的一个程序集</summary>
    [Serializable]
    public struct HotAssembly
    {
        /// <summary>程序集名，不含 .dll，Editor 下用它找已加载的程序集</summary>
        public string name;

        /// <summary>dll 在 YooAsset 里的资源地址</summary>
        public string location;
    }
}