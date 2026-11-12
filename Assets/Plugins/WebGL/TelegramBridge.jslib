mergeInto(LibraryManager.library, {
  GetTelegramInitData: function () {
    var initData = "";
    try {
      if (window.Telegram && window.Telegram.WebApp && window.Telegram.WebApp.initData) {
        initData = window.Telegram.WebApp.initData;
      } else {
        // Fallback: check URL search params for ?tgWebAppData= or ?initData=
        var urlParams = new URLSearchParams(window.location.search);
        initData = urlParams.get("tgWebAppData") || urlParams.get("initData") || "";
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
      return (window.Telegram && window.Telegram.WebApp && !!window.Telegram.WebApp.initData) ? 1 : 0;
    } catch (e) {
      return 0;
    }
  }
});

