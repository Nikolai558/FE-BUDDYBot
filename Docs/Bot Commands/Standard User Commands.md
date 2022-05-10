# Standard Discord User Commands
## Give Roles
`Give Discord Server Roles Depending on VATUSA Status.`

This command checks the users status on the [VATUSA](https://www.vatusa.net/) site. If the user has an account and their discord is linked to that account, one of the following roles is assigned based on what is returned from the VATUSA site.
* "ARTCC STAFF"
* "VERIFIED"

This command will also change the users **nickname** to what is returned from the VATUSA site in the following format:

`{First Name} {Last Name} | {ARTCC}`

If the user does not have a VATUSA account or their discord is not linked to that account, the bot will send a private message with instructions on how to link their discord account.

**Command Aliases:**
* `{prefix}` `gr`
* `{prefix}` `give-roles`
* `{prefix}` `give-role`
* `{prefix}` `assign-roles`
* `{prefix}` `assign-role`

---
