using System;

namespace GameFramework.Resource
{
    /// <summary>
    /// 资源更新阶段。启动器与更新界面靠它显示"现在在干什么"，不要用魔法字符串。
    /// </summary>
    public enum UpdateStage
    {
        None = 0,
        /// <summary>初始化资源系统（YooAsset 包裹创建/初始化）</summary>
        Initialize = 1,
        /// <summary>请求远端版本号（拉 .version）</summary>
        RequestVersion = 2,
        /// <summary>加载/比对清单</summary>
        LoadManifest = 3,
        /// <summary>预取清单、创建下载器</summary>
        Prefetch = 4,
        /// <summary>等待用户确认下载（展示流量提示）</summary>
        WaitConfirm = 5,
        /// <summary>正在下载</summary>
        Downloading = 6,
        /// <summary>清理不再使用的旧 bundle</summary>
        Cleanup = 7,
        Done = 8,
        Failed = 9,
    }

    /// <summary>
    /// 一次更新的概要信息。**在真正下载之前**填好，用来给玩家做流量提示
    /// （"本次更新 12 个文件 / 18.4 MB，是否继续？"）。手游不提示直接吃掉几十 MB 会被差评。
    /// </summary>
    public class ResourceUpdateInfo
    {
        /// <summary>需要下载的文件数</summary>
        public int FileCount;
        /// <summary>需要下载的总字节数</summary>
        public long TotalBytes;
        /// <summary>远端资源版本号</summary>
        public string RemoteVersion;
        /// <summary>本地当前资源版本号</summary>
        public string LocalVersion;
        /// <summary>远端根地址（排障用）</summary>
        public string RemoteRoot;

        public float SizeMB { get { return TotalBytes / 1048576f; } }
        public bool HasUpdate { get { return FileCount > 0; } }

        /// <summary>给玩家看的文案</summary>
        public string ToPlayerText()
        {
            if (!HasUpdate) return "已是最新版本";
            return string.Format("发现新版本 {0}\n需要下载 {1} 个文件，共 {2:0.0} MB\n建议在 WiFi 环境下更新",
                RemoteVersion, FileCount, SizeMB);
        }

        public override string ToString()
        {
            return string.Format("UpdateInfo(count={0}, bytes={1}, remote={2}, local={3}, root={4})",
                FileCount, TotalBytes, RemoteVersion, LocalVersion, RemoteRoot);
        }
    }
}
