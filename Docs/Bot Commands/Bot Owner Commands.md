# Bot Owner Commands
## Set Status
`/owner set-status [text]`

Sets the status text shown under the bot's name. Leave `text` empty to clear it. This lasts until the bot restarts; for a permanent status, set `Bot__Status` in `bot.env` (see [Configuration](../Configuration.md#bot)).

**Permission Requirements**
* `Bot Owner`: the owner of the Discord application in the Developer Portal. The command is hidden from anyone without the Administrator permission, and the bot refuses it for everyone except the owner.
* In FE-Buddy it can be used in **#feb-helper**, **#owner-chat** and **#admin-chat**.

---
