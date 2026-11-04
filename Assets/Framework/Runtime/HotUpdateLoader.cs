using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using GameFramework.Resource;
using HybridCLR;
using UnityEngine;
using YooAsset;

namespace GameFramework.Runtime
{
    /// <summary>
    /// HybridCLR 加载器。两件事，顺序绝对不能反：
    ///   ① **补充元数据**（AOT 泛型/裁剪元数据）→ 必须在加载热更 DLL 之前；
    ///   ② 加载热更 DLL 字节 → Assembly.Load。
    /// 编辑器分支直接走已编译的 AppDomain 程序集（改代码 Ctrl+S 即可生效，不用出包）。
    /// </summary>
    public static class HotUpdateLoader
    {
        public static readonly List<Assembly> LoadedAssemblies = new List<Assembly>(4);
        public static readonly StringBuilder Log = new StringBuilder();

        /// <summary>已加载的热更程序集（按名字找，避免业务层拿 Assembly 乱传）</summary>
        public static Assembly GetAssembly(string name)
        {
            for (int i = 0; i < LoadedAssemblies.Count; i++)
            {
                if (LoadedAssemblies[i].GetName().Name == name) return LoadedAssemblies[i];
            }
            return null;
        }

        // ---------------- ① 补充 AOT 元数据 ----------------

        public static IEnumerator LoadMetadata(LaunchConfig cfg, Action<bool, string> onDone)
        {
            LoadedAssemblies.Clear();
            Log.Length = 0;

#if UNITY_EDITOR
            if (cfg.skipMetadataInEditor)
            {
                GameLog.Info("[HotUpdate] 编辑器模式，跳过补充元数据");
                onDone(true, null);
                yield break;
            }
#endif

            var names = cfg.aotMetadataAssemblies ?? new string[0];
            int okCount = 0;

            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                if (string.IsNullOrEmpty(name)) continue;
                string location = cfg.aotMetadataResourceRoot + "/" + name;

                var ta = Resources.Load<TextAsset>(location);
                if (ta == null)
                {
                    // 缺一个不致命：说明这个 AOT 程序集没被裁切，热更代码能直接调
                    GameLog.Warn("[HotUpdate] 缺少补元数据文件: Resources/" + location + "（可忽略，但若热更报类型缺失则必须补）");
                    continue;
                }

                LoadImageErrorCode code = RuntimeApi.LoadMetadataForAOTAssembly(ta.bytes, HomologousImageMode.SuperSet);
                if (code == LoadImageErrorCode.OK)
                {
                    okCount++;
                    Log.Append(name).Append(":OK ");
                }
                else
                {
                    GameLog.Error("[HotUpdate] 补元数据失败 " + name + " → " + code);
                }
                yield return null;
            }

            GameLog.Info(string.Format("[HotUpdate] 补充元数据完成 {0}/{1}", okCount, names.Length));
            onDone(true, null);
        }

        // ---------------- ② 加载热更程序集 ----------------

        public static IEnumerator LoadAssemblies(ResourceService res, LaunchConfig cfg, Action<bool, string> onDone)
        {
            var names = cfg.hotUpdateAssemblies ?? new string[0];
            if (names.Length == 0)
            {
                onDone(false, "launch.json 未配置热更程序集");
                yield break;
            }

            for (int i = 0; i < names.Length; i++)
            {
                string asmName = names[i];
                byte[] bytes = null;

#if UNITY_EDITOR
                // 编辑器：Game.HotUpdate 已由 Unity 编译进 Library/ScriptAssemblies，
                // 直接取已加载实例，改代码即时生效（无需编译 DLL / 打包）
                var editorAsm = FindLoaded(asmName);
                if (editorAsm != null)
                {
                    LoadedAssemblies.Add(editorAsm);
                    GameLog.Info("[HotUpdate] 使用编辑器已编译程序集: " + asmName);
                    Log.Append(asmName).Append(":Editor ");
                    continue;
                }
                GameLog.Warn("[HotUpdate] 编辑器未找到 " + asmName + "，回退到资源加载（请先执行『编译热更 DLL』）");
#endif

                // location 规则见 AddressByPathInBundleAssets：相对 BundleAssets 的路径，只剥一层扩展名。
                // 文件叫 Game.HotUpdate.dll.bytes → location 就是 HotUpdate/Game.HotUpdate.dll
                string root = cfg.hotUpdateLocationRoot;
                string location = string.IsNullOrEmpty(root)
                    ? asmName + ".dll"
                    : root + "/" + asmName + ".dll";
                yield return res.LoadRawBytes(location, delegate (byte[] data) { bytes = data; },
                    delegate (string err) { GameLog.Error("[HotUpdate] " + err); });

                if (bytes == null)
                {
                    onDone(false, "热更程序集加载失败: " + location);
                    yield break;
                }

                try
                {
                    var asm = Assembly.Load(bytes);
                    LoadedAssemblies.Add(asm);
                    Log.Append(asmName).Append(":Loaded ");
                    GameLog.Info("[HotUpdate] 已加载 " + asmName + " (" + bytes.Length / 1024 + "KB)");
                }
                catch (Exception e)
                {
                    onDone(false, "Assembly.Load 失败 " + asmName + ": " + e.Message);
                    yield break;
                }

                yield return null;
            }

            onDone(true, null);
        }

        private static Assembly FindLoaded(string name)
        {
            var list = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i].GetName().Name == name) return list[i];
            }
            return null;
        }

        // ---------------- ③ 反射调用入口 ----------------

        public static bool InvokeEntry(LaunchConfig cfg, out string error)
        {
            error = null;
            Type entry = null;
            for (int i = 0; i < LoadedAssemblies.Count; i++)
            {
                entry = LoadedAssemblies[i].GetType(cfg.entryType, false);
                if (entry != null) break;
            }

            if (entry == null)
            {
                error = "找不到入口类型 " + cfg.entryType + "（检查 launch.json 与热更程序集）";
                return false;
            }

            var method = entry.GetMethod(cfg.entryMethod,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
            {
                error = "入口类型 " + cfg.entryType + " 缺少静态方法 " + cfg.entryMethod;
                return false;
            }

            try
            {
                method.Invoke(null, null);
                GameLog.Info("[HotUpdate] 入口已调用: " + cfg.entryType + "." + cfg.entryMethod);
                return true;
            }
            catch (Exception e)
            {
                error = "入口执行异常: " + (e.InnerException != null ? e.InnerException.ToString() : e.ToString());
                return false;
            }
        }
    }
}
