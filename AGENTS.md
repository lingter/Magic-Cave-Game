# 项目操作限制

## Unity 编辑器必须通过 MCP 操作

本项目中，任何涉及 Unity Editor 的操作，默认一律通过 Unity MCP 执行。只有用户明确批准该项操作使用非 MCP 方式时，才允许例外。

适用范围包括但不限于：

- 读取或改变编辑器实时状态、场景、GameObject、Prefab、组件、Inspector 配置及资源状态。
- 创建、修改或保存场景、Prefab、材质、ScriptableObject 等 Unity 资源。
- 触发资源导入、刷新、脚本编译、域重载，以及读取编辑器 Console。
- 进入或退出 Play Mode、模拟编辑器内输入、运行 Unity 测试、截图及构建运行包。
- 启动、关闭或控制 Unity 编辑器进程，包括独立副本、隔离项目、后台及批处理实例。

未获得明确批准时，不得通过 Unity 命令行、shell、外部脚本、编辑器自动化脚本、请求文件或文件监听器、直接 UI 自动化等方式，替代或绕过 MCP。也不得通过直接改写场景、Prefab 或其他 Unity 序列化资源文件来绕过此限制。

若当前会话没有可用的 Unity MCP 工具，先按下文规定检查并启用 MCP。若仍不可用，或 MCP 不支持所需操作：停止该项编辑器操作，说明缺少的能力，并在执行替代方案之前请求用户明确批准。可以继续进行不依赖该项编辑器操作的源码、文档等文件工作。

用户要求制作、修复、测试或构建，并不自动代表批准使用非 MCP 的编辑器操作方式。沙箱权限批准或工具自动审查通过，也不能替代这项明确批准。例外授权仅适用于用户批准的范围，不得扩展为整个项目的永久豁免。

## 允许通过终端启用 MCP

用户已授权将终端启用 MCP 作为上述限制的有限例外。执行编辑器任务时，可以通过终端检查 MCP 配置、注册或启用本项目的 Unity MCP、启动匹配版本的本地 MCP 服务，以及检查服务连接；这些步骤不需要再次取得项目操作方式的批准。环境要求的文件、网络和沙箱权限仍须遵守。

此例外仅用于建立 MCP 通道，不包含通过终端启动或控制 Unity Editor、执行编辑器方法、触发编译、运行测试、构建或修改 Unity 序列化资源。不得通过 curl、外部脚本或自行发送编辑器命令替代会话中的 MCP 工具调用。

### 启用步骤（Windows / PowerShell）

1. 检查现有配置与工具：

   ```powershell
   Get-Command codex, uvx
   codex mcp list
   ```

   同时检查当前会话是否已提供 Unity MCP 工具。`mcp list` 中显示 `enabled` 只表示配置已启用，不能证明服务在线或已连接目标 Unity 项目。

2. 本项目已配置的服务名为 `unityMCP`，地址为 `http://127.0.0.1:8080/mcp`。仅在缺失时注册，避免覆盖已有正确配置：

   ```powershell
   codex mcp add unityMCP --url http://127.0.0.1:8080/mcp
   ```

   若配置存在但被禁用，仅将对应配置段的 `enabled` 改为 `true`；保留其他服务和设置。Codex 支持用户级 `~/.codex/config.toml` 与受信任项目的 `.codex/config.toml`，修改前先确认实际生效位置。

3. 检查服务是否已运行；已运行时优先复用，不重复启动，不终止未知进程。当前项目的 Unity MCP 包版本为 `10.3.0`，其默认服务包为 `mcpforunityserver==10.3.0`。服务未运行时，可隐藏窗口启动：

   ```powershell
   Start-Process -FilePath (Get-Command uvx).Source -ArgumentList @('--from', 'mcpforunityserver==10.3.0', 'mcp-for-unity', '--transport', 'http', '--http-url', 'http://127.0.0.1:8080', '--project-scoped-tools') -WindowStyle Hidden -RedirectStandardOutput "$env:TEMP\magic-cave-unity-mcp.stdout.log" -RedirectStandardError "$env:TEMP\magic-cave-unity-mcp.stderr.log" -PassThru
   ```

   此命令启动 Python MCP 服务，不启动 Unity Editor。首次运行可能需要下载服务及依赖。插件版本、端口或服务来源变化后，先只读核对项目包与现有配置，再调整命令；不得擅自升级插件或使用不匹配的最新版服务。

4. 确认 Unity 插件已连接该服务。若 Unity 未打开，或需要在编辑器中点击 `Window > MCP for Unity` 下的 `Connect` 等按钮，仍须由用户手动操作，或取得用户对该项非 MCP 编辑器操作的明确批准；不能将启动 MCP 服务视为这类批准。

5. 服务可用但会话仍无 Unity MCP 工具时，提示用户重新加载 MCP 连接；客户端需要时重启 Codex。不要自动关闭用户客户端。工具出现后，通过 MCP 的只读能力确认连接的是 `E:\UnityProject\Magic Cave Game`，然后再执行编辑器任务。注册成功、端口可达或服务进程存在均不等于已完成此验证。

配置依据：[OpenAI MCP 配置文档](https://learn.chatgpt.com/docs/extend/mcp?surface=cli)。Unity 服务启动参数和版本依据项目已安装的 `com.coplaydev.unity-mcp` 包中 `package.json`、`Editor/Helpers/AssetPathUtility.cs` 与 `Editor/Services/Server/ServerCommandBuilder.cs`；后续以实际安装版本为准。
