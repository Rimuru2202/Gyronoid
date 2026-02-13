mergeInto(LibraryManager.library, (function () {

  function _global() {
    if (typeof globalThis !== 'undefined') return globalThis;
    if (typeof window !== 'undefined') return window;
    if (typeof self !== 'undefined') return self;
    return this;
  }

  function _getSdkSafe() {
    try {
      if (typeof ysdk !== 'undefined' && ysdk) return ysdk;
    } catch (e) { }

    try {
      var g = _global();
      if (g && g.ysdk) return g.ysdk;
    } catch (e) { }

    return null;
  }

  // IMPORTANT:
  // В некоторых ваших/старых вызовах используется getSdk().
  // Делаем безопасный полифилл, чтобы не было ReferenceError.
  try {
    var g = _global();
    if (g && typeof g.getSdk !== 'function') {
      g.getSdk = function () { return _getSdkSafe(); };
    }
  } catch (e) { }

  function _send(go, method, payload) {
    try { SendMessage(go, method, payload); } catch (e) { }
  }

  return {

    Gironoid_ServerTimeMs: function () {
      try {
        var sdk = _getSdkSafe();
        if (sdk && sdk.serverTime) return sdk.serverTime();
      } catch (e) { }
      return Date.now();
    },

    // Синхронно узнать "авторизован ли" невозможно (getPlayer() — Promise),
    // но нам важно НЕ падать. Возвращаем 1, если SDK вообще доступен.
    Gironoid_PlayerIsAuthorized: function () {
      try {
        var sdk = _getSdkSafe();
        return (sdk && sdk.getPlayer) ? 1 : 0;
      } catch (e) { }
      return 0;
    },

    Gironoid_PlayerGetData: function (goPtr, methodPtr, keyPtr) {
      var go = UTF8ToString(goPtr);
      var method = UTF8ToString(methodPtr);
      var key = UTF8ToString(keyPtr);

      try {
        var sdk = _getSdkSafe();
        if (!sdk || !sdk.getPlayer) {
          _send(go, method, "");
          return;
        }

        sdk.getPlayer().then(function (player) {
          if (!player || !player.getData) {
            _send(go, method, "");
            return;
          }

          return player.getData([key]).then(function (data) {
            var v = (data && data[key]) ? data[key] : "";
            // безопасно передаем строку в Unity
            _send(go, method, encodeURIComponent(v));
          }).catch(function () {
            _send(go, method, "");
          });
        }).catch(function () {
          _send(go, method, "");
        });

      } catch (e) {
        _send(go, method, "");
      }
    },

    Gironoid_PlayerSetData: function (goPtr, methodPtr, keyPtr, valPtr, flushInt) {
      var go = UTF8ToString(goPtr);
      var method = UTF8ToString(methodPtr);
      var key = UTF8ToString(keyPtr);

      // value приходит как URI-encoded строка
      var encoded = UTF8ToString(valPtr);
      var val = "";
      try { val = decodeURIComponent(encoded); } catch (e) { val = encoded; }

      var flush = flushInt ? true : false;

      try {
        var sdk = _getSdkSafe();
        if (!sdk || !sdk.getPlayer) {
          _send(go, method, "0");
          return;
        }

        sdk.getPlayer().then(function (player) {
          if (!player || !player.setData) {
            _send(go, method, "0");
            return;
          }

          var obj = {};
          obj[key] = val;

          // В SDK v2 setData может принимать (obj, flush)
          return player.setData(obj, flush).then(function () {
            _send(go, method, "1");
          }).catch(function () {
            _send(go, method, "0");
          });

        }).catch(function () {
          _send(go, method, "0");
        });

      } catch (e) {
        _send(go, method, "0");
      }
    }
  };
})());
