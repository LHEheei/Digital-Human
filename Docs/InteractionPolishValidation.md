# 交互细节优化与验证

日期：2026-09-20。Unity 6000.4.6f1。基线提交 `fb4f6798e3f8ce5f7a849c03dd506b158892f444`，工作区原本干净；已有 97 项 EditMode、30 项 PlayMode 通过及 Android 构建成功。

本轮完成三个优先项：语音错误恢复、动作短语误触发、聊天界面增量刷新。未增加会话持久化或 GitHub 自动化流程。

## 语音错误恢复

- 授权拒绝、永久禁止、授权超时按 `AppErr.micPerm` 处理，暂停自动重试并提供“重试语音”。编辑文字、发送文字对话和切换前后台不会擅自解除暂停；API 设置刷新或明确重试会重置恢复状态。
- 用户仍可使用文字对话，未解决的权限提示在回复结束后继续显示；不再因新建文字轮次而丢失恢复入口。
- 系统 ASR 在运行期间报告权限错误时，Java 使用专门的回调，托管桥接保留权限错误类型。回调继续校验会话 ID，忽略过期会话。
- 普通服务错误采用指数退避。默认延迟为 1.2、2.4、4.8、9.6、19.2、30 秒，之后保持 30 秒；显式设置更慢的基础间隔时不强制缩短。普通未听到说话及成功识别恢复基础间隔。手动重试可取消当前等待，但仍由原来的单一循环管理 ASR 清理与重启。
- “超时”不再一律静默忽略；已分类的权限、网络等异常优先于文案。正常无语音/无匹配仍沿用既有轻量重试。

## 动作规则

纯逻辑复现中，基线将“你好像有些累了。”与“再见面时我们可以继续聊。”均判为 `joy + wave`，现在均为 `neutral + none`；“你好。”仍为 `joy + wave`。

问候与肯定要求短语边界，支持“你好呀”“明白了”等常见形式；引号中的文本不触发本地规则，删除引用时保留词边界；共情和解释触发词检查同一分句中的邻近否定。未匹配的句子仍可使用既有模型元数据。动作时长、骨骼曲线和冷却规则不变。该实现仍是本地规则，不宣称覆盖完整中文语义或多层嵌套引用。

## 聊天刷新测量

历史和单个轮次维护修改版本；仅输入文本时更新发送按钮，仅状态变化时更新状态与操作入口。只有历史内容或界面尺寸变化才重新布局历史，消息行按轮次版本更新，继续保持裁剪、模式切换顺序和阅读位置。

在同一实际演示场景中构造 40 轮长文本，预热 10 次，再执行 100 次输入变化及界面更新。测试使用只在测试程序集中的 `ILayoutController` 探针统计历史区横向/纵向布局计算。

| 局部测量 | 优化前 | 优化后 |
| --- | --- | --- |
| 输入变化次数 | 100 | 100 |
| 历史区布局计算次数 | 200 | 0 |
| 同步更新耗时 | 504.8569 ms | 1.8323 ms |

优化前同一回归用例失败，优化后通过。证据为产物目录中的 `before-ui.xml`、`before/typing-metrics.json` 与 `after/typing-metrics.json`。耗时包含输入控件更新及测试反射调用，属于编辑器批处理中的局部测量，不是整帧时间、手机帧率或长期负载数据。当前 Mono 的线程分配计数返回零，未据此作 GC 分配结论。

## 回归与构建

产物目录：`Builds/Validation/interaction-polish-20260920/`。

新增 26 项 EditMode 用例覆盖权限类型优先、服务超时、退避上限、相似词/否定/引用、真实问候与冷却、历史版本；新增 6 项 PlayMode 用例覆盖输入与状态不重排历史、仅刷新变动消息及滚动保持、权限暂停与文字对话、按钮重复点击、退避恢复和原生权限回调类型。既有横竖屏截图用例新增权限提示与重试按钮画面。

| 验证 | 结果 | 证据 |
| --- | --- | --- |
| 全量 EditMode | 123/123 通过 | `editmode-validation-results.xml` |
| 全量 PlayMode | 36/36 通过 | `playmode-validation-results.xml` |
| 最终按钮字号/布局复核 | 1/1 专项通过，横竖屏截图已查看 | `final-ui.xml`、`speech-retry-*.png` |
| Android IL2CPP ARM64 开发包 | 最终构建成功 | `android-final-build.log`、`final.stdout.log` |
| 原生权限回调 | 生成的 IL2CPP 函数中保留 | `summary.json` |
| 配置与修改范围 | 配置和项目设置与基线一致 | `source-before.zip`、`summary.json` |

全量测试后仅统一新增按钮的字号，随后重跑包含聊天、记忆、语音恢复横竖屏截图的场景用例，并重新构建最终 Android 包。已有 FindObjectsByType 过时测试重载和 URP/MToon10 剔除提示仍存在，没有编译错误。

APK：`Builds/Validation/interaction-polish-20260920/DesktopAvatarCar-development.apk`，258,775,585 字节。SHA256：`F55DFA337B56B4FEC945C1957B279E1A1D378916102E10B23096150D09C32A81`。构建结束后恢复 Unity 写入的配置空白字符，并核对配置与项目设置和修复前完全一致。

复现：

```powershell
$env:SRTP_REFRESH_METRICS = 'D:\SRTP\Builds\Validation\interaction-polish-20260920\after'
$env:SRTP_CONVERSATION_PREVIEW = 'D:\SRTP\Builds\Validation\interaction-polish-20260920'
powershell -File Tools/Validate-EmotionPipeline.ps1 -SkipEmotionService
```

没有连接手机或请求真实模型。Android 权限弹窗和系统回调、实际设备上的重试与耗电、输入法和安全区仍需真机验证。未修改场景、Prefab、模型、包版本、持久化格式或正式 API 配置。
