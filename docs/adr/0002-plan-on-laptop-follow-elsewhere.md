# Version 1 is a laptop planner; following a Loop is handed off via GPX

The value is in generating Loops, so version 1 is a web app designed for a laptop screen (usable on a phone in a pinch) that generates, nudges, saves and exports Loops as GPX. Following a Loop is left to the OS Maps app the user already pays for. Cues are deferred to a version 2 companion phone app, because a mobile web page generally loses location tracking once the screen locks, and making following work well would have delayed the generator for no gain to it.

## Consequences

- Nothing in version 1 tracks the user's position; there is no "follow" view to build or test.
- GPX export must be good enough to follow in OS Maps without any extra help from us.
- Turn-by-turn navigation is out of scope for both versions: version 2 gives Cues at Decision Points only.
