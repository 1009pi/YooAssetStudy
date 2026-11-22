#if UNITY_EDITOR
using System;

namespace Game.AOT
{
    /// <summary>
    /// 运行时程序集与编辑器程序集之间的 桥
    /// 运行时侧只声明委托 具体实现由编辑器程序集在加载时注入
    /// 这样 Game.AOT 就不需要引用 YooAsset.Editor
    /// </summary>
    public static class YooAssetBootHooks
    {
        /// <summary>
        /// 返回编辑器模拟资源包的根目录
        /// </summary>
        public static Func<string> EditorSimulatePackageRootProvider;
    }
}
#endif