using System.Collections;
using UnityEngine;

namespace Game.AOT
{ 
    /// <summary>
    /// 热更入口契约：由AOT定义，热更程序集实现
    /// AOT不能引用热更程序集，所以只能通过接口+反射拿到实例
    /// </summary>
    public interface IHotUpdateEntry
    {
        void OnStart();
    }
}