using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.AOT
{
    /// <summary>
    /// 全局常量：资源包名 热更新程序集名等在各处复用的字符串
    /// 集中在一处 避免在多份代码里手写字符串导致不一致
    /// </summary>
    public static class CommonData
    {
        //资源包名称
        public const string PackageName = "MainPackage";

        public const string HotAssembliesManifestLocation = "hotupdate_manifest";

        /// <summary>热更程序集（含清单、AOT元数据）在收集器里的标签</summary>
        public const string HotAssembliesTag = "assemblies";

        ///<summary>热更入口类型全名</summary>
        public const string HotAssemblyEntryTypeName = "Game.Hot.GameEntry.GameEntry";

        /// <summary>CDN 跟地址 不带平台</summary>
        public const string CdnRoot = "http://127.0.0.1:8080";

        /// <summary>编辑器下是否走 模拟模式</summary>
        public static bool UseEditorSimulateInEditor = false;

        /// <summary>完整的包跟地址</summary>
        public static string HostServerRoot => $"{CdnRoot}/{GetPlatformFolder()}/{PackageName}";

        public static string GetPlatformFolder()
        {
#if UNITY_ANDROID
            return "Android";
#elif UNITY_IOS
            return "iOS";
#elif UNITY_WEBGL
            return "WebGL";
#elif UNITY_STANDALONE_WIN
            return "Windows";
#else
            return "Other";
#endif
        }
    }
}