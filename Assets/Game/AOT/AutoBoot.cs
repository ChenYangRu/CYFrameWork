using GameFramework;
using GameFramework.Runtime;
using UnityEngine;

namespace Game.AOT
{
    /// <summary>
    /// 兜底自举。`RuntimeInitializeOnLoadMethod` 会在**每个场景**加载后执行一次：
    /// 如果场景里没有显式挂 Bootstrap，就自动创建一个。
    ///
    /// 作用：新工程/新场景零配置即可跑通启动链路（防止"忘了挂脚本，一片黑屏"）。
    /// ★ 正式项目建议在主场景显式挂 Bootstrap，并把这个兜底关掉（子类化或改条件），
    ///   以免误在战斗场景里重复启动。
    /// </summary>
    public static class AutoBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Object.FindObjectOfType<Bootstrap>() != null) return;
            if (GameLauncher.Instance != null && GameLauncher.Instance.EnteredGame)
            {
                GameLog.Info("[AutoBoot] 已进入游戏，跳过自举");
                return;
            }

            GameLog.Info("[AutoBoot] 场景内未找到 Bootstrap，自动创建");
            var go = new GameObject("[Bootstrap]");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Bootstrap>();
        }
    }
}
