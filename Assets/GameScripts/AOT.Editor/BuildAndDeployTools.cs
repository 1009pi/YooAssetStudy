using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;
using HybridCLR;
using HybridCLR.Commands;

namespace Game.AOT.EditorTools
{
    /// <summary>
    /// 一键发布：HybridCLR 生成 → 拷 DLL → YooAsset 构建 → 自动升版本 → 部署到本地 CDN。
    /// </summary>
    public static class BuildAndDeployTools
    {
        private const string PackageName = "MainPackage";
        private const string HotUpdateAssemblyName = "Game.HotUpdate";
        private const string DeployRoot = @"D:\CDNServer";

        /// <summary>需要随热更一起分发的 AOT 补充元数据（按需增删）</summary>
        private static readonly string[] AOTMetaAssemblies =
        {
            "mscorlib.dll",
            "System.dll",
            "System.Core.dll",
        };

        [MenuItem("Tools/HotUpdate Framework/一键构建并发布")]
        public static void BuildAndDeploy()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("提示", "请先退出播放模式", "好");
                return;
            }

            if (BundleCollectorSettingData.Setting.Packages.Exists(p => p.PackageName == PackageName) == false)
            {
                Debug.LogError($"[Build] 收集器里找不到包 {PackageName}");
                return;
            }

            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            if (EditorUtility.DisplayDialog("确认",
                $"为 {target} 构建并发布到 {DeployRoot}？", "开始", "取消") == false)
                return;

            try
            {
                EditorUtility.DisplayProgressBar("构建热更", "HybridCLR Generate/All", 0.1f);
                PrebuildCommand.GenerateAll();

                EditorUtility.DisplayProgressBar("构建热更", "拷贝 DLL 与补充元数据", 0.3f);
                CopyHotUpdateDlls(target);
                CopyAOTMetaDlls(target);
                AssetDatabase.Refresh();

                string version = GetNextVersion(target);
                EditorUtility.DisplayProgressBar("构建资源", $"YooAsset 构建 {version}", 0.5f);
                string builtDir = BuildPackage(target, version);

                EditorUtility.DisplayProgressBar("发布", "部署到 CDN 目录", 0.9f);
                DeployToCdn(builtDir, version, target);

                Debug.Log($"[Build] 发布完成：{target} / {version} -> {DeployRoot}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Build] 失败: {e}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        #region 拷贝 DLL
        private static void CopyHotUpdateDlls(BuildTarget target)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string src = Path.Combine(projectRoot, "HybridCLRData", "HotUpdateDlls",
                target.ToString(), HotUpdateAssemblyName + ".dll");

            if (File.Exists(src) == false)
                throw new FileNotFoundException("找不到热更 DLL，检查 HybridCLR 是否生成成功", src);

            string dstDir = Path.Combine(Application.dataPath, "GameRes", "HotUpdate");
            Directory.CreateDirectory(dstDir);

            string dst = Path.Combine(dstDir, HotUpdateAssemblyName + ".dll.bytes");
            File.Copy(src, dst, true);
            Debug.Log($"[Build] DLL -> {dst} ({new FileInfo(dst).Length} bytes)");
        }

        private static void CopyAOTMetaDlls(BuildTarget target)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string srcDir = Path.Combine(projectRoot, "HybridCLRData",
                "AssembliesPostIl2CppStrip", target.ToString());
            string dstDir = Path.Combine(Application.dataPath, "GameRes", "HotUpdate");
            Directory.CreateDirectory(dstDir);

            foreach (string name in AOTMetaAssemblies)
            {
                string src = Path.Combine(srcDir, name);
                if (File.Exists(src) == false)
                {
                    Debug.Log($"[Build] 跳过缺失的元数据: {name}");
                    continue;
                }

                string dst = Path.Combine(dstDir, name + ".bytes");
                File.Copy(src, dst, true);
                Debug.Log($"[Build] 元数据 {name} -> {new FileInfo(dst).Length / 1024} KB");
            }
        }
        #endregion

        #region 资源构建
        private static string BuildPackage(BuildTarget target, string version)
        {
            bool uniqueBundleName = BundleCollectorSettingData.Setting.UniqueBundleName;
            string shaderBundleName = DefaultBundlePackRule
                .CreateShadersPackRuleResult()
                .GetBundleName(PackageName, uniqueBundleName);

            var parameters = new ScriptableBuildParameters
            {
                BuildOutputRoot = BundleBuilderHelper.GetDefaultBuildOutputRoot(),
                BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot(),
                BuildPipeline = nameof(EBuildPipeline.ScriptableBuildPipeline),
                BuildBundleType = (int)EBundleType.AssetBundle,
                BuildTarget = target,
                PackageName = PackageName,
                PackageVersion = version,
                EnableSharePackRule = true,
                VerifyBuildingResult = true,
                FileNameStyle = EFileNameStyle.HashName,
                BundledCopyOption = EBundledCopyOption.ClearAndCopyAll,
                BundledCopyParams = string.Empty,
                CompressOption = ECompressOption.LZ4,
                ClearBuildCacheFiles = false,
                UseAssetDependencyDB = true,
                BuiltinShadersBundleName = shaderBundleName,
            };

            BuildResult result = new ScriptableBuildPipeline().Run(parameters, true);
            if (result.Success == false)
                throw new Exception($"构建失败[{result.FailedTask}]: {result.ErrorInfo}");

            return result.OutputPackageDirectory;
        }
        #endregion

        #region 版本与部署
        private static string GetNextVersion(BuildTarget target)
        {
            string packageRoot = GetPackageRoot(target);
            string versionFile = Path.Combine(packageRoot, PackageName + ".version");

            if (File.Exists(versionFile))
            {
                string current = File.ReadAllText(versionFile).Trim();
                string[] parts = current.Split('.');
                if (parts.Length == 3 && int.TryParse(parts[2], out int patch))
                    return $"{parts[0]}.{parts[1]}.{patch + 1}";

                Debug.LogWarning($"[Build] 版本号无法自增（{current}），改用时间戳");
            }

            return DateTime.Now.ToString("yyyy.MM.dd.HHmm");
        }

        private static void DeployToCdn(string builtDirectory, string version, BuildTarget target)
        {
            string packageRoot = GetPackageRoot(target);
            string versionDirectory = Path.Combine(packageRoot, version);

            if (Directory.Exists(versionDirectory))
                Directory.Delete(versionDirectory, true);

            CopyDirectory(builtDirectory, versionDirectory);

            // 版本文件必须放在包根目录，客户端"请求版本"读的就是它
            string builtVersionFile = Path.Combine(builtDirectory, PackageName + ".version");
            if (File.Exists(builtVersionFile) == false)
                throw new FileNotFoundException("构建产物缺少版本文件", builtVersionFile);

            File.Copy(builtVersionFile, Path.Combine(packageRoot, PackageName + ".version"), true);

            Debug.Log($"[Build] 已部署 {versionDirectory}");
            EditorUtility.RevealInFinder(packageRoot);
        }

        private static string GetPackageRoot(BuildTarget target)
        {
            return Path.Combine(DeployRoot, GetPlatformFolder(target), PackageName);
        }

        private static string GetPlatformFolder(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return "Windows";
                case BuildTarget.Android:
                    return "Android";
                case BuildTarget.iOS:
                    return "iOS";
                case BuildTarget.WebGL:
                    return "WebGL";
                default:
                    return target.ToString();
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(source, destination));

            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                File.Copy(file, file.Replace(source, destination), true);
        }
        #endregion
    }
}