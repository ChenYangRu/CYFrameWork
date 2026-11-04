using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GameFramework.EditorTools.Mcp
{
    /// <summary>
    /// 资源检索与文本读取工具。只做只读操作，不改任何资源。
    /// </summary>
    public static class AssetTools
    {
        [McpTool("find_assets",
            "按 AssetDatabase 搜索语法检索资源（如 t:Prefab Main、t:Script UIPanel、Level）。"
            + "返回 guid/assetPath/type，便于后续用 read_text_file 读取。")]
        public static object FindAssets(
            [McpParam("搜索表达式，支持 AssetDatabase.FindAssets 语法")] string pattern,
            [McpParam("最多返回条数", Required = false)] int maxCount = 50)
        {
            if (maxCount <= 0) maxCount = 50;

            string[] guids = AssetDatabase.FindAssets(pattern ?? string.Empty);
            var items = new List<object>();
            int limit = Math.Min(guids.Length, maxCount);

            for (int i = 0; i < limit; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                items.Add(new Dictionary<string, object>
                {
                    { "guid", guids[i] },
                    { "assetPath", path },
                    { "type", asset != null ? asset.GetType().Name : "Unknown" },
                });
            }

            return new Dictionary<string, object>
            {
                { "total", guids.Length },
                { "returned", items.Count },
                { "items", items },
            };
        }

        [McpTool("read_text_file",
            "读取工程内文本文件内容（脚本 .cs / .lua / .json / .txt 等）。"
            + "path 可以为 Assets 下的相对路径。大文件可用 offset/limit 分段读。")]
        public static object ReadTextFile(
            [McpParam("文件路径，如 Assets/Scripts/Main.cs")] string path,
            [McpParam("起始行，从 0 开始", Required = false)] int offset = 0,
            [McpParam("最多读取行数，0 表示全部", Required = false)] int limit = 0)
        {
            if (string.IsNullOrEmpty(path))
                return new Dictionary<string, object> { { "error", "path 不能为空" } };

            string full = ResolvePath(path);
            if (full == null || !File.Exists(full))
                return new Dictionary<string, object> { { "error", "文件不存在: " + path } };

            string[] lines = File.ReadAllLines(full);
            if (offset < 0) offset = 0;
            if (offset > lines.Length) offset = lines.Length;
            int take = limit <= 0 ? lines.Length - offset : Math.Min(limit, lines.Length - offset);

            var body = new System.Text.StringBuilder();
            for (int i = offset; i < offset + take; i++)
                body.Append(i + 1).Append(": ").Append(lines[i]).Append('\n');

            return new Dictionary<string, object>
            {
                { "path", path },
                { "totalLines", lines.Length },
                { "offset", offset },
                { "returnedLines", take },
                { "content", body.ToString() },
            };
        }

        [McpTool("list_directory",
            "列出工程目录下的文件与子目录（只看一层），用于探查工程结构。")]
        public static object ListDirectory(
            [McpParam("相对或绝对路径，留空为工程根目录", Required = false)] string path = "",
            [McpParam("最多返回条目数", Required = false)] int maxEntries = 200)
        {
            string root = string.IsNullOrEmpty(path) ? ProjectRoot() : ResolvePath(path) ?? path;
            if (!Directory.Exists(root))
                return new Dictionary<string, object> { { "error", "目录不存在: " + path } };

            var entries = new List<object>();
            foreach (string dir in Directory.GetDirectories(root))
            {
                if (entries.Count >= maxEntries) break;
                entries.Add(new Dictionary<string, object> { { "name", Path.GetFileName(dir) }, { "type", "dir" } });
            }
            foreach (string file in Directory.GetFiles(root))
            {
                if (entries.Count >= maxEntries) break;
                entries.Add(new Dictionary<string, object>
                {
                    { "name", Path.GetFileName(file) },
                    { "type", "file" },
                    { "size", new FileInfo(file).Length },
                });
            }

            return new Dictionary<string, object> { { "path", root }, { "entries", entries } };
        }

        // ---------- 内部 ----------

        private static string ProjectRoot()
        {
            return Path.GetDirectoryName(Application.dataPath) ?? ".";
        }

        private static string ResolvePath(string path)
        {
            if (Path.IsPathRooted(path)) return path;
            string root = ProjectRoot();
            string combined = Path.GetFullPath(Path.Combine(root, path));
            return combined.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? combined : null;
        }
    }
}
