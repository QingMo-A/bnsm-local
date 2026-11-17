from pathlib import Path
p=Path('I:/BnsmModernWork/Project/Assets/Scripts/Assembly-CSharp/BscPrivateSiegeBattleDialog.cs');s=p.read_text(encoding='utf-8-sig');old='\tprotected override void OnEnable()\n\t{\n\t\tbase.OnEnable();';assert old in s;s=s.replace(old,'\tprotected override void OnEnable()\n\t{\n        BindCastleReferences();\n\t\tbase.OnEnable();');at=s.index('\tprivate void RequestEnterPrivateSiegeBattleInfo()');method='''    // The recovered map prefab also exists in an older asset bundle whose
    // controller references were not serialized. Recover only missing fields
    // from its stable named hierarchy, preserving every valid prefab reference.
    private void BindCastleReferences() {
        var nodes=GetComponentsInChildren<Transform>(true);
        Func<string,Transform> find=name=>Array.Find(nodes,x=>x.name==name);
        if(_BaseBlocks==null)_BaseBlocks=find("BaseBlockRoot");
        if(_goFx_Red==null){var n=find("Fx_Red");if(n!=null)_goFx_Red=n.gameObject;}
        if(_goFx_Blue==null){var n=find("Fx_Blue");if(n!=null)_goFx_Blue=n.gameObject;}
        if(_PriceTable==null){var n=find("Table_Price");if(n!=null)_PriceTable=n.GetComponent<UITable>();}
        if(_TableStatus==null){var n=find("Table_Board");if(n!=null)_TableStatus=n.GetComponent<UITable>();}
        if(_DynamicLabelBgName==null){var n=find("DynamicLabelBG");if(n!=null)_DynamicLabelBgName=n.GetComponent<UIDynamicLabelBG>();}
        if(_LabelSearchPrice==null){var n=find("Label_Price");if(n!=null)_LabelSearchPrice=n.GetComponent<UILabel>();}
        if(_SearchPriceInfo==null){var n=find("Table_Price");if(n!=null)_SearchPriceInfo=n.gameObject;}
        if(_Label_IntrusionSearch==null){var n=find("Btn_Attack");if(n!=null){var child=n.Find("Label_Attack");if(child!=null)_Label_IntrusionSearch=child.GetComponent<UILabel>();}}
        if(harvestBtn==null){var n=find("Btn_Harvest");if(n!=null)harvestBtn=n.gameObject;}
        if(harvestBadgeGo==null&&harvestBtn!=null){var n=harvestBtn.transform.Find("Badge");if(n!=null)harvestBadgeGo=n.gameObject;}
        if(_LabelAttackCount==null){var n=find("Label_AttackCount");if(n!=null)_LabelAttackCount=n.GetComponent<UILabel>();}
        if(_LabelRewardCount==null){var n=find("Label_RewardCount");if(n!=null)_LabelRewardCount=n.GetComponent<UILabel>();}
        if(_LabelHelpCount==null){var n=find("Label_HelpCount");if(n!=null)_LabelHelpCount=n.GetComponent<UILabel>();}
        if(_LabelFriendRaidCount==null){var n=find("Label_RaidCount");if(n!=null)_LabelFriendRaidCount=n.GetComponent<UILabel>();}
    }

''';s=s[:at]+method+s[at:];
for old,new in [('_PriceTable.repositionNow = true;','if(_PriceTable!=null)_PriceTable.repositionNow = true;'),('_DynamicLabelBgName.ForceUpdateInfo();','if(_DynamicLabelBgName!=null)_DynamicLabelBgName.ForceUpdateInfo();'),('harvestBadgeGo.SetActive(CheckHavestEnable());','if(harvestBadgeGo!=null)harvestBadgeGo.SetActive(CheckHavestEnable());'),('_LabelSearchPrice.text =','if(_LabelSearchPrice!=null)_LabelSearchPrice.text ='),('_Label_IntrusionSearch.text =','if(_Label_IntrusionSearch!=null)_Label_IntrusionSearch.text =')]:s=s.replace(old,new)
p.write_text(s,encoding='utf-8-sig')
