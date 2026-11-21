// The launcher's window (T163): WebView2 gives the page a way to ask it things; a browser doesn't.

/** Whether the page is in the launcher's window. */
export function inWindow() {
    return !!(window.chrome && window.chrome.webview);
}

/** Asks the launcher's window to switch the map: "practice", or "own" to go back. */
export function switchMap(to) {
    window.chrome.webview.postMessage(JSON.stringify({ switchTo: to }));
}
