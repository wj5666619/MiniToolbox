# 贡献指南

感谢愿意读代码、提 Issue 或发 PR。这个项目是 Windows 单文件工具集，优先保证「小、稳、可回退」，不接受为炫技引入大型依赖。

## 先确认

- **Bug / 功能建议**：先搜 [Issues](https://github.com/wj5666619/MiniToolbox/issues)，避免重复；按模板填写版本、Windows 版本、复现步骤与日志片段。
- **安全漏洞**：不要开公开 Issue，按 [SECURITY.md](SECURITY.md) 私密披露。
- **大改功能**：先开 Issue 说明场景、交互与体积影响，达成一致再写代码，避免 PR 方向不符。
- **第三方素材 / 代码**：必须标明来源与许可证，并更新 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)；许可证不兼容的代码不要提交。

## 构建环境

- Windows 10 / 11，.NET Framework 4.x（系统自带 `csc.exe`）。
- 根目录需要 `定时关机.ico`、`皮肤.txt`、`sam_tool.zip`、`clean_tool.zip`。
- 不依赖 Visual Studio、NuGet 或外部 SDK。

```powershell
# 本地验证，跳过签名
.\build.ps1 -NoSign

# 完整构建（会签名、算 SHA256、写 发布\update.json、打 7z）
.\build.ps1
```

构建后运行 `小工具箱.exe` 做冒烟测试：启动、切 3 个页面、切一套皮肤、退出重开。涉及下载 / 清理 / 更新 / 企业策略的改动，请在 PR 里写明手工验证步骤。

## 代码约定

- 目标框架与现有代码一致，**不引入 NuGet 包**，优先使用 .NET Framework 自带程序集。
- 保持单文件发布：新增资源前先评估体积；大资源走按需下载，不直接塞进 exe。
- UI 文案中英双语时，沿用现有 `T("中文", "English")` 方式；不要硬编码只显示一种语言。
- 新代码不要留空 `catch {}`；用 `Diag.Swallow("文件:行", ex)` 记录，避免排障断链。
- 不提交：`bin/`、`obj/`、`*.exe`、`*.pfx`、`*.7z`、日志、诊断包、`发布/.gh_token.txt`、任何账号令牌或本机路径。
- 不 reformat 整文件；PR 尽量只改相关行，方便 review 与回退。

## 提交与 PR

1. Fork 仓库，从 `main` 建分支：`fix/xxx`、`feat/xxx`、`docs/xxx`。
2. 一个 PR 只做一件事；提交信息用「类型: 说明」，例如 `fix: 修复纸感皮肤侧栏文字对比度`。
3. PR 描述写清：问题、改动、验证方式、体积变化（`小工具箱.exe` 字节数）、是否影响企业策略 / 更新链路。
4. CI 必须通过；未通过先修，不要反复重跑碰运气。
5. 维护者合并后，发布由维护者统一打 tag，PR 作者不需要改版本号或上传 Release。

## 文档

- 用户向说明改 `README.md`；版本变更改 `CHANGELOG.md`；企业字段改 `企业管理指南.md`。
- 官网在 `docs/`，改完在本地打开 `docs/index.html` 检查桌面与窄屏；不要引用不存在的图片或外链。
- 新增截图放 `docs/assets/`，文件名用小写英文与短横线，压缩后再提交。

## 行为准则

参与讨论请对事不对人：不人身攻击、不歧视、不泄露他人隐私与未公开漏洞。维护者可以删除违规内容并拒绝不合规 PR。
