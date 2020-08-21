# TubumuMeeting

![](http://blog.tubumu.com/postimages/mediasoup-01/004.jpg)

对该项目实现上的介绍，见：[使用 ASP.NET Core 实现 mediasoup 的信令服务器](https://blog.tubumu.com/2020/05/05/mediasoup-01/)。

`TubumuMeeting` 是基于 `mediasoup` 实现的视频会议系统，但将其服务端的 `Node.js` 模块使用 `ASP.NET Core` 重新进行了实现。对其 Web 客户端 Demo 也重新进行了实现。本项目应该是 Github 上能搜索到的唯一一个非 Node.js 实现，虽有诸多不完善但也算基本流程能够跑通，望与有兴趣者继续完善。

> 备注：在 mediasoupsettings.json 配置文件中搜索，将 AnnouncedIp 改为本机的局域网 IP。

### 1、启动服务端

打开 `mediasoupsettings.json`。在 `MediasoupStartupSettings.WorkerPath` 节点设置 `mediasoup-worker` 可执行程序的物理路径。在 `TubumuMeeting.Meeting.Web` 目录执行 `dotnet run` 或者在 `Vistual Sudio` 打开解决方案启动 `TubumuMeeting.Meeting.Web` 项目。

```
> cd TubumuMeeting.Meeting.Web
> dotnet run
```

> 备注：如果将 MediasoupStartupSettings.WorkerPath 注释，启动时将自动去 "runtimes/{platform}/native" 目录查找 "mediasoup-worker" 。其中 "{platform}" 根据平台分别可以是：win、osx 和 linux。 详见 Worker.cs 文件中 Worker 类的构造函数。

### 2、启动前端

在 `tubumumeeting-client` 安装 Node.js 包并运行。

```
> cd tubumumeeting-client
> yarn install
> yarn serve
```

### 3、打开浏览器

>备注：请使用 Chrome、Firefox 或 Edge 浏览器。

因为没有将前端放入基于 `ASP.NET Core` 的 Web 项目中，并且没有使用正式的 TLS 证书，所以先访问一次 `https://192.168.x.x:5001/` 。提示不安全时请继续访问。

在同一个浏览器用两个标签或者在两台电脑的浏览器上分别打开 `https://192.168.x.x:8080/?peerId=1` 和 `https://192.168.x.x:8080/?peerId=2` 。提示不安全时请继续访问；提示访问摄像头和麦克风当然应该允许。

> 备注：只是为了测试，PeerId如果是0或1将只消费不生产。 


