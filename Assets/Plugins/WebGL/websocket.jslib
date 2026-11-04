// WebSocket jslib —— WebGL / 小游戏侧通道
//
// 为什么需要 jslib：
//   浏览器与小程序运行时里没有 BSD socket，WebSocket 只能由 JS 持有；
//   C# 通过 DllImport 调这里，这里再通过 SendMessage 把事件推回 Unity。
//
// 三条硬约定（与 Assets/Framework/Network/WebGLWebSocketBackend.cs 一一对应，改一边必须改另一边）：
//   1. 游戏对象名固定 "WebSocketBridge"，方法名 OnOpen / OnMessageBase64 / OnError / OnClose
//   2. 二进制用 base64 传，格式 "id,base64"（SendMessage 只能传字符串）
//   3. 连接 id 由本文件分配并返回，C# 之后所有收发都带这个 id
//
// 调试：window.__cywsDebug = true 打开日志。

mergeInto(LibraryManager.library, {

  CyWsCreate: function (urlPtr) {
    var url = UTF8ToString(urlPtr);

    if (!window.__cyws) {
      window.__cyws = { nextId: 1, map: {} };
      console.log("[WS] runtime init");
    }

    // SendMessage 三重兜底：不同 Unity 版本 / 小游戏转换层暴露的入口不一样
    if (!window.__cywsSendToUnity) {
      window.__cywsSendToUnity = function (go, method, msg) {
        try {
          if (typeof Module !== "undefined" && Module && typeof Module.SendMessage === "function") {
            Module.SendMessage(go, method, msg);
            return true;
          }
          if (typeof SendMessage === "function") {
            SendMessage(go, method, msg);
            return true;
          }
          if (window.unityInstance && typeof window.unityInstance.SendMessage === "function") {
            window.unityInstance.SendMessage(go, method, msg);
            return true;
          }
        } catch (e) {
          console.error("[WS] SendToUnity error:", e);
        }
        return false;
      };
    }

    function log() {
      if (window.__cywsDebug) console.log.apply(console, arguments);
    }

    function toUnity(method, payload) {
      var ok = window.__cywsSendToUnity("WebSocketBridge", method, payload);
      if (!ok) console.warn("[WS] SendMessage 未就绪，事件丢失: " + method);
      return ok;
    }

    function uint8ToBase64(u8) {
      var binary = "";
      var chunk = 0x8000;
      for (var i = 0; i < u8.length; i += chunk) {
        binary += String.fromCharCode.apply(null, u8.subarray(i, i + chunk));
      }
      return btoa(binary);
    }

    var id = window.__cyws.nextId++;
    var conn = { ws: null, open: false, state: 0 };   // state: 0 连接中 / 1 打开 / 3 关闭
    window.__cyws.map[id] = conn;

    log("[WS] 连接 " + url + " (id=" + id + ")");

    try {
      var useWx = (typeof wx !== "undefined" && typeof wx.connectSocket === "function");

      if (useWx) {
        // 微信小游戏：wx.connectSocket 返回 SocketTask（没有 readyState，自己维护状态）
        var task = wx.connectSocket({ url: url });
        conn.ws = task;

        task.onOpen(function () {
          conn.open = true; conn.state = 1;
          log("[WS] open (id=" + id + ")");
          toUnity("OnOpen", "" + id);
        });

        task.onMessage(function (res) {
          try {
            if (res && res.data instanceof ArrayBuffer) {
              var u8 = new Uint8Array(res.data);
              toUnity("OnMessageBase64", id + "," + uint8ToBase64(u8));
            } else {
              toUnity("OnError", id + ",收到文本消息，协议只支持二进制");
            }
          } catch (e) {
            console.error("[WS] onmessage error:", e);
            toUnity("OnError", id + ",onmessage exception");
          }
        });

        task.onError(function (e) {
          console.error("[WS] error (id=" + id + ")", e);
          toUnity("OnError", id + ",ws error");
        });

        task.onClose(function (e) {
          var code = (e && e.code) ? e.code : 0;
          var reason = (e && e.reason) ? e.reason : "";
          conn.open = false; conn.state = 3;
          toUnity("OnClose", id + "," + code + "," + reason);
          delete window.__cyws.map[id];
        });

      } else {
        // 浏览器 / WebGL（微信转换层通常也提供 WebSocket 兼容实现）
        var ws = new WebSocket(url);
        ws.binaryType = "arraybuffer";
        conn.ws = ws;

        ws.onopen = function () {
          conn.open = true; conn.state = 1;
          log("[WS] open (id=" + id + ")");
          toUnity("OnOpen", "" + id);
        };

        ws.onmessage = function (evt) {
          try {
            if (evt.data instanceof ArrayBuffer) {
              var u8 = new Uint8Array(evt.data);
              toUnity("OnMessageBase64", id + "," + uint8ToBase64(u8));
            } else if (typeof Blob !== "undefined" && evt.data instanceof Blob) {
              evt.data.arrayBuffer().then(function (buf) {
                toUnity("OnMessageBase64", id + "," + uint8ToBase64(new Uint8Array(buf)));
              }).catch(function (err) {
                console.error("[WS] Blob.arrayBuffer error:", err);
              });
            } else {
              toUnity("OnError", id + ",收到文本消息，协议只支持二进制");
            }
          } catch (e) {
            console.error("[WS] onmessage error:", e);
            toUnity("OnError", id + ",onmessage exception");
          }
        };

        ws.onerror = function (e) {
          console.error("[WS] error (id=" + id + ")", e);
          toUnity("OnError", id + ",ws error");
        };

        ws.onclose = function (e) {
          var code = (e && e.code) ? e.code : 0;
          var reason = (e && e.reason) ? e.reason : "";
          conn.open = false; conn.state = 3;
          log("[WS] close (id=" + id + ") code=" + code + " reason=" + reason);
          toUnity("OnClose", id + "," + code + "," + reason);
          delete window.__cyws.map[id];
        };
      }
    } catch (e) {
      console.error("[WS] create error:", e);
      toUnity("OnError", id + "," + String(e));
    }

    return id;
  },

  CyWsSend: function (id, dataPtr, len) {
    var conn = window.__cyws && window.__cyws.map[id];
    if (!conn || !conn.ws || !conn.open) {
      console.warn("[WS] 发送失败：连接不可用 id=" + id);
      return;
    }
    if (len <= 0) return;

    try {
      // 必须 slice：HEAPU8 的视图会被复用，直接 send 视图会发到脏数据
      var bytes = HEAPU8.slice(dataPtr, dataPtr + len);
      conn.ws.send(bytes);
    } catch (e) {
      console.error("[WS] send error:", e);
    }
  },

  CyWsClose: function (id) {
    var conn = window.__cyws && window.__cyws.map[id];
    if (!conn || !conn.ws) return;
    try {
      conn.ws.close();
      console.log("[WS] 客户端主动关闭 id=" + id);
    } catch (e) {
      console.error("[WS] close error:", e);
    }
  },

  CyWsState: function (id) {
    var conn = window.__cyws && window.__cyws.map[id];
    if (!conn) return 3;              // CLOSED
    if (conn.state !== undefined) return conn.state;
    return conn.ws && conn.ws.readyState !== undefined ? conn.ws.readyState : 3;
  }
});
