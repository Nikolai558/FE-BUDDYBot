# GitHub App Setup
The bot creates FE-BUDDY issues as its own GitHub App, so issues show up as **`<app name>[bot]`** instead of under your account. The app can only touch the repositories it's installed on, and only issues.

You do this once. It takes about 10 minutes.

## 1. Create the app
1. Go to **GitHub → your profile picture → Settings → Developer settings → GitHub Apps → New GitHub App**
   ([direct link](https://github.com/settings/apps/new)).
2. Fill in:

   | Field | Value |
   |---|---|
   | **GitHub App name** | `FE-BUDDY Bot` (must be unique on all of GitHub; if it's taken, try `FE-BUDDY Discord Bot`) |
   | **Homepage URL** | `https://github.com/Nikolai558/FE-BUDDYBot` |
   | **Callback URL** | leave empty |
   | **Expire user authorization tokens** | leave as is |
   | **Request user authorization (OAuth) during installation** | ☐ off |
   | **Enable Device Flow** | ☑ **on** (used later by `/link-github`) |
   | **Setup URL** | leave empty |
   | **Webhook → Active** | ☐ **off** (webhooks come in a later stage) |

3. **Permissions → Repository permissions**. Leave everything at **No access** except:

   | Permission | Access |
   |---|---|
   | **Issues** | Read and write |
   | **Contents** | Read-only (to list FE-BUDDY's releases for the version dropdown) |
   | **Metadata** | Read-only (GitHub sets this automatically) |

   Leave **Organization** and **Account permissions** at No access.
4. **Subscribe to events**: leave all unticked.
5. **Where can this GitHub App be installed?** → **Only on this account**.
6. Click **Create GitHub App**.

## 2. Note the App ID
On the page that opens (the app's **General** settings), copy the **App ID** near the top. It's a number like `1234567`. It isn't secret.

## 3. Generate a private key
1. Scroll down to **Private keys → Generate a private key**.
2. Your browser downloads a file like `fe-buddy-bot.2026-09-28.private-key.pem`.

**This file is the app's password.** Don't commit it, paste it in chat, or email it. If it leaks, delete it on the same page and generate a new one.

## 4. Install the app
1. In the app's settings, click **Install App** (left menu) → **Install** next to your account.
2. Choose **Only select repositories** and pick:
   * `FE-BUDDY` (production)
   * `FE-BUDDYBot-sandbox` (for testing; create it first as a **private** repository if it doesn't exist)
3. Click **Install**.

The bot finds the installation by itself; you don't need the installation ID.

## 5. Give the bot the App ID and key

### Production server
1. Copy the key to the server, into the bot's folder, named exactly `github-app.pem`:
   ```bash
   sudo mkdir -p /home/febuddybot/bot/secrets
   sudo mv ~/fe-buddy-bot.*.private-key.pem /home/febuddybot/bot/secrets/github-app.pem
   sudo chown 1654:1654 /home/febuddybot/bot/secrets/github-app.pem
   sudo chmod 400 /home/febuddybot/bot/secrets/github-app.pem
   ```
2. Add to `bot.env`:
   ```
   GitHub__AppId=1234567
   GitHub__PrivateKeyPath=secrets/github-app.pem
   ```
3. Make sure `docker-compose.yml` has the `./secrets:/app/secrets:ro` volume (see the repository copy), then restart the bot (see [Deployment](Deployment.md#common-commands)).

### Development machine
Keep the key outside the repository, then:
```
cd FEBuddyDiscordBot
dotnet user-secrets set "GitHub:AppId" "1234567"
dotnet user-secrets set "GitHub:PrivateKeyPath" "C:\Users\<you>\keys\fe-buddy-bot.pem"
```
Development creates issues in `FE-BUDDYBot-sandbox` (set in `appsettings.Development.json`), never in FE-BUDDY. To give the sandbox FE-BUDDY's labels:
```
gh label clone Nikolai558/FE-BUDDY -R Nikolai558/FE-BUDDYBot-sandbox
```

## 6. Check it worked
Run `/admin settings` in Discord. The **GitHub issues** section shows `GitHub: ✅ Nikolai558/FE-BUDDY`. That only means the settings are there. The first real sign-in happens on the next GitHub check (within 2 minutes), and problems are logged as `Issues: sync with GitHub failed`. The most common one, `GitHub App sign-in failed`, means a wrong App ID, the wrong key file, or the app isn't installed on the repository.

Then set up the Discord side: [Discord Setup → Issue reporting](Discord%20Setup.md#issue-reporting).
