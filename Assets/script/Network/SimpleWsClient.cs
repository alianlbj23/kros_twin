using UnityEngine;
using WebSocketSharp;

public class SimpleWsClient : MonoBehaviour
{
    private WebSocket ws;

    void Start()
    {
        ws = new WebSocket("ws://127.0.0.1:8767");

        ws.OnOpen += (sender, e) =>
        {
            Debug.Log("✅ WebSocket Connected");

            // 連線成功後送一則測試訊息
            ws.Send("Hello from Unity");
        };

        ws.OnMessage += (sender, e) =>
        {
            if (e.IsText)
            {
                Debug.Log("📨 From Server: " + e.Data);
            }
        };

        ws.OnError += (sender, e) =>
        {
            Debug.LogError("❌ WebSocket Error: " + e.Message);
        };

        ws.OnClose += (sender, e) =>
        {
            Debug.Log("🔌 WebSocket Closed");
        };

        ws.ConnectAsync();
    }

    void OnDestroy()
    {
        if (ws != null)
        {
            ws.Close();
            ws = null;
        }
    }
}
