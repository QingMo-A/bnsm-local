public static class LocalDebugFlags
{
    public const uint FreeLevel=1, MaxLevel=2, FreeEnchant=4, MaxEnchant=8;
    public const uint FreeBreakthrough=16, MaxBreakthrough=32, FreeAwakening=64, MaxAwakening=128;
    public const uint FreeSkill=256, MaxSkill=512;
    public const uint FreeEquipmentCraft=1u<<10, MaxEquipmentCraft=1u<<11;
    public const uint FreeEquipmentAwake=1u<<12, MaxEquipmentAwake=1u<<13;
    public const uint FreeGemCraft=1u<<14, MaxGemCraft=1u<<15;
    public const uint FreeEquipmentEmpower=1u<<16, MaxEquipmentEmpower=1u<<17;
    public const uint FreeStoneCraft=1u<<18, MaxStoneCraft=1u<<19;
    public const int PairCount=10, OptionCount=PairCount*2;
    public const uint All=(1u<<OptionCount)-1;
    public static uint Normalize(uint value)
    {
        value &= All;
        for(int pair=0;pair<PairCount;pair++)if((value&(2u<<(pair*2)))!=0)value|=1u<<(pair*2);
        return value;
    }
    public static uint Change(uint value,int index,bool enabled)
    {
        if(index<0||index>=OptionCount)return Normalize(value);
        uint bit=1u<<index;
        value=enabled?value|bit:value&~bit;
        if(!enabled&&index%2==0)value&=~(bit<<1);
        return Normalize(value);
    }
}
