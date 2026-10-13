# discord-iac

Audit and manage the server's roles, categories, channels and permission overwrites from a YAML file, the way infrastructure-as-code works: export what's live, edit it, review a plan, apply it.

It only uses the Discord REST API (v10) and never opens a gateway connection, so it can safely use the production bot's token while the bot stays online. Its requests count against the same rate limits as the bot, so it waits whenever Discord says to.

| Command | What it does |
|---|---|
| `audit` | Report on the live server, flag likely problems, save a snapshot, export `current.yaml` |
| `plan` | Show every change `desired.yaml` would make, and anything the bot isn't allowed to do. Changes nothing |
| `apply` | `plan`, save a snapshot, ask you to type `yes`, then make the changes in a safe order |
| `restore <snapshot>` | Put role permissions and channel overwrites back the way a snapshot has them, with the same plan and `yes` |

## Setup
Needs Python 3.10 or newer. From this folder (`tools/discord-iac`):

```
python -m venv .venv
.venv\Scripts\activate          # Windows; on Linux/macOS: source .venv/bin/activate
pip install -r requirements.txt
copy .env.example .env          # Linux/macOS: cp .env.example .env
```

Fill in `.env`:

```
DISCORD_BOT_TOKEN=<bot token>
DISCORD_GUILD_ID=<server id>
```

`.env` is git-ignored. The tool reads the token from it and only ever sends it in the `Authorization` header; it never prints or logs it. Use `--env <file>` to point at another env file, e.g. one for the test server.

### What the bot needs
* **`audit`** only reads, so the bot just has to be in the server. It sees channels it can view; from **2026-11-16** Discord hides every other channel from bots entirely (audit lists the ones it can't view today).
* **`apply`/`restore`** need **Manage Roles** and **Manage Channels**, and the bot's highest role must sit **above** every role it edits. It must also be able to **see** every channel it changes. In an overwrite, the bot can only allow or deny permissions it has itself, and only a bot with Administrator can set `manage_roles` in one. `plan` lists anything it can't do and how to fix it. The quickest fix for all of these is to turn on Administrator for the bot's role, run `apply`, and turn it off again.

## Usage
```
python -m discord_iac audit
python -m discord_iac plan                       # desired.yaml against the live server
python -m discord_iac apply
python -m discord_iac restore snapshots/20261013T043337Z.json
```

On Windows `cmd`, run these as `.venv\Scripts\python -m discord_iac ...` (backslashes), or activate the venv first. Rate-limit waits of a second or more are always printed; add `-v` before the command to see shorter ones too. `audit` and `plan` also take `--from-snapshot <file>` to work offline against a saved snapshot.

`audit` writes:

| File | What it is |
|---|---|
| `snapshots/<UTC timestamp>.json` | Raw roles and channels as the API returned them. `restore` reads these |
| `current.yaml` | The live server in the config schema below. Copy it to `desired.yaml` to start editing |
| `audit-report.txt` | The same report it printed |

All three are git-ignored, and so is `desired.yaml`: they describe the live server and stay on your machine. Back `desired.yaml` up somewhere private if you want its history.

### The report
1. **Roles**, top to bottom, with member counts, flags and server-level permissions. The bot's highest role is marked, and so is every role it can't manage.
2. **Channels**: each category's overwrites, then its channels. A channel is **synced** when its overwrites match the category's exactly. Synced channels don't repeat the category's overwrites.
3. **Likely problems**:
   * a channel denies @everyone something and no staff role has an allow for it there. A server-level grant on a role does *not* beat an @everyone deny in a channel; only an allow overwrite for that role, or Administrator, does. Staff roles are unmanaged roles with any of administrator, manage_guild, manage_roles, manage_channels, manage_messages, manage_threads, kick_members, ban_members or moderate_members
   * unsynced channels in a category that has overwrites, with what differs
   * roles with Administrator
   * personal (member) overwrites
   * roles the bot can't manage (at or above its highest role)
   * also: missing bot permissions, @everyone holding moderation permissions, duplicate names, overwrites for deleted roles or departed members, channels the bot can't see, permission bits the tool has no name for

### plan, apply and restore
`plan` groups changes by type: roles (create, update, rename), role order, categories, category order, channels (create, update, move), channel order, overwrites, deletes. Each channel's overwrites change in one request that sets the full list, so a channel never sits half-updated. Synced channels that get the same update as their category are folded under it. `plan` exits with code 3 if there are config errors or things the bot can't do.

Two roles can only be changed by hand: the bot's integration role (managed) and the bot's own highest role (no bot can edit its own top role). When the config differs from them, `plan` lists the exact toggles under **Do by hand** and `apply` leaves them alone. `plan` also checks the end state, after both its own changes and the hand steps: the bot must have its integration role's permissions in every channel, and a warning names any channel where something would take them away.

`apply` re-reads the server and runs `plan`, saves a snapshot, and waits for you to type `yes`. It then applies in this order: roles, role order, categories, category order, channels, channel order, overwrites, deletes. Before each phase it reads the server again, so new roles and categories have real ids. Any change you didn't confirm stops the run, so if someone edits the server between your `yes` and that phase, nothing unexpected happens. It prints each change as it goes, stops at the first error, and finishes by checking that nothing is left to do.

`restore` only restores **role permissions** and **channel overwrites**, for roles and channels that still exist. It doesn't restore names, colors, order, or anything deleted.

## Config schema
`current.yaml` and `desired.yaml` share this schema. Permissions are always names, never numbers.

```yaml
roles:                      # top to bottom; this order is the role order
  - name: Admin
    color: "#e74c3c"        # or null for no color
    hoist: true             # shown separately in the member list
    mentionable: false
    permissions: [administrator]
  - name: FE-BUDDYBot
    managed: true           # bot/integration/Booster role: kept for ordering, never modified
    permissions: [...]
  - name: "@everyone"       # always last; only `permissions` applies
    permissions: [view_channel, read_message_history, add_reactions, connect, use_application_commands]

uncategorized:              # channels outside any category, top to bottom
  - name: welcome
    type: text
    overwrites: {}

categories:                 # top to bottom
  - name: Staff
    overwrites:
      "@everyone":
        deny: [view_channel]
      Moderator:
        allow: [view_channel]
    channels:               # top to bottom (voice/stage always sort below text in Discord)
      - name: staff-chat
        type: text          # text, voice, announcement, forum, stage, media
        topic: Staff only   # leave out to leave the topic alone
        sync: true          # use the category's overwrites
      - name: staff-voice
        type: voice
        overwrites:         # or its own overwrites instead of sync
          "@everyone":
            deny: [view_channel]
          Moderator:
            allow: [view_channel, priority_speaker]
          "member:123456789012345678":   # a person, by user id
            allow: [speak]

delete:                     # the only way anything gets deleted
  roles: []
  categories: []
  channels: []
```

* **Overwrite keys** are a role name, `"@everyone"`, `"member:<user id>"`, or `"role:<role id>"` when a role name is ambiguous. An overwrite with neither `allow` nor `deny` is a neutral overwrite; Discord keeps those and they count when deciding whether a channel is synced.
* **@everyone** is identified by its id (the server's id), never by name.
* **Matching** is by name. If two roles, categories or channels share a name, `current.yaml` adds an `id:` to each of them so you can say which one you mean; without it the tool refuses to guess.
* **Renaming**: add the thing's `id:` and give it the new `name:`. Without the id, a new name looks like a new role or channel. Ids are in `audit-report.txt`.
* **Leaving a key out** leaves that setting alone: no `color` keeps the color, no `topic` keeps the topic, a channel with neither `sync` nor `overwrites` keeps its overwrites. A role, category or channel missing from the file entirely is left alone and listed as a warning.
* **Quote** names that start with `@`, `#` or contain `: ` (the export does this for you).

### Permission names
Lower-case versions of the names in Discord's [permissions documentation](https://discord.com/developers/docs/topics/permissions#permissions-bitwise-permission-flags), checked 2026-10-12, in [`discord_iac/permissions.py`](discord_iac/permissions.py). Some differ from what the Discord app shows: `manage_roles` is "Manage Permissions" in a channel, `use_vad` is "Use Voice Activity", `moderate_members` is "Timeout Members", `manage_guild` is "Manage Server". A bit the tool doesn't recognize shows up as `unknown_bit_<n>` and is flagged by `audit`.

## Safety
* Nothing is deleted unless it's listed under `delete:`.
* Managed roles are never modified. The bot's own permissions are never reduced: `plan` works out what the bot can do server-wide and in every channel before and after, and refuses if anything would be lost (ignoring a temporary Administrator). `restore` may put the bot back to what it had in the snapshot.
* Every write sends an `X-Audit-Log-Reason` header, so changes show in Server Settings > Audit Log as coming from the bot with a reason starting `discord-iac:`.
* On HTTP 429 the tool waits the `retry_after` Discord gives and retries; it also waits when the `X-RateLimit-Remaining` header says a bucket is empty.

## Tests
```
python -m unittest discover -s tests
```
