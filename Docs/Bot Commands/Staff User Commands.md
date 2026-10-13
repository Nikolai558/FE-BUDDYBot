# Discord Staff User Commands
## Check Users
`/staff check-users mode:<mode>`

Compares every member (bots are skipped) with [VATUSA](https://www.vatusa.net/), then does what the chosen `mode` allows:

| Mode | Adds roles | Removes roles | Updates nicknames |
|---|---|---|---|
| **Report only** | – | – | – |
| **Add roles** | ✔ | – | – |
| **Remove roles** | – | ✔ | – |
| **Update all** | ✔ | ✔ | ✔ |

**Report only** changes nothing. It lists what **Update all** would do, so run it first to preview.

**When roles are removed**
* **Verified**: the member's Discord account is no longer linked on VATUSA.
* **ARTCC Staff**: VATUSA no longer lists the member as ATM, DATM, TA, EC, FE or WM.
* **AIRAC pings**: the member is no longer linked on VATUSA, or turned the pings off (with `/airac-pings`, or an admin removed the role by hand). Members who turned them off aren't given the role either.
* No other roles are ever touched. Members VATUSA couldn't be reached for are skipped.
* **Safety check:** if more than half of the members (and more than 5) come back as not linked, removals are skipped. That usually means VATUSA is having problems.

The `/admin events` switches still apply. With **Assign staff role** off, the staff role is never added or removed. With **Change nicknames** off, nicknames are left alone.

The reply (only visible to you) has a summary and a `.txt` report listing every member with differences. Members who aren't linked are never sent a direct message by this command. Only one check can run at a time.

**Permission Requirements**
* `Manage Roles`, `Manage Messages` and `Manage Channels` (all three)

The bot makes the changes with its own permissions, so Discord's role order isn't checked for the person running the command. The bot only ever changes the Verified, ARTCC Staff and AIRAC pings roles. To limit the command to specific roles, go to Server Settings → Integrations → the bot → `/staff check-users`.

In FE-Buddy it can be used in **#feb-helper**, **#owner-chat** and **#admin-chat** (see [Discord Setup](../Discord%20Setup.md#command-permissions)).

**Log:** each member who was changed gets one line in the bot's log, e.g.
```
Roles: someone (123…) VATUSA CID 1234567 ZLC | added: none | removed: ARTCC Staff | kept: Verified | nickname: unchanged
```

## Clear Messages
`/clear-messages amount:<1-100 | all> [keep-pinned]`

Deletes messages in the channel or thread you run it in.
* A number (1 to 100) deletes that many of the latest messages.
* `all` deletes every message in the channel. The bot asks you to confirm first, with **Delete all messages** and **Cancel** buttons.
* `keep-pinned` (default True) leaves pinned messages alone. With a number, pinned messages among the latest ones are skipped, not replaced, so fewer may be deleted. Set it to False to delete pinned messages too.

The reply (only visible to you) says how many were deleted. Discord only deletes messages under 14 days old in bulk. Older ones are deleted one at a time, so clearing a long history with `all` can take a while. The reply shows progress. If it takes more than 15 minutes, the result is sent to you as a direct message. Only one clear can run in a channel at a time.

It refuses to run in issue posts, because deleting replies there deletes their comments on GitHub too.

**Permission Requirements**
* `Manage Messages`
* The bot needs **View Channel**, **Read Message History** and **Manage Messages** in the channel (see [Discord Setup](../Discord%20Setup.md#bot-role)). It tells you if one is missing.

---
