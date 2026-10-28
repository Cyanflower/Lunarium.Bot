# 归档维护命令

## `/维护 编辑配置`

打开当前服务器的归档配置面板。首次使用会创建该服务器的配置；已有配置时进入修改模式。

权限：

- 已有配置：`DeployerIds` / `MaintainerId` 绕过，其余用户需要拥有 `StaffRoleIds` 中的任一身份组。
- 新建配置：仅 `DeployerIds` / `MaintainerId` 可操作。

面板字段：

- 活跃线程上限（900–999，默认 970）
- 归档管理身份组（空 = 仅部署者/维护者可操作）

点“编辑配置”打开模态窗修改，点“应用”才会通过 `ConfigLoader<ArchiveConfig>.TryUpdate` 写回 `appsettings.json`。
如果 `DeployerConfig.AllowModifyByInteraction` 为 `false`，面板可以查看，但应用会被拒绝。

## `/维护 重设间隔 [分钟]`

重新设置全局归档间隔（5–1440 分钟），不填参数则恢复默认 10 分钟。

权限：仅 `DeployerIds` / `MaintainerId`。

成功后立即调用 `ArchiverManager.UpdateInterval`，不需要重启。
