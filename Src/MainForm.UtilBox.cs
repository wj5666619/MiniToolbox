using System;
using System.Windows.Forms;

namespace ShutdownTool
{
    partial class MainForm
    {
        // v19.11：工作台「实用工具」入口，独立深色小窗
        void ShowUtilTools()
        {
            try
            {
                using (UtilToolboxForm f = new UtilToolboxForm())
                    f.ShowDialog(this);
            }
            catch (Exception ex)
            {
                Diag.Swallow("MainForm.UtilBox.cs:13", ex);
                MessageBox.Show(this, "工具集打开失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
