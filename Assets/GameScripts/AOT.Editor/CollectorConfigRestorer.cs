using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace Game.AOT.EditorTools
{
    /// <summary>
    /// 重建 Bundle Collector 配置（包 + 2 组 + 4 个收集器）。
    /// 用于配置资产被空配置覆盖后的快速恢复。
    /// </summary>
    public static class CollectorConfigRestorer
    {
        private const string SettingPath = "Assets/BundleCollectorSetting.asset";

        [MenuItem("Tools/HotUpdate Framework/Restore Collector Config")]
        public static void Restore()
        {
            if (AssetDatabase.LoadAssetAtPath<BundleCollectorSetting>(SettingPath) == null)
            {
                var created = ScriptableObject.CreateInstance<BundleCollectorSetting>();
                AssetDatabase.CreateAsset(created, SettingPath);
            }

            var setting = AssetDatabase.LoadAssetAtPath<BundleCollectorSetting>(SettingPath);
            setting.Packages.Clear();
            setting.ShowPackageView = true;
            setting.UniqueBundleName = false;
            setting.ShowEditorAlias = false;

            var package = new BundleCollectorPackage
            {
                PackageName = "MainPackage",
                PackageDesc = "主资源包",
                EnableAddressable = true,
                SupportExtensionless = true,
                LocationToLower = false,
                IncludeAssetGUID = false,
                AutoCollectShaders = true,
                IgnoreRuleName = nameof(NormalIgnoreRule),
            };

            var resGroup = new BundleCollectorGroup
            {
                GroupName = "Res",
                GroupDesc = "普通游戏资源",
                AssetTags = string.Empty,
                ActiveRuleName = nameof(EnableGroup),
            };
            resGroup.Collectors.Add(CreateCollector("Assets/GameRes/Common", "common"));
            resGroup.Collectors.Add(CreateCollector("Assets/GameRes/UI", "ui"));
            resGroup.Collectors.Add(CreateCollector("Assets/GameRes/Configs", "config"));

            var hotGroup = new BundleCollectorGroup
            {
                GroupName = "HotUpdate",
                GroupDesc = "热更 DLL 与元数据",
                AssetTags = "hotupdate",
                ActiveRuleName = nameof(EnableGroup),
            };
            hotGroup.Collectors.Add(CreateCollector("Assets/GameRes/HotUpdate", string.Empty));

            package.Groups.Add(resGroup);
            package.Groups.Add(hotGroup);
            setting.Packages.Add(package);

            EditorUtility.SetDirty(setting);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[YooAsset] 收集器配置已重建：MainPackage / Res + HotUpdate");
        }

        private static BundleCollector CreateCollector(string collectPath, string tags)
        {
            return new BundleCollector
            {
                CollectPath = collectPath,
                CollectorType = ECollectorType.MainAssetCollector,
                AddressRuleName = nameof(AddressByFileName),
                PackRuleName = nameof(PackDirectory),
                FilterRuleName = nameof(CollectAll),
                AssetTags = tags,
            };
        }
    }
}