# 桌面提醒

一款只在 Windows 本地运行的轻量待办工具，视觉风格参考苹果“提醒事项”。

## 当前功能

- 新建、编辑、完成和删除待办
- 今天、全部、已完成三个视图
- 搜索和拖动排序
- 设置“今天 18:00”或“明天 09:00”提醒
- 到期后通过 Windows 托盘通知提醒
- 始终置顶、隐藏到系统托盘
- 窗口缩小时自动进入仅显示待办卡片的紧凑模式
- 紧凑模式按当前卡片数量自动调整最小高度，不显示滚动条
- 其他应用进入全屏时自动隐藏，退出全屏后恢复
- 不占用任务栏，仅在系统托盘中显示
- 关闭置顶后可吸附到屏幕四边，自动收起为细条并在悬停时展开
- 自动保存窗口位置、尺寸和置顶状态
- 所有数据仅保存在本机

## 使用

打开发布目录，双击 `DesktopTodo.exe`。

- 窗口右上角的定位图标用于切换“始终置顶”
- 点击减号隐藏到系统托盘
- 双击托盘图标重新显示
- 点击待办右侧的 `⋯` 设置时间或删除
- 选中待办后按 `Delete` 可删除

## 数据位置

数据默认保存在：

`%LOCALAPPDATA%\DesktopTodo\data.json`

删除软件本身不会自动删除该数据文件。

## 开发与构建

需要 .NET 8 SDK：

```powershell
dotnet build DesktopTodo\DesktopTodo.csproj
dotnet publish DesktopTodo\DesktopTodo.csproj -c Release -r win-x64 --self-contained false
```

项目不使用在线 API，也不包含遥测或广告代码。
