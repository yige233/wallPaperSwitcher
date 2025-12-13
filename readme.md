# WallPaperSwitcher

从自定义 URL 加载图像，并设置为壁纸，并且以原生幻灯片形式播放，拥有和正常设置幻灯片壁纸相同的过渡动画。

将壁纸设置为单张图像的话，不论通过哪种方法去切换壁纸，都没有动画，切换过程是生硬的。但幻灯片不会，并且切换幻灯片图像文件夹时，也会有动画，所以可以利用这点，将我们需要的图像提前放在一个文件夹中，然后告诉系统：我要将这个文件夹作为壁纸相册。然后系统就会播放该文件夹中唯一的一张图片，而且有过渡动画。

那为啥不直接把所有壁纸放进一个文件夹，直接播放该文件夹呢？

这个程序的特别之处在于，它是从 web 获取壁纸而非本地文件夹，也就是说我可以配置好一个随机壁纸 API，本地就不用存储那么多的图片了。我的笔记本也可以使用该 API 获取壁纸，节省笔记本的存储空间。甚至于都不需要壁纸，假设有一个 pixiv 图片 API（）

其实从 web 获取壁纸也不稀奇，主要是没找到过这个功能和幻灯片播放壁纸相结合的工具，就只能想办法手搓了() 其实也不是手搓的，99%的代码都来源于 Gemini 3 Pro

## 使用方法

在 powershell 中以管理员身份运行安装脚本：

```powershell
.\install.ps1 -ImageUrl "http://localhost/random-picture?302" -IntervalSeconds 60 -BasePath "D:\wallPaper-switcher"
```

其中：

`-ImageUrl`为图片的 URL，配置一个返回随机壁纸图像的 URL，就可以做到随机切换壁纸了。总之就是下一张壁纸是什么，完全取决于该 URL 返回的图片。

`-IntervalSeconds`为图片切换间隔，以秒为单位。

`-BasePath`为图片保存路径。需要是一个文件夹。

脚本会生成 `config.ini`、生成一个`下一个壁纸`快捷方式、编译出一个`WallpaperApp.exe`，并创建一个计划任务用来运行该程序。创建计划任务需要管理员权限。

## 配置文件

```ini
[Settings]
ImageUrl=http://localhost/random-picture?302
IntervalSeconds=60
BasePath=D:\wallPaper-switcher
Log=false
CurrentSlot=Slot_1

```

其中 `Log` 为是否记录日志，默认为 `false`。不要修改 `CurrentSlot`，该项是用于存储当前正在使用的壁纸文件夹。
