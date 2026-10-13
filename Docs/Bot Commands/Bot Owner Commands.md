# Bot Owner Commands
## Set Status
`/owner set-status [text]`

Sets the status text shown under the bot's name. Leave `text` empty to clear it. This lasts until the bot restarts; for a permanent status, set `Bot__Status` in `bot.env` (see [Configuration](../Configuration.md#bot)).

**Permission Requirements**
* `Bot Owner`: the owner of the Discord application in the Developer Portal. The command is hidden from anyone without the Administrator permission, and the bot refuses it for everyone except the owner.
* In FE-Buddy it can be used in **#feb-helper**, **#owner-chat** and **#admin-chat**.

---

## Status
`/owner status`

Shows, only to you:
* **Bot:** the version and commit it's running (so you can check a deploy went live), when it started, and its memory use
* **Connections:** Discord (and its latency), whether VATUSA answers, and the GitHub App's repository and how many API requests it has left this hour
* **Background jobs:** the last AIRAC MetaFile check, issue sync and release check: when they ran, whether they worked, and what they found. These reset when the bot restarts.

**Permission Requirements**
* `Bot Owner`, like `/owner set-status`
