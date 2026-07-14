# Android RTSP 服务

项目原有的 `VLCMediaPlayer` 支持在 Android 上拉取并播放 RTSP，但不包含用于配置输出端的 Unity 组件。现在可使用 `VLCRtspServer` 将一个本地文件或网络媒体源转发为 Android 设备上的 RTSP 服务。

## 快速使用

1. 在场景中新建一个 GameObject，添加 `VLCRtspServer` 组件。
2. 设置 **Source Media Path**：可以是 `file:///...`、Android 可访问的文件 URI、HTTP/RTSP 等 LibVLC 支持的输入。
3. 保持端口为 `8554`，按需修改 **Stream Path**（默认 `stream`）。
4. 如果输入本身已是客户端支持的 H.264/AAC，关闭 **Transcode To H264 Aac** 可显著降低手机负载；格式不兼容时再开启转码。
5. 勾选 **Start On Awake**，或从业务脚本调用 `StartServer()`。
6. 启动后查看日志中的客户端地址，或者调用 `GetClientUrls()`。同一局域网客户端可打开：

   ```text
   rtsp://<Android设备IP>:8554/stream
   ```

业务脚本示例：

```csharp
using LibVLCSharp;
using UnityEngine;

public class StartStreaming : MonoBehaviour
{
    [SerializeField] private VLCRtspServer server;

    private void Start()
    {
        server.sourceMediaPath = "rtsp://192.168.1.20:554/source";
        server.StartServer();
    }

    private void OnApplicationQuit()
    {
        server.StopServer();
    }
}
```

## Android 注意事项

- 项目的 `AndroidManifest.xml` 已声明 `android.permission.INTERNET`，该权限同时允许客户端连接设备上的监听端口。
- Android Player Settings 当前为 ARM64、IL2CPP、最低 API 22、目标 API 34，随包的 ARM64 LibVLC 含 RTP/RTSP stream-output 模块。
- 手机与客户端需要处于可互访的网络；访客 Wi-Fi/AP 隔离、系统 VPN 或路由器防火墙可能阻断 8554 端口。
- 该组件在 Unity Activity 进程中运行，并不是 Android 前台 `Service`。应用被强制停止或系统回收进程后流会停止。
- 该组件转发“媒体 URI”，不直接采集 Unity `RenderTexture`、屏幕或 Android Camera2。若输入目标是实时相机画面，需要另接采集/编码层，再把编码后的媒体交给 RTSP 输出。
- `ListenUrl` 中的 `0.0.0.0` 仅表示监听所有网卡，客户端必须使用 `GetClientUrls()` 返回的实际设备 IP。

## 验证

在另一台同网段设备上可使用 VLC 或 ffplay：

```bash
ffplay -rtsp_transport tcp rtsp://<Android设备IP>:8554/stream
```

若启动失败，打开 **Log To Console**，优先检查输入地址是否可访问，以及 8554 端口是否已被占用。
