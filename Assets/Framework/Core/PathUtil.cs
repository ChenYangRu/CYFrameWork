using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 平台 / 路径约定集中在这里，别在业务里散写。
    /// CDN 目录规范：{host}/{Platform}/{AppVersion}/{ResVersion}
    ///   Platform：Android / IOS / PC（与 YooAsset 构建目标一一对应）
    /// </summary>
    public static class PathUtil
    {
        /// <summary>CDN 一级平台目录名</summary>
        public static string PlatformFolder
        {
            get
            {
#if UNITY_ANDROID
                return "Android";
#elif UNITY_IOS
                return "IOS";
#elif UNITY_WEBGL
                return "WebGL";
#else
                return "PC";
#endif
            }
        }

        /// <summary>YooAsset 构建目标同名目录（用于本地构建产物目录）</summary>
        public static string BuildTargetFolder
        {
            get
            {
#if UNITY_ANDROID
                return "Android";
#elif UNITY_IOS
                return "iOS";
#elif UNITY_WEBGL
                return "WebGL";
#else
                return "StandaloneWindows64";
#endif
            }
        }

        public static string PersistentRoot
        {
            get { return Application.persistentDataPath; }
        }

        /// <summary>拼接 URL，避免出现双斜杠（小游戏/CDN 常见坑）</summary>
        public static string CombineUrl(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            if (string.IsNullOrEmpty(b)) return a;
            return a.TrimEnd('/') + "/" + b.TrimStart('/');
        }

        /// <summary>组合资源版本目录：host/Android/1.0.0/V1.0.1</summary>
        public static string CombineResUrl(string host, string appVersion, string resVersion)
        {
            string url = host;
            url = CombineUrl(url, PlatformFolder);
            if (!string.IsNullOrEmpty(appVersion)) url = CombineUrl(url, appVersion);
            url = CombineUrl(url, resVersion);
            return url;
        }
    }
}
