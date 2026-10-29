mergeInto(LibraryManager.library, {
  // Files written to Application.persistentDataPath in a WebGL build live in an in-memory
  // filesystem backed by IndexedDB; flush it so imported weapon models survive a page reload.
  CsAimSyncFs: function () {
    if (typeof FS !== "undefined" && FS.syncfs) FS.syncfs(false, function (err) { if (err) console.warn("syncfs", err); });
  }
});
