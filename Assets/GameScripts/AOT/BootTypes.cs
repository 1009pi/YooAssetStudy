using System;

namespace Game.AOT
{
    public enum BootState
    {
        None,
        Initializing,
        CheckingVersion,
        DownloadingCode,
        LoadingScripts,
        DownloadingResources,
        EnteringGame,
        Completed,
        Failed,
    }

    public enum BootDecision
    {
        Retry,  //重试当前步骤
        SkipUpdate, //放弃本次更新 回退到上一个可用版本
        Abort,  //退出
    }

    ///<summary>进度快照：UI只依赖这一个结构</summary>
    public readonly struct BootProgress
    {
        public BootState State { get; }
        public float Value { get; }
        public string Message { get; }

        public BootProgress(BootState state, float value, string message)
        {
            State = state;
            Value = value;
            Message = message;
        }
    }

    ///<summary>失败信息：告诉UI可以给用户哪些选项</summary>
    public readonly struct BootFailure
    {
        public BootState State { get; }
        public string Message { get; }
        public bool CanSkipUpdate { get; }

        public BootFailure(BootState state, string message, bool canSkipUpdate)
        {
            State = state;
            Message = message;
            CanSkipUpdate = canSkipUpdate;
        }
    }
}