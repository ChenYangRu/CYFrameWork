using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameFramework.EditorTools.Mcp
{
    /// <summary>
    /// 编辑器状态与控制工具。
    /// 会改变编辑器状态的操作（播放模式、刷新、执行菜单）都标了 Dangerous，默认关闭。
    /// </summary>
    public static class McpEditorTools
    {
        [McpTool("get_editor_state",
            "读取编辑器当前状态：是否编译中、是否播放中、活动构建目标、当前平台宏、桥接端口与工具数。"
            + "只读，无副作用。")]
        public static object GetEditorState()
        {
            return new Dictionary<string, object>
            {
                { "compiling", EditorApplication.isCompiling },
                { "playing", EditorApplication.isPlaying },
                { "paused", EditorApplication.isPaused },
                { "activeBuildTarget", EditorUserBuildSettings.activeBuildTarget.ToString() },
                { "selectedBuildTargetGroup", EditorUserBuildSettings.selectedBuildTargetGroup.ToString() },
                { "defineSymbols", PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup) },
                { "unityVersion", Application.unityVersion },
                { "productName", Application.productName },
                { "mcpPort", McpBridge.Port },
                { "mcpRunning", McpBridge.IsRunning },
                { "mcpTools", McpBridge.Tools.Count },
                { "allowDangerousTools", McpSettings.AllowDangerousTools },
            };
        }

        [McpTool("run_menu_item",
            "执行一个 Unity 菜单项（如 Tools/MCP 桥接/打印状态）。"
            + "副作用：等同人工点击菜单，可能触发编译或构建。", Dangerous = true)]
        public static object RunMenuItem([McpParam("完整菜单路径，如 Tools/MCP 桥接/打印状态")] string menuPath)
        {
            if (string.IsNullOrEmpty(menuPath))
                return new Dictionary<string, object> { { "error", "menuPath 不能为空" } };

            bool executed = EditorApplication.ExecuteMenuItem(menuPath);
            return new Dictionary<string, object>
            {
                { "ok", executed },
                { "menuPath", menuPath },
                { "hint", executed ? string.Empty : "菜单项不存在或当前不可用，请用 get_editor_state 确认状态" },
            };
        }

        [McpTool("set_play_mode",
            "进入或退出播放模式。副作用：会重置运行时状态，未保存的场景改动可能丢失。", Dangerous = true)]
        public static object SetPlayMode([McpParam("true 进入播放模式，false 退出")] bool enter)
        {
            if (EditorApplication.isCompiling)
                return new Dictionary<string, object> { { "error", "正在编译，请稍后再试" } };

            if (enter == EditorApplication.isPlaying)
                return new Dictionary<string, object> { { "ok", true }, { "playing", EditorApplication.isPlaying }, { "changed", false } };

            EditorApplication.isPlaying = enter;
            return new Dictionary<string, object> { { "ok", true }, { "playing", enter }, { "changed", true } };
        }

        [McpTool("refresh_assets",
            "调用 AssetDatabase.Refresh 刷新资源数据库。副作用：可能触发脚本重编译，桥接会短暂断开后自动重连。", Dangerous = true)]
        public static object RefreshAssets()
        {
            AssetDatabase.Refresh();
            return new Dictionary<string, object> { { "ok", true } };
        }
    }
}
