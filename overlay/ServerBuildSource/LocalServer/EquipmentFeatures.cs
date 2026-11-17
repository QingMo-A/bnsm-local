using System.Security.Cryptography;

internal sealed partial class ProtocolRouter
{
    // Card loadouts are the single source of truth for ownership. The inventory
    // keeps the equipment instance, including while it is worn.
    private static ulong EquipmentOwner(LocalPlayer p, ulong sequence) => p.CardStates
        .Where(x => x.Key > 0 && x.Key <= (ulong)p.CardIds.Count && p.CardIds[(int)x.Key-1] != 0)
        .FirstOrDefault(x => x.Value.WeaponSequence == sequence || x.Value.AccessorySequences.Contains(sequence)).Key;
    private static bool HasLoadout(LocalCardState state) => state.WeaponSequence != 0 ||
        state.AccessorySequences.Any(x => x != 0) || state.GemIds.Any(x => x != 0);

    private Reply? HandleEquipment(string name, object? request, LocalPlayer p, SessionKeyInfo session, byte[] payload,
        Func<object?, Reply> ok, Func<string, ServerErrorCode, Reply> fail)
    {
        if (name != "GS_UPDATE_CARD_EQUIP_REQUEST") return null;
        string key = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{session.loginSessionKey}/{name}/{session.sequenceNo}")));
        string fingerprint = Convert.ToHexString(SHA256.HashData(payload));
        if (p.EquipmentReceipts.TryGetValue(key, out var saved))
            return saved.Fingerprint == fingerprint
                ? new(saved.Command, WireCodec.Deserialize(typeof(SessionKeyInfo).Assembly.GetType(saved.TypeName, true), Convert.FromBase64String(saved.Payload)), saved.Name)
                : fail("equipment sequence reused with different parameters", ServerErrorCode.ERROR_INVALID_ID);
        var result = UpdateEquipment((GsUpdateCardEquipRequest)request!, p, ok, fail);
        if (result.Name.EndsWith("_OK")) {
            if (p.EquipmentReceipts.Count >= 256) p.EquipmentReceipts.Remove(p.EquipmentReceipts.Keys.First());
            p.EquipmentReceipts[key] = new() { Fingerprint=fingerprint, Command=result.Command, Name=result.Name,
                TypeName=result.Message!.GetType().FullName!, Payload=Convert.ToBase64String(WireCodec.Serialize(result.Message)) };
            store.Save();
        }
        return result;
    }

    private static bool FitsEquipment(BscTable.ItemData item, BscTable.CardData card, uint level)
    {
        // Same priority as EquipmentItem.VerifyAttach in this APK.
        if (item.Level > level || (item.Class != BscTable.CARD_CLASS.CLASS_ALL && item.Class != card.Class)) return false;
        if (item.CardId != 0) return card.EvolveFirstAncestor == item.CardId;
        if (item.CardCharacterType != BscTable.CARD_CHARACTER_TYPE.CARD_CHARACTER_TYPE_NONE) return card.CardCharacterType == item.CardCharacterType;
        if (item.CardFaction is not (BscTable.CARD_FACTION.FACTION_ALL or BscTable.CARD_FACTION.FACTION_NONE))
            return card.Faction is BscTable.CARD_FACTION.FACTION_ALL or BscTable.CARD_FACTION.FACTION_NONE || card.Faction == item.CardFaction;
        return item.CardGroupType is BscTable.CARD_GROUP.GROUP_ALL or BscTable.CARD_GROUP.GROUP_NONE ||
            card.Group is BscTable.CARD_GROUP.GROUP_ALL or BscTable.CARD_GROUP.GROUP_NONE || card.Group == item.CardGroupType;
    }
    private uint UnwearCost(BscTable.ItemData item)
    {
        if (item.Step <= 0 || (item.ItemType == BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT && item.Rank == 0)) return 0;
        var type = item.ItemType == BscTable.ITEM_TYPE.ITEM_TYPE_GEM ? BscTable.ITEM_CARD_PROCESS_TYPE.PROCESS_GEM_UNWEAR : BscTable.ITEM_CARD_PROCESS_TYPE.PROCESS_EQUIPMENT_UNWEAR;
        var rows = catalog.CardProcesses.List.FirstOrDefault(x => x.ProcessType == type)?.ProcessDataList;
        return rows != null && item.Step <= rows.Count ? (uint)Math.Max(0, rows[item.Step-1].CostGold) : 0;
    }
    private Reply UpdateEquipment(GsUpdateCardEquipRequest change, LocalPlayer p, Func<object?, Reply> ok, Func<string, ServerErrorCode, Reply> fail)
    {
        Reply Invalid(string reason) => fail(reason, ServerErrorCode.ERROR_INVALID_ID);
        if (!Owns(p, change.cardSeq)) return Invalid("card is not owned");
        if (InTraining(p, change.cardSeq)) return fail("finish training before changing equipment",ServerErrorCode.ERROR_CARD_IN_TRAINING);
        if (p.ActiveStage != null) return Invalid("finish the current battle before changing equipment");
        if (!FeatureOpen(p, BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_EQUIPMENT_CHANGE)) return fail("equipment unlocks at the original player level", ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        if (change.accessorySeq.Count != 2 || change.jewelID.Count != 3 || change.runeID.Count is not (0 or 6) || change.runeID.Any(x => x != 0)) return Invalid("expected one weapon, two accessories and three gems; runes are not implemented");
        var state = CardState(p, change.cardSeq);
        var card = catalog.RequireCard(p.CardIds[(int)change.cardSeq-1]);
        ulong[] old = [state.WeaponSequence, ..state.AccessorySequences];
        ulong[] next = [change.weaponSeq, ..change.accessorySeq];
        if (next.Where(x => x != 0).Distinct().Count() != next.Count(x => x != 0)) return Invalid("one equipment instance cannot occupy multiple slots");
        BscTable.ITEM_SUB_TYPE[] slots = [BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_WEAPON, BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_NECKLACE, BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_EARRING];
        for (int i=0; i<3; i++) {
            if (next[i] == 0) continue;
            var owned = p.EquipmentItems.FirstOrDefault(x => x.Sequence == next[i]);
            var item = owned == null ? null : catalog.Items.FirstOrDefault(x => x.Id == owned.ItemId);
            if (item == null || item.ItemType != BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT || item.ItemSubType != slots[i]) return Invalid("equipment is not owned or has the wrong slot");
            if (!FitsEquipment(item, card, state.Level)) return Invalid("equipment does not match the card's class, identity or level");
            ulong owner = EquipmentOwner(p, next[i]);
            if (owner != 0 && owner != change.cardSeq) return Invalid("unequip this item from its other card first");
        }
        foreach (uint id in change.jewelID.Where(x => x != 0))
            if (!catalog.Items.Any(x => x.Id == id && x.ItemType == BscTable.ITEM_TYPE.ITEM_TYPE_GEM)) return Invalid("invalid gem item");

        // Local recommendation never silently destroys the old gear. Native
        // quick equip is patched to use the same non-destructive path as manual equip.
        // GameSocket.RequestUpdateCardEquip writes bUnwear = !destroyUnequipItem.
        if (!change.bUnwear && Enumerable.Range(0,3).Any(i => (old[i] != 0 && old[i] != next[i]) || (state.GemIds[i] != 0 && state.GemIds[i] != change.jewelID[i])))
            return Invalid("destructive quick replacement is disabled locally; use normal equip to keep the old items");
        ulong cost=0;
        var gemDelta=new Dictionary<uint,int>();
        for (int i=0; i<3; i++) {
            if (old[i] != 0 && old[i] != next[i]) {
                var owned=p.EquipmentItems.FirstOrDefault(x => x.Sequence==old[i]);
                if (owned==null) return Invalid("saved equipment instance is missing");
                cost+=UnwearCost(catalog.Items.First(x => x.Id==owned.ItemId));
            }
            uint prior=state.GemIds[i], target=change.jewelID[i];
            if (prior==target) continue;
            if (prior!=0) {cost+=UnwearCost(catalog.Items.First(x => x.Id==prior));gemDelta[prior]=gemDelta.GetValueOrDefault(prior)+1;}
            if (target!=0) gemDelta[target]=gemDelta.GetValueOrDefault(target)-1;
        }
        foreach (var delta in gemDelta) {
            long count=(long)p.Inventory.GetValueOrDefault(delta.Key)+delta.Value;
            if (count<0) return fail("not enough owned gems",ServerErrorCode.ERROR_NOT_ENOUGH_ITEM_COUNT);
            if (count>int.MaxValue) return fail("gem inventory capacity reached",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        }
        if (cost>p.Gold) return fail("not enough gold to unequip or replace items",ServerErrorCode.ERROR_NOT_ENOUGH_GOLD);
        // All checks finish before any inventory, coins, or slots change.
        p.Gold-=(uint)cost;
        foreach (var delta in gemDelta) {int count=p.Inventory.GetValueOrDefault(delta.Key)+delta.Value;if(count==0)p.Inventory.Remove(delta.Key);else p.Inventory[delta.Key]=count;}
        state.WeaponSequence=next[0];state.AccessorySequences=change.accessorySeq.ToList();state.GemIds=change.jewelID.ToList();
        var result=new GsUpdateCardEquipOk {cardInfo=CardInfo((uint)card.Id,change.cardSeq,p),nowCoinInfo=Coins(p)};
        foreach (ulong sequence in old.Concat(next).Where(x => x!=0).Distinct()) {
            if (old.Contains(sequence) == next.Contains(sequence)) continue;
            var metadata=EquipmentInfo(p,p.EquipmentItems.First(x => x.Sequence==sequence));
            metadata.itemCount=0; // APK SetItem updates owner; +1 would duplicate the instance.
            result.itemInfoList.Add(metadata);
        }
        result.itemInfoList.AddRange(gemDelta.Where(x => x.Value!=0).Select(x => ItemInfo(x.Key,x.Value)));
        return ok(result);
    }
}
