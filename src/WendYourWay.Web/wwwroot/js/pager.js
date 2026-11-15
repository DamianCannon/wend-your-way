// The pager card can be dragged anywhere on screen and stays where it was put, in this browser
// only (user, 2026-10-27). Its bar is always kept on screen, so it can't be lost; the Breakdown
// opening upwards from it may go off the top. Double-clicking it, anywhere but a button, link, box or
// the elevation profile, puts it back in the middle (T116: it was the bar only, mostly buttons).
// The place is kept on the page's root, which Blazor never redraws, so it survives the card
// being drawn again; app.css keeps the bar in a smaller window too.

const placeKey = "cutthrough.pager";
const root = document.documentElement;
const handles = "button, a, input, select, .profile";

const kept = () => { try { return JSON.parse(localStorage.getItem(placeKey)); } catch { return null; } };
const keep = place => { try { place ? localStorage.setItem(placeKey, JSON.stringify(place)) : localStorage.removeItem(placeKey); } catch { /* not kept */ } };

let place = kept();
put(place);

/** Puts the card at `place` ({ left, bottom } in px from the window's left and bottom), or back in the middle. */
function put(at) {
    place = at;
    root.classList.toggle("pager-placed", !!at);
    if (!at) return;
    root.style.setProperty("--pager-left", `${at.left}px`);
    root.style.setProperty("--pager-bottom", `${at.bottom}px`);
}

/** The place nearest `at` that keeps all of the card's bar, and what's below it, in the window. */
function within(at, card) {
    const box = card.getBoundingClientRect();
    const bar = card.querySelector(".bar")?.getBoundingClientRect() ?? box;
    const below = box.bottom - bar.top;
    return {
        left: Math.round(Math.min(Math.max(at.left, 0), Math.max(window.innerWidth - box.width, 0))),
        bottom: Math.round(Math.min(Math.max(at.bottom, 0), Math.max(window.innerHeight - below, 0))),
    };
}

document.addEventListener("pointerdown", e => {
    const card = e.target instanceof Element ? e.target.closest(".pager") : null;
    if (!card || e.button !== 0 || e.target.closest(handles)) return;
    const box = card.getBoundingClientRect();
    const from = { x: e.clientX, y: e.clientY, left: box.left, bottom: window.innerHeight - box.bottom };
    let moved = false;
    const move = m => {
        const dx = m.clientX - from.x, dy = m.clientY - from.y;
        // A few pixels first, so a click is still a click.
        if (!moved && Math.hypot(dx, dy) < 4) return;
        if (!moved) { moved = true; card.classList.add("dragging"); card.setPointerCapture(e.pointerId); }
        put(within({ left: from.left + dx, bottom: from.bottom - dy }, card));
    };
    const end = () => {
        document.removeEventListener("pointermove", move);
        document.removeEventListener("pointerup", end);
        document.removeEventListener("pointercancel", end);
        card.classList.remove("dragging");
        if (moved) keep(place);
    };
    document.addEventListener("pointermove", move);
    document.addEventListener("pointerup", end);
    document.addEventListener("pointercancel", end);
});

document.addEventListener("dblclick", e => {
    if (!(e.target instanceof Element) || !e.target.closest(".pager") || e.target.closest(handles)) return;
    window.getSelection()?.removeAllRanges();   // the word a double-click selects in the Breakdown
    put(null);
    keep(null);
});
