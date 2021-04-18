# Tubumu.Meeting.Demo

![截图](http://blog.tubumu.com/postimages/mediasoup-01/004.jpg)

对该项目实现上的介绍，见：[使用 ASP.NET Core 实现 mediasoup 的信令服务器](https://blog.tubumu.com/2020/05/05/mediasoup-01/)。

`Tubumu.Meeting.Demo` 是基于 `mediasoup` 实现的视频会议系统。有别于官方 Demo，本项目有如下特点：

1. 将 mediasoup 服务端的 `Node.js` 模块使用 `.Net Core` 重新进行了实现。
2. 客户端启动时不主动 Produce; 客户端可根据需要 Pull 对端的支持的音视频进行 Consume，而对端按需 Produce。这样做的目的是避免长时间挂起的而又没有对端(Consumer)浏览的客户端，可以节省带宽。
3. 客户端使用 Vue 实现。

> 备注：在 mediasoupsettings.json 配置文件中搜索，将 AnnouncedIp 改为本机的局域网 IP。

## 1、启动服务端

打开 `mediasoupsettings.json`。在 `MediasoupStartupSettings.WorkerPath` 节点设置 `mediasoup-worker` 可执行程序的物理路径。在 `Tubumu.Meeting.Web` 目录执行 `dotnet run` 或者在 `Vistual Sudio` 打开解决方案启动 `Tubumu.Meeting.Web` 项目。

``` shell
> cd Tubumu.Meeting.Web
> dotnet run
```

> 备注：如果将 MediasoupStartupSettings.WorkerPath 注释，启动时将自动去 "runtimes/{platform}/native" 目录查找 "mediasoup-worker" 。其中 "{platform}" 根据平台分别可以是：win、osx 和 linux。 详见 Worker.cs 文件中 Worker 类的构造函数。

## 2、启动前端

在 `tubumu-meeting-demo-client` 安装 Node.js 包并运行。

``` shell
> cd tubumu-meeting-demo-client
> yarn install
> yarn serve
```

## 3、打开浏览器

>备注：请使用 Chrome、Firefox 或 Edge 浏览器。

因为没有将前端放入 Web 项目中，并且没有使用正式的 TLS 证书，所以先访问一次 `https://192.168.x.x:5001/` 。提示不安全时请继续访问。

在同一个浏览器用两个标签或者在两台电脑的浏览器上分别打开 `https://192.168.x.x:8080`，然后选择不同的 Peer 。提示不安全时请继续访问；提示访问摄像头和麦克风当然应该允许。

## 关联项目

1. [Tubumu.Core: 工具类等。](https://github.com/albyho/Tubumu.Core)
2. [Tubumu.Libuv: Libuv 封装。](https://github.com/albyho/Tubumu.Libuv)
3. [Tubumu.Mediasoup: mediasoup 服务端库的 C# 实现](https://github.com/albyho/Tubumu.Mediasoup)
4. [Tubumu.Mediasoup.Common: 公共类封装。也可供 WinForm 或 WPF 等使用。](https://github.com/albyho/Tubumu.Mediasoup.Common)
5. [Tubumu.Mediasoup.AspNetCore: 主要是 Asp.Net Core 服务注册和中间件。](https://github.com/albyho/Tubumu.Mediasoup.AspNetCore)
6. [Tubumu.Mediasoup.Executable: mediasoup-work 可执行程序，目前提供了 macOS 和 Windows 版。可自行编译特定版本，比如 Linux 版。可选。](https://github.com/albyho/Tubumu.Mediasoup.Executable)
7. [Tubumu.Meeting.Server: 会议系统的业务实现。](https://github.com/albyho/Tubumu.Meeting.Server)
8. [Tubumu.Meeting.Server.Consul: 供 Consul 使用的服务注册等设置。可选。](https://github.com/albyho/Tubumu.Meeting.Server.Consul)
9. [Tubumu.Meeting.Demo: 简单的 Asp.Net Core 项目。该项目下的 tubumu-meeting-demo-client 为前端实现。](https://github.com/albyho/Tubumu.Meeting.Demo)

## WeChat group

![WeChat group](https://raw.githubusercontent.com/albyho/Tubumu.Meeting.Demo/dev-07/docs/WeChat-Group.jpg)
