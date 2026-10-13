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

with a **Stop these pings** button. Each cycle is announced once, and the bot deletes its announcement for the previous cycle first so only the latest one stays in the channel (anything else posted there is left alone). After that, the bot doesn't ask the FAA again until that cycle's effective date, when it starts looking for the cycle after it. Every check is logged (`AIRAC:` lines). If the bot is offline at a check time, it waits for the next one. With `/admin airac-check times:off`, it doesn't check at all.

Verified members get the role with the Verified role (`/give-role`, joins, voice connects, `/staff check-users`). Members who turn the pings off with [`/airac-pings`](Standard%20User%20Commands.md#airac-pings) don't get it back.

The same goes for members whose AIRAC pings role an admin removes by hand in Discord: the bot treats it as turning the pings off. If an admin gives the role back by hand, the pings are turned back on. Changes the bot makes itself (for example `/staff check-users` removing it from someone who is no longer linked) don't count.

## Nicknames and Staff Role
`change-nicknames`, `assign-staff-role`

These aren't events themselves; they control what `/give-role`, the events above and [`/staff check-users`](Staff%20User%20Commands.md) are allowed to do. With `change-nicknames` off, nicknames are never touched. With `assign-staff-role` off, the ARTCC Staff role is never given or removed.

---

## FE-BUDDY Release Announcements
Not an `/admin events` switch: it runs once a level and the review channel are set with [`/admin releases`](Admin%20User%20Commands.md#releases). It needs the [GitHub App](../GitHub%20App%20Setup.md).

Every couple of minutes the bot checks FE-BUDDY's GitHub releases. When a new one comes out at the chosen level or above, the bot posts a **preview** in the review channel. The message is the announcement exactly as it would be posted; a box under it says where it would go and what the buttons do:

> ## FE-BUDDY 3.0.0-beta.10 is released! :FEB_cheer:
> > **Pre-release (Beta):** for testing, and it may have bugs. ...
>
> ### Changelog
> **AIRAC Service**
> - Bug #361 - FAA Chart Recall: ...
>
> ### How to update
> - **Already have FE-BUDDY 2.9 or newer:** click **Update available!** ...
>
> [Pre-release 3.0.0-beta.10 on GitHub](https://github.com/Nikolai558/FE-BUDDY/releases/tag/3.0.0-beta.10)

The text comes from the release notes: the note above the first heading, the **Change log** section (without **Dev notes**), and the **Instructions to install** section. Without those sections, the bot uses the whole notes as the changelog, with standard update steps. A Discord message holds 2,000 characters, so a long changelog is shortened: the longest entries are cut to their first sentence, and if that's still too long, the last ones are left out. Whenever anything is cut, a small note ends the changelog: *"Changelog shortened to fit Discord's message limit; 12 more changes left out. See the release link below for the full changelog."* (the "left out" part only when entries were dropped).

* **Post** sends the announcement to the announcement channel, with the ping role under it and an **Opt in/out of release notifications** button. The preview is deleted and replaced with *"Release announcement for FE-BUDDY 3.0.0-beta.10 accepted by @admin and posted to #release-notifications"* and a link to the post.
* **Edit** opens a form with the whole announcement. Saving it changes the preview; press Post when it's right.
* **Cancel** deletes the preview and posts *"Manual release notification required for FE-BUDDY 3.0.0-beta.10"* in the review channel, with a link to the release.

Only members with Manage Server can use the preview's buttons, and only the first click counts.

Anyone can press **Opt in/out of release notifications** under a posted announcement: it gives them the ping role if they don't have it, and takes it away if they do. The reply is only visible to them. Nobody gets the role automatically. Each release is handled once. Releases already out when the bot first started watching, releases below the chosen level, and releases that came out while announcements were off are never announced. Every new release is logged (`Releases:` lines).
