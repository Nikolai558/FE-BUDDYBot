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

## Next AIRAC MetaFile Ping
Not an `/admin events` switch: it runs once the AIRAC pings role and channel are set (`/admin roles airac-pings:` and `/admin channels airac-pings:`).

Every day at the UTC times set with [`/admin airac-check`](Admin%20User%20Commands.md#airac-check) (`1500z, 2100z` until changed), the bot checks whether the FAA has published the d-TPP MetaFile for the next AIRAC cycle (`https://aeronav.faa.gov/d-tpp/<cycle>/xml_data/d-tpp_Metafile.xml`). The FAA usually publishes it 15–18 days before the cycle's effective date. The first time it's there, the bot posts in the AIRAC pings channel:

> ## AIRAC 2611 MetaFile is available
> The d-TPP MetaFile for the next cycle is out. You can now run FE-Buddy for AIRAC **2611** and get your full export.
> <sub>Cycle 2611 takes effect October 29, 2026.</sub>
>
> @AIRAC Notifications

with a **Stop these pings** button. Each cycle is announced once. After that, the bot doesn't ask the FAA again until that cycle's effective date, when it starts looking for the cycle after it. Every check is logged (`AIRAC:` lines). If the bot is offline at a check time, it waits for the next one. With `/admin airac-check times:off`, it doesn't check at all.

Verified members get the role with the Verified role (`/give-role`, joins, voice connects, `/staff check-users`). Members who turn the pings off with [`/airac-pings`](Standard%20User%20Commands.md#airac-pings) don't get it back.

The same goes for members whose AIRAC pings role an admin removes by hand in Discord: the bot treats it as turning the pings off. If an admin gives the role back by hand, the pings are turned back on. Changes the bot makes itself (for example `/staff check-users` removing it from someone who is no longer linked) don't count.

## Nicknames and Staff Role
`change-nicknames`, `assign-staff-role`

These aren't events themselves; they control what `/give-role`, the events above and [`/staff check-users`](Staff%20User%20Commands.md) are allowed to do. With `change-nicknames` off, nicknames are never touched. With `assign-staff-role` off, the ARTCC Staff role is never given or removed.

---
