# Security policy

## Reporting a vulnerability

Please report security issues privately, through GitHub's **Report a vulnerability** button on this
repository's Security tab, rather than opening a public issue.

Include what you did, what happened, and the SmartZoom version or commit. A proof of concept helps, but a clear
description of the mechanism is enough to start.

You can expect an acknowledgement within a week. If a fix is warranted, the advisory is published alongside it
and you will be credited unless you ask otherwise.

## What is in scope

SmartZoom installs a low-level keyboard and mouse hook, injects input, reads window contents from the screen,
and talks to Office through COM. Things worth reporting:

- SmartZoom sending injected input somewhere it was not aimed, particularly keyboard shortcuts.
- The keyboard hook recording or logging anything beyond the modifiers and the configured trigger keys.
- Screen capture reaching content outside the window SmartZoom is acting on.
- The settings file being read or written in a way another user on the machine could exploit.
- Anything that lets a document or web page cause SmartZoom to act.

## What is not

- SmartZoom cannot zoom applications running as administrator while it is not. Windows blocks injected input
  from a lower integrity level. That is the operating system behaving correctly.
- SmartZoom sees which application is under the cursor and reads pixels from it. That is how it works, and it
  is described in the README and the architecture notes.
- Vendor mouse software intercepting a button before SmartZoom sees it.

## Privacy

SmartZoom makes no network connections and sends nothing anywhere. The log at
`%LOCALAPPDATA%\SmartZoom\logs` stays on the machine and records process names, window classes, cursor
positions and which adapter ran. The keyboard hook observes only modifier keys and the keys named in the
configured triggers; other keystrokes are neither recorded nor logged.

If you attach a log to a bug report, be aware that it names the applications you were using.
