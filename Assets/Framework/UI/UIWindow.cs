using UnityEngine;

namespace GameFramework.UI
{
    /// <summary>
    /// 面板基类。两条构建路径：
    ///  A) PrefabLocation 非空 → 资源层实例化，子类在 OnBind 里取控件
    ///  B) PrefabLocation 为空 → 子类在 BuildUI 里用 UIUtil 代码搭（零美术也能跑通，Demo 用这条）
    /// 生命周期：Build → OnShow →（每帧 OnUpdate）→ OnHide → OnDestroy
    /// ★ 不要在 OnShow 里做重活（打开面板会掉帧），重活放协程 + loading。
    /// </summary>
    public abstract class UIWindow : MonoBehaviour
    {
        public string WindowName { get; internal set; }
        public UILayer Layer { get; internal set; }
        /// <summary>是否全屏弹窗：为 true 时自动加半透明遮罩并拦截点击穿透</summary>
        public bool FullScreen { get; internal set; }
        /// <summary>打开参数（任意类型，业务自己约定）</summary>
        public object Args { get; internal set; }

        public bool IsOpen { get; private set; }
        internal UIManager Manager;

        private bool _built;

        /// <summary>首次打开时调用一次</summary>
        protected virtual void BuildUI() { }
        /// <summary>每次打开调用</summary>
        protected virtual void OnShow() { }
        /// <summary>每次关闭调用</summary>
        protected virtual void OnHide() { }
        /// <summary>面板打开期间每帧调用</summary>
        protected virtual void OnUpdate(float deltaTime) { }

        internal void InternalOpen()
        {
            if (!_built)
            {
                _built = true;
                try { BuildUI(); }
                catch (System.Exception e) { GameLog.Error("[UI] " + WindowName + ".BuildUI 异常", e); }
            }

            gameObject.SetActive(true);
            IsOpen = true;
            try { OnShow(); }
            catch (System.Exception e) { GameLog.Error("[UI] " + WindowName + ".OnShow 异常", e); }
        }

        internal void InternalClose()
        {
            if (!IsOpen) return;
            IsOpen = false;
            try { OnHide(); }
            catch (System.Exception e) { GameLog.Error("[UI] " + WindowName + ".OnHide 异常", e); }
            gameObject.SetActive(false);
        }

        internal void InternalUpdate(float dt)
        {
            if (!IsOpen) return;
            try { OnUpdate(dt); }
            catch (System.Exception e) { GameLog.Error("[UI] " + WindowName + ".OnUpdate 异常", e); }
        }

        internal void InternalDestroy()
        {
            if (IsOpen) InternalClose();
            if (this != null && gameObject != null) Destroy(gameObject);
        }

        public void Close()
        {
            if (Manager != null) Manager.Close(WindowName);
        }
    }
}
