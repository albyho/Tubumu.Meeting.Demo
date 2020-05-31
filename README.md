# TubumuMeeting

![](http://blog.tubumu.com/postimages/mediasoup-01/004.jpg)

对该项目实现上的介绍，见：[使用 ASP.NET Core 实现 mediasoup 的信令服务器](https://blog.tubumu.com/2020/05/05/mediasoup-01/)。

`TubumuMeeting` 是基于 `mediasoup` 实现的视频会议系统，但将其服务端的 `Node.js` 模块使用 `ASP.NET Core` 重新进行了实现。对其 Web 客户端 Demo 也重新进行了实现。本项目应该是 Github 上能搜索到的唯一一个非 Node.js 实现，虽有诸多不完善但也算基本流程能够跑通，望与有兴趣者继续完善。

> 备注：搜索整个解决方案，将所有类似 `192.168.x.y` 的IP 改为本机的局域网 IP。涉及文件：launchSettings.json、appsetttings.json、App.vue

### 1、启动服务端

打开 `appsettings.json`。在 `MediasoupStartupSettings.WorkerPath` 节点设置 `mediasoup-worker` 可执行程序的物理路径。在 `TubumuMeeting.Meeting.Web` 目录执行 `dotnet run` 。

```
> cd TubumuMeeting.Meeting.Web
> dotnet run
```

### 2、启动客户端

在 `tubumumeeting-client` 安装 Node.js 包并运行。

```
> cd tubumumeeting-client
> yarn install
> yarn serve
```

### 3、打开浏览器

>备注：请使用 Chrome、Firefox 或 Edge 浏览器。

因为没有将客户端直接放入 ASP.NET Core 中并且没有使用正式的 TLS 证书， 所以先访问一次 `https://192.168.18.233:5001`。提示不安全时请继续访问。

在同一个浏览器用两个标签，或者在两台电脑的浏览器上分别打开`https://192.168.18.233:8080/?peerId=0` 和 `https://192.168.18.233:8080/?peerId=1`。提示不安全时请继续访问；提示访问摄像头和麦克风当然应该允许。

> 备注：因为目的是为了快速验证 TubumuMeeting 服务端实现的正确性，Web 客户端只简单实现了双人视频会议。


