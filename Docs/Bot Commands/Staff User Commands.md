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
* No other roles are ever touched. Members VATUSA couldn't be reached for are skipped.
* **Safety check:** if more than half of the members (and more than 5) come back as not linked, removals are skipped. That usually means VATUSA is having problems.

The `/admin events` switches still apply. With **Assign staff role** off, the staff role is never added or removed. With **Change nicknames** off, nicknames are left alone.

The reply (only visible to you) has a summary and a `.txt` report listing every member with differences. Members who aren't linked are never sent a direct message by this command. Only one check can run at a time.

**Permission Requirements**
* `Manage Messages` and `Manage Channels`

---
