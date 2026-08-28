using MyFirstPlugin.Events;
using System.Collections.Generic;
using UnityEngine;

namespace MyFirstPlugin.Config;

public class PluginConfig
{
    public bool AutomaticEventsEnabled { get; set; } = true;

    public EventRollConfig EventRoll { get; set; } = new();

    public BottomInfoConfig BottomInfo { get; set; } = new();

    public BlackoutEventConfig Blackout { get; set; } = new();

    public TimeToGambleEventConfig TimeToGamble { get; set; } = new();

    public SpeedDemonEventConfig SpeedDemon { get; set; } = new();

    public EscalationEventConfig Escalation { get; set; } = new();

    public JailbirdMayhemEventConfig JailbirdMayhem { get; set; } = new();

    public InfectionEventConfig Infection { get; set; } = new();
}

public class InfectionEventConfig
{
    public EventDisplayConfig Display { get; set; } = new()
    {
        Name = DefaultEventDisplayNames.Infection,
        Color = "#66FF66",
        Description =
            "Plague Doctors lead an SCP-049-2 horde whose kills convert human survivors into new zombies."
    };

    public bool Enabled { get; set; } = true;

    public int TwoDoctorMinimumPlayers { get; set; } = 15;

    public int ThreeDoctorMinimumPlayers { get; set; } = 30;

    public int MaximumStartingDoctors { get; set; } = 3;

    public float ConversionDelaySeconds { get; set; } = 1f;

    public float PlagueDoctorHealthMultiplier { get; set; } = 1f;

    public float PlagueDoctorHumeShieldMultiplier { get; set; } = 1f;

    public float ZombieHealthMultiplier { get; set; } = 1f;

    public float ZombieHumeShieldMultiplier { get; set; } = 1f;

    public string StartAnnouncement { get; set; } =
        "<color=#66ff66><b>INFECTION!</b></color> Plague Doctors lead the infected. Zombies spread the infection through their kills.";

    public ushort StartAnnouncementDurationSeconds { get; set; } = 12;
}

public class JailbirdMayhemEventConfig
{
    public EventDisplayConfig Display { get; set; } = new()
    {
        Name = DefaultEventDisplayNames.JailbirdMayhem,
        Color = "#FFD166",
        Description =
            "Playable humans keep their utility loadouts but replace spawn firearms and ammunition with Jailbirds."
    };

    public bool Enabled { get; set; } = true;

    public float SpawnProcessingDelaySeconds { get; set; } = 1f;

    public bool RemoveFirearms { get; set; } = true;

    public bool RemoveConventionalFirearmAmmunition { get; set; } = true;

    public int JailbirdAmount { get; set; } = 1;

    public string StartAnnouncement { get; set; } =
        "<color=orange><b>JAILBIRD MAYHEM!</b></color> Human firearms have been replaced with Jailbirds.";

    public ushort StartAnnouncementDurationSeconds { get; set; } = 10;
}

public class EscalationEventConfig
{
    public EventDisplayConfig Display { get; set; } = new()
    {
        Name = DefaultEventDisplayNames.Escalation,
        Color = "#FF8C42",
        Description =
            "SCPs begin empowered while the surviving humans progressively gain stronger advantages."
    };

    public float ScpMaxHealthMultiplier { get; set; } = 1.35f;

    public byte ScpDamageReductionIntensity { get; set; } = 30;

    public float StageOneTimeSeconds { get; set; } = 300f;

    public float StageTwoTimeSeconds { get; set; } = 600f;

    public float StageThreeTimeSeconds { get; set; } = 900f;

    public float StageFourTimeSeconds { get; set; } = 1200f;

    public List<ItemType> StageOneItems { get; set; } = new()
    {
        ItemType.Medkit,
        ItemType.Painkillers,
        ItemType.Adrenaline
    };

    public float HumanStageTwoMaxHealthMultiplier { get; set; } = 1.25f;

    public List<ItemType> StageThreeItems { get; set; } = new()
    {
        ItemType.GunE11SR,
        ItemType.ArmorCombat,
        ItemType.GrenadeHE
    };

    public ItemType StageThreeAmmoType { get; set; } = ItemType.Ammo556x45;

    public ushort StageThreeAmmoAmount { get; set; } = 120;

    public byte StageFourMovementBoostIntensity { get; set; } = 100;

    public float RespawnCatchUpDelaySeconds { get; set; } = 1f;

    public string StartAnnouncement { get; set; } =
        "<color=red><b>ESCALATION ACTIVATED!</b></color> SCPs begin empowered. Humans will grow stronger over time.";

    public string StageOneAnnouncement { get; set; } =
        "<color=yellow><b>ESCALATION STAGE 1</b></color> Human medical supplies deployed.";

    public string StageTwoAnnouncement { get; set; } =
        "<color=yellow><b>ESCALATION STAGE 2</b></color> Surviving humans have increased maximum health.";

    public string StageThreeAnnouncement { get; set; } =
        "<color=orange><b>ESCALATION STAGE 3</b></color> Heavy human armaments deployed.";

    public string StageFourAnnouncement { get; set; } =
        "<color=red><b>ESCALATION STAGE 4</b></color> Human movement speed doubled.";

    public ushort StartAnnouncementDurationSeconds { get; set; } = 10;

    public ushort StageAnnouncementDurationSeconds { get; set; } = 8;
}

public class SpeedDemonEventConfig
{
    public EventDisplayConfig Display { get; set; } = new()
    {
        Name = DefaultEventDisplayNames.SpeedDemon,
        Color = "#FF4D4D",
        Description = "Everyone moves at extreme speed. Good luck."
    };

    public byte Intensity { get; set; } = 170;

    public byte ScpIntensity { get; set; } = 165;

    public float DurationSeconds { get; set; } = 86400f;

    public float StaminaDrainMultiplier { get; set; } = 0.15f;

    public float StaminaRegenerationMultiplier { get; set; } = 2.0f;
}

public class TimeToGambleEventConfig
{
    public EventDisplayConfig Display { get; set; } = new()
    {
        Name = DefaultEventDisplayNames.TimeToGamble,
        Color = "#C77DFF",
        Description =
            "A modular event that strips starting equipment from human players and detects interaction with one existing workstation."
    };

    public MapGeneration.RoomName TargetRoomName { get; set; } = MapGeneration.RoomName.LczArmory;

    public int TargetWorkstationIndex { get; set; } = 0;

    public Vector3 RewardSpawnOffset { get; set; } = new Vector3(0f, 1f, 0f);

    public List<GambleReward> Rewards { get; set; } = new()
    {
        new GambleReward(ItemType.GunE11SR, "E-11 SR", "Rare", 10d),
        new GambleReward(ItemType.Medkit, "Medkit", "Uncommon", 25d),
        new GambleReward(ItemType.Flashlight, "Flashlight", "Common", 45d),
        new GambleReward(ItemType.GrenadeFlash, "Flashbang", "Uncommon", 20d)
    };
}

public class BlackoutEventConfig
{
    public EventDisplayConfig Display { get; set; } = new()
    {
        Name = DefaultEventDisplayNames.Blackout,
        Color = "#6699FF",
        Description =
            "A round-long facility blackout with a delayed cinematic intro and randomized dark and powered periods."
    };

    public float IntroStartDelaySeconds { get; set; } = 10f;

    public bool EnableFlickering { get; set; } = true;

    public int FlickerStepDurationMilliseconds { get; set; } = 225;

    public int NormalShortBlackoutSeconds { get; set; } = 30;

    public int NormalPoweredSeconds { get; set; } = 10;

    public int ShortBlackoutMinSeconds { get; set; } = 25;

    public int ShortBlackoutMaxSeconds { get; set; } = 45;

    public float ShortBlackoutChance { get; set; } = 15f;

    public int LongBlackoutMinSeconds { get; set; } = 150;

    public int LongBlackoutMaxSeconds { get; set; } = 210;

    public float BlackoutFlickerChance { get; set; } = 35f;

    public float PoweredFlickerChance { get; set; } = 55f;

    public float BlackoutFlickerMinIntervalSeconds { get; set; } = 5f;

    public float BlackoutFlickerMaxIntervalSeconds { get; set; } = 10f;

    public float BlackoutFlickerDurationSeconds { get; set; } = 0.12f;

    public float PoweredFlickerMinIntervalSeconds { get; set; } = 3f;

    public float PoweredFlickerMaxIntervalSeconds { get; set; } = 10f;

    public float SubtleFlickerMinIntervalSeconds { get; set; } = 2.5f;

    public float SubtleFlickerMaxIntervalSeconds { get; set; } = 5.5f;

    public float SubtleFlickerDurationSeconds { get; set; } = 0.15f;

    public float LightSourceChance { get; set; } = 50f;

    public float LightSourceGrantDelaySeconds { get; set; } = 1f;

    public bool CassieEnabled { get; set; } = true;

    public string CassieSpokenMessage { get; set; } =
        "ATTENTIONALLPERSONNEL . POWER FAILURE DETECTED";

    public string CassieCustomSubtitle { get; set; } =
        "Facility power failure detected.";

    public float CassiePriority { get; set; } = 0f;

    public bool CassiePlayBackgroundAudio { get; set; } = true;

    public float CassieGlitchIntensity { get; set; } = 1f;

    public string StartAnnouncement { get; set; } = "<color=red><b>BLACKOUT EVENT ACTIVATED!</b></color>";

    public string PreBlackoutWarning { get; set; } = "<color=red><b>FACILITY POWER FAILURE DETECTED</b></color>";

    public string EndAnnouncement { get; set; } = "<color=green><b>Power restored. The blackout has ended.</b></color>";

    public ushort StartAnnouncementDurationSeconds { get; set; } = 10;

    public ushort PreBlackoutWarningDurationSeconds { get; set; } = 6;

    public ushort EndAnnouncementDurationSeconds { get; set; } = 5;
}

public class BottomInfoConfig
{
    public bool Enabled { get; set; } = true;

    public float VerticalPosition { get; set; } = 50f;

    public int FontSize { get; set; } = 18;

    public string TextColor { get; set; } = "#D9F2FF";

    public bool ShowServerInfo { get; set; } = true;

    public string ServerInfoText { get; set; } = "NUKE EVENTS";

    public string ServerInfoColor { get; set; } = "";

    public float ServerInfoDurationSeconds { get; set; } = 60f;

    public bool ShowEventDetails { get; set; } = true;

    public float EventDetailsDurationSeconds { get; set; } = 45f;

    public bool TipsEnabled { get; set; } = true;

    public string TipColor { get; set; } = "#FFE6A3";

    public float TipDurationSeconds { get; set; } = 45f;

    public List<string> Tips { get; set; } = new()
    {
        "Special events are selected before each round.",
        "Adapt your strategy to the active event.",
        "Work with your team and watch for event-specific changes."
    };
}
