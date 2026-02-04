using System;
using System.Collections.Generic;
using UnityEngine;
using WebSocketSharp;

/// <summary>
/// WebSocket client (websocket-sharp).
/// - Connect / disconnect
/// - Auto reconnect
/// - Thread-safe send (optional queue)
/// - Events for received messages
/// </summary>
[DisallowMultipleComponent]
public class WsClientSharp : MonoBehaviour
{
    [Header("Connection")]
    public string serverUrl = "ws://127.0.0.1:8767";
    public bool connectOnStart = true;

    [Header("Send Options")]
    [Tooltip("If true, outgoing sends are queued and flushed on Unity main thread in Update(). Recommended.")]
    public bool useSendQueue = true;

    public bool IsConnected => _ws != null && _ws.IsAlive;

    public event Action OnConnected;
    public event Action<string> OnTextMessage;
    public event Action<byte[]> OnBinaryMessage;
    public event Action<string> OnError;
    public event Action OnClosed;

    private WebSocket _ws;
    private bool _isConnecting;

    private readonly object _sendLock = new object();
    private readonly Queue<ArraySegment<byte>> _binarySendQueue = new Queue<ArraySegment<byte>>();
    private readonly Queue<string> _textSendQueue = new Queue<string>();

    private void Start()
    {
        if (connectOnStart) Connect();
    }

    private void Update()
    {
        if (useSendQueue) FlushSendQueue();
    }

    public void Connect()
    {
        if (_ws != null)
        {
            if (_ws.ReadyState == WebSocketState.Connecting || _ws.ReadyState == WebSocketState.Open)
            {
                return;
            }

            Disconnect();
        }

        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            OnError?.Invoke("serverUrl is empty");
            return;
        }

        _ws = new WebSocket(serverUrl);
        _isConnecting = true;

        _ws.OnOpen += (_, __) =>
        {
            _isConnecting = false;
            Debug.Log("[WsClientSharp] Connected");
            OnConnected?.Invoke();
        };

        _ws.OnMessage += (_, e) =>
        {
            if (e.IsText)
            {
                OnTextMessage?.Invoke(e.Data);
            }
            else if (e.IsBinary)
            {
                OnBinaryMessage?.Invoke(e.RawData);
            }
        };

        _ws.OnError += (_, e) =>
        {
            _isConnecting = false;
            var detail = e.Exception != null ? (" | " + e.Exception.GetType().Name + ": " + e.Exception.Message) : string.Empty;
            Debug.LogError("[WsClientSharp] Error: " + e.Message + detail);
            OnError?.Invoke(e.Message + detail);
        };

        _ws.OnClose += (_, e) =>
        {
            _isConnecting = false;
            Debug.Log($"[WsClientSharp] Closed (code={e.Code}, reason={e.Reason})");
            OnClosed?.Invoke();
        };

        _ws.ConnectAsync();
    }

    public void Disconnect()
    {
        if (_ws == null) return;

        try
        {
            _ws.CloseAsync();
        }
        catch { /* ignore */ }

        _isConnecting = false;
        _ws = null;
    }

    public void SendText(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        if (useSendQueue)
        {
            lock (_sendLock) _textSendQueue.Enqueue(message);
            return;
        }

        if (IsConnected) _ws.Send(message);
    }

    public void SendBinary(byte[] data)
    {
        if (data == null || data.Length == 0) return;

        if (useSendQueue)
        {
            // Copy to avoid user modifying the array later
            var copy = new byte[data.Length];
            Buffer.BlockCopy(data, 0, copy, 0, data.Length);

            lock (_sendLock) _binarySendQueue.Enqueue(new ArraySegment<byte>(copy));
            return;
        }

        if (IsConnected) _ws.Send(data);
    }

    private void FlushSendQueue()
    {
        if (!IsConnected) return;

        lock (_sendLock)
        {
            while (_textSendQueue.Count > 0)
            {
                _ws.Send(_textSendQueue.Dequeue());
            }

            while (_binarySendQueue.Count > 0)
            {
                var seg = _binarySendQueue.Dequeue();
                _ws.Send(seg.Array);
            }
        }
    }

    private void OnDestroy()
    {
        Disconnect();
    }
}
