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

---
