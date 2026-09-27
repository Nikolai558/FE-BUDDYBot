using System.Text;
using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Services;

namespace FEBuddyDiscordBot.Modules.SlashCommands;

public enum CheckUsersMode
{
    [ChoiceDisplay("Report only (no changes)")]
    Report,

    [ChoiceDisplay("Add roles (add missing roles only)")]
    AddRoles,

    [ChoiceDisplay("Remove roles (remove roles members no longer qualify for)")]
    RemoveRoles,

    [ChoiceDisplay("Update all (add + remove roles, update nicknames)")]
    UpdateAll,
}

/// <summary>
/// Commands for server staff.
/// </summary>
[Group("staff", "Server staff commands")]
[DefaultMemberPermissions(GuildPermission.ManageRoles)]
[RequireUserPermission(GuildPermission.ManageRoles)]
public sealed class StaffSlashCommands : InteractionModuleBase<SocketInteractionContext>
{
    // Only one member check may run at a time.
    private static readonly SemaphoreSlim CheckUsersLock = new(1, 1);

    private readonly RoleAssignmentService _roleAssignment;
    private readonly ILogger<StaffSlashCommands> _logger;

    public StaffSlashCommands(RoleAssignmentService roleAssignment, ILogger<StaffSlashCommands> logger)
    {
        _roleAssignment = roleAssignment;
        _logger = logger;
    }

    [SlashCommand("check-users", "Compare every member with VATUSA, then report or fix their roles and nicknames.")]
    public async Task CheckUsersAsync([Summary("mode", "What to do with the differences found")] CheckUsersMode mode)
    {
        if (!await CheckUsersLock.WaitAsync(0))
        {
            await RespondAsync("A member check is already running. Please wait for it to finish.", ephemeral: true);
            return;
        }

        try
        {
            await DeferAsync(ephemeral: true);

            List<SocketGuildUser> members = Context.Guild.Users.Where(u => !u.IsBot).OrderBy(u => u.DisplayName).ToList();
            _logger.LogInformation("Check Users: {Mode} for {Count} members, started by {User}", mode, members.Count, Context.User.Username);

            // Step 1: look everyone up (read-only).
            List<MemberCheck> checks = [];
            List<string> failed = [];
            foreach (SocketGuildUser member in members)
            {
                try
                {
                    checks.Add(await _roleAssignment.CheckAsync(member));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Check Users: lookup failed for {User} ({UserId}): {Error}", member.Username, member.Id, ex.Message);
                    failed.Add(member.DisplayName);
                }
            }

            MemberChanges changes = ChangesFor(mode);
            int notLinked = checks.Count(c => c.Status == VatusaLookupStatus.NotLinked);
            string? safetyNote = null;

            if (changes.HasFlag(MemberChanges.RemoveRoles) && RemovalLooksUnsafe(notLinked, checks.Count))
            {
                changes &= ~MemberChanges.RemoveRoles;
                safetyNote = $"⚠️ {notLinked} of {checks.Count} members showed as not linked, which looks like a VATUSA problem. " +
                             "No roles were removed. Try again later.";
                _logger.LogWarning("Check Users: skipped removals; {NotLinked} of {Count} members showed as not linked", notLinked, checks.Count);
            }

            // Step 2: apply the changes this mode allows.
            Dictionary<MemberCheck, RoleAssignmentResult> results = [];
            if (changes != MemberChanges.None)
            {
                foreach (MemberCheck check in checks.Where(c => c.HasDifferences))
                {
                    try
                    {
                        results[check] = await _roleAssignment.ApplyAsync(check, changes, logUnchanged: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Check Users: update failed for {User} ({UserId}): {Error}", check.User.Username, check.User.Id, ex.Message);
                        failed.Add(check.User.DisplayName);
                    }
                }
            }

            string report = BuildReport(mode, changes, checks, results, failed, safetyNote);
            string summary = BuildSummary(mode, checks, results, failed, safetyNote);

            _logger.LogInformation("Check Users: finished. {Summary}", summary.ReplaceLineEndings(" "));

            try
            {
                using MemoryStream file = new(Encoding.UTF8.GetBytes(report));
                await FollowupWithFileAsync(file, $"check-users-{mode.ToString().ToLowerInvariant()}.txt", summary, ephemeral: true);
            }
            catch (Exception)
            {
                // The interaction expires after 15 minutes; fall back to a DM for very large servers.
                using MemoryStream file = new(Encoding.UTF8.GetBytes(report));
                await Context.User.SendFileAsync(file, $"check-users-{mode.ToString().ToLowerInvariant()}.txt", summary);
            }
        }
        finally
        {
            CheckUsersLock.Release();
        }
    }

    internal static MemberChanges ChangesFor(CheckUsersMode mode) => mode switch
    {
        CheckUsersMode.AddRoles => MemberChanges.AddRoles,
        CheckUsersMode.RemoveRoles => MemberChanges.RemoveRoles,
        CheckUsersMode.UpdateAll => MemberChanges.All,
        _ => MemberChanges.None,
    };

    /// <summary>
    /// If VATUSA suddenly reports lots of members as unlinked, it's more likely a VATUSA problem than real unlinks,
    /// so removing roles would strip them from people who should keep them.
    /// </summary>
    internal static bool RemovalLooksUnsafe(int notLinked, int checkedCount) => notLinked > 5 && notLinked * 2 > checkedCount;

    private static string BuildSummary(CheckUsersMode mode, List<MemberCheck> checks, Dictionary<MemberCheck, RoleAssignmentResult> results, List<string> failed, string? safetyNote)
    {
        int linked = checks.Count(c => c.Status == VatusaLookupStatus.Found);
        int notLinked = checks.Count(c => c.Status == VatusaLookupStatus.NotLinked);
        int unavailable = checks.Count(c => c.Status == VatusaLookupStatus.Unavailable);

        StringBuilder summary = new($"**Check users: {mode}**\n");
        summary.Append($"Checked {checks.Count + failed.Count} members: {linked} linked, {notLinked} not linked");
        if (unavailable > 0) summary.Append($", {unavailable} skipped (VATUSA unavailable)");
        if (failed.Count > 0) summary.Append($", {failed.Count} failed");
        summary.Append(".\n");

        if (mode == CheckUsersMode.Report)
        {
            summary.Append($"Update all would: add {checks.Sum(c => c.RolesToAdd.Count)} roles, remove {checks.Sum(c => c.RolesToRemove.Count)} roles, " +
                           $"change {checks.Count(c => c.NewNickname is not null)} nicknames. Nothing was changed.");
        }
        else
        {
            summary.Append($"Done: added {results.Values.Sum(r => r.Added.Count)} roles, removed {results.Values.Sum(r => r.Removed.Count)} roles, " +
                           $"changed {results.Values.Count(r => r.NicknameChanged)} nicknames.");
        }

        if (safetyNote is not null) summary.Append('\n').Append(safetyNote);
        summary.Append("\nDetails are in the attached file.");
        return summary.ToString();
    }

    private static string BuildReport(CheckUsersMode mode, MemberChanges changes, List<MemberCheck> checks, Dictionary<MemberCheck, RoleAssignmentResult> results, List<string> failed, string? safetyNote)
    {
        StringBuilder report = new();
        report.AppendLine($"Check users: {mode}   ({DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC)");
        if (safetyNote is not null) report.AppendLine(safetyNote);
        report.AppendLine();
        report.AppendLine("Legend: + add role, - remove role, ~ nickname.");
        report.AppendLine(mode == CheckUsersMode.Report
            ? "Report only: nothing was changed. Every line shows what 'Update all' would do."
            : "[done] = changed now, [failed] = could not change, [not in this mode] = difference found but left alone.");
        report.AppendLine();

        string Status(MemberChanges kind, bool succeeded) =>
            mode == CheckUsersMode.Report ? "" :
            !changes.HasFlag(kind) ? "  [not in this mode]" :
            succeeded ? "  [done]" : "  [failed]";

        List<MemberCheck> withDifferences = checks.Where(c => c.HasDifferences).ToList();
        report.AppendLine($"== Members with differences ({withDifferences.Count})");
        foreach (MemberCheck check in withDifferences)
        {
            results.TryGetValue(check, out RoleAssignmentResult? result);
            string vatusa = check.Status == VatusaLookupStatus.NotLinked ? "not linked on VATUSA" : $"CID {check.Vatusa?.Cid}, {check.Vatusa?.Facility}";
            report.AppendLine($"{check.User.DisplayName} (@{check.User.Username}) - {vatusa}");

            foreach (SocketRole role in check.RolesToAdd)
                report.AppendLine($"    + {role.Name}{Status(MemberChanges.AddRoles, result?.Added.Contains(role) == true)}");
            foreach (SocketRole role in check.RolesToRemove)
                report.AppendLine($"    - {role.Name}{Status(MemberChanges.RemoveRoles, result?.Removed.Contains(role) == true)}");
            if (check.NewNickname is not null)
                report.AppendLine($"    ~ \"{check.CurrentNickname}\" -> \"{check.NewNickname}\"{Status(MemberChanges.Nickname, result?.NicknameChanged == true)}");
        }

        List<MemberCheck> unavailable = checks.Where(c => c.Status == VatusaLookupStatus.Unavailable).ToList();
        if (unavailable.Count > 0 || failed.Count > 0)
        {
            report.AppendLine();
            report.AppendLine($"== Not checked ({unavailable.Count + failed.Count})");
            foreach (MemberCheck check in unavailable) report.AppendLine($"{check.User.DisplayName} (@{check.User.Username}) - VATUSA unavailable");
            foreach (string name in failed) report.AppendLine($"{name} - error, see bot log");
        }

        List<string> problems = checks.SelectMany(c => c.Problems).Concat(results.Values.SelectMany(r => r.Problems)).Distinct().ToList();
        if (problems.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("== Problems");
            foreach (string problem in problems) report.AppendLine(problem);
        }

        return report.ToString();
    }
}
