# Discord Event Commands
These happen automatically. Each one can be turned on or off with `/admin events`.

## User Joined Server
When someone joins the server, the bot does the same thing as [`/give-role`](Standard%20User%20Commands.md). If their Discord account isn't linked on VATUSA, the bot sends them a direct message explaining how to link it.

## User Connected to Voice
When someone connects to any voice channel, the bot does the same thing as `/give-role`, without sending a direct message. Moving between voice channels, muting, or deafening does not trigger it.

## Private Meeting Voice Channel
While someone is in the private meeting voice channel, they have the private meeting role. It's removed when they leave. Set the channel and role with `/admin channels` and `/admin roles`.

---
