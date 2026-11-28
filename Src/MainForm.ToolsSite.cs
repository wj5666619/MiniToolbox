using System;
using System.Windows.Forms;

namespace ShutdownTool
{
    partial class MainForm
    {
        // v19.13：工作台「工具站」入口，独立窗口内嵌 88 个离线工具
        void ShowToolsSite()
        {
            try
            {
                ToolsSiteForm f = new ToolsSiteForm();
                f.ShowDialog(this);
            }
            catch (Exception ex)
            {
                Diag.Warn("工具站窗口创建失败：" + ex);
                MessageBox.Show(this, "工具站启动失败：\r\n" + ex.Message, "小工具箱",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
