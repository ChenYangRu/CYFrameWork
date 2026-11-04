using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GameFramework.EditorTools
{
    /// <summary>
    /// Bootstrap 场景挂载工具。
    ///
    /// 为什么需要：Bootstrap 的启动参数（资源运行模式、是否跳过资源系统）是
    /// **MonoBehaviour 的序列化字段**，只有场景里真的存在这个组件，Inspector 里才能选；
    /// 否则运行时靠 `AutoBoot` 兜底 new 一个，字段全是默认值（UseConfig）。
    ///
    /// 用类型名字符串反射拿 Bootstrap，避免 Editor 程序集反向引用 Game.AOT
    /// （保持框架层与业务层之间的引用方向干净）。
    /// </summary>
    public static class BootstrapSceneTool
    {
        private const string TypeName = "Game.AOT.Bootstrap, Game.AOT";
        private const string RootName = "[Bootstrap]";

        [MenuItem("框架/启动/在当前场景添加 Bootstrap（可配启动参数）", false, 70)]
        public static void AddToScene()
        {
            var type = System.Type.GetType(TypeName);
            if (type == null)
            {
                EditorUtility.DisplayDialog("找不到 Bootstrap 类型",
                    "请确认 Game.AOT 程序集存在且编译通过（Game.AOT.asmdef 的 name 必须是 Game.AOT）。", "知道了");
                return;
            }

            var existing = Object.FindObjectOfType(type) as Component;
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing.gameObject);
                EditorUtility.DisplayDialog("已存在",
                    "当前场景已经有 Bootstrap（" + existing.gameObject.name + "），已帮你选中。\n" +
                    "在 Inspector 里改「资源运行模式」即可。", "好");
                return;
            }

            var go = new GameObject(RootName);
            go.AddComponent(type);

            Undo.RegisterCreatedObjectUndo(go, "Add Bootstrap");
            EditorSceneManager.MarkSceneDirty(go.scene);

            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);

            Debug.Log("[框架] 已在场景添加 " + RootName + "。Inspector 里可配置：\n" +
                      "  · autoStart            是否自动启动\n" +
                      "  · resourcePlayMode     资源运行模式（UseConfig / EditorSimulate / Offline / Host / Web）\n" +
                      "  · skipResourceSystem   纯业务联调时跳过资源系统");

            EditorUtility.DisplayDialog("已添加",
                "已在当前场景创建 [Bootstrap]。\n\n在 Inspector 里选「资源运行模式」，Play 时就会按它启动。\n\n" +
                "记得保存场景（Ctrl+S），否则打包时用的还是磁盘上的旧场景。", "好");
        }

        [MenuItem("框架/启动/打开主场景", false, 71)]
        public static void OpenMainScene()
        {
            const string path = "Assets/Scenes/SampleScene.unity";
            if (!System.IO.File.Exists(path))
            {
                EditorUtility.DisplayDialog("找不到场景", path, "知道了");
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(path);
        }
    }
}
