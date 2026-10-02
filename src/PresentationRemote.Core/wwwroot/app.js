// Presentation Remote — phone controller.
// Sends only whitelisted semantic commands ("presentation.next", ...) over a WebSocket.
(function () {
  'use strict';

  var TAP_LOCK_MS = 300;        // Ignore a second tap on the same button within this window.
  var ACK_TIMEOUT_MS = 2500;    // No reply this long after a command => connection is probably dead.
  var PING_EVERY_MS = 15000;    // App-level heartbeat while the page is visible.
  var PONG_TIMEOUT_MS = 4000;
  var MAX_BACKOFF_MS = 5000;
  var MESSAGE_MS = 4000;

  var CLOSE_SESSION_ENDED = 4401;
  var CLOSE_REPLACED = 4408;
  var CLOSE_BUSY = 4409;
  var CLOSE_SHUTDOWN = 4410;

  var token = new URLSearchParams(window.location.search).get('session');
  var clientId = getClientId();

  var statusEl = document.getElementById('status');
  var statusText = document.getElementById('status-text');
  var messageEl = document.getElementById('message');
  var overlay = document.getElementById('overlay');
  var overlayTitle = document.getElementById('overlay-title');
  var overlayText = document.getElementById('overlay-text');
  var overlayRetry = document.getElementById('overlay-retry');
  var buttons = Array.prototype.slice.call(document.querySelectorAll('[data-command]'));

  var ws = null;
  var state = 'connecting';
  var stopped = false;        // True once the session is over; no more reconnects.
  var everConnected = false;
  var attempt = 0;
  var nextId = 1;
  var reconnectTimer = null;
  var pingTimer = null;
  var pongTimer = null;
  var ackTimers = {};
  var lastTap = {};
  var messageTimer = null;

  // ---------- connection ----------

  function connect() {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
    if (stopped) { return; }
    if (ws && (ws.readyState === WebSocket.CONNECTING || ws.readyState === WebSocket.OPEN)) { return; }

    setState(everConnected ? 'reconnecting' : 'connecting');
    var scheme = window.location.protocol === 'https:' ? 'wss://' : 'ws://';
    var url = scheme + window.location.host + '/ws?session=' + encodeURIComponent(token) +
      '&client=' + encodeURIComponent(clientId);

    var socket;
    try {
      socket = new WebSocket(url);
    } catch (e) {
      scheduleReconnect();
      return;
    }

    var opened = false;
    ws = socket;

    socket.onopen = function () {
      if (ws !== socket) { return; }
      opened = true;
      attempt = 0;
      everConnected = true;
      setState('connected');
      startHeartbeat();
    };

    socket.onmessage = function (event) {
      if (ws !== socket) { return; }
      handleMessage(event.data);
    };

    socket.onerror = function () { /* onclose follows */ };

    socket.onclose = function (event) {
      if (ws !== socket) { return; }
      ws = null;
      stopHeartbeat();
      clearAckTimers();
      handleClose(event.code, opened);
    };
  }

  function handleClose(code, opened) {
    switch (code) {
      case CLOSE_SESSION_ENDED:
        return endSession('Session ended',
          'This QR code is no longer valid. Scan the new QR code shown on the computer.');
      case CLOSE_SHUTDOWN:
        return endSession('Presentation Remote closed',
          'The app was closed on the computer. Start it again and scan the new QR code.');
      case CLOSE_BUSY:
        return pause('Another phone is in control',
          'Only one phone can control the presentation at a time. Ask the presenter to close the remote on their phone, then try again.');
      case CLOSE_REPLACED:
        return pause('Opened somewhere else',
          'The remote was opened in another tab or window on this device. Use that one, or tap below to use this one instead.');
    }

    if (!opened) {
      // The handshake failed. Browsers hide the HTTP status, so ask whether the
      // session is still valid before retrying (401 = it is over, stop trying).
      checkSession();
    } else {
      scheduleReconnect();
    }
  }

  function checkSession() {
    setState(everConnected ? 'reconnecting' : 'connecting');
    var controller = typeof AbortController === 'function' ? new AbortController() : null;
    var timeout = setTimeout(function () { if (controller) { controller.abort(); } }, 4000);
    fetch('/api/session', {
      headers: { 'X-Session-Token': token },
      cache: 'no-store',
      signal: controller ? controller.signal : undefined
    }).then(function (response) {
      clearTimeout(timeout);
      if (response.status === 401) {
        endSession('Session ended',
          'This QR code is no longer valid. Scan the QR code shown in Presentation Remote on the computer.');
      } else {
        scheduleReconnect();
      }
    }).catch(function () {
      clearTimeout(timeout);
      scheduleReconnect();
    });
  }

  function scheduleReconnect() {
    if (stopped || reconnectTimer) { return; }
    setState(everConnected ? 'reconnecting' : 'connecting');
    var delay = Math.min(MAX_BACKOFF_MS, 400 * Math.pow(2, attempt));
    attempt += 1;
    if (attempt > 6) {
      showMessage("Can't reach the computer. Check that the phone and computer are on the same Wi-Fi.", true);
    }
    reconnectTimer = setTimeout(function () {
      reconnectTimer = null;
      connect();
    }, delay);
  }

  // Throw away a socket that is probably dead (no pong / no ack) and reconnect now.
  function dropAndReconnect() {
    var socket = ws;
    ws = null;
    stopHeartbeat();
    clearAckTimers();
    if (socket) {
      socket.onopen = socket.onmessage = socket.onclose = socket.onerror = null;
      try { socket.close(); } catch (e) { /* ignore */ }
    }
    attempt = 0;
    if (!stopped) { connect(); }
  }

  function startHeartbeat() {
    stopHeartbeat();
    pingTimer = setInterval(function () {
      if (document.visibilityState === 'visible') { ping(); }
    }, PING_EVERY_MS);
  }

  function stopHeartbeat() {
    clearInterval(pingTimer);
    clearTimeout(pongTimer);
    pingTimer = null;
    pongTimer = null;
  }

  function ping() {
    if (!ws || ws.readyState !== WebSocket.OPEN || pongTimer) { return; }
    try {
      ws.send(JSON.stringify({ type: 'ping' }));
    } catch (e) {
      dropAndReconnect();
      return;
    }
    pongTimer = setTimeout(function () {
      pongTimer = null;
      dropAndReconnect();
    }, PONG_TIMEOUT_MS);
  }

  // ---------- messages ----------

  function handleMessage(data) {
    var msg;
    try {
      msg = JSON.parse(data);
    } catch (e) {
      return;
    }
    if (!msg || typeof msg.type !== 'string') { return; }

    switch (msg.type) {
      case 'hello':
        clearMessage();
        break;
      case 'pong':
        clearTimeout(pongTimer);
        pongTimer = null;
        break;
      case 'ack':
        settle(msg.id);
        if (msg.status === 'ok') {
          flash(msg.command, true);
        } else if (msg.status === 'rate_limited') {
          // A double tap was ignored on purpose; no need to alarm anyone.
        } else {
          flash(msg.command, false);
          showMessage(msg.message || 'The computer could not do that.', true);
        }
        break;
      case 'error':
        settle(msg.id);
        showMessage('The computer did not understand that. Reload the page if this keeps happening.', true);
        break;
    }
  }

  function settle(id) {
    if (typeof id === 'number' && ackTimers[id]) {
      clearTimeout(ackTimers[id]);
      delete ackTimers[id];
    }
  }

  function clearAckTimers() {
    Object.keys(ackTimers).forEach(function (id) { clearTimeout(ackTimers[id]); });
    ackTimers = {};
  }

  // ---------- commands ----------

  function sendCommand(command) {
    var now = Date.now();
    if (now - (lastTap[command] || 0) < TAP_LOCK_MS) { return; }
    lastTap[command] = now;

    if (stopped) { return; }
    if (!ws || ws.readyState !== WebSocket.OPEN || state !== 'connected') {
      // Never queue: a command sent late would jump slides unexpectedly.
      showMessage('Not connected — that tap was not sent.', true);
      return;
    }

    var id = nextId++;
    try {
      ws.send(JSON.stringify({ type: command, id: id }));
    } catch (e) {
      showMessage('Not connected — that tap was not sent.', true);
      dropAndReconnect();
      return;
    }

    if (navigator.vibrate) {
      try { navigator.vibrate(12); } catch (e) { /* ignore */ }
    }

    ackTimers[id] = setTimeout(function () {
      delete ackTimers[id];
      showMessage('No reply from the computer. Reconnecting… (tap again once connected)', true);
      dropAndReconnect();
    }, ACK_TIMEOUT_MS);
  }

  buttons.forEach(function (button) {
    button.addEventListener('click', function () {
      if (button.getAttribute('aria-disabled') === 'true') {
        showMessage('Not connected — that tap was not sent.', true);
        return;
      }
      pressFeedback(button);
      sendCommand(button.getAttribute('data-command'));
    });
  });

  // Hardware keyboards (and desktop testing): arrows/page keys move slides.
  document.addEventListener('keydown', function (event) {
    if (event.altKey || event.ctrlKey || event.metaKey || event.repeat) { return; }
    var command = null;
    if (event.key === 'ArrowRight' || event.key === 'PageDown') { command = 'presentation.next'; }
    if (event.key === 'ArrowLeft' || event.key === 'PageUp') { command = 'presentation.previous'; }
    if (command) {
      event.preventDefault();
      sendCommand(command);
    }
  });

  // ---------- UI state ----------

  function setState(next) {
    state = next;
    statusEl.setAttribute('data-state', next);
    statusText.textContent = {
      connecting: 'Connecting…',
      connected: 'Connected',
      reconnecting: 'Reconnecting…',
      offline: 'Not connected',
      ended: 'Session ended'
    }[next] || next;
    var disabled = next !== 'connected';
    buttons.forEach(function (b) { b.setAttribute('aria-disabled', disabled ? 'true' : 'false'); });
  }

  function endSession(title, text) {
    stopped = true;
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
    setState('ended');
    showOverlay(title, text, false);
  }

  function pause(title, text) {
    stopped = true;
    setState('offline');
    showOverlay(title, text, true);
  }

  function showOverlay(title, text, canRetry) {
    overlayTitle.textContent = title;
    overlayText.textContent = text;
    overlayRetry.hidden = !canRetry;
    overlay.hidden = false;
    (canRetry ? overlayRetry : overlayTitle).focus();
  }

  overlayRetry.addEventListener('click', function () {
    overlay.hidden = true;
    stopped = false;
    attempt = 0;
    connect();
  });

  function showMessage(text, warning) {
    messageEl.textContent = text;
    messageEl.classList.toggle('is-warning', !!warning);
    clearTimeout(messageTimer);
    messageTimer = setTimeout(clearMessage, MESSAGE_MS);
  }

  function clearMessage() {
    clearTimeout(messageTimer);
    messageEl.textContent = '';
  }

  function pressFeedback(button) {
    button.classList.add('is-pressed');
    setTimeout(function () { button.classList.remove('is-pressed'); }, 120);
  }

  function flash(command, ok) {
    var button = document.querySelector('[data-command="' + command + '"]');
    if (!button) { return; }
    var cls = ok ? 'flash-ok' : 'flash-bad';
    button.classList.add(cls);
    setTimeout(function () { button.classList.remove(cls); }, 300);
  }

  // ---------- lifecycle (lock screen, app switch, network changes) ----------

  function wake() {
    if (stopped) { return; }
    if (!ws || ws.readyState === WebSocket.CLOSED || ws.readyState === WebSocket.CLOSING) {
      attempt = 0;
      clearTimeout(reconnectTimer);
      reconnectTimer = null;
      connect();
    } else if (ws.readyState === WebSocket.OPEN) {
      // The socket may look open after sleep but be dead. Prove it.
      ping();
    }
  }

  document.addEventListener('visibilitychange', function () {
    if (document.visibilityState === 'visible') { wake(); }
  });
  window.addEventListener('pageshow', function (event) { if (event.persisted) { wake(); } });
  window.addEventListener('online', wake);
  window.addEventListener('focus', wake);

  // No long-press menus, pinch zoom or double-tap zoom while presenting.
  document.addEventListener('contextmenu', function (e) { e.preventDefault(); });
  document.addEventListener('gesturestart', function (e) { e.preventDefault(); });
  document.addEventListener('dblclick', function (e) { e.preventDefault(); });

  // ---------- helpers ----------

  function getClientId() {
    var key = 'presentation-remote-client';
    var id = null;
    try { id = window.localStorage.getItem(key); } catch (e) { /* private mode */ }
    if (!id || !/^[A-Za-z0-9_-]{8,64}$/.test(id)) {
      id = randomId();
      try { window.localStorage.setItem(key, id); } catch (e) { /* ignore */ }
    }
    return id;
  }

  function randomId() {
    var bytes = new Uint8Array(16);
    if (window.crypto && window.crypto.getRandomValues) {
      window.crypto.getRandomValues(bytes);
    } else {
      for (var i = 0; i < bytes.length; i++) { bytes[i] = Math.floor(Math.random() * 256); }
    }
    return Array.prototype.map.call(bytes, function (b) { return ('0' + b.toString(16)).slice(-2); }).join('');
  }

  // ---------- start ----------

  if (!token) {
    endSession('Scan the QR code',
      'Open this page by scanning the QR code shown in Presentation Remote on the computer.');
    return;
  }

  setState('connecting');
  connect();
})();
