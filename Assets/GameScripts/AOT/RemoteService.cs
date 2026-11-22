using System;
using System.Collections.Generic;
using YooAsset;

namespace Game.AOT
{
    public sealed class RemoteService : IRemoteService
    {
        private readonly string _rootUrl;

        ///<summary>当前要下载的资源版本号，加载清单前必须赋值</summary>
        public string Version{get; set;}
        /// <param name="rootUrl">形如 http://127.0.0.1:8080/Windows/MainPackage</param>
        public RemoteService(string rootUrl)
        {
            _rootUrl = rootUrl.TrimEnd('/');
        }

        public IReadOnlyList<string> GetRemoteUrls(string fileName)
        {
            //版本文件放在包根目录 <root>/MainPackage.version
            if(fileName.EndsWith(".version", StringComparison.OrdinalIgnoreCase))
                return new List<string> {$"{_rootUrl}/{fileName}"};

            if(string.IsNullOrEmpty(Version))
                throw new InvalidOperationException("[Remote] Version 未设置，无法拼接资源 URL");

            //清单与 bundle 都在版本子目录 <root>/<version>/<fileName>
            return new List<string> {$"{_rootUrl}/{Version}/{fileName}"};
        }
    }
}