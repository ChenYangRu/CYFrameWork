namespace GameFramework.UI
{
    /// <summary>
    /// UI 层级（从下到上）。每个层级是一个独立的 RectTransform，勿在运行时改顺序。
    /// Scene  ：常驻 HUD（血条/摇杆），不参与栈
    /// Normal ：主界面，同层互斥（打开新的会自动关掉同层旧的）
    /// Popup  ：弹窗，压栈，可叠加
    /// Top    ：顶层（设置/公告），压栈
    /// Tips   ：飘字/Toast，不阻挡
    /// </summary>
    public enum UILayer
    {
        Scene = 0,
        Normal = 1,
        Popup = 2,
        Top = 3,
        Tips = 4,
    }
}
