# 演示版本库

这里存放**可以交给他人安装或用于答辩演示的 APK 副本**。每个版本一个文件夹，文件夹名使用：

```text
v<版本号>-<用途>-<YYYYMMDD>
```

每个归档应包含：

- `*.apk`：安装包；
- `*.log`：对应 Unity 构建日志（若有）；
- `README.md`：版本能力、限制、测试状态和 SHA-256。

APK 是二进制文件，不提交到 Git；Git 只保存本目录的说明和 [ReleaseIndex.md](ReleaseIndex.md)。

## 新建归档

在 PowerShell 中执行：

```powershell
.\Tools\Archive-Release.ps1 -ApkPath .\Builds\Distribution\DesktopAvatarCar-standalone.apk -Version v0.1.1 -Label standalone -BuildLog .\Builds\Distribution\Android-StandaloneBuild.log -Notes "填写本次演示能力与限制"
```

脚本会复制 APK、可选构建日志，并生成包含 SHA-256 的版本说明。归档前不要覆盖已有版本目录。
