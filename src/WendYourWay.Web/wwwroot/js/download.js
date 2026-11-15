// Saves text the app made (a GPX file) to the user's Downloads, without a round trip to a URL.

/** Offers `text` to the browser as a file called `fileName` of type `mimeType`. */
export function saveFile(fileName, mimeType, text) {
    const url = URL.createObjectURL(new Blob([text], { type: mimeType }));
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    // Some browsers read the Blob after click() returns; let them finish first.
    setTimeout(() => URL.revokeObjectURL(url), 10_000);
}
