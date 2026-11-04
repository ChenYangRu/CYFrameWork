using System.IO;
using YooAsset.Editor;

namespace GameFramework.EditorTools
{
    /// <summary>
    /// 寻址规则：location = 相对 `Assets/BundleAssets/` 的**完整路径**（去扩展名）。
    ///
    /// 为什么不用官方的三条规则：
    ///   · `AddressByFileName`            → 纯文件名，`UI/Login/panel` 与 `UI/Shop/panel` 会撞名
    ///   · `AddressByFolderAndFileName`   → `Login_panel`（下划线拼接），层级信息丢失、不可读
    ///   · `AddressByGroupAndFileName`    → `UI_panel`，同上
    ///
    /// 成熟项目要的是"看 location 就知道文件在哪"：
    ///   `UI/Login/ui_login_panel.prefab`        → `UI/Login/ui_login_panel`
    ///   `Art/Character/chr_hero.prefab`         → `Art/Character/chr_hero`
    ///   `Art/Character/char_mage_body.fbx`      → `Art/Character/char_mage_body`
    ///
    /// 副作用：**路径即契约**，改目录名 = 改代码里的加载路径。这是有意的——改名必须显式发生。
    /// </summary>
    public class AddressByPathInBundleAssets : IAddressRule
    {
        private const string RootWithSlash = "Assets/BundleAssets/";

        string IAddressRule.GetAssetAddress(AddressRuleData data)
        {
            string path = (data.AssetPath ?? string.Empty).Replace('\\', '/');

            if (path.StartsWith(RootWithSlash))
                path = path.Substring(RootWithSlash.Length);

            // 只剥一层扩展名：xxx.prefab → xxx；xxx.png → xxx
            return Path.ChangeExtension(path, null);
        }
    }
}
