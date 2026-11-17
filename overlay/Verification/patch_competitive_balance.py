from pathlib import Path
r=Path('I:/BnsmModernWork');p=r/'Project/Assets/Scripts/Assembly-CSharp/LocalRankBattle.cs';s=p.read_text(encoding='utf-8-sig');a=s.index('    public static BscUserCardInfo BalancedPlayer(');b=s.index('    public static bool Active',a);s=s[:a]+'''    public static PKBattleDeckCardInfo BalancedSnapshot(BscUserCardInfo original,uint uid) {
        var table=Singleton<DataTable>.Instance;BalanceCardPvpData row=null;
        foreach(var candidate in table.GetBalanceCardPvpDataTable().List)if(table.GetCardData(candidate.CardId,false).EvolveFirstAncestor==original.cardData.EvolveFirstAncestor){row=candidate;break;}
        if(row==null)throw new InvalidOperationException("Missing native balanced card template");
        var data=new PKBattleDeckCardInfo{cardId=(uint)row.CardId,cardSeq=original.cardSeq,uid=uid,cardLevel=(uint)row.Level,enchantGrade=(uint)row.Rank};
        for(int i=0;i<7;i++){data.cardSkillLevel.Add((uint)row.SkillLevel);data.cardSkillEnchant.Add(0);}
        foreach(int id in new[]{row.Weapone,row.Necklane,row.Earring,row.Gem1,row.Gem2,row.Gem3})data.cardEquipInfo.Add(new PKBattleDeckCardEquipInfo{itemID=(uint)id});return data;
    }
'''+s[b:];p.write_text(s,encoding='utf-8-sig')
p=r/'Project/Assets/Scripts/Assembly-CSharp/BscBattle/BattleCard.cs';s=p.read_text(encoding='utf-8-sig');s=s.replace('if (global::LocalRankBattle.Active && global::LocalRankBattle.Mode == 42) info = global::LocalRankBattle.BalancedPlayer(info);','''if (global::LocalRankBattle.Active && global::LocalRankBattle.Mode == 42) {
                var balanced=global::LocalRankBattle.BalancedSnapshot(info,cardUid);
                if(SetCardId(balanced.cardId)) {
                    userCard=true;cardSeq=info.cardSeq;uid=cardUid;level=balanced.cardLevel;enchant=balanced.enchantGrade;
                    ItemStat.SetStat(balanced.TotalEquipmentStat());_cardSkillEnchants=balanced.cardSkillEnchant;SetCardSkill(balanced);BaseStatUpdate();state=CARD_STATE.NORMAL;index=new CardUiIndex(true,-1);UpdateData();
                }
                return;
            }''');p.write_text(s,encoding='utf-8-sig')
