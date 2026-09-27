# Automatic Events
These happen on their own. Each one can be turned on or off with [`/admin events`](Admin%20User%20Commands.md#events).

## Member Joins the Server
`assign-on-join`

When someone joins, the bot does the same thing as [`/give-role`](Standard%20User%20Commands.md). If their Discord account isn't linked on VATUSA, the bot sends them a direct message explaining how to link it and where to run `/give-role` (the roles channel set with `/admin channels`). If they don't accept direct messages, this is noted in the log.

## Member Connects to Voice
`assign-on-voice-join`

When someone connects to any voice channel, including private ones, the bot does the same thing as `/give-role`, without sending a direct message. Moving between voice channels, muting, deafening, streaming or turning on video does not trigger it.

## Private Meeting Role
`private-meeting-role`

While someone is in the private meeting voice channel, they have the private meeting role (for example, to see the meeting's text channel). The role is removed when they leave. Choose the channel with `/admin channels` and the role with `/admin roles`. If either isn't set, the bot logs a warning once and does nothing.

## Nicknames and Staff Role
`change-nicknames`, `assign-staff-role`

These aren't events themselves; they control what `/give-role`, the events above and [`/staff check-users`](Staff%20User%20Commands.md) are allowed to do. With `change-nicknames` off, nicknames are never touched. With `assign-staff-role` off, the ARTCC Staff role is never given or removed.

---
