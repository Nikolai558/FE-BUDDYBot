﻿namespace FEBuddyDiscordBot.Models;
#pragma warning disable IDE1006

public class VatusaUserModel
{

    public class VatusaUserData
    {
        public UserInformation? data { get; set; }
        public bool? testing { get; set; }
    }

    public class UserInformation
    {
        public int? cid { get; set; }
        public string? fname { get; set; }
        public string? lname { get; set; }
        public string? email { get; set; }
        public string? facility { get; set; }
        public int? rating { get; set; }
        public DateTime? created_at { get; set; }
        public DateTime? updated_at { get; set; }
        public bool? flag_needbasic { get; set; }
        public bool? flag_xferOverride { get; set; }
        public DateTime? facility_join { get; set; }
        public bool? flag_homecontroller { get; set; }
        public DateTime? lastactivity { get; set; }
        public object? flag_broadcastOptedIn { get; set; }
        public object? flag_preventStaffAssign { get; set; }
        public bool? promotion_eligible { get; set; }
        public object? transfer_eligible { get; set; }
        public StaffRole[]? roles { get; set; }
        public string? rating_short { get; set; }
        public object[]? visiting_facilities { get; set; }
        public bool? isMentor { get; set; }
        public bool? isSupIns { get; set; }
        public DateTime? last_promotion { get; set; }
    }

    public class StaffRole
    {
        public int? id { get; set; }
        public int? cid { get; set; }
        public string? facility { get; set; }
        public string? role { get; set; }
        public DateTime? created_at { get; set; }
    }

}
