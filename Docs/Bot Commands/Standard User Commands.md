# Standard Discord User Commands
## Give Role
`/give-role`

Checks your account on the [VATUSA](https://www.vatusa.net/) website. If your Discord account is linked there, the bot gives you:
* The **Verified** role
* The **ARTCC Staff** role, if VATUSA lists you as ATM, DATM, TA, EC, FE or WM at a facility. VATUSA records assistants (AEC, AFE, AWM) under the same codes, so they get it too.

It also sets your **nickname** to `{First Name} {Last Name} | {ARTCC}`:
* If your nickname already has a `|`, everything before it is kept and only the ARTCC after it is updated. For example, `Nik | ZOB` becomes `Nik | ZLC`.
* If you've turned on name privacy on VATUSA, your CID is used instead of your name.
* Nicknames are limited to 32 characters, so long names are shortened.
* Discord doesn't let bots change the server owner's nickname.

If your account isn't linked, the bot tells you how to link it at https://vatusa.net/my/profile ("VATUSA Discord Link"). If VATUSA can't be reached, it asks you to try again in a few minutes.

`/give-role` only adds roles; it never removes them. The reply is only visible to you.

**Permission Requirements**
* None. Anyone in the server can use it.
* In FE-Buddy it can only be used in **#assign-my-roles** (see [Discord Setup](../Discord%20Setup.md#command-permissions)).


## Report
`/report type:<Bug report | Feature request | Documentation problem | Development task>`

Reports an FE-BUDDY issue. It does the same as the buttons in **#submit-an-issue**:
1. A form asks for a title and the main question from the matching [GitHub issue template](https://github.com/Nikolai558/FE-BUDDY/tree/v3-development/.github/ISSUE_TEMPLATE).
2. The bot shows existing issues that look similar. If one of them is yours, add a 👍 or a reply there instead. You also choose how the GitHub issue names you: your Discord username, your Discord user ID, or (after `/link-github`) your GitHub account, which gets you GitHub notifications for the issue.
3. **Continue** opens a second form for the rest of the questions and any files (logs, screenshots).

The bot then creates the GitHub issue and a post for it in the issues forum. Small `.log` and `.txt` files are pasted into the issue, with anything that looks like a GitHub token blanked out. Other files stay on Discord, and the issue links to them.

If you've already submitted one in the last hour, the next one goes to the admins for approval first, and you get a DM when it's handled.

**Permission Requirements**
* The **Verified** role (run `/give-role` first). **Development task** also needs a dev-task role (see [`/admin dev-task-roles`](Admin%20User%20Commands.md#dev-task-roles)).
* In FE-Buddy it can only be used in **#submit-an-issue**.


## Replying in an issue post
Each FE-BUDDY issue has a post in the issues forum. Replies there are copied to the GitHub issue as comments, showing your display name and a link back to your message. Editing or deleting your message updates or deletes the comment. Small `.log` and `.txt` files are pasted in (tokens blanked out). If a reply couldn't be copied, the bot reacts to it with ⚠️.

If the admins have switched replies to **Only "Send to GitHub"**, only messages you choose are copied: right-click (or long-press) your message → **Apps → Send to GitHub**.

Comments made on GitHub show up in the post within a couple of minutes. When the issue is closed, the post is locked.

## Link GitHub
`/link-github`

Links your GitHub account. The bot shows a code; open https://github.com/login/device, enter it, and approve **FE-BUDDY Bot**. The bot only reads your GitHub username and throws the sign-in away.

After linking, your replies in issue posts show your GitHub account, and when you report an issue you can be credited as `@you` on GitHub, which notifies you about it.

`/unlink-github` removes the link.

---
