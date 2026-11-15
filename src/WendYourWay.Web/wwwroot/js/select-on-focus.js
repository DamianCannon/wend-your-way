// A box marked data-select-on-focus selects all it holds when it's clicked or tabbed into, so typing
// replaces it (T115; user: "when you click on start/finish the contents should auto-select because I
// will be looking to replace it"). A click focuses on mousedown, and the mouseup that follows would
// put the caret where it was clicked and lose the selection, so that one mouseup is stopped; a
// second click in the box places the caret as usual.

let justFocused = null;

document.addEventListener("focusin", event => {
    const box = event.target;
    if (!(box instanceof HTMLInputElement) || !box.matches("[data-select-on-focus]")) return;
    box.select();
    justFocused = box;
});

document.addEventListener("mouseup", event => {
    if (justFocused && event.target === justFocused) event.preventDefault();
    justFocused = null;
}, true);
