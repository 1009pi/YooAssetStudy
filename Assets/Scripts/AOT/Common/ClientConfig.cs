using System;
using System.Collections.Generic;

namespace Game.AOT
{
    [Serializable]
    public class ClientConfig
    {
        public string PackageName;
        public List<HotUpdateAssembly> HotUpdateAssemblies;
        public string HotUpdateEntryTypeName;

        public string CdnRoot;
    }

    [Serializable]
    public struct HotUpdateAssembly
    {
        public string Name;
        public string Location;
    }
}