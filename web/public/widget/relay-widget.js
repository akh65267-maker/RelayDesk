/*!
 * Relay Desk chat widget. Dependency-free; embed with:
 *   <script async src="https://YOUR-RELAY/widget/relay-widget.js"
 *           data-relay-workspace="acme"
 *           data-relay-api="https://api.YOUR-RELAY"
 *           data-relay-realtime="https://realtime.YOUR-RELAY"></script>
 *
 * Talks to /api/widget (REST) and /hubs/visitor (SignalR JSON protocol over a raw WebSocket).
 * Renders inside a Shadow DOM so the host page's CSS can't break it, and vice versa.
 */
(function () {
  "use strict";

  var script =
    document.currentScript || document.querySelector("script[data-relay-workspace]");
  if (!script || window.__relayWidgetLoaded) return;
  window.__relayWidgetLoaded = true;

  var cfg = {
    workspace: script.getAttribute("data-relay-workspace"),
    api: (script.getAttribute("data-relay-api") || "").replace(/\/$/, ""),
    realtime: (script.getAttribute("data-relay-realtime") || "").replace(/\/$/, ""),
  };
  if (!cfg.workspace || !cfg.api) {
    console.warn("[relay-widget] data-relay-workspace and data-relay-api are required.");
    return;
  }

  // ---- Per-visitor state (best effort: storage may be blocked) --------------------------------
  var storeKey = "relay-widget:" + cfg.workspace;
  function load() {
    try { return JSON.parse(localStorage.getItem(storeKey) || "{}"); } catch { return {}; }
  }
  function save(s) {
    try { localStorage.setItem(storeKey, JSON.stringify(s)); } catch { /* private mode */ }
  }
  var state = load();
  if (state.expiresAt && new Date(state.expiresAt) <= new Date()) state = {};

  // ---- API -------------------------------------------------------------------------------------
  function request(method, path, body) {
    var headers = { "Content-Type": "application/json" };
    if (state.token) headers.Authorization = "Bearer " + state.token;
    return fetch(cfg.api + "/api/widget" + path, {
      method: method,
      headers: headers,
      body: body ? JSON.stringify(body) : undefined,
    }).then(function (res) {
      if (res.status === 401 || res.status === 404) {
        if (path !== "/sessions") { state = {}; save(state); }
      }
      if (!res.ok) throw new Error("HTTP " + res.status);
      return res.status === 204 ? null : res.json();
    });
  }

  // ---- Minimal SignalR client (JSON protocol, WebSockets, no negotiation) -----------------------
  var RS = "\u001e";
  var socket = null, retry = 0, pingTimer = null, onTyping = null, onChanged = null;

  function connect() {
    if (!cfg.realtime || !state.token) return;
    var url = cfg.realtime.replace(/^http/, "ws") + "/hubs/visitor?access_token=" + encodeURIComponent(state.token);
    socket = new WebSocket(url);
    socket.onopen = function () {
      socket.send(JSON.stringify({ protocol: "json", version: 1 }) + RS);
    };
    socket.onmessage = function (ev) {
      String(ev.data).split(RS).forEach(function (frame) {
        if (!frame) return;
        var msg = JSON.parse(frame);
        if (msg.type === undefined) { // handshake response
          if (msg.error) return console.warn("[relay-widget]", msg.error);
          retry = 0;
          if (state.conversationId) invoke("WatchConversation", [state.conversationId]);
          clearInterval(pingTimer);
          pingTimer = setInterval(function () { send({ type: 6 }); }, 15000);
        } else if (msg.type === 1) { // invocation from server
          var arg = (msg.arguments || [])[0] || {};
          if (msg.target === "conversationChanged" && onChanged) onChanged(arg);
          if (msg.target === "typing" && onTyping) onTyping(arg);
        } else if (msg.type === 7) {
          socket.close();
        }
      });
    };
    socket.onclose = function () {
      clearInterval(pingTimer);
      socket = null;
      if (!state.token) return;
      retry = Math.min(retry + 1, 6);
      setTimeout(connect, 500 * Math.pow(2, retry)); // 1s, 2s, 4s ... up to 32s
    };
  }
  function send(obj) {
    if (socket && socket.readyState === WebSocket.OPEN) socket.send(JSON.stringify(obj) + RS);
  }
  function invoke(target, args) {
    send({ type: 1, target: target, arguments: args });
  }

  // ---- UI --------------------------------------------------------------------------------------
  var host = document.createElement("div");
  host.setAttribute("data-relay-widget", "");
  document.body.appendChild(host);
  var root = host.attachShadow({ mode: "open" });
  root.innerHTML =
    "<style>" +
    ":host{all:initial;font-family:system-ui,-apple-system,'Segoe UI',Roboto,sans-serif;font-size:14px;line-height:1.45;color-scheme:light dark}" +
    "*{box-sizing:border-box}" +
    ".wrap{--bg:#fff;--fg:#171717;--muted:#6b6b6b;--border:#d9d9d9;--accent:#2f5bea;--mine:#e8eeff;--theirs:#f3f4f6}" +
    "@media (prefers-color-scheme:dark){.wrap{--bg:#16181d;--fg:#ededed;--muted:#a0a0a0;--border:#333;--accent:#6d8cff;--mine:#1f2a4d;--theirs:#23262d}}" +
    ".bubble{position:fixed;right:20px;bottom:20px;width:56px;height:56px;border-radius:50%;border:0;background:var(--accent);color:#fff;cursor:pointer;box-shadow:0 4px 16px rgba(0,0,0,.25);font-size:24px;z-index:2147483646}" +
    ".panel{position:fixed;right:20px;bottom:88px;width:min(360px,calc(100vw - 32px));height:min(520px,calc(100vh - 120px));display:none;flex-direction:column;background:var(--bg);color:var(--fg);border:1px solid var(--border);border-radius:12px;box-shadow:0 8px 32px rgba(0,0,0,.25);overflow:hidden;z-index:2147483647}" +
    ".panel.open{display:flex}" +
    "header{padding:12px 16px;background:var(--accent);color:#fff;font-weight:600;display:flex;justify-content:space-between;align-items:center}" +
    "header button{background:none;border:0;color:#fff;font-size:20px;cursor:pointer;line-height:1}" +
    ".body{flex:1;overflow-y:auto;padding:12px 16px;display:flex;flex-direction:column;gap:8px}" +
    ".msg{max-width:85%;padding:8px 12px;border-radius:12px;white-space:pre-wrap;overflow-wrap:anywhere}" +
    ".you{align-self:flex-end;background:var(--mine)}.support{align-self:flex-start;background:var(--theirs)}" +
    ".hint{color:var(--muted);font-size:12px;min-height:18px;padding:0 16px}" +
    "form{display:flex;flex-direction:column;gap:8px;padding:12px 16px;border-top:1px solid var(--border)}" +
    "input,textarea{font:inherit;padding:8px 10px;border:1px solid var(--border);border-radius:8px;background:var(--bg);color:var(--fg);width:100%}" +
    "textarea{resize:none}" +
    "form button{font:inherit;font-weight:600;padding:8px 12px;border:0;border-radius:8px;background:var(--accent);color:#fff;cursor:pointer}" +
    "form button:disabled{opacity:.6;cursor:progress}" +
    ".err{color:#c62828;font-size:12px}" +
    "</style>" +
    "<div class='wrap'>" +
    "<button class='bubble' aria-label='Chat with support' aria-expanded='false'>💬</button>" +
    "<section class='panel' role='dialog' aria-label='Chat with support'>" +
    "<header><span>Chat with us</span><button class='close' aria-label='Close chat'>×</button></header>" +
    "<div class='body' aria-live='polite'></div>" +
    "<div class='hint'></div>" +
    "<form>" +
    "<div class='intro'><input name='name' placeholder='Your name (optional)' maxlength='200' autocomplete='name'>" +
    "<input name='email' type='email' placeholder='Email, so we can follow up (optional)' maxlength='320' autocomplete='email' style='margin-top:8px'></div>" +
    "<textarea name='body' rows='3' placeholder='Type your message…' maxlength='5000' required></textarea>" +
    "<div class='err' role='alert'></div>" +
    "<button type='submit'>Send</button>" +
    "</form></section></div>";

  var $ = function (sel) { return root.querySelector(sel); };
  var bubble = $(".bubble"), panel = $(".panel"), list = $(".body"), hint = $(".hint");
  var form = $("form"), intro = $(".intro"), textarea = $("textarea"), err = $(".err"), sendBtn = $("form button");

  function toggle(open) {
    panel.classList.toggle("open", open);
    bubble.setAttribute("aria-expanded", String(open));
    if (open) { render(); textarea.focus(); }
  }
  bubble.addEventListener("click", function () { toggle(!panel.classList.contains("open")); });
  $(".close").addEventListener("click", function () { toggle(false); });

  function render() {
    intro.style.display = state.conversationId ? "none" : "block";
    if (!state.conversationId) {
      list.innerHTML = "";
      var p = document.createElement("div");
      p.className = "msg support";
      p.textContent = "Hi! How can we help? We usually reply within a few minutes.";
      list.appendChild(p);
      return;
    }
    request("GET", "/conversations/" + state.conversationId)
      .then(function (data) {
        list.innerHTML = "";
        data.messages.forEach(function (m) {
          var el = document.createElement("div");
          el.className = "msg " + (m.from === "you" ? "you" : "support");
          el.textContent = m.body; // never innerHTML: message text is untrusted
          list.appendChild(el);
        });
        list.scrollTop = list.scrollHeight;
      })
      .catch(function () { if (!state.conversationId) render(); });
  }

  var typingTimer = null;
  onTyping = function (e) {
    if (e.ticketId !== state.conversationId || e.who !== "agent") return;
    hint.textContent = "Support is typing…";
    clearTimeout(typingTimer);
    typingTimer = setTimeout(function () { hint.textContent = ""; }, 4000);
  };
  onChanged = function (e) {
    if (e.ticketId !== state.conversationId) return;
    hint.textContent = "";
    if (panel.classList.contains("open")) render();
    else bubble.textContent = "💬•"; // unread marker
  };
  bubble.addEventListener("click", function () { bubble.textContent = "💬"; });

  var lastTyping = 0;
  textarea.addEventListener("input", function () {
    if (!state.conversationId || Date.now() - lastTyping < 2000) return;
    lastTyping = Date.now();
    invoke("Typing", [state.conversationId]);
  });
  textarea.addEventListener("keydown", function (e) {
    if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); form.requestSubmit(); }
  });

  form.addEventListener("submit", function (e) {
    e.preventDefault();
    var body = textarea.value.trim();
    if (!body) return;
    err.textContent = "";
    sendBtn.disabled = true;

    var ready = state.token
      ? Promise.resolve()
      : request("POST", "/sessions", {
          workspace: cfg.workspace,
          name: form.elements.name.value || null,
          email: form.elements.email.value || null,
        }).then(function (s) {
          state = { token: s.token, expiresAt: s.expiresAt };
          save(state);
          connect();
        });

    ready
      .then(function () {
        return state.conversationId
          ? request("POST", "/conversations/" + state.conversationId + "/messages", { body: body })
          : request("POST", "/conversations", { body: body }).then(function (c) {
              state.conversationId = c.id;
              save(state);
              invoke("WatchConversation", [c.id]);
            });
      })
      .then(function () { textarea.value = ""; render(); })
      .catch(function () { err.textContent = "Couldn't send your message. Please try again."; })
      .then(function () { sendBtn.disabled = false; });
  });

  connect();
})();
