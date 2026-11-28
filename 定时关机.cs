using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Text;
using System.Media;
using Microsoft.Win32;
using System.Reflection;
using System.Collections.Generic;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Accessibility;

namespace ShutdownTool
{


    // ================= 任务配置 =================




    // ================= 主窗体 =================




    // 系统镜像下载窗（独立弹窗，不占主页面布局）




    // ================= 智能条件触发窗口 =================


    // ================= v17.9 背景 / 壁纸选择面板 =================


    // ================= 自定义背景面板 =================
    // 背景画板：有自定义背景时绘制背景图 + 深色遮罩


    // 页面画板：自己画背景（深色渐变 + 可选自定义背景图）
    // 不靠"透底"是有原因的：子控件（列表、图片框等）落在透明父容器上时会被同色混合，
    // 整体发灰发白，边缘还会漏出没画到的白边。自己画满就没这些毛病。


    // ================= v18.6 首次启动三步引导（语言/风格 → 音效 → 托盘与快捷键） =================



    // ================= 启动画面（v18.3 重制） =================
    // 设计要点：深色底 + 细网格雾点 + 斜向流光，中心六边形闪电 Logo 带旋转双弧、光圈脉冲、
    // 轨道粒子与扫光；文字逐字加字距并带轻微上浮入场；进度条带流光与阶段文案。
    // 节奏：淡入 0.24s → 内容入场 0.5s → 1.28s 走满 → 淡出 0.30s，总计约 1.6s，点一下或按 Esc 可跳过。


    // ================= 到点确认窗 =================


    // ================= 日志查看器 =================


    // ============ 美化工具：圆角路径 / 颜色加减（命名空间级，全局共用） ============
    // ============ v12.8 深色自绘滚动条 ============
    // 系统滚动槽在深色界面里是一条浅灰带（列表右侧、多行文本框右侧都有）。
    // SetWindowTheme("DarkMode_Explorer") 在部分 Win11 版本上压不住它，所以这里接管非客户区绘制：
    // 只换皮肤（轨道 / 滑块 / 两枚箭头），点击与拖动仍由系统处理，不改任何行为。


    // v17.7：五大工作语言（联合国工作语言口径：中 / 英 / 法 / 俄 / 西）。
    // 原来是「中 / 英」两态，靠 T(中文, English) 的二参结构，点一下按钮就整体重启才换语言。
    // 这里加一层语言包：T 的签名保持不动（1400 多处调用零改动），语言包按中文原文做 key，
    // 命中就用对应语种，没命中回退英文 —— 长尾文案逐批补齐时不需要动界面代码。




    // v8.2：自绘深色数字微调框。原生 NumericUpDown 的上下按钮由系统绘制，颜色改不掉，
    // 在深色界面里就是一块白胶布。这里整个换掉：深底 + 8 圆角 + 两枚自绘箭头 + 聚焦亮边。


    // v8.2：自绘复选框。原生 CheckBox 的小方框由系统画成白底黑勾，FlatStyle 也压不住，
    // 在深色卡片上就是一块白贴纸。这里整块自绘：15px 圆角方框 + 强调色勾 + 文字自己排。
    // 底色按控件自己的 BackColor 铺（主题给的就是卡片在该区域的颜色），所以不会露接缝。


    // v8.2：自绘单选框，同款思路。系统的白圆点改成“深底空心圈 + 选中填强调色 + 亮心”


    // 圆角渐变按钮：全局美化（渐变底色 + hover 提亮 + 按下变暗）


class RoundBtn : Button
    {
        public Color Bg, Fg;
        public bool Mark;   // v9.2：被当作"当前选定"的按钮（快捷预设），画一圈收束光环
        public bool NoGloss;   // v17.6：亮色实心按钮不画玻璃高光/底部阴影，避免白字被切成两截
        bool hover_, down_;
        // v17.9：按钮外框那圈"黑框"的根因 —— 控件矩形是方的，胶囊只画在中间，
        // 四个角露出的是兜底暗色 (24,32,44)，压在卡片/顶栏的渐变上就是一圈黑。
        // 用圆角 Region 把四个角从控件里裁掉，露出父容器真正画的底色。
        void ShapeRegion()
        {
            try
            {
                if (Width < 8 || Height < 8) return;
                int r2 = Math.Max(6, Math.Min(20, Math.Min(Height / 2, Width / 2)));
                using (GraphicsPath p2 = UiKit.RoundedPath(new Rectangle(0, 0, Width, Height), r2))
                {
                    Region oldr = Region;
                    Region = new Region(p2);
                    if (oldr != null) oldr.Dispose();
                }
            }
            catch { }
        }
        public bool Ghost;     // v17.6：强制描边胶囊；留空则按底色自动判定（低饱和暗色=次级按钮）
        public RoundBtn(Color bg, Color fg)
        {
            // v17.6：必须显式声明 UserPaint，Button 默认不接管系统主题绘制，
            // 在 PagePanel 这类透底父容器上系统会把按钮画回成白底黑字的原生样子。
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            UseVisualStyleBackColor = false;
            // v17.6：Button 的 BackColor 默认是系统白，一旦系统在 owner-draw 之外还擦一次底，
            // 圆角外面就会亮一块。这里把底色压到主题深色，作为自绘之外的兜底。
            BackColor = UiKit.A(26, 36, 48);
            ForeColor = fg;
            FlatStyle = FlatStyle.Standard;
            FlatAppearance.BorderSize = 1;
            FlatAppearance.BorderColor = UiKit.A(58, 70, 88);
            Bg = bg; Fg = fg; Ghost = false; TabStop = true;
            if (UiKit.Paper)
            {
                // v19.16 纸感对比度修正：在"暖墨"和"纸白"里取与底色对比更高的那个，
                // 黄铜/琥珀这类中等亮度的实心按钮也能落到墨字，不再出现白字压浅金。
                Color pw = Color.FromArgb(246, 241, 229);
                Fg = UiKit.ContrastRatio(UiKit.Ink, bg) >= UiKit.ContrastRatio(pw, bg) ? UiKit.Ink : pw;
                if (UiKit.Lum(bg) > 0.90) Bg = UiKit.PaperCard;
                // 深色按钮经过纸感色调映射后会变成一块暖灰；这种低饱和中间调统一收回暖纸面，
                // 否则就是用户截图里“灰按钮压浅纸”的观感。金黄/危险红等彩色按钮不动。
                else if (bg.A >= 200 && UiKit.Lum(bg) > 0.45 && UiKit.Lum(bg) < 0.90
                    && bg.GetSaturation() < 0.28)
                    Bg = Color.FromArgb(250, 245, 233);
            }
            // v17.6：早年页面里还有一批白底 / 近白底按钮（重新检测、解析、返回定时…），
            // 在深色界面里就是一块白胶布。统一落到次级描边胶囊的暗底 + 浅字；
            // 本来就彩色的主操作（蓝 / 青绿 / 红）原样保留。
            if (!UiKit.Paper)
            {
                int mx0 = Math.Max(bg.R, Math.Max(bg.G, bg.B));
                int mn0 = Math.Min(bg.R, Math.Min(bg.G, bg.B));
                if (bg.A > 200 && mx0 >= 168 && (mx0 - mn0) <= 30)
                {
                    Bg = UiKit.A(26, 36, 48);
                    Fg = Color.White;
                }
            }
        }   // v8.1：按钮可被 Tab 键走到
        // v17.6：按钮外观统一到方案四草稿的同一套口径，所有页面、以后新增的页面都走这里——
        //   ① 圆角之外先铺父容器真正画出来的背景，消灭那圈「黑边 / 黑框」；
        //   ② 不再画顶部玻璃高光和底部 66 alpha 的黑色阴影（亮色按钮上就是一道横切白带 + 一条黑带）；
        //   ③ 低饱和暗色按钮自动落成「描边胶囊」（草稿里的取消任务 / EN / 快捷预设 / 进阶三键都是这个形态），
        //      实心强色按钮保持实心胶囊，描边线按底色明暗自动选深线或亮线。
        bool AsGhost()
        {
            if (Ghost) return true;
            Color c0 = Bg;
            int mx = Math.Max(c0.R, Math.Max(c0.G, c0.B));
            int mn = Math.Min(c0.R, Math.Min(c0.G, c0.B));
            // v17.6：(43,58,80) 这类暗蓝灰绝对差值是 37，但相对饱和度只有 46%，明显是次级的灰；
            // (22,88,82) 深青绿差 66、相对 75%，是强调色，必须保持实心。改看相对饱和度判定。
            return c0.A > 200 && mx > 0 && mx <= 96 && (mx - mn) * 100 / mx < 65;
        }

        // v17.6：句柄建好后再钉一次自绘样式 —— Button 在 CreateHandle 阶段会把
        // 系统主题绘制接回去，之前 SetStyle 的结果就被丢掉了，屏幕上于是露出一颗白按钮。
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer, true);
                UseVisualStyleBackColor = false;
                BackColor = Bg;
            }
            catch { }
            ShapeRegion();
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ShapeRegion();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // v17.6：圆角之外先用与页面/卡片接近的暗底铺满整个客户区。
            // 之前只画 rc.Inflate(-1,-1) 的胶囊，四边 1px 与四个圆角外露出的是控件自己的
            // 系统白底 —— 用户看到的就是那圈"黑边 / 白框"。
            // v17.6：底色不能用 BackColor —— 程序里有一批按钮的 BackColor 被外部写成白色，
            // 而 Ghost（次级按钮）的填充是低 alpha 的白，透出来就成了一颗白按钮，四个圆角外
            // 还会露出白底方角，就是用户说的"按钮旁边的黑边 / 白框"。
            // 这里改为向上找最近一层不透明父容器（页面 / 卡片）的底色来铺，既不会白，
            // 四角也和卡片融在一起。
            // 直接钉死与工作区一致的深色，不再问父容器要颜色 ——
            // 页面里那几层容器的 BackColor 其实是白/系统色（渐变是另外画上去的），
            // 取到就会重新变白。四角已由 Region 圆角裁掉，露出来的是真正的页面底。
            Color fb = UiKit.Paper ? UiKit.PaperBg : UiKit.A(24, 32, 44);
            using (SolidBrush b0 = new SolidBrush(fb)) g.FillRectangle(b0, ClientRectangle);
            Rectangle rc = ClientRectangle;
            if (down_) rc.Offset(0, 1);
            int cr = Math.Max(6, Math.Min(20, Math.Min(rc.Height / 2, rc.Width / 2)));
            using (GraphicsPath path = UiKit.RoundedPath(rc, cr))
            {
                bool ghost = AsGhost();
                Color c = !Enabled ? UiKit.Darken(Bg, 46) : (hover_ ? UiKit.Lighten(Bg, 18) : (down_ ? UiKit.Darken(Bg, 14) : Bg));
                if (UiKit.Paper && UiKit.Lum(c) < 0.30)
                {
                    // 启动早期建立的控件可能还停在深色态；纸感绘制时统一收成暖白面，避免黑块。
                    c = Color.FromArgb(250, 245, 233);
                }
                if (UiKit.Paper)
                {
                    // v18.7 纸感手绘：平涂马克笔色块 + 1.6px 墨线，不要渐变和玻璃感
                    using (SolidBrush pb2 = new SolidBrush(c))
                        g.FillPath(pb2, path);
                    Color edge = UiKit.Paper
                        ? (UiKit.Lum(c) > 0.86 ? Color.FromArgb(172, 158, 132) : UiKit.Ink)
                        : UiKit.Ink;
                    using (Pen inkp = new Pen(edge, UiKit.Paper ? (UiKit.Lum(c) > 0.86 ? 1.2f : 1.4f) : 1.6f))
                        g.DrawPath(inkp, path);
                }
                else if (ghost)
                {
                    // v17.6：次级按钮改成实心深底，不再用低 alpha 白叠加 ——
                    // 在这套自绘环境里 alpha 叠加不可靠（会把系统白底透出来，成了一颗白按钮）。
                    // 观感仍是草稿里的「深底 + 一道亮描边」，和整页底色融为一体。
                    int fa2 = !Enabled ? 8 : (down_ ? 22 : (hover_ ? 46 : 34));
                    Color gfill = UiKit.Composite(UiKit.AA(fa2, 255, 255, 255), UiKit.A(30, 40, 52));
                    using (SolidBrush gb = new SolidBrush(gfill))
                        g.FillPath(gb, path);
                }
                else
                {
                    using (LinearGradientBrush br = new LinearGradientBrush(rc, UiKit.Lighten(c, 8), UiKit.Darken(c, 5), 90f))
                        g.FillPath(br, path);
                }
                if (!UiKit.Paper)
                {
                    Color lc = ghost
                        ? UiKit.AA(!Enabled ? 30 : (hover_ ? 126 : 66), 255, 255, 255)
                        : (UiKit.Lum(c) > 0.42 ? UiKit.AA(48, 0, 0, 0) : UiKit.AA(52, 255, 255, 255));
                    using (Pen bp = new Pen(lc, 1f))
                        g.DrawPath(bp, path);
                }
                // v8.1 / v18.6：键盘焦点环。鼠标点过不留痕，只有 Tab 过来时才亮；加一圈内亮边更清楚
                if (Focused)
                {
                    using (Pen fp = new Pen(UiKit.AA(235, 74, 148, 255), 2f))
                        g.DrawPath(fp, path);
                    Rectangle fr = Rectangle.Inflate(rc, -2, -2);
                    using (GraphicsPath fpth = UiKit.RoundedPath(fr, Math.Max(5, cr - 2)))
                    using (Pen fp2 = new Pen(UiKit.AA(110, 255, 255, 255), 1f))
                        g.DrawPath(fp2, fpth);
                }
                // v9.2：选中光环。外圈一道亮线 + 内圈一圈柔光，压在按键自己的描边里面
                if (Mark)
                {
                    using (Pen mp = new Pen(UiKit.AA(196, 132, 186, 255), 1.5f))
                        g.DrawPath(mp, path);
                    Rectangle ir = Rectangle.Inflate(rc, -2, -2);
                    using (GraphicsPath ip2 = UiKit.RoundedPath(ir, Math.Max(6, cr - 2)))
                    using (Pen sg = new Pen(UiKit.AA(58, 150, 200, 255), 2.4f))
                        g.DrawPath(sg, ip2);
                }
                if (UiKit.Paper)
                {
                    // 纸感浅底：TextRenderer 在浅底上会被低透明度合成（墨字看着发白），
                    // 改用 GDI+ DrawString，实心墨字压得住纸白。
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    Color pw2 = Color.FromArgb(246, 241, 229);
                    Color pf = UiKit.ContrastRatio(UiKit.Ink, c) >= UiKit.ContrastRatio(pw2, c) ? UiKit.Ink : pw2;
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        sf.Trimming = StringTrimming.EllipsisCharacter;
                        sf.FormatFlags = StringFormatFlags.NoWrap;
                        using (SolidBrush tb = new SolidBrush(Enabled ? pf : UiKit.A(120, 110, 92)))
                            g.DrawString(Text, Font, tb, rc, sf);
                    }
                }
                else
                    TextRenderer.DrawText(g, Text, Font, rc, Enabled ? Fg : UiKit.A(150, 160, 176), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnMouseEnter(EventArgs e) { hover_ = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover_ = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs mevent) { down_ = true; Invalidate(); base.OnMouseDown(mevent); }
        protected override void OnMouseUp(MouseEventArgs mevent) { down_ = false; Invalidate(); base.OnMouseUp(mevent); }
    }
    // ============ v7.9 视觉令牌 + 全局皮肤：字体统一 / 文字防裁切 / 标题强调条 / 轻提示 ============


    // ============ v7.9 音效引擎：所有声音由代码合成（零素材、零体积、可关） ============
    // 设计：每种音有自己的“签名”——错误音是两段下坠 + 26Hz 振颤的齿状音（像卡扣卡住），
    // 成功音是 660/990/1320 三连上行金属铃，投递音是噪声道扫 + 高音咔，提醒音是双脉冲，
    // 全部 22.05kHz / 16bit 单声道，运行时现合成进内存，不写磁盘、不依赖系统提示音。


    // ================= 更新 / 版本信息页（卡片式） =================

    // ================= 本地音乐播放（winmm MCI）=================




    // ================= 一起听（房主开房间，多人同步听同一首歌）=================
    // 房主用 TCP(55124) 承载房间：成员、曲库、播放位置、聊天；访客连上后按需向房主拉取音频，
    // 房主每 900ms 广播一次播放位置，访客本地位置漂移超过 500ms 就自行 seek 对时。


    // ================= 定时提醒（数据 + 存储 + 管理窗 + 编辑窗 + 弹窗）=================




    // 提醒管理窗口：增删改 + 启用开关 + 立即测试


    // 新建 / 编辑提醒


    // 到点弹窗：置顶深色卡片，「知道了」/「稍后 10 分钟」

    // 组件下载进度框（暗色自绘，避免系统白控件）


    // ============ Steam 成就：直接解锁（不开 SAM 界面，直连 Steamworks） ============
    //  原理：SteamAPI_Init 之后用 ISteamUserStats 的 SetAchievement + StoreStats 直接把成就写进账号，
    //  不需要一款一款进 SAM 的成就页去点。一个 appid 必须一个进程，所以由主程序拉起自己的子进程干活。


    // ================= v6.7 新增结构与独立窗口 =================
    // ===== v6.8：全局热键 =====
    // ================= v10.0 清理历史 =================
    // 每次扫完 C 盘追加一行「时间|字节|文件数」，最多留 60 条；空态卡片有历史时改画一条迷你折线。


    // ================= v10.0 用时统计 =================
    // 每执行一次定时任务追加一行：日期|次数|分钟；概览窗口读最近 7 天画柱状。


    // ================= v10.0 动作链 =================
    // 到点执行主任务之前，先把"收尾动作"按顺序做一遍：静音 / 锁屏 / 关屏 / 结束指定进程 /
    // 清空剪贴板历史 / 清临时文件 / 弹一条通知。配置一行一项，存 动作链.txt。




    // 热键捕获小窗：按下组合键即返回 {mods, vk}


    // ===== v6.8：本地加密保险箱（AES-256-CBC + PBKDF2） =====


    // 回收站删除（可撤销），用于重复文件清理


    // ===== v6.8：一键游戏加速核心 =====


    // ===== v6.8：桌面自动整理核心 =====


    // ===== v6.8：重复文件扫描核心 =====



    // ===================== v6.9 新增类：剪贴板历史 / 录屏 / 备份 / 文件加密 / 截图标注 =====================

    // v11.0 剪贴板智能分类：按内容特征给出类型，纯本地正则，不外发。



    // ============ v14.0 智能脱敏：手机号 / 身份证 / 银行卡 / 邮箱 / IPv4 ============
















    // ===================== v7.0 快捷指令面板 =====================



    // v8.0：自绘列表（替代 ListBox）。系统滚动条在深色面板里是一条白色亮条，怎么都去不掉，
    // 也不接受配色；这里自己画行、自己画滚动拇指、自己画空态，颜色与整个指令舱一致。




    // ===================== v7.0 系统监控悬浮条 =====================



    // ===================== v7.0 视频 / 音频转换内核 =====================



    // ===================== v7.0 批量重命名 / 格式转换 =====================





    // ===================== v7.0 键鼠宏录制 =====================







    // ===================== v7.0 离线密码本 + 两步验证 =====================









    // ===================== v7.0 隐私痕迹清理 =====================








    // ================= v12.3 排版系统：字号档位 / 字族随语言 / 按钮按文字自适应 / 同行基线对齐 =================
    // 目标只有一个：文字永远看得全，控件永远不互相压。做法是把"量文字"这一步交给程序自己算，
    // 不再依赖当初摆版面时手估的宽度——中英文切换、字体缩放、系统 DPI 变化都不会把字裁掉。


    // ================= v7.1 公共 UI 小件（各功能页共用，独立于 MainForm） =================


    // ================= v7.1 ② 窗口管家 =================


    // ================= v7.1 ② 窗口管家内核 =================


    // ================= v7.1 ④ 网络工具箱 =================


    // ================= v7.1 ④ 网络工具箱内核 =================


    // ================= v7.1 ⑥ Steam 进阶（库清单 / 搬家 / 节点测速） =================
    // ================= v7.8 ① 愿望单降价监控 =================




    // ================= v7.8 ② 游戏库体检 + 一键修 =================






    // ================= v7.1 ⑥ Steam 内核 =================


    // ================= v7.1 ⑧ 二维码编码内核（版本 6~10 自动选 / EC=M / UTF-8 字节模式） =================


    // ================= v7.1 ③ 启动项管理（计划任务补齐） =================




    // ================= v7.1 ⑨ 视频进阶（压缩 / 拼接 / 抽字幕音轨） =================



    // ================= v13.2 视频压缩引擎（目标体积两遍 / 质量优先 / 实时进度） =================




    // ================= v7.1 ⑩ 桌面便签 + 番茄钟 =================


    // 单条便签的小窗口（置顶、可拖动、可编辑）






    // ================= v7.1 ⑧ 图片批量压缩 / 二维码 =================




    // ================= v7.2 ⑥ 端口与进程管家 =================




    // ================= v7.2 ⑧ 高清录屏（硬件编码 → MP4） =================




    // ================= v7.2 ⑨ 音频工具箱 =================




    // ================= v7.2 ⑪ 代理 / 节点管理 =================




    // ================= v7.2 ⑫ ADB 手机工具箱 =================




    // ==================== v7.3 ① UPnP 端口映射 / 开服助手 ====================



    // ==================== v7.3 ② 游戏 / 下载区域延迟优选 ====================



    // ==================== v7.3 ③ Wi-Fi 信号 / 信道分析 ====================






    // 局域网互传：TcpListener 实现的最小 HTTP 服务（不需要管理员权限，也不占用 HttpListener 的 URL ACL）
    // ===================== v11.0 手机遥控服务端 =====================
    // TcpListener 实现的最小 HTTP 服务：不需要管理员权限，也不占 HttpListener 的 URL ACL。
    // 只做三件事：GET / 给手机一张遥控页、GET /api/status 给倒计时、GET /api/cmd?c=xx 执行动作。
    // ================= v12.2 文件秒搜：内存索引（Everything 式） =================
    // 只存「大小 + 全路径」，一次性遍历固定磁盘，边建边报进度，落盘缓存；查询走子串 / 通配 / 体积过滤。


    // ================= v12.2 自动化触发器 =================
    // 一条触发器 = 启用标志 + 类型 + 参数 + 动作。存 触发器.txt（Tab 分隔，方便手改）。


    // ================= v12.2 插件系统 =================
    // 插件目录里放 .ps1 / .py / .cmd，文件名即功能名；头几行写「# 描述: xxx」会拼进面板标题。



    // ================= v12.4 ① 本地语义引擎（离线，零依赖） =================
    // 目的：把「半小时后关电脑」「晚上 11 点关机」「帮我打开视频下载」这类口语，
    // 在指令舱里直接变成一条可执行条目。全部本地计算，不联网、不加载模型。
    // 若 AI\ 目录里放了 llama-cli.exe + *.gguf，AiLocal 会额外启用大模型问答（可选）。


    // ================= v12.4 ①-b 可选本地大模型接入（llama.cpp） =================


    // ================= v12.5 ① 短语记忆（记住你的说法，下次一键套用） =================
    // 存储：AI\短语.txt，一行一条：说法 <TAB> 动作 <TAB> 秒 <TAB> 次数 <TAB> 最后时间



    // ================= v12.6 ③ AI 记忆打包（短语 + 插件清单 + 仓库源） =================


    // ================= v12.5 ② 离线中文语音输入（用 Windows 自带识别引擎，不联网） =================


    // ================= v12.4 ② 插件仓库内核 =================
    // 索引：插件\仓库.json（本地，首次自动生成 4 个内置插件）
    // 远端：插件\仓库源.txt 一行一个 JSON 地址（可选，失败不影响本地）






    // 桌面看板：CPU / 内存 / 网速 / C 盘余量，独立小窗（快捷指令 / 系统监控条入口）
    // ================= 每日一图壁纸（Bing 今日图 → 设成桌面壁纸） =================
}
