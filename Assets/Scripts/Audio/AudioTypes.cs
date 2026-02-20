namespace SeekerDungeon.Audio
{
    public enum ButtonSfxCategory
    {
        Primary = 0,
        Secondary = 1,
        Danger = 2,
        Nav = 3,
        Confirm = 4,
        ModalOpen = 5
    }

    public enum StingerSfxId
    {
        ExtractionSuccess = 0,
        EnterDungeon = 1,
        DungeonEntered = 2
    }

    public enum WorldSfxId
    {
        DoorOpenOpen = 0,
        DoorOpenRubble = 1,
        DoorOpenLocked = 2,
        StairsExit = 3,
        CharacterSwap = 4,
        Equip = 5,
        RatOneShot = 6,
        SessionWalletTopUp = 7,
        RatSquash = 8,
        DoorUnlock = 9
    }

    public enum AudioLoopId
    {
        GameAmbience = 0,
        Mining = 1,
        BossAttack = 2,
        BossMonsterLoop = 3
    }
}
