using FEBuddyDiscordBot.Models;
using FEBuddyDiscordBot.Services;

namespace FEBuddyDiscordBot.Tests;

public class NicknameTests
{
    private static VatusaUser User(string first = "Kyle", string last = "Sanders", string facility = "ZOB", bool privacy = false) =>
        new() { Cid = 1187148, FirstName = first, LastName = last, Facility = facility, NamePrivacy = privacy };

    [Fact]
    public void Uses_first_last_and_facility()
    {
        Assert.Equal("Kyle Sanders | ZOB", RoleAssignmentService.BuildNickname(null, User()));
    }

    [Fact]
    public void Keeps_custom_name_before_pipe_and_updates_facility()
    {
        Assert.Equal("Nik | ZOB", RoleAssignmentService.BuildNickname("Nik | ZLC", User()));
    }

    [Fact]
    public void Existing_nickname_without_pipe_is_replaced()
    {
        Assert.Equal("Kyle Sanders | ZOB", RoleAssignmentService.BuildNickname("kyle123", User()));
    }

    [Fact]
    public void Name_privacy_uses_cid()
    {
        Assert.Equal("1187148 | ZOB", RoleAssignmentService.BuildNickname(null, User(privacy: true)));
    }

    [Fact]
    public void Long_names_are_shortened_to_discord_limit()
    {
        string nickname = RoleAssignmentService.BuildNickname(null, User(first: "Bartholomew-Maximilian", last: "Featherstonehaugh"));

        Assert.True(nickname.Length <= 32, nickname);
        Assert.EndsWith(" | ZOB", nickname);
    }
}
