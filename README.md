# YooAssetStudy 热更框架说明

基于 YooAsset（资源热更）+ HybridCLR（代码热更）的 Unity 热更框架。

| 项        | 版本        |
| --------- | ----------- |
| Unity     | 6000.3.23f1 |
| YooAsset  | 3.0.7       |
| HybridCLR | 9.1.0       |
| UniTask   | 2.5.11      |

整体分两部分：

- **AOT 部分（`Game.AOT`）**：随安装包发布，只负责引导、资源更新、加载热更程序集，不含框架和业务代码。
- **热更部分（`Game.Hot.*`）**：框架和业务都在这里，以 dll 的形式当作资源下发。

命名约定：程序侧（程序集、类、清单）统一用 `HotAssembly` / `HotAssemblies`；`HotUpdate` 只用于流程和资源侧（热更流程、测试版本号）。

---

## 一、程序集划分

| 程序集               | 目录                           | 职责                               | 可热更 | 依赖                                             |
| -------------------- | ------------------------------ | ---------------------------------- | ------ | ------------------------------------------------ |
| `Game.AOT`           | `Assets/Scripts/AOT`           | 启动流程、资源更新、热更程序集加载 | 否     | YooAsset、UniTask、HybridCLR Runtime             |
| `Game.AOT.Editor`    | `Assets/Scripts/Editor`        | 编辑器工具                         | 否     | YooAsset.Editor                                  |
| `Game.Hot.Framework` | `Assets/Scripts/Hot/Framework` | 框架底层，不含业务                 | 是     | 无                                               |
| `Game.Hot.GameMain`  | `Assets/Scripts/Hot/Game`      | 游戏通用玩法                       | 是     | Framework                                        |
| `Game.Hot.GameEntry` | `Assets/Scripts/Hot/Entry`     | 热更入口和业务                     | 是     | Framework、GameMain、Game.AOT、YooAsset、UniTask |

### 规则

1. **依赖方向只能单向**：`GameEntry → GameMain → Framework`。Framework 不能依赖另外两个，否则分层失效。
2. **AOT 不能引用热更程序集**。热更程序集可以引用 `Game.AOT`，但只用 `IHotAssemblyEntry`、`CommonData`、`YooAssetBoot` 这类稳定的引导接口。
3. **入口靠接口，不靠引用**：入口类实现 `Game.AOT.IHotAssemblyEntry`，AOT 用反射找到它并调用 `OnStart()`。
4. **程序集名 = asmdef 的 `name` 字段**，与文件名、命名空间无关。热更程序集统一以 `Game.Hot.` 开头。
5. 目前 Framework 和 GameMain 里只有占位脚本，划分是为了约束依赖方向，随业务填充。

### 新增一个热更程序集

1. 新建 asmdef，`name` 以 `Game.Hot.` 开头，按分层填好引用。
2. 在 `HybridCLR → Settings → 热更新程序集` 里注册这个 asmdef。
3. 执行 `HybridCLR → Generate → All`。
4. 把生成的 `xxx.dll.bytes` 放进 `Assets/HotAssemblies/`。
5. 在 `hotupdate_manifest.json` 的 `assemblies` 里加一条，被依赖的排在前面。

---

## 二、资源划分

YooAsset 只有一个资源包 `MainPackage`，资源按目录分成两组：

| 目录                              | 收集器组      | 标签          | 内容                                 |
| --------------------------------- | ------------- | ------------- | ------------------------------------ |
| `Assets/HotAssemblies`            | HotAssemblies | `assemblies`  | 热更 dll、AOT 元数据 dll、程序集清单 |
| `Assets/HotResources/Global`      | HotResources  | `global`      | 全局资源（Common、UI）               |
| `Assets/HotResources/Configs`     | HotResources  | `config`      | 配置表                               |
| `Assets/HotResources/Level_Lobby` | HotResources  | `level_lobby` | 大厅关卡（场景、Prefab、RawAssets）  |

### 规则

1. **所有收集器统一配置**：地址规则 `AddressByFileName`，打包规则 `PackDirectory`（每个子目录打成一个 Bundle）。
2. **资源地址 = 文件名去掉最后一个扩展名**：
   - `Game.Hot.GameEntry.dll.bytes` → `Game.Hot.GameEntry.dll`
   - `hotupdate_manifest.json` → `hotupdate_manifest`（不带 `.json`）
   - `MainPanel.prefab` → `MainPanel`
3. **标签决定下载时机**。`assemblies` 在加载热更代码之前下载，其余资源在代码加载之后下载。新增一类需要提前下载的资源时，才需要新标签。
4. 一个资源可以有多个标签，收集器里用 `;` 分隔；不同收集器可以共用同一个标签。
5. 按标签下载时，没打标签的 Bundle 只要被依赖也会一起下载。
6. **新增关卡**：在 `HotResources` 下新建 `Level_X` 目录，加一个收集器，标签用 `level_x`。
7. 代码用到的地址和标签集中写在 `CommonData`，不要在其他脚本里手写字符串。

---

## 三、热更流程（AOT 基础框架）

入口是场景里的 `Launcher`，它调用 `BootFlow.RunAsync()`。

| 步骤 | 状态                 | 做什么                                      | 失败后可否跳过 |
| ---- | -------------------- | ------------------------------------------- | -------------- |
| 1    | Initializing         | 初始化 YooAsset 资源包                      | 否             |
| 2    | CheckingVersion      | 请求版本号，加载对应清单                    | 是             |
| 3    | DownloadingCode      | 下载标签 `assemblies` 的资源                | 是             |
| 4    | LoadingScripts       | 读清单、补充 AOT 元数据、按顺序加载热更 dll | 否             |
| 5    | DownloadingResources | 下载剩余全部资源                            | 否             |
| 6    | EnteringGame         | 反射调用入口 `OnStart()`                    | 否             |

### 加载热更程序集（步骤 4）

`HotAssemblyLoader.LoadAsync` 依次做四件事：

1. 读取 `hotupdate_manifest.json`，校验 `protocolVersion`。
2. 按 `aotMetadata` 列表逐个调用 `HybridCLR.RuntimeApi.LoadMetadataForAOTAssembly`。
3. 按 `assemblies` 列表顺序 `Assembly.Load`。
4. 在已加载的程序集里找定义了入口类型的那个，记为 `EntryAssembly`。

Editor 下 Unity 已经编译并加载了热更程序集，加载器按程序集名直接复用，不会重复 `Assembly.Load`；补充元数据在 Editor 下是空实现。这两步只在真机上真正生效。

### 失败处理

- **重试**：重新执行当前步骤。
- **跳过更新**：只在步骤 2、3 可用，回退到上次启动成功的版本（记录在 PlayerPrefs）。热更代码一旦加载就无法卸载，所以步骤 4 之后不允许回退。
- **中止**：流程结束。

启动全部成功后，当前版本被 `MarkVersionUsable` 记为“上次可用版本”。

---

## 四、程序集清单 hotupdate_manifest.json

和 dll 放在一起，随资源热更，路径 `Assets/HotAssemblies/hotupdate_manifest.json`：

```json
{
  "protocolVersion": 1,
  "aotMetadata": [
    { "name": "mscorlib", "location": "mscorlib.dll" },
    { "name": "System", "location": "System.dll" },
    { "name": "System.Core", "location": "System.Core.dll" },
    { "name": "UniTask", "location": "UniTask.dll" },
    { "name": "YooAsset", "location": "YooAsset.dll" }
  ],
  "assemblies": [
    { "name": "Game.Hot.Framework", "location": "Game.Hot.Framework.dll" },
    { "name": "Game.Hot.GameMain", "location": "Game.Hot.GameMain.dll" },
    { "name": "Game.Hot.GameEntry", "location": "Game.Hot.GameEntry.dll" }
  ]
}
```

| 字段              | 说明                                                         |
| ----------------- | ------------------------------------------------------------ |
| `protocolVersion` | 清单格式版本。高于客户端支持的版本时拒绝加载，提示更新安装包 |
| `aotMetadata`     | 需要补充元数据的 AOT 程序集，在热更 dll 之前加载             |
| `assemblies`      | 热更程序集，被依赖的排在前面                                 |
| `name`            | 程序集名，不带 `.dll`                                        |
| `location`        | 在 YooAsset 里的资源地址                                     |

AOT 元数据 dll 取自 `Library/HybridCLR/AssembliesPostIl2CppStrip/<平台>/`，列表和 `HybridCLR/Generate/AOTGenericReferences.cs` 里的 `PatchedAOTAssemblyList` 保持一致。不同平台的元数据 dll 不能混用。

---

## 五、AOT 脚本一览

| 脚本                               | 说明                                                                                 |
| ---------------------------------- | ------------------------------------------------------------------------------------ |
| `Boot/Launcher.cs`                 | 场景入口 MonoBehaviour，启动 `BootFlow`                                              |
| `Boot/BootFlow.cs`                 | 启动流程。只管步骤和失败决策，通过事件把进度和失败告诉界面                           |
| `Boot/BootUI.cs`                   | 启动界面：进度条和失败弹窗。只订阅 `BootFlow` 事件                                   |
| `Boot/BootTypes.cs`                | `BootState`、`BootDecision`、`BootProgress`、`BootFailure`                           |
| `Common/CommonData.cs`             | 全局常量：包名、清单地址、标签、入口类型名、服务器地址                               |
| `Common/IHotAssemblyEntry.cs`      | 热更入口接口，只有一个 `OnStart()`                                                   |
| `HotAssembly/HotAssemblyLoader.cs` | 读清单、补充元数据、加载热更程序集、启动入口                                         |
| `Resource/YooAssetBoot.cs`         | 初始化 YooAsset 资源包，提供 `Package`、`PackageVersion`                             |
| `Resource/ResourceUpdater.cs`      | 请求版本、加载清单、按标签下载、回退、标记可用版本                                   |
| `Resource/RemoteService.cs`        | 联机模式的远程服务：版本文件在 `<root>/MainPackage.version`，其余在 `<root>/<版本>/` |
| `Resource/YooAssetBootHooks.cs`    | 运行时和编辑器程序集之间的桥，让 `Game.AOT` 不用引用 `YooAsset.Editor`               |

| 编辑器脚本（`Game.AOT.Editor`） | 说明                                            |
| ------------------------------- | ----------------------------------------------- |
| `EditorSimulateBuildHook.cs`    | Editor 启动时把模拟构建注入 `YooAssetBootHooks` |
| `CollectorConfigRestorer.cs`    | 重建收集器配置的工具，内容尚未和当前配置同步    |

### 常用接口

```csharp
// 启动流程
BootFlow.RunAsync();                      // 返回是否成功
BootFlow.ProgressChanged / Failed / Finished   // 事件
BootFlow.Answer(BootDecision);            // 界面回传用户选择

// 热更程序集
HotAssemblyLoader.LoadAsync(onProgress);  // 引导阶段调用
HotAssemblyLoader.StartGame();            // 引导阶段调用
HotAssemblyLoader.LoadAssembleAsync(dllLocation);   // 预留：热更代码后续加载额外 dll

// 资源更新
ResourceUpdater.UpdateManifestAsync(onProgress);
ResourceUpdater.DownloadAsync(tags, onProgress);    // tags 传 null 表示全部
ResourceUpdater.RollbackToLastGoodAsync(onProgress);
ResourceUpdater.MarkVersionUsable();
```

---

## 六、热更脚本一览

| 脚本                                | 说明                                                               |
| ----------------------------------- | ------------------------------------------------------------------ |
| `Hot/Entry/GameEntry.cs`            | 热更入口，实现 `IHotAssemblyEntry`。`OnStart()` 里加载 `MainPanel` |
| `Hot/Framework/Core/EmptyScript.cs` | 占位脚本，保证 asmdef 有内容                                       |
| `Hot/Game/TestScript.cs`            | 占位脚本                                                           |

入口类型全名是 `Game.Hot.GameEntry.GameEntry`（命名空间 `Game.Hot.GameEntry` + 类名 `GameEntry`），对应常量 `CommonData.HotAssemblyEntryTypeName`。

---

## 七、Editor 调试

`CommonData.UseEditorSimulateInEditor` 决定 Editor 下用哪种模式：

| 值      | 模式     | 说明                                                        |
| ------- | -------- | ----------------------------------------------------------- |
| `true`  | 模拟构建 | 不需要服务器，按当前收集器配置直接读工程资源                |
| `false` | 联机模式 | 从 `CommonData.HostServerRoot` 取版本和资源，流程和真机一致 |

联机模式下，本地服务器根目录需要有这样的结构：

```
<服务器根目录>/Windows/MainPackage/MainPackage.version
<服务器根目录>/Windows/MainPackage/<版本>/...
```

排查“地址不存在”这类错误时，先看 Console 里的 `清单就绪，版本 X` 是不是刚构建的那个版本。
