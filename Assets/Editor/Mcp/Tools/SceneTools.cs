using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameFramework.EditorTools.Mcp
{
    /// <summary>
    /// 场景相关工具：层级读取、查找、组件查看、创建物体。
    /// 全部在主线程执行（由 McpBridge 投递）。
    /// </summary>
    public static class SceneTools
    {
        [McpTool("get_hierarchy",
            "读取当前活动场景的层级树。返回 name/path/active/components 数量。大数据场景请调小 maxDepth 与 maxNodes。")]
        public static object GetHierarchy(
            [McpParam("遍历最大深度，1 表示只取根物体", Required = false)] int maxDepth = 3,
            [McpParam("最多返回节点数，超出会截断", Required = false)] int maxNodes = 200)
        {
            if (maxDepth <= 0) maxDepth = 3;
            if (maxNodes <= 0) maxNodes = 200;

            Scene scene = SceneManager.GetActiveScene();
            var nodes = new List<object>();
            int truncated = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (nodes.Count >= maxNodes) { truncated++; continue; }
                Walk(root.transform, null, 1, maxDepth, maxNodes, nodes, ref truncated);
            }

            return new Dictionary<string, object>
            {
                { "scene", scene.name },
                { "scenePath", scene.path ?? string.Empty },
                { "rootCount", scene.rootCount },
                { "nodes", nodes },
                { "truncated", truncated > 0 },
            };
        }

        [McpTool("find_gameobject",
            "按名称或路径查找 GameObject。先在场景里按名字搜，找不到再用路径直接取。返回 path/active/components。")]
        public static object FindGameObject(
            [McpParam("物体名称或场景路径（如 Canvas/Panel），二选一即可")] string nameOrPath,
            [McpParam("是否只返回第一个匹配", Required = false)] bool firstOnly = true)
        {
            if (string.IsNullOrEmpty(nameOrPath))
                return new Dictionary<string, object> { { "error", "nameOrPath 不能为空" } };

            var results = new List<object>();

            if (nameOrPath.IndexOf('/') >= 0)
            {
                Transform found = FindByPath(nameOrPath);
                if (found != null) results.Add(Describe(found));
            }
            else
            {
                Scene scene = SceneManager.GetActiveScene();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name != nameOrPath) continue;
                        results.Add(Describe(t));
                        if (firstOnly) break;
                    }
                    if (firstOnly && results.Count > 0) break;
                }
            }

            return new Dictionary<string, object> { { "count", results.Count }, { "results", results } };
        }

        [McpTool("get_components",
            "列出指定物体上的所有组件类型名，并附带 Transform 的位置/旋转/缩放。"
            + "只做读取，不修改任何字段。")]
        public static object GetComponents([McpParam("物体名称或场景路径")] string nameOrPath)
        {
            Transform target = Resolve(nameOrPath);
            if (target == null)
                return new Dictionary<string, object> { { "error", "未找到物体: " + nameOrPath } };

            var components = new List<object>();
            foreach (Component c in target.GetComponents<Component>())
            {
                if (c == null) continue;
                components.Add(new Dictionary<string, object>
                {
                    { "type", c.GetType().FullName },
                    { "shortType", c.GetType().Name },
                });
            }

            return new Dictionary<string, object>
            {
                { "path", PathOf(target) },
                { "active", target.gameObject.activeInHierarchy },
                { "selfActive", target.gameObject.activeSelf },
                { "position", Vec(target.position) },
                { "rotation", Vec(target.eulerAngles) },
                { "scale", Vec(target.localScale) },
                { "components", components },
            };
        }

        [McpTool("create_gameobject",
            "在当前活动场景创建一个空物体并挂到指定父节点下（parentPath 为空则挂到场景根）。"
            + "副作用：会修改当前场景，记得用编辑器保存。", Dangerous = true)]
        public static object CreateGameObject(
            [McpParam("新物体名称")] string name,
            [McpParam("父物体路径，为空表示场景根", Required = false)] string parentPath = "")
        {
            if (string.IsNullOrEmpty(name))
                return new Dictionary<string, object> { { "error", "name 不能为空" } };

            var go = new GameObject(name);
            if (!string.IsNullOrEmpty(parentPath))
            {
                Transform parent = Resolve(parentPath);
                if (parent == null)
                {
                    Object.DestroyImmediate(go);
                    return new Dictionary<string, object> { { "error", "父节点未找到: " + parentPath } };
                }
                go.transform.SetParent(parent, false);
            }

            Undo.RegisterCreatedObjectUndo(go, "MCP CreateGameObject");
            EditorSceneManager.MarkSceneDirty(go.scene);

            return new Dictionary<string, object> { { "ok", true }, { "path", PathOf(go.transform) } };
        }

        // ---------- 内部 ----------

        private static void Walk(Transform node, string parentPath, int depth, int maxDepth, int maxNodes, List<object> output, ref int truncated)
        {
            if (node == null || depth > maxDepth) return;
            if (output.Count >= maxNodes) { truncated++; return; }

            output.Add(Describe(node));
            for (int i = 0; i < node.childCount; i++)
                Walk(node.GetChild(i), PathOf(node), depth + 1, maxDepth, maxNodes, output, ref truncated);
        }

        private static Dictionary<string, object> Describe(Transform t)
        {
            int count = 0;
            foreach (Component c in t.GetComponents<Component>())
                if (c != null) count++;

            return new Dictionary<string, object>
            {
                { "name", t.name },
                { "path", t.parent == null ? t.name : PathOf(t) },
                { "active", t.gameObject.activeInHierarchy },
                { "childCount", t.childCount },
                { "components", count },
            };
        }

        private static Transform Resolve(string nameOrPath)
        {
            if (string.IsNullOrEmpty(nameOrPath)) return null;
            if (nameOrPath.IndexOf('/') >= 0) return FindByPath(nameOrPath);

            Scene scene = SceneManager.GetActiveScene();
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == nameOrPath) return t;
            return null;
        }

        private static Transform FindByPath(string path)
        {
            Scene scene = SceneManager.GetActiveScene();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == path) return root.transform;
                Transform child = root.transform.Find(path);
                if (child != null) return child;
            }
            return null;
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            Transform p = t.parent;
            while (p != null)
            {
                path = p.name + "/" + path;
                p = p.parent;
            }
            return path;
        }

        private static Dictionary<string, object> Vec(Vector3 v)
        {
            return new Dictionary<string, object>
            {
                { "x", (double)System.Math.Round(v.x, 4) },
                { "y", (double)System.Math.Round(v.y, 4) },
                { "z", (double)System.Math.Round(v.z, 4) },
            };
        }
    }
}
