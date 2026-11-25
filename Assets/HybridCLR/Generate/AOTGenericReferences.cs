using System.Collections.Generic;
public class AOTGenericReferences : UnityEngine.MonoBehaviour
{

    // {{ AOT assemblies
    public static readonly IReadOnlyList<string> PatchedAOTAssemblyList = new List<string>
    {
        "UniTask.dll",
        "YooAsset.dll",
        "mscorlib.dll",
    };
    // }}

    // {{ constraint implement type
    // }} 

    // {{ AOT generic types
    // Cysharp.Threading.Tasks.CompilerServices.AsyncUniTaskVoid.<>c<Game.Hot.GameEntry.GameEntry.<LoadMainPanel>d__4>
    // Cysharp.Threading.Tasks.CompilerServices.AsyncUniTaskVoid<Game.Hot.GameEntry.GameEntry.<LoadMainPanel>d__4>
    // Cysharp.Threading.Tasks.ITaskPoolNode<object>
    // System.Func<int>
    // }}

    public void RefMethods()
    {
        // System.Void Cysharp.Threading.Tasks.CompilerServices.AsyncUniTaskVoidMethodBuilder.AwaitUnsafeOnCompleted<YooAsset.OperationAwaiter,Game.Hot.GameEntry.GameEntry.<LoadMainPanel>d__4>(YooAsset.OperationAwaiter&,Game.Hot.GameEntry.GameEntry.<LoadMainPanel>d__4&)
        // System.Void Cysharp.Threading.Tasks.CompilerServices.AsyncUniTaskVoidMethodBuilder.Start<Game.Hot.GameEntry.GameEntry.<LoadMainPanel>d__4>(Game.Hot.GameEntry.GameEntry.<LoadMainPanel>d__4&)
        // YooAsset.AssetHandle YooAsset.ResourcePackage.LoadAssetAsync<object>(string,uint)
    }
}
