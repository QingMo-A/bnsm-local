using System.Collections.Generic;
using ProtoBuf;
#if !BNSM_SERVER
using USER_PACKET;
#endif

// Local extension of GS_EXTRACT_EQUIPMENT_ITEM. Fields 1..3 retain the APK layout.
// Operation zero remains the original single-item request.
[ProtoContract]
public sealed class LocalEquipmentRequest
{
    public const uint State=1, Lock=2, Unlock=3, Preview=4, Extract=5;
    public const int Maximum=200;
    [ProtoMember(1,IsRequired=true)] public SessionKeyInfo keyInfo=new SessionKeyInfo();
    [ProtoMember(2,IsRequired=true)] public ulong equipmentItemSeq;
    [ProtoMember(3)] public bool isTutorial;
    [ProtoMember(4)] public uint operation;
    [ProtoMember(5)] public List<ulong> sequences=new List<ulong>();
    [ProtoMember(6)] public bool allowImproved;
    [ProtoMember(7)] public string previewToken="";
    [ProtoMember(8)] public string transactionId="";
}

[ProtoContract]
public sealed class LocalEquipmentTier
{
    [ProtoMember(1,IsRequired=true)] public int rank;
    [ProtoMember(2,IsRequired=true)] public int count;
}

// Fields 1..2 also decode as the original GsExtractEquipmentItemOk.
[ProtoContract]
public sealed class LocalEquipmentReply
{
    [ProtoMember(1,IsRequired=true)] public PKCoinInfo userCoinInfo=new PKCoinInfo();
    [ProtoMember(2)] public List<PKUserItem> deltaItemList=new List<PKUserItem>();
    [ProtoMember(3)] public List<ulong> locked=new List<ulong>();
    [ProtoMember(4)] public List<LocalEquipmentTier> tiers=new List<LocalEquipmentTier>();
    [ProtoMember(5)] public string previewToken="";
}
