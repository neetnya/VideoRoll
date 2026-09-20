VideoRoll —— 便携式视频播放列表小工具
========================================

一个绿色便携的小窗口程序：保存视频播放列表，双击用本地 mpv 播放器播放，
并且把“软件里的列表”当作播放列表（自动绕过 autoload.lua 脚本）。


【直接使用（无需安装任何东西）】
1. 把整个 "VideoRoll" 文件夹复制到任意位置（已自带 .NET 运行环境，绿色便携）。
2. 双击运行 VideoRoll.exe。
3. 首次使用：点右上角“设置”按钮，选择你的 mpv.exe（也可以直接在 config.json
   里填 MpvPath）。
4. 把视频文件夹（或单个视频文件）拖进窗口，即自动导入列表。
5. 双击列表中某一项 → 用 mpv 播放，且整个软件列表就是 mpv 的播放列表。


【操作说明】
- 拖拽文件夹 / 视频文件到窗口：导入（默认递归搜索子文件夹）。
- 双击（或按回车）：从该项开始播放，播放顺序 = 软件列表当前顺序。
- Ctrl + 点击 / Shift + 点击：多选。
- Del：从列表中移除（只删列表项，绝不删除真实文件）。
- 右键菜单：播放 / 打开文件所在目录 / 从列表移除 / 添加文件 / 添加文件夹 / 清空。
- 顶部按钮：添加、随机打乱、清空列表、设置。


【数据保存在哪】
- config.json   —— 设置（mpv 路径、扩展名、是否递归、额外参数）
- playlist.json —— 播放列表（自动保存；删除、打乱、导入都会立即记住）
两个文件都保存在 VideoRoll.exe 同目录，随文件夹一起携带，下次打开自动恢复。


【关于“越过 autoload.lua”是怎么实现的】
点播放时，程序会以如下参数启动 mpv：

    --playlist=<临时列表.m3u8> --playlist-start=<第 N 项> --script-opts=autoload-disabled=yes

- --script-opts=autoload-disabled=yes：明确禁用 autoload.lua 脚本
  （该脚本支持 disabled 选项，见 autoload.lua 顶部注释里的 disabled=no）。
- 程序把软件列表按当前顺序写进一个临时 .m3u8 播放列表传给 mpv，并用
  --playlist-start 从你双击的那一项开始。这样 mpv 里的“上一个/下一个”
  （< 和 > 键）就在你的软件列表里顺序切换，不会再被 autoload.lua 把
  “当前目录的视频”塞进播放列表。


【设置项说明（config.json）】
- MpvPath   ：mpv.exe 完整路径；留空则自动探测（exe 同目录、mpv 子目录、PATH）。
- Extensions：识别为视频的扩展名列表（不含点）。
- Recursive ：导入文件夹时是否递归搜索子文件夹（true / false）。
- ExtraArgs ：额外追加给 mpv 的启动参数（一般不用改）。


【自行编译（可选，需要联网）】
需要 .NET 9 SDK。在源码目录执行：

    dotnet publish -c Release -r win-x64 --self-contained true ^
      -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true ^
      -p:IncludeNativeLibrariesForSelfExtract=true -o dist-single

即可得到单文件 VideoRoll.exe（单文件打包需要联网下载组件；本压缩包内的版本
为免联网即可运行的“文件夹版”，功能完全一致）。
