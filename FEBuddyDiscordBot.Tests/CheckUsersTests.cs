using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Models;
using FEBuddyDiscordBot.Modules.SlashCommands;
using FEBuddyDiscordBot.Services;

namespace FEBuddyDiscordBot.Tests;

public class CheckUsersTests
{
    private static VatusaLookup Found(params string[] staffRoles) => new(VatusaLookupStatus.Found, new VatusaUser
    {
        Cid = 1,
        Facility = "ZLC",
        Roles = staffRoles.Select(r => new VatusaStaffRole { Facility = "ZLC", Role = r }).ToArray(),
    });

    [Fact]
    public void Linked_member_qualifies_for_verified_only()
    {
        Assert.Equal((true, false), RoleAssignmentService.Qualifications(Found()));
    }

    [Theory]
    [InlineData("ATM")]
    [InlineData("datm")]
    [InlineData("FE")]
    [InlineData("WM")]
    public void Facility_staff_qualify_for_staff_role(string position)
    {
        Assert.Equal((true, true), RoleAssignmentService.Qualifications(Found(position)));
    }

    [Fact]
    public void Non_staff_vatusa_roles_do_not_count()
    {
        Assert.Equal((true, false), RoleAssignmentService.Qualifications(Found("MTR", "INS")));
    }

    [Fact]
    public void Unlinked_member_qualifies_for_nothing()
    {
        Assert.Equal((false, false), RoleAssignmentService.Qualifications(new VatusaLookup(VatusaLookupStatus.NotLinked)));
    }

    [Theory]
    [InlineData(CheckUsersMode.Report, MemberChanges.None)]
    [InlineData(CheckUsersMode.AddRoles, MemberChanges.AddRoles)]
    [InlineData(CheckUsersMode.RemoveRoles, MemberChanges.RemoveRoles)]
    [InlineData(CheckUsersMode.UpdateAll, MemberChanges.AddRoles | MemberChanges.RemoveRoles | MemberChanges.Nickname)]
    public void Each_mode_allows_only_its_changes(CheckUsersMode mode, MemberChanges expected)
    {
        Assert.Equal(expected, StaffSlashCommands.ChangesFor(mode));
    }

    [Theory]
    [InlineData(1, 46, false)]   // normal: one unlinked member
    [InlineData(5, 6, false)]    // small numbers never trip the check
    [InlineData(20, 46, false)]  // under half
    [InlineData(24, 46, true)]   // over half: looks like a VATUSA outage
    [InlineData(46, 46, true)]
    public void Removal_safety_check(int notLinked, int total, bool unsafeExpected)
    {
        Assert.Equal(unsafeExpected, StaffSlashCommands.RemovalLooksUnsafe(notLinked, total));
    }
}
