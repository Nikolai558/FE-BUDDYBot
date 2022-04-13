# Discord Event Commands
## User Joined Guild Event
`When a user joins the guild (discord server), this event is triggered.`
The bot will check a users status on the [VATUSA](https://www.vatusa.net/) website. If the user has an account and the discord is linked to that account, one of the following roles is assigned based on what is returned from the VATUSA website.
    * "ARTCC STAFF"
    * "VERIFIED"
This command will also change the users **nickname** to what is returned from the VATUSA website in the following format:
    `{First Name} {Last Name} | {ARTCC} {Staff Position}`

If the user does not have a VATUSA account or their discord is not linked to that account, the bot will send a private message with instructions on how to link their discord account.

---

## User Joined "Private Meeting" Voice Channel
`When a user connects to the "Private Meeting" voice channel in the guild (discord server), this event is triggered.`
The bot will give the following role to that user. When the user disconnects from this specified channel the role is removed.
    * "voice-meeting-txt"

---