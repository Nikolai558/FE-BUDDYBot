# GitHub Issue Sync (design)

Status: Stages 1–3 built (submitting; GitHub ↔ Discord sync; /link-github). Stage 4 (webhooks) not built; see [Stages](#stages).

## Summary

Let people in the FE-BUDDY Discord submit, follow, and discuss [FE-BUDDY](https://github.com/Nikolai558/FE-BUDDY/issues) issues without leaving Discord. **GitHub is the source of truth**; Discord mirrors it.

- One **Forum channel** (e.g. `#issues`). Every open GitHub issue has one forum post, created and owned by the bot.
- A **submit panel** (buttons) in `#submit-an-issue`, plus `/report`.
- Issues created on GitHub get a forum post too. Replies in a post become GitHub comments; GitHub comments, label changes, and closes flow back to Discord.

## Discord setup

**Forum channel `#issues`**
- @everyone: View Channel. **No "Create Posts"** — only the bot creates posts, so tags stay authoritative.
- **Verified**: Send Messages in Threads (in every post, dev tasks included).
- Bot (channel-level overrides only, not server-wide): Create Posts, Send Messages in Threads, Manage Threads (lock/archive/tags), Manage Webhooks (GitHub comments shown with the commenter's name + avatar), Embed Links, Read Message History, Manage Channels (only if the bot manages the tag list itself).

**Tags** (max 20 per forum, max 5 per post)

| Group | Tags | Source |
|---|---|---|
| Type | Bug, Feature, Docs, Dev task, Needs triage | `bug` / `feature` / `docs` / `task` label (fallback for Dev task: `[TASK]` title prefix or a `### Kind of work` section in the body); none → Needs triage. Posts created from Discord already know their type. |
| Status | Planned, In progress, Needs research, Partially done, Question, Priority, Help wanted | matching labels |
| Closed | Fixed, Won't fix, Duplicate, Invalid | close reason + labels |
| Version | v3.x (v2.x optional) | matching label |

When more than 5 apply, keep in order: type → closed/status → priority → version.

**Other channels**
- `#submit-an-issue`: persistent button panel.
- `#admin-chat`: approval queue (see below). The bot needs to be able to ping @Admin (role mentionable, or bot has "Mention All Roles").

## GitHub App

- New GitHub App ([GitHub App Setup](GitHub%20App%20Setup.md)), installed on `Nikolai558/FE-BUDDY` and a private `FE-BUDDYBot-sandbox` repo for development.
- Permissions: **Issues: read & write**, Metadata: read. Nothing else.
- Device flow enabled (for `/link-github`).
- `bot.env` holds the App ID; the private key is a file mounted from `./secrets`. The bot looks up the installation itself.
- Issues and comments appear as `<app-name>[bot]`.

## Submitting

1. User clicks **[Report a bug] / [Request a feature] / [Docs problem] / [Dev task]** (or `/report`).
2. Gate: must have **Verified**. **Dev task** also requires Contributor, Project Management, or Admin.
3. **Modal 1** (Discord allows 5 questions per modal): title plus the template's main question (the one required box; for dev tasks, the description).
4. **Duplicate check**: search issues by the title's keywords and show the 5 closest matches, linking to their forum posts. If one matches, the member is asked to 👍 or reply there.
5. Credit choice (dropdown on the same message): Discord username or Discord ID (verified GitHub @username once linked, Stage 3). Free-typed names are never accepted.
6. **Continue** opens **modal 2**: the optional questions, file upload, (bugs) the required "nothing private attached" checkbox, and (dev tasks) the kind of work.
7. Rate check (below), then create the GitHub issue with the template's title prefix, labels, and body layout (plus "Submitted from Discord by … · link to post"), create the forum post, and reply to the user with both links.

## Approval queue

- A user's **2nd+ submission within 1 hour** goes to approval instead of GitHub (the limit is `/admin issues per-hour`). Approvers and members with Manage Server aren't limited.
- Posted in `#admin-chat`, pinging **@Admin**: preview, submitter, **[Approve] [Deny]**.
- Only the approver role and members with Manage Server can press the buttons.
- Pending submissions are stored in SQLite so buttons survive restarts.
- Approve → created as normal. Deny → DM the user (if their DMs are open).

## Sync

**GitHub → Discord** (poll every ~2 min using `since` + ETags; later webhooks)
- New issue → new forum post (pull requests ignored).
- Title/body edit → update the post title / starter message.
- New comment → posted in the thread by the bot, as an embed with the GitHub user's name + avatar linking to the comment. Edits update it. (An embed instead of a webhook: no extra permission or stored webhook token.)
- Labels → tags.
- Closed → final message with the close reason, closed tag, **lock + archive**. Reopened → unarchive + unlock.
- First run: mirror **currently open issues only**. Closed issues are not backfilled.
- Each check asks for issues and comments changed since the last one (`since`), with a minute's overlap; the bot remembers what each post shows and which comments it copied, so repeats change nothing. The first check starts from when the oldest post was made, so old history isn't replayed.
- A post deleted in Discord is forgotten and made again while its issue is open.

**Discord → GitHub**
- Default: **every message** in a post becomes a GitHub comment ("**name** on Discord: …" + link). Edits update the comment; deletes delete it.
- `/admin` setting switches to **"Send to GitHub"** mode: only messages sent via that message action are mirrored.
- Bot/webhook messages are never mirrored back (no loops).
- Requires the **Message Content** intent to stay **on**.

**Attachments**
- Files stay in Discord. The comment notes: "📎 `file.png` (image/png, 240 KB) was attached on Discord — [view in the thread](link)". Maintainers copy vital ones to GitHub by hand.
- Small `.log` / `.txt` files (≈50 KB) are also inlined in a collapsed `<details>` block, with token-like strings (`ghp_…`, `github_pat_…`, `gho_…`) redacted.

**Deleted Discord post** → unlink only; never closes the GitHub issue.

## `/admin` settings

- Sync mode: mirror all / "Send to GitHub" only.
- Forum, submit, and approval channels.
- Verified role, dev-task submitter roles, approver role.
- Rate limit (default 1 per hour).

## Data (SQLite)

- `IssueLinks`: issue number ↔ thread ID, reporter Discord ID, state.
- `CommentLinks`: Discord message ID ↔ GitHub comment ID.
- `PendingSubmissions`: approval queue.
- `GitHubLinks`: Discord user ID ↔ verified GitHub login.
- `SyncCursor`: last poll time + ETags.

## Stages

- [x] **Stage 1 — Submit:** GitHub App setup, GitHub client, forum + tags, submit panel + flows, duplicate check, rate limit + approval queue, create issue + post, backfill open issues, posts for issues opened on GitHub (polling).
- [x] **Stage 2 — GitHub → Discord:** comments (and their edits), title/body edits, labels → tags, close/reopen + lock/archive, deleted posts re-created.
- [x] **Stage 3 — Discord → GitHub:** message mirroring (+ edits/deletes), attachment notes + log inlining, mode toggle + "Send to GitHub" action, `/link-github` (device flow) + GitHub credit option, Message Content intent (opt-in with `Bot:MessageContentIntent`). Also: paste as many log files as fit.
- [ ] **Stage 4 — Webhooks:** `bot.febuddy.com` via Cloudflare tunnel, signature check; polling drops to an hourly safety net.
