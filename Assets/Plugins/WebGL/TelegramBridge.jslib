mergeInto(LibraryManager.library, {
  GetTelegramInitData: function () {
    var initData = "";
    try {
      if (window.Telegram && window.Telegram.WebApp && window.Telegram.WebApp.initData) {
        initData = window.Telegram.WebApp.initData;
      } else {
        // Fallback 1: check URL query params for ?tgWebAppData= or ?initData=
        var urlParams = new URLSearchParams(window.location.search);
        initData = urlParams.get("tgWebAppData") || urlParams.get("initData") || "";

        // Fallback 2: check URL hash params for #tgWebAppData= or #initData=
        if (!initData && window.location.hash) {
          var hashStr = window.location.hash.startsWith("#") ? window.location.hash.substring(1) : window.location.hash;
          var hashParams = new URLSearchParams(hashStr);
          initData = hashParams.get("tgWebAppData") || hashParams.get("initData") || "";
        }
      }
    } catch (e) {
      console.warn("Failed to get Telegram initData:", e);
    }

    var bufferSize = lengthBytesUTF8(initData) + 1;
    var buffer = _malloc(bufferSize);
    stringToUTF8(initData, buffer, bufferSize);
    return buffer;
  },

  HasTelegramWebApp: function () {
    try {
      if (window.Telegram && window.Telegram.WebApp && !!window.Telegram.WebApp.initData) return 1;
      var urlParams = new URLSearchParams(window.location.search);
      if (urlParams.get("tgWebAppData") || urlParams.get("initData")) return 1;
      if (window.location.hash) {
        var hashStr = window.location.hash.startsWith("#") ? window.location.hash.substring(1) : window.location.hash;
        var hashParams = new URLSearchParams(hashStr);
        if (hashParams.get("tgWebAppData") || hashParams.get("initData")) return 1;
      }
      return 0;
    } catch (e) {
      return 0;
    }
  }
});

