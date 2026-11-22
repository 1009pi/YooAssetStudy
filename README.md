# YooAssetStudy

> YooAsset 3.0.7 + HybridCLR + UniTask 热更框架 · 代码笔记
>
> 工程环境：Unity 6000.3.24f1 / 资源包 `MainPackage` / 本地 CDN `D:\CDNServer`
> 运行时包根地址：`http://127.0.0.1:8080/Windows/MainPackage`

---

## 目录

1. [代码结构与职责](#1-代码结构与职责)
2. [启动时序](#2-启动时序)
3. [程序集与依赖约束](#3-程序集与依赖约束)
4. [YooAsset 3.0 API 速查](#4-yooasset-30-api-速查)
5. [Handle 与资源生命周期](#5-handle-与资源生命周期)
6. [封装层实现要点](#6-封装层实现要点)
7. [HybridCLR 运行时](#7-hybridclr-运行时)
8. [编辑器脚本 API](#8-编辑器脚本-api)
9. [收集规则与地址](#9-收集规则与地址)
10. [代码级易错点](#10-代码级易错点)
11. [构建发布流程](#11-构建发布流程)
12. [小结论](#12-小结论)

---

## 1. 代码结构与职责

```
Assets/GameScripts/
├── AOT/                        Game.AOT
│   ├── Game.AOT.asmdef
│   ├── Boot/                   启动编排
│   │   ├── Launcher.cs
│   │   ├── BootFlow.cs
│   │   └── BootUI.cs
│   ├── Resource/               资源系统
│   │   ├── YooAssetBoot.cs
│   │   ├── YooAssetBootHooks.cs
│   │   ├── RemoteService.cs
│   │   └── ResourceUpdater.cs
│   ├── HotUpdate/              热更程序集加载（AOT 侧）
│   │   └── HotUpdateLoader.cs
│   ├── Common/                 跨层共享
│   │   ├── GameConsts.cs
│   │   ├── BootTypes.cs
│   │   └── IHotUpdateEntry.cs
│   └── Legacy/                 已废弃，待删除
│       └── BootTest.cs
├── AOT.Editor/                 Game.AOT.Editor  仅编辑器
│   ├── Game.AOT.Editor.asmdef
│   ├── BuildAndDeployTools.cs
│   ├── CollectorConfigRestorer.cs
│   └── EditorSimulateBuildHook.cs
└── HotUpdate/                  Game.HotUpdate   热更业务
    ├── Game.HotUpdate.asmdef
    └── GameEntry.cs
```

> 目录只做物理分层，命名空间仍统一为 `Game.AOT`；将来某个模块要独立成程序集时再顺势拆命名空间。
> `Legacy/BootTest.cs` 没有任何场景引用，是旧的测试入口，确认后可删除。

| 类型 | 职责 | 不该做的事 |
|---|---|---|
| `GameConsts` | 包名、CDN 地址、程序集名、模式开关 | 不写逻辑 |
| `RemoteService` | 文件名 → URL | 不知道清单内容 |
| `YooAssetBoot` | 初始化 YooAssets + 建包 + 挂文件系统 | **不碰版本、清单、下载** |
| `ResourceUpdater` | 请求版本 → 加载清单 → 差量下载 | 不加载业务资源 |
| `HotUpdateLoader` | 补充元数据 → 加载 DLL → 反射入口 | 不知道业务逻辑 |
| `BootFlow` | 状态机、重试/回退决策 | 不直接操作 UI |
| `BootUI` | 进度与失败弹窗，只订阅事件 | 不控制流程 |
| `Launcher` | 场景唯一启动点 | — |
| `GameEntry`（热更侧） | 业务入口，实现 `IHotUpdateEntry` | 不引用编辑器程序集 |

---

## 2. 启动时序

```
Launcher.Start
  └─ BootFlow.RunAsync
       ├─ 1. YooAssetBoot.InitializeAsync
       │      YooAssets.Initialize → CreatePackage → InitializePackageAsync
       ├─ 2. ResourceUpdater.UpdateManifestAsync
       │      RequestPackageVersionAsync → RemoteService.Version = v
       │      LoadPackageManifestAsync(v)
       ├─ 3. ResourceUpdater.DownloadAsync(["hotupdate"])
       ├─ 4. HotUpdateLoader.LoadAsync
       │      LoadMetadataForAOTAssembly → Assembly.Load(dllBytes) → 反射入口
       ├─ 5. ResourceUpdater.DownloadAsync(null)
       ├─ 6. ResourceUpdater.MarkVersionUsable
       └─ 7. HotUpdateLoader.StartGame → GameEntry.OnStart
```

顺序约束（都是硬性的）：

- 版本 / 清单必须在 `RemoteService.Version` 赋值之后；
- 更新必须在加载任何业务资源之前；
- 元数据必须在 `Assembly.Load` 之前；
- 代码 DLL 先下载，美术资源后下载（避免"新资源 + 旧代码"）。

---

## 3. 程序集与依赖约束

| 约束 | 代码体现 |
|---|---|
| 运行时不能引用编辑器程序集 | `Game.AOT` 不引用 `YooAsset.Editor` / `HybridCLR.Editor` |
| AOT 不能引用热更程序集 | 通过 `IHotUpdateEntry` + 反射 |
| 依赖单向 | `Game.HotUpdate` → `Game.AOT` |
| 热更程序集不能进安装包 | asmdef `Auto Referenced = false` + 登记 HybridCLR Hot Update Assemblies |

asmdef 引用一览：

| asmdef | 引用 |
|---|---|
| `Game.AOT` | `YooAsset`、`UniTask`、`HybridCLR.Runtime` |
| `Game.AOT.Editor` | `Game.AOT`、`YooAsset.Editor`、`HybridCLR.Editor`（仅 Editor 平台） |
| `Game.HotUpdate` | `Game.AOT`、`YooAsset`、`UniTask` |

> `HybridCLR.Runtime` 是 asmdef 名；代码里的命名空间是 `HybridCLR`。

---

## 4. YooAsset 3.0 API 速查

```csharp
// 系统
YooAssets.Initialize();                              // 全进程一次
YooAssets.IsInitialized
YooAssets.CreatePackage("MainPackage");
YooAssets.TryGetPackage("MainPackage", out var p);
YooAssets.Destroy();

// 包初始化
package.InitializeStatus                            // EOperationStatus
package.InitializePackageAsync(InitializePackageOptions)

// 版本 / 清单
package.RequestPackageVersionAsync(RequestPackageVersionOptions(bool appendTimeTicks, int timeout))
       → RequestPackageVersionOperation.PackageVersion
package.LoadPackageManifestAsync(new LoadPackageManifestOptions(version, timeout))
package.GetPackageVersion()                         // 需要已加载清单，否则 NRE
package.IsLocationValid(location)                   // 查清单，地址自检

// 下载
package.CreateResourceDownloader(new ResourceDownloaderOptions(maxConcurrency, retryCount))
package.CreateResourceDownloader(new ResourceDownloaderOptions(string[] tags, max, retry))
package.CreateBundleDownloader(new BundleDownloaderOptions(assetInfos, downloadDependencies, max, retry))
package.GetDownloadSize(location)

// 加载
package.LoadAssetAsync<T>(location, priority)
package.LoadSubAssetsAsync<T>(location)
package.LoadAllAssetsAsync<T>(location)
package.LoadSceneAsync(location, LoadSceneMode.Single, ..., allowSceneActivation, priority)
```

### Options 类（3.0 与 2.x 的分界线）

```csharp
new EditorSimulateModeOptions { EditorFileSystemParameters = ... }
new OfflinePlayModeOptions    { BuiltinFileSystemParameters = ... }
new HostPlayModeOptions       { BuiltinFileSystemParameters = ..., CacheFileSystemParameters = ... }
new WebPlayModeOptions        { WebServerFileSystemParameters = ..., WebNetworkFileSystemParameters = ... }
```

文件系统参数工厂：

```csharp
FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot);
FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
FileSystemParameters.CreateDefaultSandboxFileSystemParameters(remoteService);
```

`HostPlayModeOptions` 的顺序很重要：**最后一个文件系统是主文件系统**，版本请求与清单加载只走它（沙盒放最后）。

### 3.0 与 2.x 差异

| 2.x | 3.0 |
|---|---|
| `HostPlayModeParameters` | `HostPlayModeOptions` |
| `InitializeAsync(params)` | `InitializePackageAsync(options)` |
| `RequestPackageVersionAsync(bool,int)` | `RequestPackageVersionAsync(RequestPackageVersionOptions)` |
| `UpdatePackageManifestAsync(version)` | `LoadPackageManifestAsync(new LoadPackageManifestOptions(version, 60))` |
| `CreateResourceDownloader(max,retry)` | `CreateResourceDownloader(new ResourceDownloaderOptions(max,retry))` |
| `BeginDownload()` | `StartDownload()` |
| `DownloadUpdateCallback = ...` | `DownloadProgressChanged += ...`（事件） |
| `EOperationStatus.Succeed` | `EOperationStatus.Succeeded` |
| `IRemoteServices.GetRemoteMainURL/FallbackURL` | `IRemoteService.GetRemoteUrls(fileName) : IReadOnlyList<string>` |
| `YooAssets.SetDefaultPackage()` + 静态加载 | 已移除，全部走 `ResourcePackage` |

### 状态与异步的两个坑

```csharp
public enum EOperationStatus { None, Processing, Succeeded, Failed }
```

1. **`await op` 不抛异常**（`OperationAwaiter.GetResult()` 是空实现）：

```csharp
await op;
if (op.Status != EOperationStatus.Succeeded)
    Debug.LogError(op.Error);
```

2. **操作必须"先启动后等待"**：

```csharp
downloader.StartDownload();   // 提交到调度器
await downloader;             // 否则永远不完成
```

`CreateResourceDownloader` 只创建对象，不启动。

---

## 5. Handle 与资源生命周期

```csharp
AssetHandle handle = package.LoadAssetAsync<GameObject>("MainPanel");
await handle;
if (handle.Status != EOperationStatus.Succeeded) return;

GameObject prefab = handle.GetAssetObject<GameObject>();   // 或 handle.AssetObject
var instOp = handle.InstantiateAsync();
await instOp;
GameObject go = instOp.Result;

handle.Release();     // 实例销毁后再释放
```

- `HandleBase`：`Status` / `Error` / `IsDone` / `IsValid` / `Release()` / `GetAwaiter()`
- `AssetHandle`：`AssetObject` / `GetAssetObject<T>()` / `InstantiateAsync()` / `InstantiateSync()` / `WaitForAsyncComplete()`
- `InstantiateOperation`：`Result` / `Status` / `Error`

---

## 6. 封装层实现要点

### 6.1 `RemoteService : IRemoteService`

```csharp
public IReadOnlyList<string> GetRemoteUrls(string fileName)
{
    if (fileName.EndsWith(".version")) return new[] { $"{root}/{fileName}" };
    return new[] { $"{root}/{Version}/{fileName}" };
}
```

- 3.0 传进来的只有**文件名**（`MainPackage.version`、`xxx.bundle`），包名和版本目录由业务拼；
- 返回的是候选地址列表，可做 CDN 主备；
- `Version` 必须在加载清单前赋值。

### 6.2 `YooAssetBoot`：只做初始化

```csharp
YooAssets.Initialize();
if (YooAssets.TryGetPackage(name, out var exist)) Package = exist;
else Package = YooAssets.CreatePackage(name);

if (Package.InitializeStatus == EOperationStatus.Succeeded) return true;   // 幂等

var initOp = Package.InitializePackageAsync(options);
await initOp;
if (initOp.Status != EOperationStatus.Succeeded) return false;
```

职责边界：**不请求版本、不加载清单**。这两个动作依赖 `RemoteService.Version`，属于更新层。

### 6.3 `ResourceUpdater`：三段式

```csharp
// 请求（失败回退本地记录）
var (ok, version) = await RequestVersionAsync();

// 清单
RemoteService.Version = version;                 // 顺序关键
await package.LoadPackageManifestAsync(new LoadPackageManifestOptions(version, 60));
PackageVersion = version;

// 下载（tags = null 表示全部）
var downloader = package.CreateResourceDownloader(options);
if (downloader.TotalDownloadCount == 0) return true;
downloader.DownloadProgressChanged += args => ...;
downloader.DownloadError += args => ...;
downloader.StartDownload();
await downloader;
return downloader.Status == EOperationStatus.Succeeded;
```

`LastGoodVersion` 只在**下载成功后**写入，避免把"清单成功但资源不全"的版本记成可用。

### 6.4 `HotUpdateLoader`

```csharp
// ① 补充元数据：必须在 Assembly.Load 之前
LoadImageErrorCode err = RuntimeApi.LoadMetadataForAOTAssembly(bytes, HomologousImageMode.SuperSet);
if (err != LoadImageErrorCode.OK) return false;

// ② 热更主程序集
Assembly asm = FindLoadedAssembly("Game.HotUpdate") ?? Assembly.Load(dllBytes);
// 编辑器里 Unity 已加载同程序集，再 Load 会产生同名双份 → 类型转换失败

// ③ 反射入口 + 接口转换
Type type = asm.GetType("Game.HotUpdate.GameEntry");
if (typeof(IHotUpdateEntry).IsAssignableFrom(type) == false) return false;
((IHotUpdateEntry)Activator.CreateInstance(type)).OnStart();
```

### 6.5 `BootFlow`：状态机 + 决策回传

```csharp
Func<Action<string, float>, UniTask<bool>>   // 每个步骤的统一签名
RunStepAsync(state, title, action, from, to, canSkipUpdate)

// 失败询问 UI
_decisionSource = new UniTaskCompletionSource<BootDecision>();
Failed?.Invoke(failure);
BootDecision decision = await _decisionSource.Task;

// UI 回传
BootFlow.Answer(BootDecision.Retry);
```

进度映射：

```csharp
Report(state, Mathf.Lerp(from, to, Mathf.Clamp01(local)), message);
```

`canSkipUpdate` 只在**加载热更程序集之前**为 true（状态一致性）。

### 6.6 `BootUI`：只订阅事件

```csharp
BootFlow.ProgressChanged += OnProgress;
BootFlow.Failed += OnFailed;
BootFlow.Finished += OnFinished;
```

- 全部代码构建，无预制体：Canvas + CanvasScaler(1920×1080) + Image + Text + Button；
- 进度条用 `RectTransform.anchorMax.x` 驱动，不用 Slider；
- 中文字体 `Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 20)`。

### 6.7 `Launcher`

```csharp
private async UniTaskVoid Start()   // 生命周期方法不能用 async UniTask
{
    bool ok = await BootFlow.RunAsync();
    ...
}
```

热更侧同样使用 `UniTaskVoid` + `.Forget()`：

```csharp
private async UniTaskVoid LoadMainPanel() { ... }
public void OnStart() { LoadMainPanel().Forget(); }
```

---

## 7. HybridCLR 运行时

```csharp
using HybridCLR;

public enum HomologousImageMode { Consistent, SuperSet }

public enum LoadImageErrorCode
{
    OK = 0,
    BAD_IMAGE,
    NOT_IMPLEMENT,
    AOT_ASSEMBLY_NOT_FIND,
    HOMOLOGOUS_ONLY_SUPPORT_AOT_ASSEMBLY,
    HOMOLOGOUS_ASSEMBLY_HAS_LOADED,
    INVALID_HOMOLOGOUS_MODE,
    PDB_BAD_FILE,
    UNKNOWN_IMAGE_FORMAT,
    UNSUPPORT_FORMAT_VERSION,
    UNMATCH_FORMAT_VARIANT,
}

RuntimeApi.LoadMetadataForAOTAssembly(byte[] dllBytes, HomologousImageMode mode);
```

注意：

- 编辑器下 `LoadMetadataForAOTAssembly` 是空实现，直接返回 `OK`；真机才生效；
- 补充元数据只对 AOT 程序集有效，误传热更 DLL 会得到 `HOMOLOGOUS_ONLY_SUPPORT_AOT_ASSEMBLY`；
- 泛型问题典型表现：编辑器正常，真机 `MissingMethodException` / `ExecutionEngineException`。

---

## 8. 编辑器脚本 API

```csharp
// 模拟构建（编辑器调试）
var p = new PackageBuildParameters(GameConsts.PackageName)
{
    BuildPipelineName = nameof(EBuildPipeline.EditorSimulateBuildPipeline),
    BuildBundleType = (int)EBundleType.VirtualAssetBundle,
};
string root = BundleSimulateBuilder.SimulateBuild(p).PackageRootDirectory;

// 真机构建（等价于 Bundle Builder 的 Build 按钮）
var parameters = new ScriptableBuildParameters
{
    BuildOutputRoot = BundleBuilderHelper.GetDefaultBuildOutputRoot(),
    BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot(),
    BuildPipeline = nameof(EBuildPipeline.ScriptableBuildPipeline),
    BuildBundleType = (int)EBundleType.AssetBundle,
    BuildTarget = EditorUserBuildSettings.activeBuildTarget,
    PackageName = "MainPackage",
    PackageVersion = version,
    EnableSharePackRule = true,
    VerifyBuildingResult = true,
    FileNameStyle = EFileNameStyle.HashName,
    BundledCopyOption = EBundledCopyOption.ClearAndCopyAll,
    CompressOption = ECompressOption.LZ4,
};
BuildResult result = new ScriptableBuildPipeline().Run(parameters, true);
// result.Success / ErrorInfo / FailedTask / OutputPackageDirectory
```

```csharp
// 热更 DLL 一键生成（等价于菜单 HybridCLR/Generate/All）
HybridCLR.Commands.PrebuildCommand.GenerateAll();

// 收集器配置
BundleCollectorSettingData.Setting.Packages / .UniqueBundleName
BundleCollectorSettingData.SaveFile();
AssetDatabase.LoadAssetAtPath<BundleCollectorSetting>("Assets/BundleCollectorSetting.asset");

// 强制重新导入（避免"文件新了但包里是旧的"）
AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
```

---

## 9. 收集规则与地址

```csharp
// AddressByFileName 的实现
Path.GetFileNameWithoutExtension(data.AssetPath)
```

| 文件 | 生成的地址 |
|---|---|
| `MainPanel.prefab` | `MainPanel` |
| `Game.HotUpdate.dll.bytes` | `Game.HotUpdate.dll` |
| `mscorlib.dll.bytes` | `mscorlib.dll` |

- `PackDirectory`：每个子文件夹一个 bundle；
- 文件名寻址要求**全局唯一**，否则构建报 address 冲突；
- DLL 必须命名为 `.dll.bytes`，直接放 `.dll` 会被 Unity 当托管插件编进包；
- 收集器路径不存在时 `CheckConfigError` 抛 `Collect path is invalid`；
- 按标签下载时，**未打任何标签的 bundle 视为公共依赖，始终包含**。

构建产物（`Bundles/<平台>/<包>/<版本>/`）：

```
MainPackage.version      版本号
MainPackage_<版本>.hash  清单哈希
MainPackage_<版本>.bytes 清单二进制（运行时读它）
MainPackage_<版本>.json  清单可读版
MainPackage_<版本>.report
*.bundle
```

---

## 10. 代码级易错点

| 代码症状 | 根因 |
|---|---|
| `await` 返回但对象为 null，不报错 | `OperationAwaiter.GetResult()` 空实现 |
| 协程/UniTask 卡住不动 | 下载器没 `StartDownload()` |
| `[Remote] Version 未设置` | `LoadPackageManifestAsync` 早于 `RemoteService.Version` 赋值 |
| 新资源加载报 bundle 缺失 | 更新未在加载资源前完成，或 tags 下载不全 |
| 编辑器正常、真机 `MissingMethodException` | 泛型缺补充元数据 |
| `未实现 IHotUpdateEntry`（仅编辑器） | 重复 `Assembly.Load` 产生同名程序集 |
| 构建报 403 `BundleTempSizeIsZero` | bundle 内资源总字节数为 0（空文件） |
| 改了热更代码但包里还是旧的 | 资产未重新导入 / 构建缓存 / 源码未保存 |
| 出包报热更程序集冲突 | asmdef `Auto Referenced` 未关闭，或未登记 Hot Update Assemblies |
| 编辑器缓存清不掉 | 沙盒目录是 `Library/yoo`，不是 `persistentDataPath` |
| 收集器配置全空 | `Assets/BundleCollectorSetting.asset` 被空配置覆盖 |

---

## 11. 构建发布流程

一键脚本 `BuildAndDeployTools` 的正确顺序：

```
1. PrebuildCommand.GenerateAll()
      → HybridCLRData/HotUpdateDlls/<Target>/Game.HotUpdate.dll
      → HybridCLRData/AssembliesPostIl2CppStrip/<Target>/*.dll
2. 拷贝 DLL      → Assets/GameRes/HotUpdate/Game.HotUpdate.dll.bytes
3. 拷贝元数据    → Assets/GameRes/HotUpdate/<name>.bytes
4. AssetDatabase.ImportAsset(..., ForceUpdate)   强制重新导入
5. new ScriptableBuildPipeline().Run(params, true)
6. 版本号 = 读取 CDN 上 MainPackage.version + 1
7. 部署：<平台>/<包>/<版本>/ 全量拷贝 + 包根 MainPackage.version 覆盖
```

服务端回滚：把 `MainPackage.version` 改回旧版本号即可。YooAsset 不做版本比较，只认服务器给的版本；**旧版本目录不要删**。

---

## 12. 小结论

1. `HostPlayMode` = 内置（只读）+ 沙盒（可写）双文件系统，**沙盒是主文件系统**。
2. 沙盒的 `.hash` / `.bytes` 存在就跳过下载 → 断网可回退的前提。
3. `GetPackageVersion()` 依赖已加载清单，别在初始化阶段调用。
4. 版本号是更新的唯一依据，YooAsset 不做版本比较。
5. 编辑器模拟模式直接读 `Assets`，不会走 `Assembly.Load`（复用已编译程序集），只有真机验证得了热更。
6. `PrebuildCommand.GenerateAll()` 编译 DLL 用的是 **active build target**，切平台后要重新生成。
7. 空文件不能做资源占位：0 字节 bundle 会以 `ErrorCode 403` 中断构建。
